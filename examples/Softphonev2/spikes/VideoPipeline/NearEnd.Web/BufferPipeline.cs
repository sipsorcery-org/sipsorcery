using System.Text.Json.Nodes;
using FFmpeg.AutoGen;
using Microsoft.Web.WebView2.Core;
using VideoSpike;

namespace NearEnd.Web;

/// <summary>
/// Path 2. C# decodes each stream with FFmpeg (D3D11VA where FFmpeg supports the codec) and writes
/// NV12 into a WebView2 shared buffer per tile, which the page uploads to WebGL textures on each
/// animation frame. The camera is captured and encoded in C# too, and drawn as the self-view tile
/// through the same buffers.
///
/// Each tile's buffer is triple-buffered with no locks: C# writes the slot after the latest one,
/// then publishes it by bumping a sequence number in the header; the page reads the latest slot.
/// C# only comes back to that slot two frames later, long after the upload finished.
/// </summary>
internal sealed unsafe class BufferPipeline(MainForm form, RunConfig cfg, List<SipLeg> legs) : IPipeline
{
    public const int HeaderBytes = 4096;
    public const int Slots = 3;
    public const int SlotBytes = 1920 * 1088 * 3 / 2;

    private sealed class TileBuffer : IDisposable
    {
        public required int Index;
        public required CoreWebView2SharedBuffer Shared;
        public byte* Base;
        public int Seq;
        public int Latest = Slots - 1;
        public long Written;
        public readonly FrameWriter Writer = new();

        public void Write(AVFrame* frame, double recvMs)
        {
            int slot = (Latest + 1) % Slots;
            Writer.WriteNv12(frame, Base + HeaderBytes + (long)slot * SlotBytes);
            *(double*)(Base + 64 + slot * 16) = recvMs;
            *(int*)(Base + 8) = frame->width;
            *(int*)(Base + 12) = frame->height;
            Volatile.Write(ref *(int*)(Base + 4), slot);
            Latest = slot;
            Volatile.Write(ref *(int*)Base, ++Seq);
            Written++;
        }

        public void Dispose()
        {
            Writer.Dispose();
            Shared.Dispose();
        }
    }

    private readonly List<TileBuffer> _tiles = new();
    private readonly List<DecodePipeline> _decoders = new();
    private CameraSource? _camera;
    private CameraHub? _hub;
    private long[] _receivedAtStart = [];
    private long[] _decodedAtStart = [];
    private long _capturedAtStart, _sentAtStart;
    private long[] _encodedAtStart = [];
    private double[] _encodeMsAtStart = [];
    private long[] _bytesAtStart = [];

    public Task StartAsync()
    {
        FfSetup.Init();
        int tiles = legs.Count + (cfg.Camera == CameraKind.Off ? 0 : 1);
        for (int i = 0; i < tiles; i++)
        {
            var shared = form.Core.Environment.CreateSharedBuffer((ulong)(HeaderBytes + (long)Slots * SlotBytes));
            var tb = new TileBuffer { Index = i, Shared = shared, Base = (byte*)shared.Buffer };
            new Span<byte>(tb.Base, HeaderBytes).Clear();
            _tiles.Add(tb);
            var meta = $"{{\"tile\":{i},\"kind\":\"{(i < legs.Count ? "remote" : "self")}\",\"header\":{HeaderBytes},\"slots\":{Slots},\"slotBytes\":{SlotBytes}}}";
            form.Core.PostSharedBufferToScript(shared, CoreWebView2SharedBufferAccess.ReadOnly, meta);
        }

        foreach (var leg in legs)
        {
            var tile = _tiles[leg.Index];
            var dp = new DecodePipeline(leg.Index, cfg.Codec, cfg.HwDecode);
            dp.Frame += (f, recvMs) => tile.Write((AVFrame*)f, recvMs);
            _decoders.Add(dp);
            leg.Rtp.OnVideoFrameReceived += (_, _, frame, _) => dp.Push(frame, Clock.NowMs);
        }

        if (cfg.Camera != CameraKind.Off)
        {
            var self = _tiles[^1];
            _hub = new CameraHub(cfg, legs);
            _camera = new CameraSource(cfg.Camera);
            _camera.Frame += (f, ms) =>
            {
                self.Write((AVFrame*)f, ms);
                _hub.OnFrame(f, ms);
            };
            _camera.Start();
        }
        ResetCounters();
        return Task.CompletedTask;
    }

