// SharpSRTP
// Copyright (C) 2025 Lukas Volf
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE 
// SOFTWARE.

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using SIPSorcery.Net.SharpSRTP.SRTP.Readers;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
#if NET8_0_OR_GREATER
using ReadOnlyBytes = System.ReadOnlySpan<byte>;
using Bytes = System.Span<byte>;
#else
using ReadOnlyBytes = System.ArraySegment<byte>;
using Bytes = byte[];
#endif

namespace SIPSorcery.Net.SharpSRTP.SRTP
{
    public enum SrtpContextType
    {
        RTP,
        RTCP
    }

    public class SsrcSrtpContext
    {
        public const int REPLAY_WINDOW_SIZE = 64; // Minumum is 64 according to the RFC, our current implmentation is using a bit mask, so it won't allow more than 64.

        public ulong Bitmap { get; private set; } = 0;
        public bool LastSeqSet { get; private set; } = false;

        /// <summary>
        /// Receiver only - highest sequence number received.
        /// </summary>
        public uint S_l { get; private set; }
        public bool S_l_set { get; private set; } = false;

        /// <summary>
        /// Read-only replay check. Returns whether <paramref name="sequenceNumber"/> (a 32-bit packet
        /// index) is acceptable, WITHOUT mutating any state. Per RFC 3711 section 3.3 the replay list,
        /// s_l and ROC MUST NOT be advanced until the packet has been authenticated, so callers must do
        /// this read-only check before decryption and only call <see cref="UpdateReplayWindow"/> once
        /// the packet has authenticated.
        /// </summary>
        /// <param name="sequenceNumber">RTP/RTCP 32-bit packet index.</param>
        /// <returns>true if the packet is not a replay and is within the window; otherwise false.</returns>
        /// <remarks>https://datatracker.ietf.org/doc/html/rfc2401 Appendix C</remarks>
        public bool CheckReplayWindow(uint sequenceNumber)
        {
            if (sequenceNumber == 0)
            {
                return !S_l_set; /* index 0 only acceptable as the very first packet */
            }

            if (sequenceNumber > S_l)
            {
                return true; /* new larger index */
            }

            var diff = (int)(S_l - sequenceNumber);
            if (diff >= REPLAY_WINDOW_SIZE)
            {
                return false; /* too old or wrapped */
            }

            if ((Bitmap & ((ulong)1 << diff)) == ((ulong)1 << diff))
            {
                return false; /* already seen */
            }

            return true; /* out of order but within the window and not yet seen */
        }

        /// <summary>
        /// Advances the replay window / highest-index state for an index that has already passed
        /// <see cref="CheckReplayWindow"/> AND been authenticated. Must only be called after the packet
        /// authenticates, per RFC 3711 section 3.3, otherwise an unauthenticated packet could desync the
        /// ROC for the whole stream.
        /// </summary>
        /// <param name="sequenceNumber">The 32-bit packet index that was authenticated.</param>
        public void UpdateReplayWindow(uint sequenceNumber)
        {
            if (sequenceNumber == 0)
            {
                S_l_set = true;
                return;
            }

            if (sequenceNumber > S_l)
            {
                var diff = (int)(sequenceNumber - S_l);
                if (diff < REPLAY_WINDOW_SIZE)
                {
                    Bitmap = Bitmap << diff;
                    Bitmap |= 1; /* set bit for this packet */
                }
                else
                {
                    Bitmap = 1; /* This packet has a "way larger" index */
                }
                S_l = sequenceNumber;
                S_l_set = true;
                return;
            }

            var d = (int)(S_l - sequenceNumber);
            if (d < REPLAY_WINDOW_SIZE)
            {
                Bitmap |= ((ulong)1 << d); /* mark as seen */
            }
        }

        public void SetInitialSequence(uint sequenceNumber)
        {
            if (!S_l_set)
            {
                S_l = sequenceNumber;
                S_l_set = true;
            }
        }

        public void SetSequence(uint sequenceNumber)
        {
            S_l = sequenceNumber;
            S_l_set = true;
        }

        /// <summary>
        /// Sender only -- current rollover counter (ROC) for this SSRC.
        /// Per RFC 3711 section 3.2.1 each SRTP stream maintains its own ROC;
        /// SSRCs that share the same SrtpContext (e.g. audio + video
        /// bundled on the same DTLS-SRTP transport) MUST track ROC
        /// independently. Incremented from 0 each time the 16-bit
        /// RTP sequence number wraps from 0xFFFF to 0x0000 on this
        /// SSRC.
        /// </summary>
        public uint OutboundRoc { get; set; } = 0;
    }

    /// <summary>
    /// SRTP context used for protecting/unprotecting RTP/RTCP packets as defined in RFC 3711.
    /// </summary>
    public class SrtpContext : ISrtpContext
    {
        public Dictionary<uint, SsrcSrtpContext> ReplayProtection { get; } = new Dictionary<uint, SsrcSrtpContext>();

        public const int ERROR_GENERIC = -1;
        public const int ERROR_UNSUPPORTED_CIPHER = -2;
        public const int ERROR_HMAC_CHECK_FAILED = -3;
        public const int ERROR_REPLAY_CHECK_FAILED = -4;
        public const int ERROR_MASTER_KEY_ROTATION_REQUIRED = -5;
        public const int ERROR_MKI_CHECK_FAILED = -6;

        public const uint E_FLAG = 0x80000000;

        private readonly SrtpContextType _contextType;
        public SrtpContextType ContextType { get { return _contextType; } }

        public event EventHandler<EventArgs> OnRekeyingRequested;

        public HMac HMAC { get; private set; }
        public IBlockCipher PayloadCTR { get; private set; }
        public IBlockCipher PayloadF8 { get; private set; }
        public IAeadBlockCipher PayloadAEAD { get; private set; }

        public byte[] Iv12 { get; } = new byte[Encryption.AEAD.BLOCK_SIZE];
        public byte[] Iv16 { get; } = new byte[Encryption.CTR.BLOCK_SIZE];

        public IBlockCipher HeaderCTR { get; private set; }
        public IBlockCipher HeaderF8 { get; private set; }

        public SrtpProtectionProfileConfiguration ProtectionProfile { get; set; }
        public SrtpCiphers Cipher { get; set; }
        public SrtpAuth Auth { get; set; }

        public ReadOnlyMemory<byte> MasterKey { get; set; }
        public ReadOnlyMemory<byte> MasterSalt { get; set; }

        /// <summary>
        /// Rollover counter.
        /// </summary>
        /// <summary>
        /// DEPRECATED. RFC 3711 section 3.2.1 specifies that the rollover
        /// counter is per-SSRC, not per-SrtpContext. Use
        /// <see cref="SsrcSrtpContext.OutboundRoc"/> on the per-SSRC
        /// context returned from <see cref="ReplayProtection"/> instead.
        /// This property is retained as a settable field for binary
        /// compatibility but is no longer read or written by ProtectRtp
        /// or UnprotectRtp.
        /// </summary>
        public uint Roc { get; set; } = 0;

        private long _masterKeySentCounter = 0;

        /// <summary>
        /// Specified how many times was the current master key used.
        /// </summary>
        public long MasterKeySentCounter { get { return _masterKeySentCounter; } }

        /// <summary>
        /// Key derivation rate.
        /// </summary>
        public ulong KeyDerivationRate { get; set; }

        /// <summary>
        /// From, To values, specifying the lifetime for a master key.
        /// </summary>
        //public int From { get; set; }
        //public int To { get; set; }

        /// <summary>
        /// Master Key Identifier.
        /// </summary>
        public ReadOnlyMemory<byte> Mki { get; private set; }

