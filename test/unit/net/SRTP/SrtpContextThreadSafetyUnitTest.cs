//-----------------------------------------------------------------------------
// Filename: SrtpContextThreadSafetyUnitTest.cs
//
// Description: Regression tests for an intermittent
//
//   System.IndexOutOfRangeException
//      at Org.BouncyCastle.Crypto.Digests.GeneralDigest.BlockUpdate(ReadOnlySpan)
//      at ...SRTP.Authentication.HMAC.GenerateAuthTag(...)
//      at ...SRTP.SrtpContext.UnprotectRtcp(...)   (also reachable via ProtectRtp)
//
// observed after a WebRTC peer connection had been streaming for an extended
// period.
//
// Root cause: a SrtpContext carries shared, non-thread-safe mutable state used
// by every protect/unprotect call - the BouncyCastle HMac, the cached cipher
// engines (PayloadCTR/HeaderCTR/...), the rollover counter and the
// replay-protection state. A single SrtpContext can be driven from more than
// one thread concurrently: with audio + video bundled on one DTLS-SRTP
// transport they share a single EncodeRtpContext and are protected from
// separate send timer threads. Concurrent BlockUpdate calls corrupt the
// digest's internal block-buffer offset (which can only exceed its bounds via
// interleaved writes - single-threaded use, partial state, or malformed input
// cannot produce the IndexOutOfRangeException above), and the cipher engines
// can be corrupted into silently producing a bad keystream.
//
// Fix: SrtpContext serialises its four public operations on a per-context lock.
// Both tests assert the correct (thread-safe) behaviour: they FAIL on the
// unfixed code (reproducing the crash) and PASS once the lock is in place.
//
//   - ProtectRtp test  : the send-side trigger (audio + video share EncodeRtpContext).
//   - UnprotectRtcp test: the exact path from the reported stack (RTCP decode).
//
// Author(s):
// Claude Opus 4.8 (Anthropic AI assistant, model: claude-opus-4-8), commissioned by Aaron Clauson
//
// History:
// 21 Jun 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Threading;
using SIPSorcery.Net.SharpSRTP.SRTP;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class SrtpContextThreadSafetyUnitTest
    {
        private static SrtpProtectionProfileConfiguration Aes128CmHmacSha1_80() =>
            new SrtpProtectionProfileConfiguration(
                SrtpCiphers.AES_128_CM,
                cipherKeyLength:  128,
                cipherSaltLength: 112,
                maximumLifetime:  int.MaxValue,
                auth:             SrtpAuth.HMAC_SHA1,
                authKeyLength:    160,
                authTagLength:    80);

        private static (byte[] key, byte[] salt) DeterministicKey()
        {
            var key = new byte[16];
            var salt = new byte[14];
            for (int i = 0; i < key.Length;  i++) { key[i]  = (byte)(0x10 + i); }
            for (int i = 0; i < salt.Length; i++) { salt[i] = (byte)(0x80 + i); }
            return (key, salt);
        }

        /// <summary>
        /// Reproduces the send-side trigger: with audio + video bundled on one
        /// DTLS-SRTP transport they share a single EncodeRtpContext, and frames are
        /// protected from separate send threads. Two threads (two SSRCs) call
        /// ProtectRtp on the same SrtpContext. Asserts no thread throws.
        /// </summary>
        [Fact]
        public void ProtectRtp_ConcurrentSendersOnSharedContext_DoesNotThrow()
        {
            var (key, salt) = DeterministicKey();
            var sender = new SrtpContext(SrtpContextType.RTP, Aes128CmHmacSha1_80(), key, salt);

            const uint audioSsrc = 0x0A0A0A0Au;
            const uint videoSsrc = 0x0B0B0B0Bu;
            const int iterations = 50_000;

            var failures = new ConcurrentBag<string>();
            var stop = 0;

            Thread MakeSender(uint ssrc) => new Thread(() =>
            {
                for (int i = 0; i < iterations && Volatile.Read(ref stop) == 0; i++)
                {
                    try
                    {
                        var rtp = MakeRtpPacket(ssrc, (ushort)(i & 0xFFFF), payloadType: 96, payloadLen: 160);
                        var output = new byte[sender.CalculateRequiredSrtpPayloadLength(rtp.Length)];
                        ProtectRtp(sender, rtp, output, out _);
                    }
                    catch (Exception excp)
                    {
                        failures.Add($"{excp.GetType().Name}: {excp.Message}");
                        Interlocked.Exchange(ref stop, 1);
                        return;
                    }
                }
            });

            var audio = MakeSender(audioSsrc);
            var video = MakeSender(videoSsrc);

            audio.Start();
            video.Start();
            audio.Join();
            video.Join();

            Assert.True(failures.IsEmpty,
                "SrtpContext.ProtectRtp is not thread-safe: audio + video sharing one EncodeRtpContext " +
                $"corrupts the shared HMAC/cipher state. First failure: {FirstOrNone(failures)}");
        }

        /// <summary>
        /// Reproduces the exact path from the reported stack: concurrent
        /// SrtpContext.UnprotectRtcp on a single shared decode context. A pool of
        /// distinct, validly-protected SRTCP packets is generated up front, then
        /// unprotected from several threads at once. Asserts no thread throws (the
        /// bug manifested as an IndexOutOfRangeException inside the auth HMAC).
        /// </summary>
        [Fact]
        public void UnprotectRtcp_ConcurrentCallersOnSharedContext_DoesNotThrow()
        {
            var (key, salt) = DeterministicKey();
            var sender   = new SrtpContext(SrtpContextType.RTCP, Aes128CmHmacSha1_80(), key, salt);
            var receiver = new SrtpContext(SrtpContextType.RTCP, Aes128CmHmacSha1_80(), key, salt);

            const uint ssrc = 0x0C0C0C0Cu;
            const int packetCount = 20_000;

            // Pre-generate distinct protected SRTCP packets (sender increments the
            // SRTCP index each call), so the concurrent unprotect below is exercising
            // the decode path, not replayed indices.
            var protectedPackets = new ConcurrentQueue<byte[]>();
            for (int i = 0; i < packetCount; i++)
            {
                var rtcp = MakeReceiverReport(ssrc, (uint)i);
                var output = new byte[sender.CalculateRequiredSrtcpPayloadLength(rtcp.Length)];
                int rc = ProtectRtcp(sender, rtcp, output, out int protectedLen);
                Assert.Equal(0, rc);

                var packet = new byte[protectedLen];
                Buffer.BlockCopy(output, 0, packet, 0, protectedLen);
                protectedPackets.Enqueue(packet);
            }

            var failures = new ConcurrentBag<string>();
            int threadCount = Math.Max(2, Environment.ProcessorCount);
            var threads = new Thread[threadCount];

            for (int t = 0; t < threadCount; t++)
            {
                threads[t] = new Thread(() =>
                {
                    while (protectedPackets.TryDequeue(out var packet))
                    {
                        try
                        {
                            var decoded = new byte[packet.Length];
                            // Return code may legitimately be a replay failure due to the
                            // non-deterministic cross-thread ordering; the bug we are guarding
                            // against is a thrown exception / corruption, so only catch throws.
                            UnprotectRtcp(receiver, packet, decoded, out _);
                        }
                        catch (Exception excp)
                        {
                            failures.Add($"{excp.GetType().Name}: {excp.Message}");
                            return;
                        }
                    }
                });
            }

            foreach (var thread in threads) { thread.Start(); }
            foreach (var thread in threads) { thread.Join(); }

            Assert.True(failures.IsEmpty,
                "SrtpContext.UnprotectRtcp is not thread-safe: concurrent callers on one DecodeRtcpContext " +
                $"corrupt the shared auth HMAC. First failure: {FirstOrNone(failures)}");
        }

        // ---- helpers ----

        private static string FirstOrNone(ConcurrentBag<string> bag) => bag.TryPeek(out var first) ? first : "(none)";

        private static byte[] MakeRtpPacket(uint ssrc, ushort seq, byte payloadType, int payloadLen)
        {
            var pkt = new byte[12 + payloadLen];
            pkt[0]  = 0x80;
            pkt[1]  = payloadType;
            pkt[2]  = (byte)(seq >> 8);
            pkt[3]  = (byte)(seq & 0xFF);
            pkt[8]  = (byte)((ssrc >> 24) & 0xFF);
            pkt[9]  = (byte)((ssrc >> 16) & 0xFF);
            pkt[10] = (byte)((ssrc >>  8) & 0xFF);
            pkt[11] = (byte)( ssrc        & 0xFF);
            return pkt;
        }

        // Minimal RTCP Receiver Report: V=2, P=0, RC=0, PT=201, length=1 (8 bytes),
        // followed by the packet-sender SSRC. The timestamp slot is reused as a
        // per-packet marker so each generated packet is distinct.
        private static byte[] MakeReceiverReport(uint ssrc, uint marker)
        {
            var pkt = new byte[8];
            pkt[0] = 0x80;
            pkt[1] = 201;
            pkt[2] = 0x00;
            pkt[3] = 0x01;
            pkt[4] = (byte)(((ssrc ^ marker) >> 24) & 0xFF);
            pkt[5] = (byte)(((ssrc ^ marker) >> 16) & 0xFF);
            pkt[6] = (byte)(((ssrc ^ marker) >>  8) & 0xFF);
            pkt[7] = (byte)( (ssrc ^ marker)        & 0xFF);
            return pkt;
        }

#if NET8_0_OR_GREATER
        private static int ProtectRtp(SrtpContext ctx, byte[] input, byte[] output, out int outLen)
            => ctx.ProtectRtp(input.AsSpan(), output.AsSpan(), out outLen);

        private static int ProtectRtcp(SrtpContext ctx, byte[] input, byte[] output, out int outLen)
            => ctx.ProtectRtcp(input.AsSpan(), output.AsSpan(), out outLen);

        private static int UnprotectRtcp(SrtpContext ctx, byte[] input, byte[] output, out int outLen)
            => ctx.UnprotectRtcp(input.AsSpan(), output.AsSpan(), out outLen);
#else
        private static int ProtectRtp(SrtpContext ctx, byte[] input, byte[] output, out int outLen)
            => ctx.ProtectRtp(new ArraySegment<byte>(input), output, out outLen);

        private static int ProtectRtcp(SrtpContext ctx, byte[] input, byte[] output, out int outLen)
            => ctx.ProtectRtcp(new ArraySegment<byte>(input), output, out outLen);

        private static int UnprotectRtcp(SrtpContext ctx, byte[] input, byte[] output, out int outLen)
            => ctx.UnprotectRtcp(new ArraySegment<byte>(input), output, out outLen);
#endif
    }
}
