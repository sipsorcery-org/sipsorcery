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
using System.Collections.Generic;
using System.Linq;
using System.Threading;

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
        /// Checks and updates the replay window for the given sequence number.
        /// </summary>
        /// <param name="sequenceNumber">RTP/RTCP sequence number.</param>
        /// <returns>true if the replay check passed, false when the packed was replayed.</returns>
        /// <remarks>https://datatracker.ietf.org/doc/html/rfc2401 Appendix C</remarks>
        public bool CheckAndUpdateReplayWindow(uint sequenceNumber)
        {
            int diff;

            if (sequenceNumber == 0)
            {
                return false; /* first == 0 or wrapped */
            }
            if (sequenceNumber > S_l)
            {
                /* new larger sequence number */
                diff = (int)(sequenceNumber - S_l);
                if (diff < REPLAY_WINDOW_SIZE)
                {
                    /* In window */
                    Bitmap = Bitmap << diff;
                    Bitmap |= 1; /* set bit for this packet */
                }
                else
                {
                    Bitmap = 1; /* This packet has a "way larger" */
                }
                S_l = sequenceNumber;
                return true; /* larger is good */
            }
            diff = (int)(S_l - sequenceNumber);
            if (diff >= REPLAY_WINDOW_SIZE)
            {
                return false; /* too old or wrapped */
            }
            if ((Bitmap & ((ulong)1 << diff)) == ((ulong)1 << diff))
            {
                return false; /* already seen */
            }
            Bitmap |= ((ulong)1 << diff); /* mark as seen */
            return true; /* out of order but good */
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

        public IBlockCipher HeaderCTR { get; private set; }
        public IBlockCipher HeaderF8 { get; private set; }

        public SrtpProtectionProfileConfiguration ProtectionProfile { get; set; }
        public SrtpCiphers Cipher { get; set; }
        public SrtpAuth Auth { get; set; }

        public byte[] MasterKey { get; set; }
        public byte[] MasterSalt { get; set; }

        /// <summary>
        /// Rollover counter.
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
        public byte[] Mki { get; private set; }

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

        public SrtpContext(SrtpContextType contextType, SrtpProtectionProfileConfiguration protectionProfile, byte[] masterKey, byte[] masterSalt, byte[] mki = null)
        {
            this._contextType = contextType;
            this.ProtectionProfile = protectionProfile ?? throw new ArgumentNullException(nameof(protectionProfile));
            this.MasterKey = masterKey ?? throw new ArgumentNullException(nameof(masterKey));
            this.MasterSalt = masterSalt ?? throw new ArgumentNullException(nameof(masterSalt));
            this.Mki = mki ?? new byte[0];

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
            int labelBaseValue = _contextType == SrtpContextType.RTP ? 0 : 3;

            switch(Cipher)
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
                        var aesKeys = new AesEngine();
                        this.K_e = GenerateSessionKey(aesKeys, Cipher, MasterKey, MasterSalt, N_e, labelBaseValue + 0, index, KeyDerivationRate);
                        this.K_a = GenerateSessionKey(aesKeys, Cipher, MasterKey, MasterSalt, N_a, labelBaseValue + 1, index, KeyDerivationRate);
                        this.K_s = GenerateSessionKey(aesKeys, Cipher, MasterKey, MasterSalt, N_s, labelBaseValue + 2, index, KeyDerivationRate);
                        this.K_he = GenerateSessionKey(aesKeys, Cipher, MasterKey, MasterSalt, N_e, 6, index, KeyDerivationRate);
                        this.K_hs = GenerateSessionKey(aesKeys, Cipher, MasterKey, MasterSalt, N_s, 7, index, KeyDerivationRate);

                        if (Cipher >= SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM)
                        {
                            byte[] outerK_e = K_e.Skip(K_e.Length / 2).ToArray();
                            byte[] outerK_he = K_he.Skip(K_he.Length / 2).ToArray();

                            var aesPayload = new AesEngine();
                            aesPayload.Init(true, new KeyParameter(outerK_e));
                            this.PayloadCTR = aesPayload;

                            var aesHeader = new AesEngine();
                            aesHeader.Init(true, new KeyParameter(outerK_he));
                            this.HeaderCTR = aesHeader;
                        }
                        else
                        {
                            var aesPayload = new AesEngine();
                            aesPayload.Init(true, new KeyParameter(K_e));
                            this.PayloadCTR = aesPayload;

                            var aesHeader = new AesEngine();
                            aesHeader.Init(true, new KeyParameter(K_he));
                            this.HeaderCTR = aesHeader;
                        }

                        if (Cipher == SrtpCiphers.AES_128_F8)
                        {
                            this.PayloadF8 = new AesEngine();
                            this.HeaderF8 = new AesEngine();
                        }
                        else if (Cipher == SrtpCiphers.AEAD_AES_128_GCM || Cipher == SrtpCiphers.AEAD_AES_256_GCM) 
                        {
                            this.PayloadAEAD = new GcmBlockCipher(new AesEngine());
                        }
                        else if (Cipher == SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM || Cipher == SrtpCiphers.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM)
                        {
                            this.PayloadAEAD = new GcmBlockCipher(new AesEngine());
                        }
                    }
                    break;

                case SrtpCiphers.ARIA_128_CTR:
                case SrtpCiphers.ARIA_256_CTR:
                case SrtpCiphers.AEAD_ARIA_128_GCM:
                case SrtpCiphers.AEAD_ARIA_256_GCM:
                    {
                        var ariaKeys = new AriaEngine();
                        this.K_e = GenerateSessionKey(ariaKeys, Cipher, MasterKey, MasterSalt, N_e, labelBaseValue + 0, index, KeyDerivationRate);
                        this.K_a = GenerateSessionKey(ariaKeys, Cipher, MasterKey, MasterSalt, N_a, labelBaseValue + 1, index, KeyDerivationRate);
                        this.K_s = GenerateSessionKey(ariaKeys, Cipher, MasterKey, MasterSalt, N_s, labelBaseValue + 2, index, KeyDerivationRate);
                        this.K_he = GenerateSessionKey(ariaKeys, Cipher, MasterKey, MasterSalt, N_e, 6, index, KeyDerivationRate);
                        this.K_hs = GenerateSessionKey(ariaKeys, Cipher, MasterKey, MasterSalt, N_s, 7, index, KeyDerivationRate);

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
                        this.K_e = GenerateSessionKey(seedKeys, Cipher, MasterKey, MasterSalt, N_e, labelBaseValue + 0, index, KeyDerivationRate);
                        this.K_a = GenerateSessionKey(seedKeys, Cipher, MasterKey, MasterSalt, N_a, labelBaseValue + 1, index, KeyDerivationRate);
                        this.K_s = GenerateSessionKey(seedKeys, Cipher, MasterKey, MasterSalt, N_s, labelBaseValue + 2, index, KeyDerivationRate);
                        this.K_he = GenerateSessionKey(seedKeys, Cipher, MasterKey, MasterSalt, N_e, 6, index, KeyDerivationRate);
                        this.K_hs = GenerateSessionKey(seedKeys, Cipher, MasterKey, MasterSalt, N_s, 7, index, KeyDerivationRate);

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
                        else if(Cipher == SrtpCiphers.SEED_128_GCM)
                        {
                            this.PayloadAEAD = new GcmBlockCipher(new SeedEngine());
                        }
                    }
                    break;

                default:
                    throw new NotSupportedException($"Unsupported cipher {Cipher.ToString()}!");

            }

            switch(Auth)
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

        public static byte[] GenerateSessionKey(IBlockCipher engineKeys, SrtpCiphers cipher, byte[] masterKey, byte[] masterSalt, int length, int label, ulong index, ulong kdr)
        {
            byte[] key = new byte[length];
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
                        engineKeys.Init(true, new KeyParameter(masterKey));
                        byte[] iv = Encryption.CTR.GenerateSessionKeyIV(masterSalt, index, kdr, (byte)label);
                        Encryption.CTR.Encrypt(engineKeys, key, 0, length, iv);
                    }
                    break;

                case SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM:
                case SrtpCiphers.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM:
                    {

                        byte[] innerSalt = masterSalt.Take(masterSalt.Length / 2).ToArray();
                        byte[] innerKey = masterKey.Take(masterKey.Length / 2).ToArray();
                        byte[] innerIv = Encryption.CTR.GenerateSessionKeyIV(innerSalt, index, kdr, (byte)label);
                        engineKeys.Init(true, new KeyParameter(innerKey));
                        Encryption.CTR.Encrypt(engineKeys, key, 0, key.Length / 2, innerIv);

                        byte[] outerSalt = masterSalt.Skip(masterSalt.Length / 2).ToArray();
                        byte[] outerKey = masterKey.Skip(masterKey.Length / 2).ToArray();
                        byte[] outerIv = Encryption.CTR.GenerateSessionKeyIV(outerSalt, index, kdr, (byte)label);
                        engineKeys.Init(true, new KeyParameter(outerKey));
                        Encryption.CTR.Encrypt(engineKeys, key, key.Length / 2, key.Length, outerIv);
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
            byte[] mki = context.Mki;
            return rtpLen + mki.Length + context.N_tag + (Cipher >= SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM ? 1 : 0);
        }

        public virtual int ProtectRtp(byte[] payload, int length, out int outputBufferLength)
        {
            var context = this;

            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (payload.Length < CalculateRequiredSrtpPayloadLength(length))
            {
                throw new ArgumentOutOfRangeException($"{nameof(ProtectRtp)} failed, {nameof(payload)} buffer is too small!");
            }

            if (!context.IncrementMasterKeyUseCounter())
            {
                outputBufferLength = 0;
                return ERROR_MASTER_KEY_ROTATION_REQUIRED;
            }

            uint ssrc = RtpReader.ReadSsrc(payload);
            ushort sequenceNumber = RtpReader.ReadSequenceNumber(payload);
            int offset = RtpReader.ReadHeaderLen(payload);
            uint roc = context.Roc;
            ulong index = SrtpContext.GenerateRtpIndex(roc, sequenceNumber);

            // RFC6904
            byte[] rtpExtensionsMask = RtpHeaderExtensionsEncryptionMask;
            if (rtpExtensionsMask != null && rtpExtensionsMask.Length > 0)
            {
                int rtpExtensionsOffset = RtpReader.ReadHeaderLenWithoutExtensions(payload) + 4; // 4 bytes of "defined by profile" and "length" fields
                if (RtpReader.ReadExtensionsLength(payload) <= 0)
                {
                    throw new InvalidOperationException("RTP header extensions encryption mask is set, but the RTP packet does not contain any header extensions!");
                }

                byte[] rtpExtensions = RtpReader.ReadHeaderExtensions(payload);
                int ret = ProtectUnprotectRtpHeaderExtensions(payload, rtpExtensions, rtpExtensionsMask, ssrc, roc, index);
                if (ret != 0)
                {
                    outputBufferLength = 0;
                    return ret;
                }

                Buffer.BlockCopy(rtpExtensions, 0, payload, rtpExtensionsOffset, rtpExtensions.Length);
            }

            switch (context.Cipher)
            {
                case SrtpCiphers.NULL:
                    break;

                case SrtpCiphers.AES_128_F8:
                    {
                        byte[] iv = SRTP.Encryption.F8.GenerateRtpMessageKeyIV(context.PayloadF8, context.K_e, context.K_s, payload, roc);
                        SRTP.Encryption.F8.Encrypt(context.PayloadCTR, payload, offset, length, iv);
                    }
                    break;

                case SrtpCiphers.AES_128_CM:
                case SrtpCiphers.AES_192_CM:
                case SrtpCiphers.AES_256_CM:
                case SrtpCiphers.ARIA_128_CTR:
                case SrtpCiphers.ARIA_256_CTR:
                case SrtpCiphers.SEED_128_CTR:
                    {
                        byte[] iv = SRTP.Encryption.CTR.GenerateMessageKeyIV(context.K_s, ssrc, index);
                        SRTP.Encryption.CTR.Encrypt(context.PayloadCTR, payload, offset, length, iv);
                    }
                    break;

                case SrtpCiphers.AEAD_AES_128_GCM:
                case SrtpCiphers.AEAD_AES_256_GCM:
                case SrtpCiphers.AEAD_ARIA_128_GCM:
                case SrtpCiphers.AEAD_ARIA_256_GCM:
                case SrtpCiphers.SEED_128_CCM:
                case SrtpCiphers.SEED_128_GCM:
                    {
                        byte[] iv = SRTP.Encryption.AEAD.GenerateMessageKeyIV(context.K_s, ssrc, index);
                        byte[] associatedData = payload.Take(offset).ToArray();
                        SRTP.Encryption.AEAD.Encrypt(context.PayloadAEAD, payload, offset, length, iv, context.K_e, context.N_tag, associatedData);
                        length += context.N_tag;
                    }
                    break;

                case SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM:
                case SrtpCiphers.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM:
                    {
                        // form a synthetic RTP packet
                        int rtpHeaderLength = RtpReader.ReadHeaderLenWithoutExtensions(payload);
                        int rtpExtensionsLength = RtpReader.ReadExtensionsLength(payload);
                        byte[] syntheticRtpPacket = new byte[length - rtpExtensionsLength + (context.N_tag / 2)];

                        // copy header without extensions
                        Buffer.BlockCopy(payload, 0, syntheticRtpPacket, 0, rtpHeaderLength);

                        // set X bit to 0
                        syntheticRtpPacket[0] &= 0xEF;

                        // copy the original payload
                        Buffer.BlockCopy(payload, offset, syntheticRtpPacket, rtpHeaderLength, length - offset);

                        // apply inner cryptographic algorithm
                        byte[] innerK_e = context.K_e.Take(context.K_e.Length / 2).ToArray();    
                        byte[] innerK_s = context.K_s.Take(context.K_s.Length / 2).ToArray();    
                        byte[] innerIv = SRTP.Encryption.AEAD.GenerateMessageKeyIV(innerK_s, ssrc, index);
                        byte[] innerAssociatedData = syntheticRtpPacket.Take(rtpHeaderLength).ToArray();
                        SRTP.Encryption.AEAD.Encrypt(context.PayloadAEAD, syntheticRtpPacket, rtpHeaderLength, length - rtpExtensionsLength, innerIv, innerK_e, context.N_tag / 2, innerAssociatedData);

                        // copy the protected payload back to the original payload buffer
                        Buffer.BlockCopy(syntheticRtpPacket, rtpHeaderLength, payload, offset, syntheticRtpPacket.Length - rtpHeaderLength);
                        length += context.N_tag / 2;

                        // append OHB
                        payload[length] = 0; // all empty OHB

                        length += 1;

                        // apply outer cryptographic algorithm
                        byte[] outerK_e = context.K_e.Skip(context.K_e.Length / 2).ToArray();
                        byte[] outerK_s = context.K_s.Skip(context.K_s.Length / 2).ToArray();
                        byte[] outerIv = SRTP.Encryption.AEAD.GenerateMessageKeyIV(outerK_s, ssrc, index);
                        byte[] outerAssociatedData = payload.Take(offset).ToArray();

                        SRTP.Encryption.AEAD.Encrypt(context.PayloadAEAD, payload, offset, length, outerIv, outerK_e, context.N_tag / 2, outerAssociatedData);
                        length += context.N_tag / 2;
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
                payload[length + 0] = (byte)(roc >> 24);
                payload[length + 1] = (byte)(roc >> 16);
                payload[length + 2] = (byte)(roc >> 8);
                payload[length + 3] = (byte)roc;

                auth = SRTP.Authentication.HMAC.GenerateAuthTag(context.HMAC, payload, 0, length + 4);
            }

            byte[] mki = context.Mki;
            if (mki.Length > 0)
            {
                Buffer.BlockCopy(mki, 0, payload, length, mki.Length);
                length += mki.Length;
            }

            if (auth != null)
            {
                System.Buffer.BlockCopy(auth, 0, payload, length, context.N_tag); // we don't append ROC in SRTP
                length += context.N_tag;
            }

            // TODO: review
            if (sequenceNumber == 0xFFFF)
            {
                context.Roc++;
            }

            outputBufferLength = length;

            return 0;
        }

        public int ProtectUnprotectRtpHeaderExtensions(byte[] payload, byte[] rtpExtensions, byte[] rtpExtensionsMask, uint ssrc, uint roc, ulong index)
        {
            var context = this;

            byte[] rtpExtensionsEncrypted = rtpExtensions.ToArray();

            // in case of Double AEAD, this should use the outer cryptographic key
            switch (context.Cipher)
            {
                case SrtpCiphers.NULL:
                    return 0;

                case SrtpCiphers.AES_128_F8:
                    {
                        byte[] iv = SRTP.Encryption.F8.GenerateRtpMessageKeyIV(context.HeaderF8, context.K_he, context.K_hs, payload, roc);
                        SRTP.Encryption.F8.Encrypt(context.HeaderCTR, rtpExtensionsEncrypted, 0, rtpExtensionsEncrypted.Length, iv);
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
                        byte[] iv = SRTP.Encryption.CTR.GenerateMessageKeyIV(context.K_hs, ssrc, index);
                        SRTP.Encryption.CTR.Encrypt(context.HeaderCTR, rtpExtensionsEncrypted, 0, rtpExtensionsEncrypted.Length, iv);
                    }
                    break;

                case SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM:
                case SrtpCiphers.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM:
                    {
                        byte[] outerK_hs = context.K_hs.Skip(context.K_hs.Length / 2).ToArray();
                        byte[] outerIv = SRTP.Encryption.CTR.GenerateMessageKeyIV(outerK_hs, ssrc, index);
                        SRTP.Encryption.CTR.Encrypt(context.HeaderCTR, rtpExtensionsEncrypted, 0, rtpExtensionsEncrypted.Length, outerIv);
                    }
                    break;

                default:
                    return ERROR_UNSUPPORTED_CIPHER;
            }

            for (int i = 0; i < rtpExtensions.Length; i++)
            {
                // EncryptedHeader = (Encrypt(Key, Plaintext) AND MASK) OR (Plaintext AND (NOT MASK))
                rtpExtensions[i] = unchecked((byte)((rtpExtensionsEncrypted[i] & rtpExtensionsMask[i]) | (rtpExtensions[i] & ~rtpExtensionsMask[i])));
            }

            return 0;
        }

        public virtual int UnprotectRtp(byte[] payload, int length, out int outputBufferLength)
        {
            var context = this;

            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            byte[] mki = context.Mki;

            for (int i = 0; i < mki.Length; i++)
            {
                if (payload[length - mki.Length - context.N_tag + i] != mki[i])
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

            uint ssrc = RtpReader.ReadSsrc(payload);
            ushort sequenceNumber = RtpReader.ReadSequenceNumber(payload);

            if (context.Auth != SrtpAuth.NONE)
            {
                // TODO: optimize memory allocation - we could preallocate 4 byte array and add another GenerateAuthTag overload that processes 2 blocks
                int authenticatedLen = length - mki.Length - context.N_tag;
                byte[] msgAuth = new byte[authenticatedLen + 4];
                Buffer.BlockCopy(payload, 0, msgAuth, 0, authenticatedLen);
                msgAuth[authenticatedLen + 0] = (byte)(context.Roc >> 24);
                msgAuth[authenticatedLen + 1] = (byte)(context.Roc >> 16);
                msgAuth[authenticatedLen + 2] = (byte)(context.Roc >> 8);
                msgAuth[authenticatedLen + 3] = (byte)(context.Roc);

                byte[] auth = SRTP.Authentication.HMAC.GenerateAuthTag(context.HMAC, msgAuth, 0, authenticatedLen + 4);
                for (int i = 0; i < context.N_tag; i++)
                {
                    if (payload[authenticatedLen + mki.Length + i] != auth[i])
                    {
                        outputBufferLength = 0;
                        return ERROR_HMAC_CHECK_FAILED;
                    }
                }

                msgAuth = null;
            }

            SsrcSrtpContext ssrcContext;
            if (context.ReplayProtection.TryGetValue(ssrc, out ssrcContext) == false)
            {
                ssrcContext = new SsrcSrtpContext();
                context.ReplayProtection.Add(ssrc, ssrcContext);
            }

            ssrcContext.SetInitialSequence(sequenceNumber);

            int offset = RtpReader.ReadHeaderLen(payload);
            uint roc = context.Roc;
            uint index = SrtpContext.DetermineRtpIndex(ssrcContext.S_l, sequenceNumber, roc);

            if (!ssrcContext.CheckAndUpdateReplayWindow(index))
            {
                outputBufferLength = 0;
                return ERROR_REPLAY_CHECK_FAILED;
            }

            switch (context.Cipher)
            {
                case SrtpCiphers.NULL:
                    {
                        outputBufferLength = length - mki.Length - context.N_tag;
                    }
                    break;

                case SrtpCiphers.AES_128_F8:
                    {
                        byte[] iv = SRTP.Encryption.F8.GenerateRtpMessageKeyIV(context.PayloadF8, context.K_e, context.K_s, payload, roc);
                        SRTP.Encryption.F8.Encrypt(context.PayloadCTR, payload, offset, length - mki.Length - context.N_tag, iv);
                        outputBufferLength = length - mki.Length - context.N_tag;
                    }
                    break;

                case SrtpCiphers.AES_128_CM:
                case SrtpCiphers.AES_192_CM:
                case SrtpCiphers.AES_256_CM:
                case SrtpCiphers.ARIA_128_CTR:
                case SrtpCiphers.ARIA_256_CTR:
                case SrtpCiphers.SEED_128_CTR:
                    {
                        byte[] iv = SRTP.Encryption.CTR.GenerateMessageKeyIV(context.K_s, ssrc, index);
                        SRTP.Encryption.CTR.Encrypt(context.PayloadCTR, payload, offset, length - mki.Length - context.N_tag, iv);
                        outputBufferLength = length - mki.Length - context.N_tag;
                    }
                    break;

                case SrtpCiphers.AEAD_AES_128_GCM:
                case SrtpCiphers.AEAD_AES_256_GCM:
                case SrtpCiphers.AEAD_ARIA_128_GCM:
                case SrtpCiphers.AEAD_ARIA_256_GCM:
                case SrtpCiphers.SEED_128_CCM:
                case SrtpCiphers.SEED_128_GCM:
                    {
                        byte[] iv = SRTP.Encryption.AEAD.GenerateMessageKeyIV(context.K_s, ssrc, index);
                        byte[] associatedData = payload.Take(offset).ToArray();
                        SRTP.Encryption.AEAD.Encrypt(context.PayloadAEAD, payload, offset, length - mki.Length - context.N_tag, iv, context.K_e, context.N_tag, associatedData);
                        outputBufferLength = length - mki.Length - context.N_tag;
                    }
                    break;

                case SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM:
                case SrtpCiphers.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM:
                    {
                        // apply outer cryptographic algorithm
                        byte[] outerK_e = context.K_e.Skip(context.K_e.Length / 2).ToArray();
                        byte[] outerK_s = context.K_s.Skip(context.K_s.Length / 2).ToArray();
                        byte[] outerIv = SRTP.Encryption.AEAD.GenerateMessageKeyIV(outerK_s, ssrc, index);
                        byte[] outerAssociatedData = payload.Take(offset).ToArray();
                        SRTP.Encryption.AEAD.Encrypt(context.PayloadAEAD, payload, offset, length - mki.Length - context.N_tag / 2, outerIv, outerK_e, context.N_tag / 2, outerAssociatedData);

                        // calculate OHB size - it can now be larger than 1 byte if it was modified
                        int lastOhbByteIndex = length - mki.Length - context.N_tag / 2 - 1;
                        byte ohbConfig = payload[lastOhbByteIndex];
                        int ohbLength = 1;
                        if((ohbConfig & 0x01) == 0x01)
                        {
                            ohbLength += 2;
                        }
                        if((ohbConfig & 0x02) == 0x02)
                        {
                            ohbLength += 1;
                        }

                        // form a synthetic RTP packet
                        int rtpHeaderLength = RtpReader.ReadHeaderLenWithoutExtensions(payload);
                        int rtpExtensionsLength = RtpReader.ReadExtensionsLength(payload);
                        byte[] syntheticRtpPacket = new byte[length - rtpExtensionsLength - (context.N_tag / 2) - ohbLength];

                        // copy header without extensions
                        Buffer.BlockCopy(payload, 0, syntheticRtpPacket, 0, rtpHeaderLength);

                        // set X bit to 0
                        syntheticRtpPacket[0] &= 0xEF;

                        // restore original header values from the OHB
                        if ((ohbConfig & 0x01) == 0x01)
                        {
                            syntheticRtpPacket[2] = payload[lastOhbByteIndex - ohbLength - 1];
                            syntheticRtpPacket[3] = payload[lastOhbByteIndex - ohbLength];
                        }
                        if ((ohbConfig & 0x02) == 0x02)
                        {
                            byte pt = payload[lastOhbByteIndex - ohbLength];
                            syntheticRtpPacket[1] = (byte)((syntheticRtpPacket[1] & 0x80) | (pt & 0x7F));
                        }
                        if ((ohbConfig & 0x04) == 0x04)
                        {
                            bool markerBit = (ohbConfig & 0x08) == 0x08;
                            syntheticRtpPacket[1] = (byte)((markerBit ? 0x80 : 0x00) | (syntheticRtpPacket[1] & 0x7F));
                        }

                        // copy the payload including the inner authentication tag
                        Buffer.BlockCopy(payload, offset, syntheticRtpPacket, rtpHeaderLength, length - offset - mki.Length - context.N_tag / 2 - ohbLength);

                        uint innerSsrc = RtpReader.ReadSsrc(syntheticRtpPacket);
                        ushort innerSequenceNumber = RtpReader.ReadSequenceNumber(syntheticRtpPacket);
                        uint innerIndex = SrtpContext.DetermineRtpIndex(ssrcContext.S_l, sequenceNumber, roc);

                        // apply inner cryptographic algorithm
                        byte[] innerK_e = context.K_e.Take(context.K_e.Length / 2).ToArray();
                        byte[] innerK_s = context.K_s.Take(context.K_s.Length / 2).ToArray();
                        byte[] innerIv = SRTP.Encryption.AEAD.GenerateMessageKeyIV(innerK_s, innerSsrc, innerIndex);
                        byte[] innerAssociatedData = syntheticRtpPacket.Take(rtpHeaderLength).ToArray();
                        SRTP.Encryption.AEAD.Encrypt(context.PayloadAEAD, syntheticRtpPacket, rtpHeaderLength, syntheticRtpPacket.Length - context.N_tag / 2, innerIv, innerK_e, context.N_tag / 2, innerAssociatedData);

                        // copy the unprotected payload back to the original payload buffer
                        Buffer.BlockCopy(syntheticRtpPacket, rtpHeaderLength, payload, offset, syntheticRtpPacket.Length - rtpHeaderLength - context.N_tag / 2);

                        // copy the synthetic header back to the original payload buffer
                        Buffer.BlockCopy(syntheticRtpPacket, 0, payload, 0, rtpHeaderLength);

                        // update the output buffer length
                        outputBufferLength = offset + syntheticRtpPacket.Length - rtpHeaderLength - context.N_tag / 2;
                    }
                    break;

                default:
                    {
                        outputBufferLength = 0;
                        return ERROR_UNSUPPORTED_CIPHER;
                    }
            }

            // because of CCM/GCM, RTP headers must be unprotected only after the payload is unprotected and HMAC is verified
            // RFC6904
            byte[] rtpExtensionsMask = RtpHeaderExtensionsEncryptionMask;
            if (rtpExtensionsMask != null && rtpExtensionsMask.Length > 0)
            {
                int rtpExtensionsOffset = RtpReader.ReadHeaderLenWithoutExtensions(payload) + 4; // 4 bytes of "defined by profile" and "length" fields
                if (RtpReader.ReadExtensionsLength(payload) <= 0)
                {
                    throw new InvalidOperationException("RTP header extensions encryption mask is set, but the RTP packet does not contain any header extensions!");
                }

                byte[] rtpExtensions = RtpReader.ReadHeaderExtensions(payload);
                int ret = ProtectUnprotectRtpHeaderExtensions(payload, rtpExtensions, rtpExtensionsMask, ssrc, roc, index);
                if (ret != 0)
                {
                    outputBufferLength = 0;
                    return ret;
                }

                Buffer.BlockCopy(rtpExtensions, 0, payload, rtpExtensionsOffset, rtpExtensions.Length);
            }

            return 0;
        }

        public virtual int CalculateRequiredSrtcpPayloadLength(int rtcpLen)
        {
            var context = this;
            byte[] mki = context.Mki;
            return rtcpLen + 4 + mki.Length + context.N_tag;
        }

        public int ProtectRtcp(byte[] payload, int length, out int outputBufferLength)
        {
            var context = this;
            
            if(payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if(payload.Length < CalculateRequiredSrtcpPayloadLength(length))
            {
                throw new ArgumentOutOfRangeException($"{nameof(ProtectRtcp)} failed, {nameof(payload)} buffer is too small!");
            }

            if (!context.IncrementMasterKeyUseCounter())
            {
                outputBufferLength = 0;
                return ERROR_MASTER_KEY_ROTATION_REQUIRED;
            }

            uint ssrc = RtcpReader.ReadSsrc(payload);
            int offset = RtcpReader.GetHeaderLen();

            SsrcSrtpContext ssrcContext;
            if (context.ReplayProtection.TryGetValue(ssrc, out ssrcContext) == false)
            {
                ssrcContext = new SsrcSrtpContext();
                context.ReplayProtection.Add(ssrc, ssrcContext);
            }

            uint index = ssrcContext.S_l | E_FLAG;

            switch (context.Cipher)
            {
                case SrtpCiphers.NULL:
                    break;

                case SrtpCiphers.AES_128_F8:
                    {
                        byte[] iv = SRTP.Encryption.F8.GenerateRtcpMessageKeyIV(context.PayloadF8, context.K_e, context.K_s, payload, index);
                        SRTP.Encryption.F8.Encrypt(context.PayloadCTR, payload, offset, length, iv);
                    }
                    break;

                case SrtpCiphers.AES_128_CM:
                case SrtpCiphers.AES_192_CM:
                case SrtpCiphers.AES_256_CM:
                case SrtpCiphers.ARIA_128_CTR:
                case SrtpCiphers.ARIA_256_CTR:
                case SrtpCiphers.SEED_128_CTR:
                    {
                        byte[] iv = SRTP.Encryption.CTR.GenerateMessageKeyIV(context.K_s, ssrc, ssrcContext.S_l);
                        SRTP.Encryption.CTR.Encrypt(context.PayloadCTR, payload, offset, length, iv);
                    }
                    break;

                case SrtpCiphers.AEAD_AES_128_GCM:
                case SrtpCiphers.AEAD_AES_256_GCM:
                case SrtpCiphers.AEAD_ARIA_128_GCM:
                case SrtpCiphers.AEAD_ARIA_256_GCM:
                case SrtpCiphers.SEED_128_CCM:
                case SrtpCiphers.SEED_128_GCM:
                    {
                        byte[] iv = SRTP.Encryption.AEAD.GenerateMessageKeyIV(context.K_s, ssrc, ssrcContext.S_l);
                        byte[] associatedData = payload.Take(offset).Concat(new byte[] { (byte)(index >> 24), (byte)(index >> 16), (byte)(index >> 8), (byte)index }).ToArray(); // associatedData include also index
                        SRTP.Encryption.AEAD.Encrypt(context.PayloadAEAD, payload, offset, length, iv, context.K_e, context.N_tag, associatedData);
                        length += context.N_tag;
                    }
                    break;

                case SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM:
                case SrtpCiphers.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM:
                    {
                        // RTCP under Double AEAD is protected only with the outer layer
                        byte[] outerK_e = context.K_e.Skip(context.K_e.Length / 2).ToArray();
                        byte[] outerK_s = context.K_s.Skip(context.K_s.Length / 2).ToArray();
                        byte[] outerIv = SRTP.Encryption.AEAD.GenerateMessageKeyIV(outerK_s, ssrc, ssrcContext.S_l);
                        byte[] associatedData = payload.Take(offset).Concat(new byte[] { (byte)(index >> 24), (byte)(index >> 16), (byte)(index >> 8), (byte)index }).ToArray();
                        SRTP.Encryption.AEAD.Encrypt(context.PayloadAEAD, payload, offset, length, outerIv, outerK_e, context.N_tag / 2, associatedData);
                        length += context.N_tag / 2;
                    }
                    break;

                default:
                    {
                        outputBufferLength = 0;
                        return ERROR_UNSUPPORTED_CIPHER;
                    }
            }

            payload[length + 0] = (byte)(index >> 24);
            payload[length + 1] = (byte)(index >> 16);
            payload[length + 2] = (byte)(index >> 8);
            payload[length + 3] = (byte)index;
            length += 4;

            byte[] mki = context.Mki;
            if (mki.Length > 0)
            {
                Buffer.BlockCopy(mki, 0, payload, length, mki.Length);
                length += mki.Length;
            }

            if (context.Auth != SrtpAuth.NONE)
            {
                byte[] auth = SRTP.Authentication.HMAC.GenerateAuthTag(context.HMAC, payload, 0, length);
                System.Buffer.BlockCopy(auth, 0, payload, length, context.N_tag);
                length += context.N_tag;
            }

            ssrcContext.SetSequence((ushort)((ssrcContext.S_l + 1) % 0x80000000));
            outputBufferLength = length;

            return 0;
        }

        public virtual int UnprotectRtcp(byte[] payload, int length, out int outputBufferLength)
        {
            var context = this;

            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            byte[] mki = context.Mki;

            for (int i = 0; i < mki.Length; i++)
            {
                if (payload[length - context.N_tag - mki.Length + i] != mki[i])
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

            uint ssrc = RtcpReader.ReadSsrc(payload);
            int offset = RtcpReader.GetHeaderLen();

            SsrcSrtpContext ssrcContext;
            if (context.ReplayProtection.TryGetValue(ssrc, out ssrcContext) == false)
            {
                ssrcContext = new SsrcSrtpContext();
                context.ReplayProtection.Add(ssrc, ssrcContext);
            }

            uint index = RtcpReader.SrtcpReadIndex(payload, context.N_a > 0 ? (context.N_tag + mki.Length) : 0);
            bool isEncrypted = false;

            if ((index & E_FLAG) == E_FLAG)
            {
                index = index & ~E_FLAG;
                isEncrypted = true;
            }

            if (context.Auth != SrtpAuth.NONE)
            {
                byte[] auth = SRTP.Authentication.HMAC.GenerateAuthTag(context.HMAC, payload, 0, length - context.N_tag - mki.Length);
                for (int i = 0; i < context.N_tag; i++)
                {
                    if (payload[length - context.N_tag + i] != auth[i])
                    {
                        outputBufferLength = 0;
                        return ERROR_HMAC_CHECK_FAILED;
                    }
                }
            }

            if (!ssrcContext.CheckAndUpdateReplayWindow(index))
            {
                outputBufferLength = 0;
                return ERROR_REPLAY_CHECK_FAILED;
            }

            if (isEncrypted)
            {
                switch (context.Cipher)
                {
                    case SrtpCiphers.NULL:
                        {
                            outputBufferLength = length - 4 - context.N_tag - mki.Length;
                        }
                        break;

                    case SrtpCiphers.AES_128_F8:
                        {
                            byte[] iv = SRTP.Encryption.F8.GenerateRtcpMessageKeyIV(context.PayloadF8, context.K_e, context.K_s, payload, index);
                            SRTP.Encryption.F8.Encrypt(context.PayloadCTR, payload, offset, length - 4 - context.N_tag - mki.Length, iv);
                            outputBufferLength = length - 4 - context.N_tag - mki.Length;
                        }
                        break;

                    case SrtpCiphers.AES_128_CM:
                    case SrtpCiphers.AES_192_CM:
                    case SrtpCiphers.AES_256_CM:
                    case SrtpCiphers.ARIA_128_CTR:
                    case SrtpCiphers.ARIA_256_CTR:
                    case SrtpCiphers.SEED_128_CTR:
                        {
                            byte[] iv = SRTP.Encryption.CTR.GenerateMessageKeyIV(context.K_s, ssrc, ssrcContext.S_l);
                            SRTP.Encryption.CTR.Encrypt(context.PayloadCTR, payload, offset, length - 4 - context.N_tag - mki.Length, iv);
                            outputBufferLength = length - 4 - context.N_tag - mki.Length;
                        }
                        break;

                    case SrtpCiphers.AEAD_AES_128_GCM:
                    case SrtpCiphers.AEAD_AES_256_GCM:
                    case SrtpCiphers.AEAD_ARIA_128_GCM:
                    case SrtpCiphers.AEAD_ARIA_256_GCM:
                    case SrtpCiphers.SEED_128_CCM:
                    case SrtpCiphers.SEED_128_GCM:
                        {
                            byte[] iv = SRTP.Encryption.AEAD.GenerateMessageKeyIV(context.K_s, ssrc, ssrcContext.S_l);
                            byte[] associatedData = payload.Take(offset).Concat(payload.Skip(length - 4).Take(4)).ToArray(); // associatedData include also index
                            SRTP.Encryption.AEAD.Encrypt(context.PayloadAEAD, payload, offset, length - 4 - context.N_tag - mki.Length, iv, context.K_e, context.N_tag, associatedData);
                            outputBufferLength = length - 4 - context.N_tag - mki.Length;
                        }
                        break;

                    case SrtpCiphers.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM:
                    case SrtpCiphers.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM:
                        {
                            // RTCP under Double AEAD is protected only with the outer layer
                            byte[] outerK_e = context.K_e.Skip(context.K_e.Length / 2).ToArray();
                            byte[] outerK_s = context.K_s.Skip(context.K_s.Length / 2).ToArray();
                            byte[] outerIv = SRTP.Encryption.AEAD.GenerateMessageKeyIV(outerK_s, ssrc, ssrcContext.S_l);
                            byte[] associatedData = payload.Take(offset).Concat(payload.Skip(length - 4).Take(4)).ToArray(); // associatedData include also index
                            SRTP.Encryption.AEAD.Encrypt(context.PayloadAEAD, payload, offset, length - 4 - context.N_tag / 2 - mki.Length, outerIv, outerK_e, context.N_tag / 2, associatedData);
                            outputBufferLength = length - 4 - context.N_tag / 2 - mki.Length;
                        }
                        break;

                    default:
                        {
                            outputBufferLength = 0;
                            return ERROR_UNSUPPORTED_CIPHER;
                        }
                }
            }
            else
            {
                outputBufferLength = length;
            }

            return 0;
        }

        public static uint DetermineRtpIndex(uint s_l, ushort SEQ, ulong ROC)
        {
            // RFC 3711 - Appendix A
            ulong v;
            if (s_l < 32768)
            {
                if (SEQ - s_l > 32768)
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
            long currentValue = Interlocked.Increment(ref _masterKeySentCounter);
            long maxAllowedValue = _contextType == SrtpContextType.RTP ? 281474976710656L : 2147483648L;
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