        /// <summary>
        /// The byte-length of the session keys for encryption.
        /// </summary>
        public int N_e { get; set; }

        /// <summary>
        /// Session key for encryption.
        /// </summary>
        public byte[] K_e { get; set; }

        /// <summary>
        /// The byte-length of k_s.
        /// </summary>
        public int N_s { get; set; }

        /// <summary>
        /// Session salting key.
        /// </summary>
        public byte[] K_s { get; set; }

        /// <summary>
        /// Session key for RTP header encyption. Not used in RTCP.
        /// </summary>
        public byte[] K_he { get; set; }

        /// <summary>
        /// Session salt for header encryption.
        /// </summary>
        public byte[] K_hs { get; set; }

        /// <summary>
        /// Gets or sets the encryption mask applied to RTP header extensions.
        /// </summary>
        /// <remarks>The encryption mask is used to protect the contents of RTP header extensions. If set to null, header extensions will not be encrypted.</remarks>
        public byte[] RtpHeaderExtensionsEncryptionMask { get; set; } = null;

        /// <summary>
        /// The byte-length of the session keys for authentication.
        /// </summary>
        public int N_a { get; set; }

        /// <summary>
        /// The session message authentication key.
        /// </summary>
        public byte[] K_a { get; set; }

        /// <summary>
        /// The byte-length of the output authentication tag.
        /// </summary>
        public int N_tag { get; set; }

        /// <summary>
        /// SRTP_PREFIX_LENGTH SHALL be zero for HMAC-SHA1.
        /// </summary>
        public int SRTP_PREFIX_LENGTH { get; set; } = 0;

        public SrtpContext(SrtpContextType contextType, SrtpProtectionProfileConfiguration protectionProfile, ReadOnlyMemory<byte> masterKey, ReadOnlyMemory<byte> masterSalt, ReadOnlyMemory<byte> mki = default)
        {
            this._contextType = contextType;
            this.ProtectionProfile = protectionProfile ?? throw new ArgumentNullException(nameof(protectionProfile));
            this.MasterKey = masterKey;
            this.MasterSalt = masterSalt;
            this.Mki = mki;

            Cipher = protectionProfile.Cipher;
            Auth = protectionProfile.Auth;
            N_e = protectionProfile.CipherKeyLength >> 3;
            N_a = protectionProfile.AuthKeyLength >> 3;
            N_s = protectionProfile.CipherSaltLength >> 3;
            N_tag = protectionProfile.AuthTagLength >> 3;
            SRTP_PREFIX_LENGTH = protectionProfile.SrtpPrefixLength;

            DeriveSessionKeys();
        }

        public virtual void DeriveSessionKeys(ulong index = 0)
        {
            var labelBaseValue = _contextType == SrtpContextType.RTP ? 0 : 3;

            switch (Cipher)
            {
                case SrtpCiphers.NULL:
                case SrtpCiphers.AES_128_F8:
                case SrtpCiphers.AES_128_CM:
                case SrtpCiphers.AES_192_CM:
                case SrtpCiphers.AES_256_CM:
                case SrtpCiphers.AEAD_AES_128_GCM:
                case SrtpCiphers.AEAD_AES_256_GCM:
                case SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM:
                case SrtpCiphers.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM:
                    {
                        var aesKeys = AesUtilities.CreateEngine();
                        this.K_e = GenerateSessionKey(aesKeys, Cipher, MasterKey, MasterSalt, N_e, labelBaseValue + 0, index, KeyDerivationRate, Iv16);
                        this.K_a = GenerateSessionKey(aesKeys, Cipher, MasterKey, MasterSalt, N_a, labelBaseValue + 1, index, KeyDerivationRate, Iv16);
                        this.K_s = GenerateSessionKey(aesKeys, Cipher, MasterKey, MasterSalt, N_s, labelBaseValue + 2, index, KeyDerivationRate, Iv16);
                        this.K_he = GenerateSessionKey(aesKeys, Cipher, MasterKey, MasterSalt, N_e, 6, index, KeyDerivationRate, Iv16);
                        this.K_hs = GenerateSessionKey(aesKeys, Cipher, MasterKey, MasterSalt, N_s, 7, index, KeyDerivationRate, Iv16);

                        if (Cipher >= SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM)
                        {
                            var aesPayload = AesUtilities.CreateEngine();
                            aesPayload.Init(true, new KeyParameter(K_e, K_e.Length / 2, K_e.Length / 2));
                            this.PayloadCTR = aesPayload;

                            var aesHeader = AesUtilities.CreateEngine();
                            aesHeader.Init(true, new KeyParameter(K_he, K_he.Length / 2, K_he.Length / 2));
                            this.HeaderCTR = aesHeader;
                        }
                        else
                        {
                            var aesPayload = AesUtilities.CreateEngine();
                            aesPayload.Init(true, new KeyParameter(K_e));
                            this.PayloadCTR = aesPayload;

                            var aesHeader = AesUtilities.CreateEngine();
                            aesHeader.Init(true, new KeyParameter(K_he));
                            this.HeaderCTR = aesHeader;
                        }

                        if (Cipher == SrtpCiphers.AES_128_F8)
                        {
                            this.PayloadF8 = AesUtilities.CreateEngine();
                            this.HeaderF8 = AesUtilities.CreateEngine();
                        }
                        else if (Cipher == SrtpCiphers.AEAD_AES_128_GCM || Cipher == SrtpCiphers.AEAD_AES_256_GCM)
                        {
                            this.PayloadAEAD = new GcmBlockCipher(AesUtilities.CreateEngine());
                        }
                        else if (Cipher == SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM || Cipher == SrtpCiphers.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM)
                        {
                            this.PayloadAEAD = new GcmBlockCipher(AesUtilities.CreateEngine());
                        }
                    }
                    break;

                case SrtpCiphers.ARIA_128_CTR:
                case SrtpCiphers.ARIA_256_CTR:
                case SrtpCiphers.AEAD_ARIA_128_GCM:
                case SrtpCiphers.AEAD_ARIA_256_GCM:
                    {
                        var ariaKeys = new AriaEngine();
                        this.K_e = GenerateSessionKey(ariaKeys, Cipher, MasterKey, MasterSalt, N_e, labelBaseValue + 0, index, KeyDerivationRate, Iv16);
                        this.K_a = GenerateSessionKey(ariaKeys, Cipher, MasterKey, MasterSalt, N_a, labelBaseValue + 1, index, KeyDerivationRate, Iv16);
                        this.K_s = GenerateSessionKey(ariaKeys, Cipher, MasterKey, MasterSalt, N_s, labelBaseValue + 2, index, KeyDerivationRate, Iv16);
                        this.K_he = GenerateSessionKey(ariaKeys, Cipher, MasterKey, MasterSalt, N_e, 6, index, KeyDerivationRate, Iv16);
                        this.K_hs = GenerateSessionKey(ariaKeys, Cipher, MasterKey, MasterSalt, N_s, 7, index, KeyDerivationRate, Iv16);

                        var ariaPayload = new AriaEngine();
                        ariaPayload.Init(true, new KeyParameter(K_e));
                        this.PayloadCTR = ariaPayload;

                        var ariaHeader = new AriaEngine();
                        ariaHeader.Init(true, new KeyParameter(K_he));
                        this.HeaderCTR = ariaHeader;

                        if (Cipher == SrtpCiphers.AEAD_ARIA_128_GCM || Cipher == SrtpCiphers.AEAD_ARIA_256_GCM)
                        {
                            this.PayloadAEAD = new GcmBlockCipher(new AriaEngine());
                        }
                    }
                    break;

                case SrtpCiphers.SEED_128_CTR:
                case SrtpCiphers.SEED_128_CCM:
                case SrtpCiphers.SEED_128_GCM:
                    {
                        var seedKeys = new SeedEngine();
                        this.K_e = GenerateSessionKey(seedKeys, Cipher, MasterKey, MasterSalt, N_e, labelBaseValue + 0, index, KeyDerivationRate, Iv16);
                        this.K_a = GenerateSessionKey(seedKeys, Cipher, MasterKey, MasterSalt, N_a, labelBaseValue + 1, index, KeyDerivationRate, Iv16);
                        this.K_s = GenerateSessionKey(seedKeys, Cipher, MasterKey, MasterSalt, N_s, labelBaseValue + 2, index, KeyDerivationRate, Iv16);
                        this.K_he = GenerateSessionKey(seedKeys, Cipher, MasterKey, MasterSalt, N_e, 6, index, KeyDerivationRate, Iv16);
                        this.K_hs = GenerateSessionKey(seedKeys, Cipher, MasterKey, MasterSalt, N_s, 7, index, KeyDerivationRate, Iv16);

                        var seedPayload = new SeedEngine();
                        seedPayload.Init(true, new KeyParameter(K_e));
                        this.PayloadCTR = seedPayload;

                        var seedHeader = new AriaEngine();
                        seedHeader.Init(true, new KeyParameter(K_he));
                        this.HeaderCTR = seedHeader;

                        if (Cipher == SrtpCiphers.SEED_128_CCM)
                        {
                            this.PayloadAEAD = new CcmBlockCipher(new SeedEngine());
                        }
                        else if (Cipher == SrtpCiphers.SEED_128_GCM)
                        {
                            this.PayloadAEAD = new GcmBlockCipher(new SeedEngine());
                        }
                    }
                    break;

                default:
                    throw new NotSupportedException($"Unsupported cipher {Cipher.ToString()}!");

            }

            switch (Auth)
            {
                case SrtpAuth.NONE:
                    break;

                case SrtpAuth.HMAC_SHA1:
                    {
                        var hmac = new HMac(new Sha1Digest());
                        hmac.Init(new KeyParameter(K_a));
                        this.HMAC = hmac;
                    }
                    break;

                default:
                    throw new NotSupportedException($"Unsupported auth {Auth.ToString()}!");
            }
        }

