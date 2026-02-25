//-----------------------------------------------------------------------------
// Filename: SrtpReorderedPacketUnitTest.cs
//
// Description: Regression tests for the RFC 3711 Appendix A packet index
// estimation in SrtpContext.DetermineRtpIndex. The original implementation
// performed the "SEQ - s_l > 32768" comparison with unsigned arithmetic, so
// for any REORDERED packet (SEQ slightly below the highest received sequence
// number while s_l < 32768) the subtraction wrapped to a huge value and the
// packet was assigned ROC-1 instead of ROC. The wrong rollover counter
// corrupts the HMAC input (and the keystream IV), so legitimate out-of-order
// packets failed authentication with ERROR_HMAC_CHECK_FAILED (-3) and were
// dropped. Observed in the wild as sporadic "SRTP unprotect failed for
// video, result -3" on internet paths with mild UDP reordering.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 12 Jun 2026  Aaron Clauson   Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using SIPSorcery.Net.SharpSRTP.SRTP;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class SrtpReorderedPacketUnitTest
    {
        /// <summary>
        /// The index estimation cases from RFC 3711 Appendix A. The reordered-packet rows are the
        /// regression cases: under unsigned arithmetic they selected ROC-1.
        /// </summary>
        [Theory]
        // Reordered packet, no wrap in play: must stay on the current ROC.
        [InlineData(100u, (ushort)95, 0ul, 95u)]
        [InlineData(100u, (ushort)95, 5ul, (5u << 16) | 95u)]
        [InlineData(1u, (ushort)65535, 1ul, 65535u)]            // straggler from before the wrap -> ROC-1.
        // In order packets.
        [InlineData(100u, (ushort)105, 0ul, 105u)]
        [InlineData(40000u, (ushort)40010, 3ul, (3u << 16) | 40010u)]
        // Reordered packet in the upper half: was already handled correctly.
        [InlineData(40000u, (ushort)39995, 2ul, (2u << 16) | 39995u)]
        // Receiver sees the wrap: s_l just before the wrap, new packet just after -> ROC+1.
        [InlineData(65000u, (ushort)5, 0ul, (1u << 16) | 5u)]
        public void DetermineRtpIndexEstimatesRocPerRfc(uint s_l, ushort seq, ulong roc, uint expectedIndex)
        {
            Assert.Equal(expectedIndex, SrtpContext.DetermineRtpIndex(s_l, seq, roc));
        }

        /// <summary>
        /// End to end regression: packets protected in order 100, 101, 102 but delivered in order
        /// 100, 102, 101 must ALL decrypt. Under the unsigned arithmetic bug the late packet (101
        /// arriving after 102) was assigned ROC-1 and failed authentication with -3.
        /// </summary>
        [Fact]
        public void ReorderedPacketWithinWindowDecrypts()
        {
            var profile = new SrtpProtectionProfileConfiguration(
                SrtpCiphers.AES_128_CM,
                cipherKeyLength: 128,
                cipherSaltLength: 112,
                maximumLifetime: int.MaxValue,
                auth: SrtpAuth.HMAC_SHA1,
                authKeyLength: 160,
                authTagLength: 80);

            var key = new byte[16];
            var salt = new byte[14];
            for (int i = 0; i < key.Length; i++) { key[i] = (byte)(0x10 + i); }
            for (int i = 0; i < salt.Length; i++) { salt[i] = (byte)(0x80 + i); }

            var sender = new SrtpContext(SrtpContextType.RTP, profile, key, salt);
            var receiver = new SrtpContext(SrtpContextType.RTP, profile, key, salt);

            const uint ssrc = 0x33333333u;

            // Protect in transmission order.
            byte[] enc100 = ProtectOnePacket(sender, ssrc, 100);
            byte[] enc101 = ProtectOnePacket(sender, ssrc, 101);
            byte[] enc102 = ProtectOnePacket(sender, ssrc, 102);

            // Deliver out of order: 100, 102, then the late 101.
            Assert.Equal(0, UnprotectOnePacket(receiver, enc100));
            Assert.Equal(0, UnprotectOnePacket(receiver, enc102));

            int lateResult = UnprotectOnePacket(receiver, enc101);
            Assert.True(lateResult == 0,
                $"A reordered packet within the replay window failed to decrypt (rc={lateResult}, " +
                "-3 = ERROR_HMAC_CHECK_FAILED). DetermineRtpIndex must use signed arithmetic so a " +
                "reordered packet keeps the current ROC rather than being assigned ROC-1.");
        }

        /// <summary>
        /// A genuine duplicate must still be rejected by the replay check, proving the reordering
        /// fix has not weakened replay protection.
        /// </summary>
        [Fact]
        public void DuplicatePacketIsStillRejected()
        {
            var profile = new SrtpProtectionProfileConfiguration(
                SrtpCiphers.AES_128_CM,
                cipherKeyLength: 128,
                cipherSaltLength: 112,
                maximumLifetime: int.MaxValue,
                auth: SrtpAuth.HMAC_SHA1,
                authKeyLength: 160,
                authTagLength: 80);

            var key = new byte[16];
            var salt = new byte[14];
            for (int i = 0; i < key.Length; i++) { key[i] = (byte)(0x10 + i); }
            for (int i = 0; i < salt.Length; i++) { salt[i] = (byte)(0x80 + i); }

            var sender = new SrtpContext(SrtpContextType.RTP, profile, key, salt);
            var receiver = new SrtpContext(SrtpContextType.RTP, profile, key, salt);

            byte[] enc = ProtectOnePacket(sender, 0x44444444u, 200);

            Assert.Equal(0, UnprotectOnePacket(receiver, enc));
            Assert.Equal(SrtpContext.ERROR_REPLAY_CHECK_FAILED, UnprotectOnePacket(receiver, enc));
        }

        // ---- helpers (same packet construction as SrtpContextRolloverUnitTest) ----

        private static byte[] ProtectOnePacket(SrtpContext sender, uint ssrc, ushort seq)
        {
            var rtpPacket = MakeRtpPacket(ssrc, seq, payloadType: 96, payload: new byte[16]);
            int extra = sender.CalculateRequiredSrtpPayloadLength(rtpPacket.Length) - rtpPacket.Length;
            var encrypted = new byte[rtpPacket.Length + extra];

            int rc = Protect(sender, rtpPacket, encrypted, out int encLen);
            Assert.True(rc == 0, $"ProtectRtp failed for seq {seq} (rc={rc}).");

            var trimmed = new byte[encLen];
            Buffer.BlockCopy(encrypted, 0, trimmed, 0, encLen);
            return trimmed;
        }

        private static int UnprotectOnePacket(SrtpContext receiver, byte[] encrypted)
        {
            var decrypted = new byte[encrypted.Length];
            return Unprotect(receiver, encrypted, decrypted, out _);
        }

        private static byte[] MakeRtpPacket(uint ssrc, ushort seq, byte payloadType, byte[] payload)
        {
            var pkt = new byte[12 + payload.Length];
            pkt[0] = 0x80;
            pkt[1] = payloadType;
            pkt[2] = (byte)(seq >> 8);
            pkt[3] = (byte)(seq & 0xFF);
            pkt[8] = (byte)((ssrc >> 24) & 0xFF);
            pkt[9] = (byte)((ssrc >> 16) & 0xFF);
            pkt[10] = (byte)((ssrc >> 8) & 0xFF);
            pkt[11] = (byte)(ssrc & 0xFF);
            Buffer.BlockCopy(payload, 0, pkt, 12, payload.Length);
            return pkt;
        }

#if NET8_0_OR_GREATER
        private static int Protect(SrtpContext ctx, byte[] input, byte[] output, out int outLen)
            => ctx.ProtectRtp(input.AsSpan(), output.AsSpan(), out outLen);

        private static int Unprotect(SrtpContext ctx, byte[] input, byte[] output, out int outLen)
            => ctx.UnprotectRtp(input.AsSpan(), output.AsSpan(), out outLen);
#else
        private static int Protect(SrtpContext ctx, byte[] input, byte[] output, out int outLen)
            => ctx.ProtectRtp(new ArraySegment<byte>(input), output, out outLen);

        private static int Unprotect(SrtpContext ctx, byte[] input, byte[] output, out int outLen)
            => ctx.UnprotectRtp(new ArraySegment<byte>(input), output, out outLen);
#endif
    }
}
