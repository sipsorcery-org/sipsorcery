//-----------------------------------------------------------------------------
// Filename: SrtcpGcmMultiPacketUnitTest.cs
//
// Description: Regression test for the SRTCP AES-GCM decrypt IV. UnprotectRtcp
// must derive the per-packet AES-GCM IV from the SRTCP index carried in the
// packet being decrypted, NOT from the connection's highest-seen index (S_l).
//
// This is the RTCP analogue of the concern raised against the replay-window
// refactor in PR #1675: when the replay window update is (correctly) deferred
// until after the packet authenticates, S_l no longer equals the current
// packet's index at IV-generation time. If the IV is taken from S_l it becomes
// the PREVIOUS packet's index, so every encrypted RTCP packet after the first
// decrypts with the wrong keystream and AEAD authentication fails.
//
// A single-packet round trip cannot catch this (S_l defaults to 0 and the first
// index is 0, so they coincide). This test sends a multi-packet in-order stream
// and asserts every packet round-trips and the recovered bytes match, which only
// holds when the IV tracks the per-packet index.
//
// Author(s):
// Aaron Clauson
//
// History:
// 07 Jun 2026  Aaron Clauson   Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using SIPSorcery.Net.SharpSRTP.SRTP;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class SrtcpGcmMultiPacketUnitTest
    {
        [Fact]
        public void MultiPacketRtcpStream_AllPacketsRoundTrip()
        {
            // SRTP_AEAD_AES_128_GCM - the profile WebRTC (and OpenAI's realtime endpoint) negotiates.
            var profile = new SrtpProtectionProfileConfiguration(
                SrtpCiphers.AEAD_AES_128_GCM,
                cipherKeyLength:  128,
                cipherSaltLength: 96,
                maximumLifetime:  int.MaxValue,
                auth:             SrtpAuth.NONE,
                authKeyLength:    0,
                authTagLength:    128);

            // Deterministic key + salt so the test is reproducible.
            var key  = new byte[16];
            var salt = new byte[12];
            for (int i = 0; i < key.Length;  i++) { key[i]  = (byte)(0x10 + i); }
            for (int i = 0; i < salt.Length; i++) { salt[i] = (byte)(0x80 + i); }

            var sender   = new SrtpContext(SrtpContextType.RTCP, profile, key, salt);
            var receiver = new SrtpContext(SrtpContextType.RTCP, profile, key, salt);

            const uint ssrc = 0x1234abcdu;

            // An in-order stream of several RTCP packets. On the first packet the connection's highest-seen
            // index (S_l) happens to equal the packet index (both 0), so a single packet would pass even with
            // an IV taken from S_l. From the second packet on, the IV must come from the packet's own index;
            // taking it from S_l (the previous packet's index) makes the GCM authentication fail.
            for (int i = 0; i < 5; i++)
            {
                byte marker = (byte)(0xA0 + i); // distinct payload per packet so we verify the decrypt, not just the return code.
                byte[] rtcp = MakeRtcpPacket(ssrc, marker);

                int extra = sender.CalculateRequiredSrtcpPayloadLength(rtcp.Length) - rtcp.Length;
                var encrypted = new byte[rtcp.Length + extra];

                int encRc = ProtectRtcp(sender, rtcp, encrypted, out int encLen);
                Assert.True(encRc == 0, $"ProtectRtcp failed for packet {i} (rc={encRc}).");

                var encryptedTrimmed = new byte[encLen];
                Buffer.BlockCopy(encrypted, 0, encryptedTrimmed, 0, encLen);

                var decrypted = new byte[encLen];
                int decRc = UnprotectRtcp(receiver, encryptedTrimmed, decrypted, out int decLen);

                Assert.True(decRc == 0,
                    $"UnprotectRtcp failed for in-order packet {i} (rc={decRc}). A non-zero result here means the " +
                    $"AES-GCM IV did not track the packet's own SRTCP index (e.g. it was taken from S_l, the " +
                    $"previous packet's index).");

                Assert.Equal(rtcp.Length, decLen);
                Assert.Equal(rtcp, AsArray(decrypted, decLen));
            }
        }

        // ---- helpers ----

        private static byte[] MakeRtcpPacket(uint ssrc, byte marker)
        {
            // Minimal well-formed RTCP Receiver Report: 8 byte header (V=2,P=0,RC=0,PT=201,length,SSRC) plus
            // an 8 byte body. Bytes after the first two 32-bit words are the part SRTCP encrypts, so the
            // varying marker lives there to prove the payload was correctly recovered. Length is in 32-bit
            // words minus one: 16 bytes => 4 words => 3.
            var pkt = new byte[16];
            pkt[0]  = 0x80;                         // V=2, P=0, RC=0
            pkt[1]  = 201;                          // PT = Receiver Report
            pkt[2]  = 0x00;
            pkt[3]  = 0x03;                         // length = (16/4) - 1
            pkt[4]  = (byte)((ssrc >> 24) & 0xFF);
            pkt[5]  = (byte)((ssrc >> 16) & 0xFF);
            pkt[6]  = (byte)((ssrc >>  8) & 0xFF);
            pkt[7]  = (byte)( ssrc        & 0xFF);
            for (int i = 8; i < pkt.Length; i++) { pkt[i] = marker; }
            return pkt;
        }

        private static byte[] AsArray(byte[] buffer, int length)
        {
            var trimmed = new byte[length];
            Buffer.BlockCopy(buffer, 0, trimmed, 0, length);
            return trimmed;
        }

#if NET8_0_OR_GREATER
        private static int ProtectRtcp(SrtpContext ctx, byte[] input, byte[] output, out int outLen)
            => ctx.ProtectRtcp(input.AsSpan(), output.AsSpan(), out outLen);

        private static int UnprotectRtcp(SrtpContext ctx, byte[] input, byte[] output, out int outLen)
            => ctx.UnprotectRtcp(input.AsSpan(), output.AsSpan(), out outLen);
#else
        private static int ProtectRtcp(SrtpContext ctx, byte[] input, byte[] output, out int outLen)
            => ctx.ProtectRtcp(new ArraySegment<byte>(input), output, out outLen);

        private static int UnprotectRtcp(SrtpContext ctx, byte[] input, byte[] output, out int outLen)
            => ctx.UnprotectRtcp(new ArraySegment<byte>(input), output, out outLen);
#endif
    }
}
