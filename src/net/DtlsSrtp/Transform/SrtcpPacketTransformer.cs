using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Net
{
    public class SrtcpPacketTransformer : IDataPacketTransformer, IDisposable
    {
        private static ILogger Log = SIPSorcery.Sys.Log.Logger;

        private readonly object _lockObject = new object();
        private readonly SecureRtpTransformEngine _forwardEngine;
        private readonly SecureRtpTransformEngine _reverseEngine;
        private readonly ConcurrentDictionary<long, SrtcpCryptoContext> _contexts;
        private volatile bool _disposed;
        private TimeSpan? _contextRotationInterval;
        private DateTime _lastContextRotation;
        private bool _contextRotationEnabled;
        private readonly RTPPacket _sharedPacket = new RTPPacket();

        public SrtcpPacketTransformer(SecureRtpTransformEngine engine) : this(engine, engine) { }

        public SrtcpPacketTransformer(SecureRtpTransformEngine forwardEngine, SecureRtpTransformEngine reverseEngine, TimeSpan? contextRotationInterval = null)
        {
            _forwardEngine = forwardEngine ?? throw new ArgumentNullException(nameof(forwardEngine));
            _reverseEngine = reverseEngine ?? throw new ArgumentNullException(nameof(reverseEngine));
            _contexts = new ConcurrentDictionary<long, SrtcpCryptoContext>();
            _contextRotationInterval = contextRotationInterval;
            _contextRotationEnabled = contextRotationInterval.HasValue;
            _lastContextRotation = DateTime.UtcNow;
        }

        public byte[] EncodePacket(byte[] packet)
        {
            return EncodePacket(packet, 0, packet?.Length ?? 0);
        }

        public byte[] EncodePacket(byte[] packet, int startIndex, int count)
        {
            if (packet == null)
            {
                throw new ArgumentNullException(nameof(packet));
            }
            ValidatePacketParameters(packet, startIndex, count);

            return ProcessPacket(packet.AsSpan(startIndex, count), _forwardEngine, true);
        }

        public byte[] DecodePacket(byte[] packet)
        {
            return DecodePacket(packet, 0, packet?.Length ?? 0);
        }

        public byte[] DecodePacket(byte[] packet, int startIndex, int count)
        {
            if (packet == null)
            {
                throw new ArgumentNullException(nameof(packet));
            }
            ValidatePacketParameters(packet, startIndex, count);

            return ProcessPacket(packet.AsSpan(startIndex, count), _reverseEngine, false);
        }

        public byte[] DecodePacket(ReadOnlySpan<byte> packet, int startIndex, int count)
        {
            if (packet == null)
            {
                throw new ArgumentNullException(nameof(packet));
            }
            ValidatePacketParameters(packet, startIndex, count);

            return ProcessPacket(packet.Slice(startIndex, count), _reverseEngine, false);
        }

        public void Shutdown()
        {
            Dispose();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                lock (_lockObject)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    try
                    {
                        _sharedPacket?.Dispose();

                        _forwardEngine.CloseContexts();
                        if (_forwardEngine != _reverseEngine)
                        {
                            _reverseEngine.CloseContexts();
                        }

                        foreach (var context in _contexts.Values)
                        {
                            context.Close();
                        }
                        _contexts.Clear();
                    }
                    catch (Exception ex)
                    {
                        // Log the exception
                        Log.LogError(ex, $"Error during disposal");
                    }
                    finally
                    {
                        _disposed = true;
                    }
                }
            }

            _disposed = true;
        }

        private byte[] ProcessPacket(ReadOnlySpan<byte> pkt, SecureRtpTransformEngine engine, bool isEncode)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SrtcpPacketTransformer));
            }

            lock (_lockObject)
            {
                RotateContextsIfNeeded();
                return EncodeOrDecodePacket(pkt, engine, isEncode);
            }
        }

        private byte[] EncodeOrDecodePacket(ReadOnlySpan<byte> pkt, SecureRtpTransformEngine engine, bool isEncode)
        {
            _sharedPacket.Wrap(pkt.ToArray());
            var context = GetOrCreateContext(_sharedPacket.GetRTCPSSRC(), engine);

            if (isEncode)
            {
                context.TransformPacket(_sharedPacket);
            }
            else if (!context.ReverseTransformPacket(_sharedPacket))
            {
                return null;
            }

            return _sharedPacket.GetData();
        }

        private SrtcpCryptoContext GetOrCreateContext(long ssrc, SecureRtpTransformEngine engine)
        {
            return _contexts.GetOrAdd(ssrc, _ =>
            {
                var context = engine.GetDefaultSrtcpContext().DeriveContext(ssrc);
                context.DeriveSrtcpKeys();
                return context;
            });
        }

        /// <summary>
        /// Enables or disables context rotation and sets the rotation interval.
        /// </summary>
        /// <param name="enable">True to enable context rotation, false to disable.</param>
        /// <param name="interval">The interval at which to rotate contexts. Ignored if enable is false.</param>
        public void SetContextRotation(bool enable, TimeSpan? interval = null)
        {
            lock (_lockObject)
            {
                _contextRotationEnabled = enable;
                if (enable && interval.HasValue)
                {
                    _contextRotationInterval = interval.Value;
                }
            }
        }

        private void RotateContextsIfNeeded()
        {
            if (!_contextRotationEnabled || !_contextRotationInterval.HasValue)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (now - _lastContextRotation >= _contextRotationInterval.Value)
            {
                try
                {
                    foreach (var context in _contexts.Values)
                    {
                        context.RotateKeys();
                    }
                    _lastContextRotation = now;
                }
                catch (Exception ex)
                {
                    // Log the exception
                    Log.LogError(ex, $"Error during context rotation");
                    // Optionally, disable rotation to prevent further errors
                    // _contextRotationEnabled = false;
                }
            }
        }

        private static void ValidatePacketParameters(byte[] packet, int startIndex, int count)
        {
            if (startIndex < 0 || startIndex >= packet.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            }
            if (count < 0 || startIndex + count > packet.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
        }

        private static void ValidatePacketParameters(ReadOnlySpan<byte> packet, int startIndex, int count)
        {
            if (startIndex < 0 || startIndex >= packet.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            }
            if (count < 0 || startIndex + count > packet.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
        }
    }
}