    public void ResetCounters()
    {
        _receivedAtStart = _decoders.Select(d => Interlocked.Read(ref d.Received)).ToArray();
        _decodedAtStart = _decoders.Select(d => d.Decoder.Decoded).ToArray();
        _capturedAtStart = _camera == null ? 0 : Interlocked.Read(ref _camera.Captured);
        _sentAtStart = _hub == null ? 0 : Interlocked.Read(ref _hub.SentToSip);
        _encodedAtStart = _hub?.Encoders.Select(e => e.Encoded).ToArray() ?? [];
        _encodeMsAtStart = _hub?.Encoders.Select(e => e.TotalEncodeMs).ToArray() ?? [];
        _bytesAtStart = _hub?.Encoders.Select(e => e.Bytes).ToArray() ?? [];
        decodeMsAtStart = _decoders.Select(d => d.Decoder.TotalDecodeMs).ToArray();
    }

    private double[] decodeMsAtStart = [];

    public void OnMessage(string type, JsonObject msg) { }

    public NearEndStats Collect(JsonObject page, double window)
    {
        var stats = new NearEndStats
        {
            LatencyMethod = "SIP receive in C# to the page's requestAnimationFrame that uploaded and drew the frame, " +
                            "plus one display refresh (16.7 ms) for the compositor to put it on screen",
        };
        var offset = form.ClockOffsetMs;

        foreach (var t in page["tiles"]!.AsArray())
        {
            var tile = t!.AsObject();
            int index = tile["tile"]!.GetValue<int>();
            var ts = new TileStats
            {
                Tile = index,
                Kind = tile["kind"]!.GetValue<string>(),
                FramesPresented = tile["presented"]!.GetValue<long>(),
                FramesDropped = tile["skipped"]!.GetValue<long>(),
                Stalls = tile["stalls"]!.GetValue<int>(),
                LongestGapMs = Math.Round(tile["longestGap"]!.GetValue<double>(), 1),
                Width = (int?)tile["width"] ?? 0,
                Height = (int?)tile["height"] ?? 0,
            };
            ts.PresentedFps = Math.Round(ts.FramesPresented / window, 1);
            ts.Latency = LatencySummary.From(tile["latency"]!.AsArray().Select(v => v!.GetValue<double>() - offset));

            if (ts.Kind == "remote" && index < _decoders.Count)
            {
                var d = _decoders[index];
                ts.FramesReceived = Interlocked.Read(ref d.Received) - _receivedAtStart[index];
                ts.FramesDecoded = d.Decoder.Decoded - _decodedAtStart[index];
                ts.FramesDropped += Math.Max(0, ts.FramesReceived - ts.FramesDecoded);
                ts.AvgDecodeMs = ts.FramesDecoded > 0 ? Math.Round((d.Decoder.TotalDecodeMs - decodeMsAtStart[index]) / ts.FramesDecoded, 2) : null;
                ts.Decoder = d.Decoder.Name;
                ts.HardwareDecode = d.Decoder.Hardware;
            }
            stats.Tiles.Add(ts);
        }

        if (_camera != null && _hub != null)
        {
            long encoded = _hub.Encoders.Select((e, i) => e.Encoded - _encodedAtStart[i]).Sum();
            double encMs = _hub.Encoders.Select((e, i) => e.TotalEncodeMs - _encodeMsAtStart[i]).Sum();
            long bytes = _hub.Encoders.Select((e, i) => e.Bytes - _bytesAtStart[i]).Sum();
            stats.Camera = new CameraStats
            {
                Source = _camera.Description,
                FramesCaptured = Interlocked.Read(ref _camera.Captured) - _capturedAtStart,
                Encoders = _hub.Encoders.Count,
                FramesEncoded = encoded,
                EncodeFpsPerEncoder = Math.Round(encoded / (double)_hub.Encoders.Count / window, 1),
                AvgEncodeMs = encoded > 0 ? Math.Round(encMs / encoded, 2) : null,
                SentKbpsPerEncoder = Math.Round(bytes * 8 / 1000.0 / _hub.Encoders.Count / window, 0),
                Encoder = _hub.Encoders[0].Name,
                HardwareEncode = _hub.Encoders[0].Hardware,
                FramesSentToSip = Interlocked.Read(ref _hub.SentToSip) - _sentAtStart,
                Width = CameraSource.Width,
                Height = CameraSource.Height,
            };
            stats.Camera.CaptureFps = Math.Round(stats.Camera.FramesCaptured / window, 1);
            stats.Camera.SentFpsPerLeg = Math.Round(stats.Camera.FramesSentToSip / (double)legs.Count / window, 1);
        }
        return stats;
    }

    public void Dispose()
    {
        _camera?.Dispose();
        foreach (var d in _decoders) d.Dispose();
        _hub?.Dispose();
        foreach (var t in _tiles) t.Dispose();
    }
}