        public static byte[] GenerateSessionKey(IBlockCipher engineKeys, SrtpCiphers cipher, ReadOnlyMemory<byte> masterKey, ReadOnlyMemory<byte> masterSalt, int length, int label, ulong index, ulong kdr, byte[] iv)
        {
            var key = GC.AllocateUninitializedArray<byte>(length);
            switch (cipher)
            {
                case SrtpCiphers.NULL:
                case SrtpCiphers.AES_128_CM:
                case SrtpCiphers.AES_128_F8:
                case SrtpCiphers.AES_192_CM:
                case SrtpCiphers.AES_256_CM:
                case SrtpCiphers.AEAD_AES_128_GCM:
                case SrtpCiphers.AEAD_AES_256_GCM:
                case SrtpCiphers.ARIA_128_CTR:
                case SrtpCiphers.ARIA_256_CTR:
                case SrtpCiphers.AEAD_ARIA_128_GCM:
                case SrtpCiphers.AEAD_ARIA_256_GCM:
                case SrtpCiphers.SEED_128_CTR:
                case SrtpCiphers.SEED_128_CCM:
                case SrtpCiphers.SEED_128_GCM:
                    {
                        engineKeys.Init(true, KeyParameter.Create(masterKey));
                        Encryption.CTR.GenerateSessionKeyIV(masterSalt, index, kdr, (byte)label, iv);
                        Encryption.CTR.Encrypt(engineKeys, key, key, iv);
                    }
                    break;

                case SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM:
                case SrtpCiphers.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM:
                    {
                        var innerSalt = masterSalt.Slice(0, masterSalt.Length / 2);
                        var innerKey = masterKey.Slice(0, masterKey.Length / 2);

                        Encryption.CTR.GenerateSessionKeyIV(innerSalt, index, kdr, (byte)label, iv);
                        engineKeys.Init(true, KeyParameter.Create(innerKey));
                        var halfLen = key.Length / 2;
                        Encryption.CTR.Encrypt(engineKeys, key.AsSpan(0, halfLen), key.AsSpan(0, halfLen), iv);

                        var outerSalt = masterSalt.Slice(masterSalt.Length / 2);
                        var outerKey = masterKey.Slice(masterKey.Length / 2);

                        Encryption.CTR.GenerateSessionKeyIV(outerSalt, index, kdr, (byte)label, iv);
                        engineKeys.Init(true, KeyParameter.Create(outerKey));
                        Encryption.CTR.Encrypt(engineKeys, key.AsSpan(halfLen), key.AsSpan(halfLen), iv);
                    }
                    break;

                default:
                    throw new NotSupportedException($"Unsupported cipher {cipher.ToString()}!");
            }

            return key;
        }

        public virtual int CalculateRequiredSrtpPayloadLength(int rtpLen)
        {
            var context = this;
            var mki = context.Mki;
            return rtpLen + mki.Length + context.N_tag + (Cipher >= SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM ? 1 : 0);
        }

        public virtual int ProtectRtp(ReadOnlyBytes input, Bytes output, out int outputBufferLength)
        {
            var context = this;
            var length = input.Length;

#if !NET8_0_OR_GREATER
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }
#endif

            if (output.Length < CalculateRequiredSrtpPayloadLength(length))
            {
                throw new ArgumentOutOfRangeException($"{nameof(ProtectRtp)} failed, {nameof(output)} buffer is too small!");
            }

            if (!context.IncrementMasterKeyUseCounter())
            {
                outputBufferLength = 0;
                return ERROR_MASTER_KEY_ROTATION_REQUIRED;
            }

            var ssrc = RtpReader.ReadSsrc(input);
            var sequenceNumber = RtpReader.ReadSequenceNumber(input);
            var offset = RtpReader.ReadHeaderLen(input);

            // RFC 3711 section 3.2.1 -- ROC is per-SSRC. Look up (or initialise)
            // the per-SSRC context for this stream. Audio + video bundled
            // on the same transport share this SrtpContext but MUST have
            // independent rollover counters; conflating them via a single
            // context-wide Roc field caused all streams sharing the
            // context to have their keystream desynchronise from the
            // receiver whenever ANY stream's sequence number wrapped.
            SsrcSrtpContext outboundCtx;
            if (!context.ReplayProtection.TryGetValue(ssrc, out outboundCtx))
            {
                outboundCtx = new SsrcSrtpContext();
                context.ReplayProtection.Add(ssrc, outboundCtx);
            }
            var roc = outboundCtx.OutboundRoc;
            var index = SrtpContext.GenerateRtpIndex(roc, sequenceNumber);

            // copy header from input to output
            input.Slice(0, offset).CopyTo(output.Slice(0, offset));

            // RFC6904
            var rtpExtensionsMask = RtpHeaderExtensionsEncryptionMask;
            if (rtpExtensionsMask != null && rtpExtensionsMask.Length > 0)
            {
                int extLen = RtpReader.ReadExtensionsLength(input);
                if (extLen <= 0)
                {
                    throw new InvalidOperationException("RTP header extensions encryption mask is set, but the RTP packet does not contain any header extensions!");
                }

                var rtpExtensionsOffset = RtpReader.ReadHeaderLenWithoutExtensions(input) + 4; // 4 bytes of "defined by profile" and "length" fields
                var ret = ProtectUnprotectRtpHeaderExtensions(input, output.Slice(rtpExtensionsOffset, extLen), rtpExtensionsMask, ssrc, roc, index);
                if (ret != 0)
                {
                    outputBufferLength = 0;
                    return ret;
                }
            }

