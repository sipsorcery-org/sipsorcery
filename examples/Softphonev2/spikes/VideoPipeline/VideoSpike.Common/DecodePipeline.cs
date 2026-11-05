using System.Collections.Concurrent;
using SIPSorceryMedia.Abstractions;

namespace VideoSpike;

/// <summary>
/// Decodes one incoming stream on its own thread, so a slow decode never blocks the RTP receive
/// thread. If decoding falls behind, the queue is capped: frames are dropped until the next
/// keyframe (dropping one inter frame would corrupt everything after it anyway).
/// </summary>
public sealed unsafe class DecodePipeline : IDisposable
{
    private readonly BlockingCollection<(byte[] data, double recvMs)> _queue = new(boundedCapacity: 16);
    private readonly FfDecoder _decoder;
    private readonly Thread _thread;
    private readonly VideoCodecsEnum _codec;
    private bool _waitKey;

    public long Received;
    public long DroppedInQueue;

    /// <summary>Called on the decode thread with (AVFrame* software frame, receive time).</summary>
    public event Action<IntPtr, double>? Frame;

    public FfDecoder Decoder => _decoder;

    public DecodePipeline(int index, VideoCodecsEnum codec, bool hardware)
    {
        _codec = codec;
        _decoder = new FfDecoder(codec, hardware);
        _thread = new Thread(Run) { IsBackground = true, Name = $"decode-{index}" };
        _thread.Start();
    }

    public void Push(byte[] frame, double recvMs)
    {
        Interlocked.Increment(ref Received);
        if (_waitKey)
        {
            if (!IsKeyframe(frame)) { Interlocked.Increment(ref DroppedInQueue); return; }
            _waitKey = false;
        }
        if (!_queue.TryAdd((frame, recvMs)))
        {
            Interlocked.Increment(ref DroppedInQueue);
            _waitKey = true;
        }
    }

    private void Run()
    {
        foreach (var (data, recvMs) in _queue.GetConsumingEnumerable())
        {
            _decoder.Decode(data, f => Frame?.Invoke(f, recvMs));
        }
    }

    private bool IsKeyframe(byte[] f)
    {
        if (_codec == VideoCodecsEnum.VP8) return f.Length > 0 && (f[0] & 0x01) == 0;
        // H.264 Annex B: an IDR slice (type 5) anywhere in the access unit.
        for (int i = 0; i + 3 < f.Length; i++)
        {
            if (f[i] == 0 && f[i + 1] == 0 && f[i + 2] == 1 && (f[i + 3] & 0x1f) == 5) return true;
        }
        return false;
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(1000);
        _decoder.Dispose();
    }
}
