// Regression test for double-AEAD SRTP unprotect preserving RTP extensions.
//
// Root cause:
//   In SrtpContext.UnprotectRtp, the DOUBLE_AEAD_AES_* branch first decrypts
//   the outer SRTP packet. The outer RTP header may have X=1, meaning that
//   extension data follows the 12-byte fixed header (plus any CSRC entries).
//   It then builds a synthetic RTP packet for inner AEAD decryption. That
//   synthetic packet deliberately excludes the extensions and clears X to 0;
//   this is correct for the inner authentication/decryption operation.
//   The bug occurs when the synthetic header is copied back over the output
//   RTP header after decryption. This also copies the synthetic X=0, although
//   the output still contains the outer packet's extension bytes. The result
//   is a malformed RTP packet: the next parser starts the media payload at
//   byte 12 instead of byte 24 for the extension used below (12 + 4 + 8).
//   Consequently, extension bytes can be mistaken for VP8/audio payload.
//
// Fix:
//   Preserve the outer RTP header's X bit in the final plaintext packet.
//   Keep X=0 only in the temporary synthetic header used for inner AEAD.
//   If the reconstructed synthetic header must be copied to output (for
//   example, to restore fields represented by the OHB), restore the original
//   outer X bit afterward, or merge the header fields without overwriting X.
//   This test verifies the public ProtectRtp/UnprotectRtp round trip and does
//   not depend on any application-level correction in DtlsSrtpTransport.
//
// Author: ChatGPT
// Model: GPT-6 Sol
// Created: 30 September 2026
// License: BSD 3-Clause, as used by the SIPSorcery repository.

using System;
using Org.BouncyCastle.Tls;
using SIPSorcery.Net.SharpSRTP.DTLSSRTP;
using SIPSorcery.Net.SharpSRTP.SRTP;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class SrtpDoubleAeadHeaderExtensionUnitTest
    {
        [Theory]
        [InlineData(SrtpProtectionProfile.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM, true)]
        [InlineData(SrtpProtectionProfile.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM, false)]
        [InlineData(SrtpProtectionProfile.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM, true)]
        [InlineData(SrtpProtectionProfile.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM, false)]
        public void ProtectThenUnprotectRtp_PreservesHeaderExtensionsAndPayload(
            int protectionProfile, bool withExtension)
        {
            // Use the production DTLS-SRTP profile definition. Double AEAD has
            // separate inner/outer keys, salts, and authentication tags.
            var profile = DtlsSrtpProtocol.DtlsProtectionProfiles[protectionProfile];
            var key = new byte[profile.CipherKeyLength / 8];
            var salt = new byte[profile.CipherSaltLength / 8];
            for (int i = 0; i < key.Length; i++) { key[i] = (byte)(i + 1); }
            for (int i = 0; i < salt.Length; i++) { salt[i] = (byte)(0x80 + i); }

            var sender = new SrtpContext(SrtpContextType.RTP, profile, key, salt);
            var receiver = new SrtpContext(SrtpContextType.RTP, profile, key, salt);
            byte[] original = CreateRtpPacket(withExtension);

            var protectedPacket = new byte[sender.CalculateRequiredSrtpPayloadLength(original.Length)];
            int protectResult = Protect(sender, original, protectedPacket, out int protectedLength);
            Assert.Equal(0, protectResult);
            Assert.Equal(protectedPacket.Length, protectedLength);
            Assert.Equal(withExtension, (protectedPacket[0] & 0x10) != 0);

            // Give UnprotectRtp a separate output buffer, as its public API
            // permits. Only pass the bytes actually written by ProtectRtp.
            var protectedBytes = new byte[protectedLength];
            Buffer.BlockCopy(protectedPacket, 0, protectedBytes, 0, protectedLength);
            var unprotectedPacket = new byte[protectedLength];
            int unprotectResult = Unprotect(receiver, protectedBytes, unprotectedPacket,
                out int unprotectedLength);

            Assert.Equal(0, unprotectResult);
            Assert.Equal(original.Length, unprotectedLength);
            Assert.Equal(withExtension, (unprotectedPacket[0] & 0x10) != 0);
            var actual = new byte[unprotectedLength];
            Buffer.BlockCopy(unprotectedPacket, 0, actual, 0, unprotectedLength);
            Assert.Equal(original, actual);
        }

        private static byte[] CreateRtpPacket(bool withExtension)
        {
            // RTP header: 12 bytes. With X=1, the extension adds its 4-byte
            // profile/length header plus two 32-bit words (8 bytes). The
            // payload therefore starts at byte 24, not byte 12.
            byte[] packet = new byte[withExtension ? 30 : 18];
            packet[0] = withExtension ? (byte)0x90 : (byte)0x80; // V=2, X flag
            packet[1] = 0xE0; // marker=1, payload type=96 (VP8)
            packet[2] = 0x12; packet[3] = 0x34; // sequence number
            packet[4] = 0x01; packet[5] = 0x02;
            packet[6] = 0x03; packet[7] = 0x04; // timestamp
            packet[8] = 0x11; packet[9] = 0x22;
            packet[10] = 0x33; packet[11] = 0x44; // SSRC

            int payloadOffset = 12;
            if (withExtension)
            {
                packet[12] = 0xBE; packet[13] = 0xDE; // RFC 8285 one-byte profile
                packet[14] = 0x00; packet[15] = 0x02; // two 32-bit words
                packet[16] = 0x10; packet[17] = 0x7F; // ID=1, one-byte value
                packet[18] = 0x21; packet[19] = 0xAA; packet[20] = 0xBB; // ID=2, two-byte value
                // bytes 21..23 are extension padding (zero)
                payloadOffset = 24;
            }

            // Distinct payload bytes make a misplaced payload boundary visible.
            byte[] payload = { 0x10, 0x00, 0x9D, 0x01, 0x2A, 0x55 };
            Buffer.BlockCopy(payload, 0, packet, payloadOffset, payload.Length);
            return packet;
        }

#if NET8_0_OR_GREATER
        private static int Protect(SrtpContext context, byte[] input, byte[] output, out int length)
            => context.ProtectRtp(input.AsSpan(), output.AsSpan(), out length);

        private static int Unprotect(SrtpContext context, byte[] input, byte[] output, out int length)
            => context.UnprotectRtp(input.AsSpan(), output.AsSpan(), out length);
#else
        private static int Protect(SrtpContext context, byte[] input, byte[] output, out int length)
            => context.ProtectRtp(new ArraySegment<byte>(input), output, out length);

        private static int Unprotect(SrtpContext context, byte[] input, byte[] output, out int length)
            => context.UnprotectRtp(new ArraySegment<byte>(input), output, out length);
#endif
    }
}