            switch (context.Cipher)
            {
                case SrtpCiphers.NULL:
                    input.Slice(offset, length - offset).CopyTo(output.Slice(offset, length - offset));
                    break;

                case SrtpCiphers.AES_128_F8:
                    {
                        SRTP.Encryption.F8.GenerateRtpMessageKeyIV(context.PayloadF8, context.K_e, context.K_s, input, roc, context.Iv16);
                        SRTP.Encryption.F8.Encrypt(context.PayloadCTR, input.Slice(offset, length - offset), output.Slice(offset, length - offset), context.Iv16);
                    }
                    break;

                case SrtpCiphers.AES_128_CM:
                case SrtpCiphers.AES_192_CM:
                case SrtpCiphers.AES_256_CM:
                case SrtpCiphers.ARIA_128_CTR:
                case SrtpCiphers.ARIA_256_CTR:
                case SrtpCiphers.SEED_128_CTR:
                    {
                        SRTP.Encryption.CTR.GenerateMessageKeyIV(context.K_s, ssrc, index, context.Iv16);
                        SRTP.Encryption.CTR.Encrypt(context.PayloadCTR, input.Slice(offset, length - offset), output.Slice(offset, length - offset), context.Iv16);
                    }
                    break;

                case SrtpCiphers.AEAD_AES_128_GCM:
                case SrtpCiphers.AEAD_AES_256_GCM:
                case SrtpCiphers.AEAD_ARIA_128_GCM:
                case SrtpCiphers.AEAD_ARIA_256_GCM:
                case SrtpCiphers.SEED_128_CCM:
                case SrtpCiphers.SEED_128_GCM:
                    {
                        SRTP.Encryption.AEAD.GenerateMessageKeyIV(context.K_s, ssrc, index, context.Iv12);
                        SRTP.Encryption.AEAD.Encrypt(context.PayloadAEAD, true, input.Slice(offset, length - offset), output.Slice(offset), context.Iv12, context.K_e, context.N_tag, output.Slice(0, offset));
                        length += context.N_tag;
                    }
                    break;

                case SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM:
                case SrtpCiphers.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM:
                    {
                        // form a synthetic RTP packet
                        var rtpHeaderLength = RtpReader.ReadHeaderLenWithoutExtensions(input);
                        var rtpExtensionsLength = RtpReader.ReadExtensionsLength(input);
                        var syntheticRtpPacketLen = length - rtpExtensionsLength + (context.N_tag / 2);
                        var syntheticRtpPacket = ArrayPool<byte>.Shared.Rent(syntheticRtpPacketLen);

                        try
                        {
                            // copy header without extensions
                            input.Slice(0, rtpHeaderLength).CopyTo(syntheticRtpPacket.AsSpan(0, rtpHeaderLength));

                            // set X bit to 0
                            syntheticRtpPacket[0] &= 0xEF;

                            // copy the original payload
                            input.Slice(offset, length - offset).CopyTo(syntheticRtpPacket.AsSpan(rtpHeaderLength, length - offset));

                            // apply inner cryptographic algorithm
                            var innerK_e = KeyParameter.Create(context.K_e.Slice(0, context.K_e.Length / 2));
                            var innerK_s = context.K_s.AsSpan(0, context.K_s.Length / 2);
                            SRTP.Encryption.AEAD.GenerateMessageKeyIV(innerK_s, ssrc, index, context.Iv12);
                            SRTP.Encryption.AEAD.Encrypt(context.PayloadAEAD, true, syntheticRtpPacket.Slice(rtpHeaderLength, length - rtpExtensionsLength - rtpHeaderLength), syntheticRtpPacket.Slice(rtpHeaderLength, syntheticRtpPacketLen - rtpHeaderLength), context.Iv12, innerK_e, context.N_tag / 2, syntheticRtpPacket.Slice(0, rtpHeaderLength));

                            // copy the protected payload back to the output buffer
                            syntheticRtpPacket.AsSpan(rtpHeaderLength, syntheticRtpPacketLen - rtpHeaderLength).CopyTo(output.Slice(offset, syntheticRtpPacketLen - rtpHeaderLength));
                            length += context.N_tag / 2;

                            // append OHB
                            output[length] = 0; // all empty OHB

                            length += 1;

                            // apply outer cryptographic algorithm
                            var outerK_e = KeyParameter.Create(context.K_e.Slice(context.K_e.Length / 2));
                            var outerK_s = context.K_s.AsSpan(context.K_s.Length / 2);
                            SRTP.Encryption.AEAD.GenerateMessageKeyIV(outerK_s, ssrc, index, context.Iv12);

                            SRTP.Encryption.AEAD.Encrypt(context.PayloadAEAD, true, output.Slice(offset, length - offset), output.Slice(offset), context.Iv12, outerK_e, context.N_tag / 2, output.Slice(0, offset));
                            length += context.N_tag / 2;
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(syntheticRtpPacket);
                        }
                    }
                    break;

                default:
                    {
                        outputBufferLength = 0;
                        return ERROR_UNSUPPORTED_CIPHER;
                    }
            }

            byte[] auth = null;
            if (context.Auth != SrtpAuth.NONE)
            {
                BinaryPrimitives.WriteUInt32BigEndian(output.Slice(length, 4), roc);

                auth = SRTP.Authentication.HMAC.GenerateAuthTag(context.HMAC, output.Slice(0, length + 4));
            }

            var mki = context.Mki;
            if (mki.Length > 0)
            {
                mki.Span.CopyTo(output.Slice(length, mki.Length));
                length += mki.Length;
            }

            if (auth != null)
            {
                auth.AsSpan(0, context.N_tag).CopyTo(output.Slice(length, context.N_tag)); // we don't append ROC in SRTP
                length += context.N_tag;
            }

            // Increment the per-SSRC ROC when the RTP sequence wraps
            // from 0xFFFF to 0x0000. Per RFC 3711 section 3.3.1 the next packet
            // (with sequence 0) belongs to the next epoch.
            if (sequenceNumber == 0xFFFF)
            {
                outboundCtx.OutboundRoc++;
            }

            outputBufferLength = length;

            return 0;
        }

