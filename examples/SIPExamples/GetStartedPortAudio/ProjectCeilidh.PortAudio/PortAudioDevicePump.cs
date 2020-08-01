using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ProjectCeilidh.PortAudio.Native;

namespace ProjectCeilidh.PortAudio
{
    /// <summary>
    /// A PortAudio device stream driven by callbacks, rather than blocking read/write calls.
    /// </summary>
    /// <inheritdoc />
    public class PortAudioDevicePump : IDisposable
    {
        private const int BUFFER_CHAIN_LENGTH = 5;
        private const ulong FRAMES_TO_BUFFER = 256;

        public delegate int ReadDataCallback(byte[] buffer, int offset, int count);
        public delegate void WriteDataCallback(byte[] buffer, int offset, int count);

        private static readonly PaStreamFinishedCallback StreamFinishedCallback = OnStreamFinished;
        private static readonly PaStreamCallback StreamCallback = OnStreamData;

        /// <summary>
        /// The sample format the pump was opened with.
        /// </summary>
        public PortAudioSampleFormat SampleFormat { get; }
        /// <summary>
        /// The number of channels the pump was opened with
        /// </summary>
        public int Channels { get; }
        /// <summary>
        /// The sample rate the pump was opened with
        /// </summary>
        public double SampleRate { get; }
        /// <summary>
        /// The suggested latency the pump was opened with
        /// </summary>
        public TimeSpan SuggestedLatency { get; }

        private GCHandle _handle;
        private readonly bool _isOutput;
        private readonly ReadDataCallback _readDataCallback;
        private readonly WriteDataCallback _writeDataCallback;

        private SemaphoreSlim _queueCount;
        private SemaphoreSlim _poolCount;
        private readonly ConcurrentQueue<BufferContainer> _dataQueue;
        private readonly ConcurrentBag<BufferContainer> _bufferPool;
        private CancellationTokenSource _processingThreadCancel;
        private readonly ManualResetEventSlim _threadEndEvent;
        // private Thread _dataThread;
        // private readonly ManualResetEventSlim _requestThreadTermination;

        private readonly PaStream _stream;

        /// <summary>
        /// Create a PortAudioDevicePump for an output device
        /// Note: This MUST be disposed, or you risk breaking audio on the host until the next reboot
        /// </summary>
        /// <param name="device">The output device to create a pump for</param>
        /// <param name="channelCount">The number of channels in the input PCM data</param>
        /// <param name="sampleFormat">The sample format of the input PCM data</param>
        /// <param name="suggestedLatency">The latency the device will attempt to achieve</param>
        /// <param name="sampleRate">The sample rate of playback</param>
        /// <param name="callback">The callback that will supply data to the output device</param>
        /// <exception cref="ArgumentNullException">Thrown in the case that the device or callback are null</exception>
        public unsafe PortAudioDevicePump(PortAudioDevice device, int channelCount, PortAudioSampleFormat sampleFormat,
            TimeSpan suggestedLatency, double sampleRate, ReadDataCallback callback)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            PortAudioLifetimeRegistry.Register(this);

            SampleFormat = sampleFormat;
            Channels = channelCount;
            SampleRate = sampleRate;
            SuggestedLatency = suggestedLatency;

            _handle = GCHandle.Alloc(this);
            _isOutput = true;
            _readDataCallback = callback;

            var outputParams = new PaStreamParameters
            {
                ChannelCount = channelCount,
                DeviceIndex = device.DeviceIndex,
                HostApiSpecificStreamInfo = IntPtr.Zero,
                SampleFormats = sampleFormat.SampleFormat,
                SuggestedLatency = new PaTime(suggestedLatency)
            };

            var err = Native.PortAudio.Pa_OpenStream(out _stream, null, &outputParams, sampleRate, FRAMES_TO_BUFFER, PaStreamFlags.NoFlag,
                StreamCallback, GCHandle.ToIntPtr(_handle));
            if (err < PaErrorCode.NoError) throw PortAudioException.GetException(err);

            err = Native.PortAudio.Pa_SetStreamFinishedCallback(_stream, StreamFinishedCallback);
            if (err < PaErrorCode.NoError) throw PortAudioException.GetException(err);

            _dataQueue = new ConcurrentQueue<BufferContainer>();
            _bufferPool = new ConcurrentBag<BufferContainer>();

            for (var i = 0; i < BUFFER_CHAIN_LENGTH; i++)
            {
                var buffer = new byte[FRAMES_TO_BUFFER * (ulong) channelCount * (ulong) sampleFormat.FormatSize];
                var readLength = WriteAudioFrame(buffer, buffer.Length);
                _dataQueue.Enqueue(new BufferContainer(buffer)
                {
                    ReadLength = readLength
                });
            }

