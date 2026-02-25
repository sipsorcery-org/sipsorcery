//-----------------------------------------------------------------------------
// Filename: SrtpGcmReplayAuthUnitTest.cs
//
// Description: Regression test for the AES-GCM SRTP "ROC poisoning" bug
// (issue #1662). For AEAD/GCM profiles, authentication happens during the
// payload decrypt. The original UnprotectRtp advanced the replay window /
// highest-index state (S_l, which carries the ROC) BEFORE that decrypt, so a
// single packet that failed AEAD authentication - or a stray/reordered packet -
// permanently advanced S_l. Every subsequent packet then derived the wrong
// AES-GCM IV (or was rejected by the replay window), producing an endless
// "mac check in GCM failed" and never recovering.
//
// Per RFC 3711 section 3.3 the replay list / s_l / ROC MUST only be updated
// AFTER the packet authenticates. This test injects a packet that fails AEAD
// authentication mid-stream and asserts:
//   1. UnprotectRtp returns ERROR_HMAC_CHECK_FAILED (it does not throw); and
//   2. the next legitimate in-order packet still decrypts.
//
// On the original (buggy) implementation step 1 throws
// InvalidCipherTextException and step 2 fails with ERROR_REPLAY_CHECK_FAILED
// (the forged high sequence number poisoned S_l). After the fix both pass.
//
// Author(s):
// Aaron Clauson
//
// History:
// 02 Jun 2026  Aaron Clauson   Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using SIPSorcery.Net.SharpSRTP.SRTP;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class SrtpGcmReplayAuthUnitTest
    {
        [Fact]
        public void FailedAeadAuth_DoesNotPoisonSubsequentPackets()
        {
            // SRTP_AEAD_AES_128_GCM - the profile OpenAI's realtime endpoint negotiates.
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

            var sender   = new SrtpContext(SrtpContextType.RTP, profile, key, salt);
            var attacker = new SrtpContext(SrtpContextType.RTP, profile, key, salt); // produces the forged packet
            var receiver = new SrtpContext(SrtpContextType.RTP, profile, key, salt);

            const uint ssrc = 0x1234abcdu;

            // ---- 1. Establish in-order receiver state (seqs 1..3) ----
            for (ushort seq = 1; seq <= 3; seq++)
            {
                Assert.True(TryRoundTrip(sender, receiver, ssrc, seq) == 0, $"Round-trip failed for seq {seq}.");
            }

            // ---- 2. Inject a packet that fails AEAD authentication, with a higher sequence number ----
            // A forged/corrupted packet whose 16-bit sequence number (100) is greater than the last
            // accepted one. Pre-fix the receiver advanced S_l to 100 BEFORE the GCM auth ran, then threw.
            // With the fix it must return ERROR_HMAC_CHECK_FAILED without throwing and without advancing
            // any state.
            byte[] forged = EncryptThenCorrupt(attacker, ssrc, seq: 100);
            var scratch = new byte[forged.Length];
            int forgedResult = Unprotect(receiver, forged, scratch, out _);
            Assert.True(forgedResult == SrtpContext.ERROR_HMAC_CHECK_FAILED,
                $"A packet that fails AEAD authentication should be rejected with ERROR_HMAC_CHECK_FAILED ({SrtpContext.ERROR_HMAC_CHECK_FAILED}) and must not throw; got {forgedResult}.");

            // ---- 3. The next legitimate in-order packet must still decrypt ----
            // Pre-fix this returned ERROR_REPLAY_CHECK_FAILED (-4) because the forged seq 100 had poisoned
            // S_l (seq 4 then looked "too old"). With the fix S_l was never advanced, so seq 4 decrypts.
            int afterResult = TryRoundTrip(sender, receiver, ssrc, seq: 4);
            Assert.True(afterResult == 0,
                $"A failed-authentication packet poisoned the SRTP ROC/replay state: the next legitimate packet returned {afterResult} instead of 0. Per RFC 3711 section 3.3 state must only advance after authentication.");
        }

        // ---- helpers ----

        private static byte[] EncryptThenCorrupt(SrtpContext sender, uint ssrc, ushort seq)
        {
            var rtpPacket = MakeRtpPacket(ssrc, seq, payloadType: 96, payload: new byte[16]);
            int extra = sender.CalculateRequiredSrtpPayloadLength(rtpPacket.Length) - rtpPacket.Length;
            var encrypted = new byte[rtpPacket.Length + extra];

            int encLen;
            int encRc = Protect(sender, rtpPacket, encrypted, out encLen);
            Assert.True(encRc == 0, $"Protect failed (rc={encRc}).");

            var trimmed = new byte[encLen];
            Buffer.BlockCopy(encrypted, 0, trimmed, 0, encLen);

            // Flip a ciphertext byte (first payload byte, after the 12-byte RTP header) so the GCM
            // authentication tag no longer matches the payload.
            trimmed[12] ^= 0xFF;
            return trimmed;
        }

        private static int TryRoundTrip(SrtpContext sender, SrtpContext receiver, uint ssrc, ushort seq)
        {
            var rtpPacket = MakeRtpPacket(ssrc, seq, payloadType: 96, payload: new byte[16]);
            int extra = sender.CalculateRequiredSrtpPayloadLength(rtpPacket.Length) - rtpPacket.Length;
            var encrypted = new byte[rtpPacket.Length + extra];

            int encLen;
            int encRc = Protect(sender, rtpPacket, encrypted, out encLen);
            if (encRc != 0) { return encRc; }

            var encryptedTrimmed = new byte[encLen];
            Buffer.BlockCopy(encrypted, 0, encryptedTrimmed, 0, encLen);

            var decrypted = new byte[encLen];
            return Unprotect(receiver, encryptedTrimmed, decrypted, out _);
        }

        private static byte[] MakeRtpPacket(uint ssrc, ushort seq, byte payloadType, byte[] payload)
        {
            // Minimal valid RTP packet: 12-byte header + payload. V=2, P=0, X=0, CC=0, M=0, PT=payloadType.
            var pkt = new byte[12 + payload.Length];
            pkt[0]  = 0x80;
            pkt[1]  = payloadType;
            pkt[2]  = (byte)(seq >> 8);
            pkt[3]  = (byte)(seq & 0xFF);
            pkt[8]  = (byte)((ssrc >> 24) & 0xFF);
            pkt[9]  = (byte)((ssrc >> 16) & 0xFF);
            pkt[10] = (byte)((ssrc >>  8) & 0xFF);
            pkt[11] = (byte)( ssrc        & 0xFF);
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