        public int ProtectUnprotectRtpHeaderExtensions(ReadOnlySpan<byte> payload, Span<byte> rtpExtensions, ReadOnlySpan<byte> rtpExtensionsMask, uint ssrc, uint roc, ulong index)
        {
            var context = this;

            var rtpExtensionsEncrypted = ArrayPool<byte>.Shared.Rent(rtpExtensions.Length);

            try
            {
                // in case of Double AEAD, this should use the outer cryptographic key
                switch (context.Cipher)
                {
                    case SrtpCiphers.NULL:
                        return 0;

                    case SrtpCiphers.AES_128_F8:
                        {
                            SRTP.Encryption.F8.GenerateRtpMessageKeyIV(context.HeaderF8, context.K_he, context.K_hs, payload, roc, context.Iv16);
                            SRTP.Encryption.F8.Encrypt(context.HeaderCTR, rtpExtensions, rtpExtensionsEncrypted.AsSpan(0, rtpExtensions.Length), context.Iv16);
                        }
                        break;

                    case SrtpCiphers.AES_128_CM:
                    case SrtpCiphers.AES_192_CM:
                    case SrtpCiphers.AES_256_CM:
                    case SrtpCiphers.ARIA_128_CTR:
                    case SrtpCiphers.ARIA_256_CTR:
                    case SrtpCiphers.SEED_128_CTR:
                    case SrtpCiphers.AEAD_AES_128_GCM:
                    case SrtpCiphers.AEAD_AES_256_GCM:
                    case SrtpCiphers.AEAD_ARIA_128_GCM:
                    case SrtpCiphers.AEAD_ARIA_256_GCM:
                    case SrtpCiphers.SEED_128_CCM:
                    case SrtpCiphers.SEED_128_GCM:
                        {
                            SRTP.Encryption.CTR.GenerateMessageKeyIV(context.K_hs, ssrc, index, context.Iv16);
                            SRTP.Encryption.CTR.Encrypt(context.HeaderCTR, rtpExtensions, rtpExtensionsEncrypted.AsSpan(0, rtpExtensions.Length), context.Iv16);
                        }
                        break;

                    case SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM:
                    case SrtpCiphers.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM:
                        {
                            var outerK_hs = context.K_hs.AsSpan(context.K_hs.Length / 2);
                            SRTP.Encryption.CTR.GenerateMessageKeyIV(outerK_hs, ssrc, index, context.Iv16);
                            SRTP.Encryption.CTR.Encrypt(context.HeaderCTR, rtpExtensions, rtpExtensionsEncrypted.AsSpan(0, rtpExtensions.Length), context.Iv16);
                        }
                        break;

                    default:
                        return ERROR_UNSUPPORTED_CIPHER;
                }

                for (var i = 0; i < rtpExtensions.Length; i++)
                {
                    // EncryptedHeader = (Encrypt(Key, Plaintext) AND MASK) OR (Plaintext AND (NOT MASK))
                    rtpExtensions[i] = unchecked((byte)((rtpExtensionsEncrypted[i] & rtpExtensionsMask[i]) | (rtpExtensions[i] & ~rtpExtensionsMask[i])));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rtpExtensionsEncrypted);
            }

            return 0;
        }

        public virtual int UnprotectRtp(ReadOnlyBytes input, Bytes output, out int outputBufferLength)
        {
            var context = this;
            var length = input.Length;

#if !NET8_0_OR_GREATER
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }
#endif

            ReadOnlySpan<byte> inputSpan = input;
            var mki = context.Mki;

            var mkiSpan = mki.Span;
            for (var i = 0; i < mki.Length; i++)
            {
                if (inputSpan[length - mki.Length - context.N_tag + i] != mkiSpan[i])
                {
                    outputBufferLength = 0;
                    return ERROR_MKI_CHECK_FAILED;
                }
            }

            if (!context.IncrementMasterKeyUseCounter())
            {
                outputBufferLength = 0;
                return ERROR_MASTER_KEY_ROTATION_REQUIRED;
            }

            var ssrc = RtpReader.ReadSsrc(input);
            var sequenceNumber = RtpReader.ReadSequenceNumber(input);

            SsrcSrtpContext ssrcContext;
            if (context.ReplayProtection.TryGetValue(ssrc, out ssrcContext) == false)
            {
                ssrcContext = new SsrcSrtpContext();
                context.ReplayProtection.Add(ssrc, ssrcContext);
            }

            ssrcContext.SetInitialSequence(sequenceNumber);

            // Derive ROC/index for this packet from the last accepted index.
            var lastIndex = ssrcContext.S_l;
            var lastSeq = (ushort)(lastIndex & 0xFFFF);
            var lastRoc = lastIndex >> 16;
            var index = SrtpContext.DetermineRtpIndex(lastSeq, sequenceNumber, lastRoc);
            var roc = index >> 16;