            _queueCount = new SemaphoreSlim(BUFFER_CHAIN_LENGTH);
            _poolCount = new SemaphoreSlim(0);
            _threadEndEvent = new ManualResetEventSlim(false);
            _processingThreadCancel = new CancellationTokenSource();

            Task.Run(() => DataTask(_processingThreadCancel.Token));
        }

        private async Task DataTask(CancellationToken token)
        {
            while (await _poolCount.WaitAsyncCancellable(token))
            {
                BufferContainer result;
                while (!_bufferPool.TryTake(out result)) { }

                result.ReadLength = WriteAudioFrame(result.Buffer, result.ReadLength);
                _dataQueue.Enqueue(result);
                _queueCount.Release();
            }

            Debug.WriteLine("Data task is terminating...");

            _threadEndEvent.Set();
        }

        /// <summary>
        /// Create a PortAudioDevicePump for an input device
        /// Note: This MUST be disposed, or you risk breaking audio on the host until the next reboot
        /// </summary>
        /// <param name="device">The input device to create a pump for</param>
        /// <param name="channelCount">The number of channels to capture from the input device</param>
        /// <param name="sampleFormat">The sample format of the output PCM data</param>
        /// <param name="suggestedLatency">The latency the device will attempt to achieve</param>
        /// <param name="sampleRate">The sample rate of the output PCM data</param>
        /// <param name="callback">The callback that will be invoked when data is produced</param>
        /// <exception cref="ArgumentNullException">Thrown in the case that the device or callback are null</exception>
        public unsafe PortAudioDevicePump(PortAudioDevice device, int channelCount, PortAudioSampleFormat sampleFormat,
            TimeSpan suggestedLatency, double sampleRate, WriteDataCallback callback)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            PortAudioLifetimeRegistry.Register(this);

            SampleFormat = sampleFormat;
            Channels = channelCount;
            SampleRate = sampleRate;
            SuggestedLatency = suggestedLatency;

            _handle = GCHandle.Alloc(this);
            _isOutput = false;
            _writeDataCallback = callback;

            var inputParams = new PaStreamParameters
            {
                ChannelCount = channelCount,
                DeviceIndex = device.DeviceIndex,
                HostApiSpecificStreamInfo = IntPtr.Zero,
                SampleFormats = sampleFormat.SampleFormat,
                SuggestedLatency = new PaTime(suggestedLatency)
            };

            var err = Native.PortAudio.Pa_OpenStream(out _stream, &inputParams, null, sampleRate, FRAMES_TO_BUFFER, PaStreamFlags.NoFlag,
                StreamCallback, GCHandle.ToIntPtr(_handle));
            if (err < PaErrorCode.NoError) throw PortAudioException.GetException(err);

