//-----------------------------------------------------------------------------
// Filename: SyntheticH264Source.cs
//
// Description: Synthetic H.264 Annex-B video source for standalone testing
// and demonstration without external camera or native transcoding dependencies.
//
// Author(s):
// SIPSorcery contributors
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WebRtcVideoFecDemo
{
    /// <summary>
    /// Generates periodic H.264 Annex B access units for standalone WebRTC transmission.
    /// Access units contain SPS, PPS, IDR keyframes, and inter slices.
    /// </summary>
    public class SyntheticH264Source : IDisposable
    {
        private static readonly byte[] AnnexBStartCode = new byte[] { 0x00, 0x00, 0x00, 0x01 };

        // Valid Baseline profile (profile-level-id=42e01f) SPS for 320x240 video
        private static readonly byte[] SpsNal = new byte[]
        {
            0x67, 0x42, 0xE0, 0x1F, 0xDA, 0x01, 0x40, 0x16,
            0xE8, 0x06, 0xD0, 0xA1, 0x35
        };

        // Matching PPS (CAVLC)
        private static readonly byte[] PpsNal = new byte[]
        {
            0x68, 0xCE, 0x3C, 0x80
        };

        private readonly int _fps;
        private readonly int _frameDurationRtpUnits;
        private readonly Timer _frameTimer;
        private uint _frameNumber = 0;
        private bool _disposed = false;

        public event Action<uint, byte[]>? OnFrameReady;

        public SyntheticH264Source(int fps = 30)
        {
            _fps = Math.Max(1, fps);
            _frameDurationRtpUnits = 90000 / _fps;
            _frameTimer = new Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Start()
        {
            int intervalMs = 1000 / _fps;
            _frameTimer.Change(0, intervalMs);
        }

        public void Stop()
        {
            _frameTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void OnTimerTick(object? state)
        {
            if (_disposed) return;

            uint currentFrame = Interlocked.Increment(ref _frameNumber);
            bool isKeyFrame = (currentFrame % 30) == 1; // Keyframe every 1 second

            byte[] accessUnit = GenerateAccessUnit(isKeyFrame, currentFrame);
            OnFrameReady?.Invoke((uint)_frameDurationRtpUnits, accessUnit);
        }

        /// <summary>
        /// Assembles an Annex B access unit.
        /// Large IDR frames generate multiple packets on RTP packetization to exercise FU-A and FEC.
        /// </summary>
        public static byte[] GenerateAccessUnit(bool isKeyFrame, uint frameIndex)
        {
            var au = new List<byte>();

            if (isKeyFrame)
            {
                // Prefix IDR with SPS and PPS
                au.AddRange(AnnexBStartCode);
                au.AddRange(SpsNal);

                au.AddRange(AnnexBStartCode);
                au.AddRange(PpsNal);

                // IDR Slice NAL (Type 5). Sized ~2500 bytes to force FU-A fragmentation across 3 RTP packets.
                int idrPayloadSize = 2500;
                byte[] idrNal = new byte[idrPayloadSize];
                idrNal[0] = 0x65; // NAL Header: NRI=3, Type=5 (IDR)
                idrNal[1] = 0x88; // Slice header
                for (int i = 2; i < idrPayloadSize; i++)
                {
                    idrNal[i] = (byte)((frameIndex * 13 + i) & 0xFF);
                }

                au.AddRange(AnnexBStartCode);
                au.AddRange(idrNal);
            }
            else
            {
                // Non-IDR Slice NAL (Type 1). Sized ~800 bytes (fits in single RTP packet).
                int interPayloadSize = 800;
                byte[] interNal = new byte[interPayloadSize];
                interNal[0] = 0x41; // NAL Header: NRI=2, Type=1 (Non-IDR)
                interNal[1] = 0x80; // Slice header
                for (int i = 2; i < interPayloadSize; i++)
                {
                    interNal[i] = (byte)((frameIndex * 7 + i) & 0xFF);
                }

                au.AddRange(AnnexBStartCode);
                au.AddRange(interNal);
            }

            return au.ToArray();
        }

        public void Dispose()
        {
            _disposed = true;
            _frameTimer.Dispose();
        }
    }
}