            if (context.Auth != SrtpAuth.NONE)
            {
                var authenticatedLen = length - mki.Length - context.N_tag;
                var msgAuth = ArrayPool<byte>.Shared.Rent(authenticatedLen + 4);
                try
                {
                    input.Slice(0, authenticatedLen).CopyTo(msgAuth.AsSpan(0, authenticatedLen));
                    BinaryPrimitives.WriteUInt32BigEndian(msgAuth.AsSpan(authenticatedLen, 4), roc);

                    var auth = SRTP.Authentication.HMAC.GenerateAuthTag(context.HMAC, msgAuth.Slice(0, authenticatedLen + 4));
                    for (var i = 0; i < context.N_tag; i++)
                    {
                        if (inputSpan[authenticatedLen + mki.Length + i] != auth[i])
                        {
                            outputBufferLength = 0;
                            return ERROR_HMAC_CHECK_FAILED;
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(msgAuth);
                }
            }

            var offset = RtpReader.ReadHeaderLen(input);

            // Read-only replay check. The window/ROC state is NOT advanced here; that only happens once
            // the packet has authenticated (UpdateReplayWindow below). RFC 3711 section 3.3.
            if (!ssrcContext.CheckReplayWindow(index))
            {
                outputBufferLength = 0;
                return ERROR_REPLAY_CHECK_FAILED;
            }

            try
            {
                switch (context.Cipher)
                {
                    case SrtpCiphers.NULL:
                        {
                            var dataLen = length - mki.Length - context.N_tag;
                            input.Slice(0, dataLen).CopyTo(output.Slice(0, dataLen));
                            outputBufferLength = dataLen;
                        }
                        break;

                    case SrtpCiphers.AES_128_F8:
                        {
                            SRTP.Encryption.F8.GenerateRtpMessageKeyIV(context.PayloadF8, context.K_e, context.K_s, input, roc, context.Iv16);
                            var decLen = length - mki.Length - context.N_tag;
                            input.Slice(0, offset).CopyTo(output.Slice(0, offset));
                            SRTP.Encryption.F8.Encrypt(context.PayloadCTR, input.Slice(offset, decLen - offset), output.Slice(offset, decLen - offset), context.Iv16);
                            outputBufferLength = decLen;
                        }
                        break;

                    case SrtpCiphers.AES_128_CM:
                    case SrtpCiphers.AES_192_CM:
                    case SrtpCiphers.AES_256_CM:
                    case SrtpCiphers.ARIA_128_CTR:
                    case SrtpCiphers.ARIA_256_CTR:
                    case SrtpCiphers.SEED_128_CTR:
                        {
                            SRTP.Encryption.CTR.GenerateMessageKeyIV(context.K_s, ssrc, index, context.Iv16);
                            var decLen = length - mki.Length - context.N_tag;
                            input.Slice(0, offset).CopyTo(output.Slice(0, offset));
                            SRTP.Encryption.CTR.Encrypt(context.PayloadCTR, input.Slice(offset, decLen - offset), output.Slice(offset, decLen - offset), context.Iv16);
                            outputBufferLength = decLen;
                        }
                        break;

                    case SrtpCiphers.AEAD_AES_128_GCM:
                    case SrtpCiphers.AEAD_AES_256_GCM:
                    case SrtpCiphers.AEAD_ARIA_128_GCM:
                    case SrtpCiphers.AEAD_ARIA_256_GCM:
                    case SrtpCiphers.SEED_128_CCM:
                    case SrtpCiphers.SEED_128_GCM:
                        {
                            SRTP.Encryption.AEAD.GenerateMessageKeyIV(context.K_s, ssrc, index, context.Iv12);
                            input.Slice(0, offset).CopyTo(output.Slice(0, offset));
                            SRTP.Encryption.AEAD.Encrypt(context.PayloadAEAD, false, input.Slice(offset, length - mki.Length - offset), output.Slice(offset), context.Iv12, context.K_e, context.N_tag, input.Slice(0, offset));
                            outputBufferLength = length - mki.Length - context.N_tag;
                        }
                        break;

                    case SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM:
                    case SrtpCiphers.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM:
                        {
                            // apply outer cryptographic algorithm
                            var outerK_e = KeyParameter.Create(context.K_e.Slice(context.K_e.Length / 2));
                            var outerK_s = context.K_s.AsSpan(context.K_s.Length / 2);
                            SRTP.Encryption.AEAD.GenerateMessageKeyIV(outerK_s, ssrc, index, context.Iv12);
                            SRTP.Encryption.AEAD.Encrypt(context.PayloadAEAD, false, input.Slice(offset, length - mki.Length - offset), output.Slice(offset), context.Iv12, outerK_e, context.N_tag / 2, input.Slice(0, offset));

                            // copy header from input to output
                            input.Slice(0, offset).CopyTo(output.Slice(0, offset));

                            // calculate OHB size - it can now be larger than 1 byte if it was modified
                            var lastOhbByteIndex = length - mki.Length - context.N_tag / 2 - 1;
                            var ohbConfig = output[lastOhbByteIndex];
                            var ohbLength = 1;
                            if ((ohbConfig & 0x01) == 0x01)
                            {
                                ohbLength += 2;
                            }
                            if ((ohbConfig & 0x02) == 0x02)
                            {
                                ohbLength += 1;
                            }

                            // form a synthetic RTP packet
                            var rtpHeaderLength = RtpReader.ReadHeaderLenWithoutExtensions(output);
                            var rtpExtensionsLength = RtpReader.ReadExtensionsLength(output);
                            var syntheticRtpPacketLen = length - rtpExtensionsLength - (context.N_tag / 2) - ohbLength;
                            var syntheticRtpPacket = ArrayPool<byte>.Shared.Rent(syntheticRtpPacketLen);

                            try
                            {
                                // copy header without extensions
                                output.Slice(0, rtpHeaderLength).CopyTo(syntheticRtpPacket.AsSpan(0, rtpHeaderLength));

                                // set X bit to 0
                                syntheticRtpPacket[0] &= 0xEF;

                                // restore original header values from the OHB
                                if ((ohbConfig & 0x01) == 0x01)
                                {
                                    syntheticRtpPacket[2] = output[lastOhbByteIndex - ohbLength - 1];
                                    syntheticRtpPacket[3] = output[lastOhbByteIndex - ohbLength];
                                }
                                if ((ohbConfig & 0x02) == 0x02)
                                {
                                    var pt = output[lastOhbByteIndex - ohbLength];
                                    syntheticRtpPacket[1] = (byte)((syntheticRtpPacket[1] & 0x80) | (pt & 0x7F));
                                }
                                if ((ohbConfig & 0x04) == 0x04)
                                {
                                    var markerBit = (ohbConfig & 0x08) == 0x08;
                                    syntheticRtpPacket[1] = (byte)((markerBit ? 0x80 : 0x00) | (syntheticRtpPacket[1] & 0x7F));
                                }

                                // copy the payload including the inner authentication tag
                                output.Slice(offset, length - offset - mki.Length - context.N_tag / 2 - ohbLength).CopyTo(syntheticRtpPacket.AsSpan(rtpHeaderLength, length - offset - mki.Length - context.N_tag / 2 - ohbLength));

                                var innerSsrc = RtpReader.ReadSsrc(syntheticRtpPacket);
                                var innerSequenceNumber = RtpReader.ReadSequenceNumber(syntheticRtpPacket);
                                var innerIndex = SrtpContext.DetermineRtpIndex(lastSeq, innerSequenceNumber, lastRoc);

                                // apply inner cryptographic algorithm
                                var innerK_e = KeyParameter.Create(context.K_e.Slice(0, context.K_e.Length / 2));
                                var innerK_s = context.K_s.AsSpan(0, context.K_s.Length / 2);
                                SRTP.Encryption.AEAD.GenerateMessageKeyIV(innerK_s, innerSsrc, innerIndex, context.Iv12);
                                SRTP.Encryption.AEAD.Encrypt(context.PayloadAEAD, false, syntheticRtpPacket.Slice(rtpHeaderLength, syntheticRtpPacketLen - rtpHeaderLength), syntheticRtpPacket.Slice(rtpHeaderLength, syntheticRtpPacketLen - rtpHeaderLength), context.Iv12, innerK_e, context.N_tag / 2, syntheticRtpPacket.Slice(0, rtpHeaderLength));

                                // copy the unprotected payload back to the output buffer
                                syntheticRtpPacket.AsSpan(rtpHeaderLength, syntheticRtpPacketLen - rtpHeaderLength - context.N_tag / 2).CopyTo(output.Slice(offset, syntheticRtpPacketLen - rtpHeaderLength - context.N_tag / 2));

                                // copy the synthetic header back to the output buffer
                                syntheticRtpPacket.AsSpan(0, rtpHeaderLength).CopyTo(output.Slice(0, rtpHeaderLength));

                                // update the output buffer length
                                outputBufferLength = offset + syntheticRtpPacketLen - rtpHeaderLength - context.N_tag / 2;
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(syntheticRtpPacket);
                            }
                        }
                        break;

                    default:
                        {
                            outputBufferLength = 0;
                            return ERROR_UNSUPPORTED_CIPHER;
                        }
                }
            }
            catch (Org.BouncyCastle.Crypto.InvalidCipherTextException)
            {
                // AEAD (GCM/CCM) authentication failed. Drop the packet WITHOUT advancing the replay
                // window / ROC, so a single unauthenticated, corrupted or reordered packet cannot desync
                // the ROC and cause every subsequent packet to fail to decrypt. RFC 3711 section 3.3.
                outputBufferLength = 0;
                return ERROR_HMAC_CHECK_FAILED;
            }

            // The packet has now been authenticated (HMAC above for HMAC profiles, or the AEAD decrypt
            // for GCM/CCM profiles). Only now is it safe to advance the replay window / ROC.
            // RFC 3711 section 3.3.
            ssrcContext.UpdateReplayWindow(index);

            // because of CCM/GCM, RTP headers must be unprotected only after the payload is unprotected and HMAC is verified
            // RFC6904
            var rtpExtensionsMask = RtpHeaderExtensionsEncryptionMask;
            if (rtpExtensionsMask != null && rtpExtensionsMask.Length > 0)
            {
                int extLen = RtpReader.ReadExtensionsLength(output);
                if (extLen <= 0)
                {
                    throw new InvalidOperationException("RTP header extensions encryption mask is set, but the RTP packet does not contain any header extensions!");
                }

                var rtpExtensionsOffset = RtpReader.ReadHeaderLenWithoutExtensions(output) + 4; // 4 bytes of "defined by profile" and "length" fields
                var ret = ProtectUnprotectRtpHeaderExtensions(output, output.Slice(rtpExtensionsOffset, extLen), rtpExtensionsMask, ssrc, roc, index);
                if (ret != 0)
                {
                    outputBufferLength = 0;
                    return ret;
                }
            }

            return 0;
        }

        public virtual int CalculateRequiredSrtcpPayloadLength(int rtcpLen)
        {
            var context = this;
            var mki = context.Mki;
            return rtcpLen + 4 + mki.Length + context.N_tag;
        }

        public int ProtectRtcp(ReadOnlyBytes input, Bytes output, out int outputBufferLength)
        {
            var context = this;
            var length = input.Length;

#if !NET8_0_OR_GREATER
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }
#endif

            if (output.Length < CalculateRequiredSrtcpPayloadLength(length))
            {
                throw new ArgumentOutOfRangeException($"{nameof(ProtectRtcp)} failed, {nameof(output)} buffer is too small!");
            }

            if (!context.IncrementMasterKeyUseCounter())
            {
                outputBufferLength = 0;
                return ERROR_MASTER_KEY_ROTATION_REQUIRED;
            }

            var ssrc = RtcpReader.ReadSsrc(input);
            var offset = RtcpReader.GetHeaderLen();

            SsrcSrtpContext ssrcContext;
            if (context.ReplayProtection.TryGetValue(ssrc, out ssrcContext) == false)
            {
                ssrcContext = new SsrcSrtpContext();
                context.ReplayProtection.Add(ssrc, ssrcContext);
            }

            var index = ssrcContext.S_l | E_FLAG;

            switch (context.Cipher)
            {
                case SrtpCiphers.NULL:
                    input.Slice(0, length).CopyTo(output.Slice(0, length));
                    break;

                case SrtpCiphers.AES_128_F8:
                    {
                        var iv = SRTP.Encryption.F8.GenerateRtcpMessageKeyIV(context.PayloadF8, context.K_e, context.K_s, input, index);
                        input.Slice(0, offset).CopyTo(output.Slice(0, offset));
                        SRTP.Encryption.F8.Encrypt(context.PayloadCTR, input.Slice(offset, length - offset), output.Slice(offset, length - offset), iv);
                    }
                    break;

                case SrtpCiphers.AES_128_CM:
                case SrtpCiphers.AES_192_CM:
                case SrtpCiphers.AES_256_CM:
                case SrtpCiphers.ARIA_128_CTR:
                case SrtpCiphers.ARIA_256_CTR:
                case SrtpCiphers.SEED_128_CTR:
                    {
                        SRTP.Encryption.CTR.GenerateMessageKeyIV(context.K_s, ssrc, ssrcContext.S_l, context.Iv16);
                        input.Slice(0, offset).CopyTo(output.Slice(0, offset));
                        SRTP.Encryption.CTR.Encrypt(context.PayloadCTR, input.Slice(offset, length - offset), output.Slice(offset, length - offset), context.Iv16);
                    }
                    break;

                case SrtpCiphers.AEAD_AES_128_GCM:
                case SrtpCiphers.AEAD_AES_256_GCM:
                case SrtpCiphers.AEAD_ARIA_128_GCM:
                case SrtpCiphers.AEAD_ARIA_256_GCM:
                case SrtpCiphers.SEED_128_CCM:
                case SrtpCiphers.SEED_128_GCM:
                    {
                        SRTP.Encryption.AEAD.GenerateMessageKeyIV(context.K_s, ssrc, ssrcContext.S_l, context.Iv12);
                        var associatedDataRented = ArrayPool<byte>.Shared.Rent(offset + 4);
                        try
                        {
                            input.Slice(0, offset).CopyTo(associatedDataRented.AsSpan(0, offset));
                            BinaryPrimitives.WriteUInt32BigEndian(associatedDataRented.AsSpan(offset, 4), index);
                            input.Slice(0, offset).CopyTo(output.Slice(0, offset));
                            SRTP.Encryption.AEAD.Encrypt(context.PayloadAEAD, true, input.Slice(offset, length - offset), output.Slice(offset), context.Iv12, context.K_e, context.N_tag, associatedDataRented.Slice(0, offset + 4));
                            length += context.N_tag;
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(associatedDataRented);
                        }
                    }
                    break;

                case SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM:
                case SrtpCiphers.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM:
                    {
                        // RTCP under Double AEAD is protected only with the outer layer
                        var outerK_e = KeyParameter.Create(context.K_e.Slice(context.K_e.Length / 2));
                        var outerK_s = context.K_s.AsSpan(context.K_s.Length / 2);
                        SRTP.Encryption.AEAD.GenerateMessageKeyIV(outerK_s, ssrc, ssrcContext.S_l, context.Iv12);
                        var associatedDataRented = ArrayPool<byte>.Shared.Rent(offset + 4);
                        try
                        {
                            input.Slice(0, offset).CopyTo(associatedDataRented.AsSpan(0, offset));
                            BinaryPrimitives.WriteUInt32BigEndian(associatedDataRented.AsSpan(offset, 4), index);
                            input.Slice(0, offset).CopyTo(output.Slice(0, offset));
                            SRTP.Encryption.AEAD.Encrypt(context.PayloadAEAD, true, input.Slice(offset, length - offset), output.Slice(offset), context.Iv12, outerK_e, context.N_tag / 2, associatedDataRented.Slice(0, offset + 4));
                            length += context.N_tag / 2;
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(associatedDataRented);
                        }
                    }
                    break;

                default:
                    {
                        outputBufferLength = 0;
                        return ERROR_UNSUPPORTED_CIPHER;
                    }
            }

            BinaryPrimitives.WriteUInt32BigEndian(output.Slice(length, 4), index);
            length += 4;

            var mki = context.Mki;
            if (mki.Length > 0)
            {
                mki.Span.CopyTo(output.Slice(length, mki.Length));
                length += mki.Length;
            }

            if (context.Auth != SrtpAuth.NONE)
            {
                var auth = SRTP.Authentication.HMAC.GenerateAuthTag(context.HMAC, output.Slice(0, length));
                auth.AsSpan(0, context.N_tag).CopyTo(output.Slice(length, context.N_tag));
                length += context.N_tag;
            }

            ssrcContext.SetSequence((ushort)((ssrcContext.S_l + 1) % 0x80000000));
            outputBufferLength = length;

            return 0;
        }

