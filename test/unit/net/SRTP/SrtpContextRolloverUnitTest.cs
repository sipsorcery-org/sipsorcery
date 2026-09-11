//-----------------------------------------------------------------------------
// Filename: SrtpContextRolloverUnitTest.cs
//
// Description: Regression test for the per-SSRC outbound rollover counter
// fix in SrtpContext (RFC 3711 section 3.2.1). Before the fix, SrtpContext used
// a single context-wide Roc field that was incremented whenever ANY SSRC's
// 16-bit RTP sequence number wrapped from 0xFFFF to 0x0000. Sharing the
// counter across SSRCs caused the keystream for every other SSRC sharing
// the same SrtpContext (e.g. audio + video bundled on a DTLS-SRTP
// transport) to desynchronise from the receiver -- the receiver's
// per-SSRC ROC inference is unaffected by another SSRC's wrap, so HMAC
// verification fails and packets are silently dropped.
//
// This test demonstrates the fix:
//   1. Encrypt + round-trip-decrypt SSRC A through a sequence wrap.
//   2. Encrypt a single packet on SSRC B with a low (non-wrapping)
//      sequence number.
//   3. Assert UnprotectRtp on the SSRC B packet succeeds.
//
// On the original (buggy) implementation step 3 returns
// ERROR_HMAC_CHECK_FAILED (-3); after the fix it returns 0.
//
// Author(s):
// Claude Opus 4.7 (Anthropic AI assistant, model: claude-opus-4-7), commissioned by Aaron Clauson
//
// History:
// 27 Apr 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using SIPSorcery.Net.SharpSRTP.SRTP;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class SrtpContextRolloverUnitTest
    {
        [Fact]
        public void OutboundRollover_OnOneSsrc_DoesNotBreakAnotherSsrc()
        {
            // SRTP_AES128_CM_HMAC_SHA1_80 -- the standard WebRTC profile.
            var profile = new SrtpProtectionProfileConfiguration(
                SrtpCiphers.AES_128_CM,
                cipherKeyLength:  128,
                cipherSaltLength: 112,
                maximumLifetime:  int.MaxValue,
                auth:             SrtpAuth.HMAC_SHA1,
                authKeyLength:    160,
                authTagLength:    80);

            // Deterministic key + salt so the test is reproducible.
            var key = new byte[16];
            var salt = new byte[14];
            for (int i = 0; i < key.Length;  i++) { key[i]  = (byte)(0x10 + i); }
            for (int i = 0; i < salt.Length; i++) { salt[i] = (byte)(0x80 + i); }

            var sender   = new SrtpContext(SrtpContextType.RTP, profile, key, salt);
            var receiver = new SrtpContext(SrtpContextType.RTP, profile, key, salt);

            const uint ssrcA = 0x11111111u;     // high-rate stream, will wrap
            const uint ssrcB = 0x22222222u;     // low-rate stream, will NOT wrap

            // ---- 1. Drive SSRC A through a sequence wrap ----
            // Send seqs 65530..65535 then 0..6 (13 packets total, with the
            // wrap from 65535 to 0 in the middle). Each one round-trip-
            // decrypts via the receiver to keep both sides' per-SSRC state
            // in sync. Without the fix the wrap would also corrupt
            // SSRC B's outbound ROC; the test in step 2 below catches that.
            for (int step = 0; step < 13; step++)
            {
                ushort seq = (ushort)((65530 + step) & 0xFFFF);
                RoundTripOnePacket(sender, receiver, ssrcA, seq);
            }

            // ---- 2. Send a single packet on SSRC B with a low seq ----
            // Without the fix: sender uses the shared (now-incremented)
            // Roc=1, but SSRC B's receiver-side ROC is 0 (no wrap observed
            // on B). HMAC mismatch -- UnprotectRtp returns -3.
            //
            // With the fix: SSRC B has its own outbound ROC=0, the sender
            // encrypts with roc=0, the receiver decrypts with roc=0,
            // UnprotectRtp returns 0.
            int decryptResult = TryRoundTrip(sender, receiver, ssrcB, seq: 100);
            Assert.True(decryptResult == 0,
                $"Per-SSRC outbound ROC: SSRC B's encryption was corrupted by SSRC A's sequence wrap. UnprotectRtp returned {decryptResult} (-3 = ERROR_HMAC_CHECK_FAILED).  Per RFC 3711 section 3.2.1 each SRTP stream maintains its own ROC; the SrtpContext must not share Roc across SSRCs.");
        }

        // ---- helpers ----

        private static void RoundTripOnePacket(SrtpContext sender, SrtpContext receiver, uint ssrc, ushort seq)
        {
            int rc = TryRoundTrip(sender, receiver, ssrc, seq);
            Assert.True(rc == 0, $"Round-trip failed on SSRC {ssrc.ToString("x8")} seq {seq} (rc={rc})");
        }

        private static int TryRoundTrip(SrtpContext sender, SrtpContext receiver, uint ssrc, ushort seq)
        {
            var rtpPacket = MakeRtpPacket(ssrc, seq, payloadType: 96, payload: new byte[16]);
            int extra = sender.CalculateRequiredSrtpPayloadLength(rtpPacket.Length) - rtpPacket.Length;
            var encrypted = new byte[rtpPacket.Length + extra];

            int encLen;
            int encRc = Protect(sender, rtpPacket, encrypted, out encLen);
            if (encRc != 0) { return encRc; }

            // Slice encrypted to its actual length for decrypt.
            var encryptedTrimmed = new byte[encLen];
            Buffer.BlockCopy(encrypted, 0, encryptedTrimmed, 0, encLen);

            var decrypted = new byte[rtpPacket.Length];
            int decLen;
            return Unprotect(receiver, encryptedTrimmed, decrypted, out decLen);
        }

        private static byte[] MakeRtpPacket(uint ssrc, ushort seq, byte payloadType, byte[] payload)
        {
            // Minimal valid RTP packet: 12-byte header + payload, no CSRCs,
            // no extensions, no padding. V=2, P=0, X=0, CC=0, M=0, PT=payloadType.
            var pkt = new byte[12 + payload.Length];
            pkt[0]  = 0x80;                          // V=2, P=0, X=0, CC=0
            pkt[1]  = payloadType;                   // M=0, PT=payloadType
            pkt[2]  = (byte)(seq >> 8);
            pkt[3]  = (byte)(seq & 0xFF);
            // timestamp = 0 (bytes 4..7 already zero)
            // ssrc
            pkt[8]  = (byte)((ssrc >> 24) & 0xFF);
            pkt[9]  = (byte)((ssrc >> 16) & 0xFF);
            pkt[10] = (byte)((ssrc >>  8) & 0xFF);
            pkt[11] = (byte)( ssrc        & 0xFF);
            // payload
            Buffer.BlockCopy(payload, 0, pkt, 12, payload.Length);
            return pkt;
        }

        // SrtpContext's ProtectRtp / UnprotectRtp signatures use Span on net8+
        // and ArraySegment / byte[] on older frameworks (see the type aliases
        // at the top of SrtpContext.cs). These wrappers paper over the
        // difference so the test itself reads cleanly on every TFM the
        // SIPSorcery.UnitTests project targets.

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