            err = Native.PortAudio.Pa_SetStreamFinishedCallback(_stream, StreamFinishedCallback);
            if (err < PaErrorCode.NoError) throw PortAudioException.GetException(err);
        }

        /// <summary>
        /// Start playback.
        /// </summary>
        public void Start()
        {
            var err = Native.PortAudio.Pa_StartStream(_stream);
            if (err < PaErrorCode.NoError) throw PortAudioException.GetException(err);
        }

        /// <summary>
        /// Stop playback, waiting for existing data to drain.
        /// </summary>
        public void Stop()
        {
            var err = Native.PortAudio.Pa_StopStream(_stream);
            if (err < PaErrorCode.NoError) throw PortAudioException.GetException(err);
        }

        /// <summary>
        /// Stop playback without waiting for existing data to drain.
        /// </summary>
        public void Abort()
        {
            var err = Native.PortAudio.Pa_AbortStream(_stream);
            if (err < PaErrorCode.NoError) throw PortAudioException.GetException(err);
        }

        /// <summary>
        /// Stop all buffering and clear the current buffer status.
        /// </summary>
        public void ClearBuffers()
        {
            if (Native.PortAudio.Pa_IsStreamActive(_stream) != PaErrorCode.NoError) throw new InvalidOperationException();

            _processingThreadCancel.Cancel();
            _threadEndEvent.Wait();
            _threadEndEvent.Reset();
            _processingThreadCancel.Dispose();
            _poolCount.Dispose();
            _queueCount.Dispose();

            // Empty the queue and buffers
            while (_bufferPool.Count > 0)
                while (!_bufferPool.TryTake(out _)) { }

            while (_dataQueue.Count > 0)
                while (!_dataQueue.TryDequeue(out _)) { }
        }

        /// <summary>
        /// Restart processing after a clear buffers operation.
        /// </summary>
        public void RestartAfterClear()
        {
            for (var i = 0; i < BUFFER_CHAIN_LENGTH; i++)
            {
                var buffer = new byte[FRAMES_TO_BUFFER * (ulong)Channels * (ulong)SampleFormat.FormatSize];
                var readLength = WriteAudioFrame(buffer, buffer.Length);
                _dataQueue.Enqueue(new BufferContainer(buffer)
                {
                    ReadLength = readLength
                });
            }

            _queueCount = new SemaphoreSlim(BUFFER_CHAIN_LENGTH);
            _poolCount = new SemaphoreSlim(0);
            _processingThreadCancel = new CancellationTokenSource();

            Task.Run(() => DataTask(_processingThreadCancel.Token));

            Start();
        }

        /// <summary>
        /// Emitted when the stream finishes processing. This is invoked when <see cref="Stop"/> or <see cref="Abort"/> are called.
        /// </summary>
        public event StreamFinishedEventHandler StreamFinished;

        private int WriteAudioFrame(byte[] buffer, int count)
        {
            var offset = 0;
            var len = 0;
            while (offset < count && (len = _readDataCallback(buffer, offset, count - len)) > 0)
                offset += len;

            return len;
        }

        private void ReleaseUnmanagedResources()
        {
            Native.PortAudio.Pa_CloseStream(_stream);
            PortAudioLifetimeRegistry.UnRegister(this);
        }

        private void Dispose(bool disposing)
        {
            ReleaseUnmanagedResources();
            if (!disposing) return;

            _processingThreadCancel?.Cancel();
            _threadEndEvent?.Wait();
            _threadEndEvent?.Dispose();
            _processingThreadCancel?.Dispose();
            _queueCount?.Dispose();
            _poolCount?.Dispose();

            if (_handle.IsAllocated) _handle.Free();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~PortAudioDevicePump()
        {
            Dispose(false);
        }

        private static void OnStreamFinished(IntPtr userData)
        {
            var handle = GCHandle.FromIntPtr(userData);

            if (!handle.IsAllocated || !(handle.Target is PortAudioDevicePump pump)) return;

            pump.StreamFinished?.Invoke(pump, EventArgs.Empty);
        }

        private static unsafe PaStreamCallbackResult OnStreamData(IntPtr input, IntPtr output, ulong frameCount, in PaStreamCallbackTimeInfo timeInfo, PaStreamCallbackFlags statusFlags, IntPtr userData)
        {
            var handle = GCHandle.FromIntPtr(userData);

            if (!handle.IsAllocated || !(handle.Target is PortAudioDevicePump pump)) return PaStreamCallbackResult.Abort;

            if (pump._isOutput)
            {
                pump._queueCount.Wait();

                BufferContainer result;
                while (!pump._dataQueue.TryDequeue(out result)) { }

                var audioBufLen = (long) frameCount * pump.Channels * pump.SampleFormat.FormatSize;

                Marshal.Copy(result.Buffer, 0, output, Math.Min(result.ReadLength, (int)frameCount * pump.Channels * pump.SampleFormat.FormatSize));

                if (result.ReadLength < audioBufLen)
                {
                    var audioBufPtr = (byte*) (output + result.ReadLength);
                    if (audioBufPtr == null) return PaStreamCallbackResult.Abort; // This should never happen

                    for (var i = 0; i < audioBufLen - result.ReadLength; i++)
                        audioBufPtr[i] = 0;
                }

                var res = result.ReadLength <= 0 ? PaStreamCallbackResult.Complete : PaStreamCallbackResult.Continue;

                pump._bufferPool.Add(result);
                pump._poolCount.Release();

                if (statusFlags.HasFlag(PaStreamCallbackFlags.OutputUnderflow)) // The ammount of buffering is too little for this stream, increase the buffer count
                {
                    Debug.WriteLine("Output underflow, increasing the buffer chain length");

                    pump._bufferPool.Add(new BufferContainer(new byte[FRAMES_TO_BUFFER * (ulong)pump.Channels * (ulong)pump.SampleFormat.FormatSize]));
                    pump._poolCount.Release();
                }

                return res;
            }

            var buf = new byte[frameCount * (ulong)pump.SampleFormat.FormatSize * (ulong)pump.Channels];

            Marshal.Copy(input, buf, 0, buf.Length);

            pump._writeDataCallback(buf, 0, buf.Length);

            return PaStreamCallbackResult.Continue;
        }

        private class BufferContainer
        {
            public int ReadLength { get; set; }

            public byte[] Buffer { get; }

            public BufferContainer(byte[] buffer) => Buffer = buffer;
        }
    }
}