        public virtual int UnprotectRtcp(ReadOnlyBytes input, Bytes output, out int outputBufferLength)
        {
            var context = this;
            var length = input.Length;

#if !NET8_0_OR_GREATER
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }
#endif

            ReadOnlySpan<byte> inputSpan = input;
            var mki = context.Mki;

            for (var i = 0; i < mki.Length; i++)
            {
                if (inputSpan[length - context.N_tag - mki.Length + i] != mki.Span[i])
                {
                    outputBufferLength = 0;
                    return ERROR_MKI_CHECK_FAILED;
                }
            }

            if (!context.IncrementMasterKeyUseCounter())
            {
                outputBufferLength = 0;
                return ERROR_MASTER_KEY_ROTATION_REQUIRED;
            }

            var ssrc = RtcpReader.ReadSsrc(input);
            var offset = RtcpReader.GetHeaderLen();

            SsrcSrtpContext ssrcContext;
            if (context.ReplayProtection.TryGetValue(ssrc, out ssrcContext) == false)
            {
                ssrcContext = new SsrcSrtpContext();
                context.ReplayProtection.Add(ssrc, ssrcContext);
            }

            var originalIndex = RtcpReader.SrtcpReadIndex(input, context.N_a > 0 ? (context.N_tag + mki.Length) : 0);
            var index = originalIndex;
            var isEncrypted = false;

            if ((index & E_FLAG) == E_FLAG)
            {
                index = index & ~E_FLAG;
                isEncrypted = true;
            }

            if (context.Auth != SrtpAuth.NONE)
            {
                var auth = SRTP.Authentication.HMAC.GenerateAuthTag(context.HMAC, input.Slice(0, length - context.N_tag - mki.Length));
                for (var i = 0; i < context.N_tag; i++)
                {
                    if (inputSpan[length - context.N_tag + i] != auth[i])
                    {
                        outputBufferLength = 0;
                        return ERROR_HMAC_CHECK_FAILED;
                    }
                }
            }

            if (!ssrcContext.CheckReplayWindow(index))
            {
                outputBufferLength = 0;
                return ERROR_REPLAY_CHECK_FAILED;
            }

            if (isEncrypted)
            {
                try
                {
                    switch (context.Cipher)
                    {
                        case SrtpCiphers.NULL:
                            {
                                var dataLen = length - 4 - context.N_tag - mki.Length;
                                input.Slice(0, dataLen).CopyTo(output.Slice(0, dataLen));
                                outputBufferLength = dataLen;
                            }
                            break;

                        case SrtpCiphers.AES_128_F8:
                            {
                                var decLen = length - 4 - context.N_tag - mki.Length;
                                var iv = SRTP.Encryption.F8.GenerateRtcpMessageKeyIV(context.PayloadF8, context.K_e, context.K_s, input, originalIndex);
                                input.Slice(0, offset).CopyTo(output.Slice(0, offset));
                                SRTP.Encryption.F8.Encrypt(context.PayloadCTR, input.Slice(offset, decLen - offset), output.Slice(offset, decLen - offset), iv);
                                outputBufferLength = decLen;
                            }
                            break;

                        case SrtpCiphers.AES_128_CM:
                        case SrtpCiphers.AES_192_CM:
                        case SrtpCiphers.AES_256_CM:
                        case SrtpCiphers.ARIA_128_CTR:
                        case SrtpCiphers.ARIA_256_CTR:
                        case SrtpCiphers.SEED_128_CTR:
                            {
                                var decLen = length - 4 - context.N_tag - mki.Length;
                                SRTP.Encryption.CTR.GenerateMessageKeyIV(context.K_s, ssrc, index, context.Iv16);
                                input.Slice(0, offset).CopyTo(output.Slice(0, offset));
                                SRTP.Encryption.CTR.Encrypt(context.PayloadCTR, input.Slice(offset, decLen - offset), output.Slice(offset, decLen - offset), context.Iv16);
                                outputBufferLength = decLen;
                            }
                            break;

                        case SrtpCiphers.AEAD_AES_128_GCM:
                        case SrtpCiphers.AEAD_AES_256_GCM:
                        case SrtpCiphers.AEAD_ARIA_128_GCM:
                        case SrtpCiphers.AEAD_ARIA_256_GCM:
                        case SrtpCiphers.SEED_128_CCM:
                        case SrtpCiphers.SEED_128_GCM:
                            {
                                SRTP.Encryption.AEAD.GenerateMessageKeyIV(context.K_s, ssrc, index, context.Iv12);
                                var associatedDataRented = ArrayPool<byte>.Shared.Rent(offset + 4);
                                try
                                {
                                    input.Slice(0, offset).CopyTo(associatedDataRented.AsSpan(0, offset));
                                    BinaryPrimitives.WriteUInt32BigEndian(associatedDataRented.AsSpan(offset, 4), originalIndex);
                                    input.Slice(0, offset).CopyTo(output.Slice(0, offset));
                                    SRTP.Encryption.AEAD.Encrypt(context.PayloadAEAD, false, input.Slice(offset, length - 4 - mki.Length - offset), output.Slice(offset), context.Iv12, context.K_e, context.N_tag, associatedDataRented.Slice(0, offset + 4));
                                    outputBufferLength = length - 4 - context.N_tag - mki.Length;
                                }
                                finally
                                {
                                    ArrayPool<byte>.Shared.Return(associatedDataRented);
                                }
                            }
                            break;

                        case SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM:
                        case SrtpCiphers.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM:
                            {
                                // RTCP under Double AEAD is protected only with the outer layer
                                var outerK_e = KeyParameter.Create(context.K_e.Slice(context.K_e.Length / 2));
                                var outerK_s = context.K_s.AsSpan(context.K_s.Length / 2);
                                SRTP.Encryption.AEAD.GenerateMessageKeyIV(outerK_s, ssrc, index, context.Iv12);
                                var associatedDataRented = ArrayPool<byte>.Shared.Rent(offset + 4);
                                try
                                {
                                    input.Slice(0, offset).CopyTo(associatedDataRented.AsSpan(0, offset));
                                    BinaryPrimitives.WriteUInt32BigEndian(associatedDataRented.AsSpan(offset, 4), originalIndex);
                                    input.Slice(0, offset).CopyTo(output.Slice(0, offset));
                                    SRTP.Encryption.AEAD.Encrypt(context.PayloadAEAD, false, input.Slice(offset, length - 4 - mki.Length - offset), output.Slice(offset), context.Iv12, outerK_e, context.N_tag / 2, associatedDataRented.Slice(0, offset + 4));
                                    outputBufferLength = length - 4 - context.N_tag / 2 - mki.Length;
                                }
                                finally
                                {
                                    ArrayPool<byte>.Shared.Return(associatedDataRented);
                                }
                            }
                            break;

                        default:
                            {
                                outputBufferLength = 0;
                                return ERROR_UNSUPPORTED_CIPHER;
                            }
                    }
                }
                catch (Org.BouncyCastle.Crypto.InvalidCipherTextException)
                {
                    // AEAD (GCM/CCM) authentication failed. Drop the packet WITHOUT advancing the replay
                    // window / ROC, so a single unauthenticated, corrupted or reordered packet cannot desync
                    // the ROC and cause every subsequent packet to fail to decrypt. RFC 3711 section 3.3.
                    outputBufferLength = 0;
                    return ERROR_HMAC_CHECK_FAILED;
                }
            }
            else
            {
                var dataLen = length - 4 - context.N_tag - mki.Length;
                input.Slice(0, dataLen).CopyTo(output.Slice(0, dataLen));
                outputBufferLength = dataLen;
            }

            // The packet has now been authenticated (HMAC above for HMAC profiles, or the AEAD decrypt
            // for GCM/CCM profiles). Only now is it safe to advance the replay window.
            ssrcContext.UpdateReplayWindow(index);

            return 0;
        }

        public static uint DetermineRtpIndex(uint s_l, ushort SEQ, ulong ROC)
        {
            // RFC 3711 - Appendix A
            ulong v;
            if (s_l < 32768)
            {
                // The subtraction MUST be signed. SEQ and s_l are 16 bit sequence values, but with
                // unsigned arithmetic a reordered packet (SEQ slightly below s_l) wraps the
                // subtraction to a huge value, incorrectly selecting ROC-1 and corrupting the HMAC
                // input/keystream IV, so legitimate out-of-order packets fail authentication. The
                // ROC-1 branch is only for stragglers from before a sequence wrap, i.e. when SEQ is
                // in the upper half FAR ABOVE a recently wrapped s_l.
                if ((long)SEQ - s_l > 32768)
                {
                    v = (ROC - 1) % 4294967296L;
                }
                else
                {
                    v = ROC;
                }
            }
            else
            {
                if (s_l - 32768 > SEQ)
                {
                    v = (ROC + 1) % 4294967296L;
                }
                else
                {
                    v = ROC;
                }
            }
            return (uint)(SEQ + v * 65536U);
        }

        public static ulong GenerateRtpIndex(uint ROC, ushort SEQ)
        {
            // RFC 3711 - 3.3.1
            // i = 2 ^ 16 * ROC + SEQ
            return ((ulong)ROC << 16) | SEQ;
        }

        /// <summary>
        /// Increments the master key use counter.
        /// </summary>
        public virtual bool IncrementMasterKeyUseCounter()
        {
            var currentValue = Interlocked.Increment(ref _masterKeySentCounter);
            var maxAllowedValue = _contextType == SrtpContextType.RTP ? 281474976710656L : 2147483648L;
            if (currentValue >= maxAllowedValue)
            {
                OnRekeyingRequested?.Invoke(this, new EventArgs());

                // at this point we shall not transmit any other packets protected by these keys
                return false;
            }

            return true;
        }
    }
}
