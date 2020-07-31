//-----------------------------------------------------------------------------
// Filename: SrtpCryptoContext.cs
//
// Description: SRTPCryptoContext class is the core class of SRTP implementation.
// There can be multiple SRTP sources in one SRTP session.And each SRTP stream 
// has a corresponding SRTPCryptoContext object, identified by SSRC.In this way,
// different sources can be protected independently.
//
// Derived From:
// https://github.com/jitsi/jitsi-srtp/blob/master/src/main/java/org/jitsi/srtp/SrtpCryptoContext.java
//
// Author(s):
// Rafael Soares (raf.csoares@kyubinteractive.com)
//
// History:
// 01 Jul 2020	Rafael Soares   Created.
//
// License:
// Customisations: BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// Original Source: Apache License: see below
//-----------------------------------------------------------------------------

/*
 * Copyright @ 2015 - present 8x8, Inc
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 * Some of the code in this class is derived from ccRtp's SRTP implementation,
 * which has the following copyright notice:
 *
 * Copyright (C) 2004-2006 the Minisip Team
 *
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 *
 * This library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with this library; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307 USA
*/

using System.IO;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Utilities;

namespace SIPSorcery.Net
{
    public class SrtcpCryptoContext
    {
        /** The replay check windows size */
        private const long REPLAY_WINDOW_SIZE = 64;

        /** RTCP SSRC of this cryptographic context */
        private long ssrcCtx;

        /** Master key identifier */
        private byte[] mki;

        /** Index received so far */
        private int receivedIndex = 0;

        /** Index sent so far */
        private int sentIndex = 0;

        /** Bit mask for replay check */
        private long replayWindow;

        /** Master encryption key */
        private byte[] masterKey;

        /** Master salting key */
        private byte[] masterSalt;

        /** Derived session encryption key */
        private byte[] encKey;

        /** Derived session authentication key */
        private byte[] authKey;

        /** Derived session salting key */
        private byte[] saltKey;

        /** Encryption / Authentication policy for this session */
        private SrtpPolicy policy;

        /**
         * The HMAC object we used to do packet authentication
         */
        private IMac mac;             // used for various HMAC computations

        // The symmetric cipher engines we need here
        private IBlockCipher cipher = null;
        private IBlockCipher cipherF8 = null; // used inside F8 mode only

        // implements the counter cipher mode for RTP according to RFC 3711
        private SrtpCipherCTR cipherCtr = new SrtpCipherCTR();

        // Here some fields that a allocated here or in constructor. The methods
        // use these fields to avoid too many new operations

        private byte[] tagStore;
        private byte[] ivStore = new byte[16];
        private byte[] rbStore = new byte[4];

        // this is some working store, used by some methods to avoid new operations
        // the methods must use this only to store some results for immediate processing
        private byte[] tempStore = new byte[100];

        /**
         * Construct an empty SRTPCryptoContext using ssrc.
         * The other parameters are set to default null value.
         * 
         * @param ssrc SSRC of this SRTPCryptoContext
         */
        public SrtcpCryptoContext(long ssrcIn)
        {
            ssrcCtx = ssrcIn;
            mki = null;
            masterKey = null;
            masterSalt = null;
            encKey = null;
            authKey = null;
            saltKey = null;
            policy = null;
            tagStore = null;
        }

        /**
         * Construct a normal SRTPCryptoContext based on the given parameters.
         * 
         * @param ssrc
         *            the RTP SSRC that this SRTP cryptographic context protects.
         * @param masterKey
         *            byte array holding the master key for this SRTP cryptographic
         *            context. Refer to chapter 3.2.1 of the RFC about the role of
         *            the master key.
         * @param masterSalt
         *            byte array holding the master salt for this SRTP cryptographic
         *            context. It is used to computer the initialization vector that
         *            in turn is input to compute the session key, session
         *            authentication key and the session salt.
         * @param policy
         *            SRTP policy for this SRTP cryptographic context, defined the
         *            encryption algorithm, the authentication algorithm, etc
         */
        public SrtcpCryptoContext(long ssrcIn, byte[] masterK, byte[] masterS, SrtpPolicy policyIn)
        {
            ssrcCtx = ssrcIn;
            mki = null;
            policy = policyIn;
            masterKey = new byte[policy.EncKeyLength];
            System.Array.Copy(masterK, 0, masterKey, 0, masterK.Length);
            masterSalt = new byte[policy.SaltKeyLength];
            System.Array.Copy(masterS, 0, masterSalt, 0, masterS.Length);

            switch (policy.EncType)
            {
                case SrtpPolicy.NULL_ENCRYPTION:
                    encKey = null;
                    saltKey = null;
                    break;

                case SrtpPolicy.AESF8_ENCRYPTION:
                    cipherF8 = new AesEngine();
                    cipher = new AesEngine();
                    encKey = new byte[this.policy.EncKeyLength];
                    saltKey = new byte[this.policy.SaltKeyLength];
                    break;

                case SrtpPolicy.AESCM_ENCRYPTION:
                    cipher = new AesEngine();
                    encKey = new byte[this.policy.EncKeyLength];
                    saltKey = new byte[this.policy.SaltKeyLength];
                    break;

                case SrtpPolicy.TWOFISHF8_ENCRYPTION:
                    cipherF8 = new TwofishEngine();
                    cipher = new TwofishEngine();
                    encKey = new byte[this.policy.EncKeyLength];
                    saltKey = new byte[this.policy.SaltKeyLength];
                    break;

                case SrtpPolicy.TWOFISH_ENCRYPTION:
                    cipher = new TwofishEngine();
                    encKey = new byte[this.policy.EncKeyLength];
                    saltKey = new byte[this.policy.SaltKeyLength];
                    break;
            }

            switch (policy.AuthType)
            {
                case SrtpPolicy.NULL_AUTHENTICATION:
                    authKey = null;
                    tagStore = null;
                    break;

                case SrtpPolicy.HMACSHA1_AUTHENTICATION:
                    mac = new HMac(new Sha1Digest());
                    authKey = new byte[policy.AuthKeyLength];
                    tagStore = new byte[mac.GetMacSize()];
                    break;

                case SrtpPolicy.SKEIN_AUTHENTICATION:
                    authKey = new byte[policy.AuthKeyLength];
                    tagStore = new byte[policy.AuthTagLength];
                    break;

                default:
                    tagStore = null;
                    break;
            }
        }

        /**
         * Close the crypto context.
         * 
         * The close functions deletes key data and performs a cleanup of the 
         * crypto context.
         * 
         * Clean up key data, maybe this is the second time. However, sometimes
         * we cannot know if the CryptoContext was used and the application called
         * deriveSrtpKeys(...) that would have cleaned the key data.
         * 
         */
        public void Close()
        {
            Arrays.Fill(masterKey, (byte)0);
            Arrays.Fill(masterSalt, (byte)0);
        }

        /**
         * Get the authentication tag length of this SRTP cryptographic context
         * 
         * @return the authentication tag length of this SRTP cryptographic context
         */
        public int GetAuthTagLength()
        {
            return policy.AuthTagLength;
        }

        /**
         * Get the MKI length of this SRTP cryptographic context
         * 
         * @return the MKI length of this SRTP cryptographic context
         */
        public int GetMKILength()
        {
            if (mki != null)
            {
                return mki.Length;
            }
            return 0;
        }

        /**
         * Get the SSRC of this SRTP cryptographic context
         *
         * @return the SSRC of this SRTP cryptographic context
         */
        public long GetSSRC()
        {
            return ssrcCtx;
        }

        /**
         * Transform a RTP packet into a SRTP packet. 
         * This method is called when a normal RTP packet ready to be sent.
         * 
         * Operations done by the transformation may include: encryption, using
         * either Counter Mode encryption, or F8 Mode encryption, adding
         * authentication tag, currently HMC SHA1 method.
         * 
         * Both encryption and authentication functionality can be turned off
         * as long as the SRTPPolicy used in this SRTPCryptoContext is requires no
         * encryption and no authentication. Then the packet will be sent out
         * untouched. However this is not encouraged. If no SRTP feature is enabled,
         * then we shall not use SRTP TransformConnector. We should use the original
         * method (RTPManager managed transportation) instead.  
         * 
         * @param pkt the RTP packet that is going to be sent out
         */
        public void TransformPacket(RawPacket pkt)
        {
            bool encrypt = false;
            // Encrypt the packet using Counter Mode encryption
            if (policy.EncType == SrtpPolicy.AESCM_ENCRYPTION || policy.EncType == SrtpPolicy.TWOFISH_ENCRYPTION)
            {
                ProcessPacketAESCM(pkt, sentIndex);
                encrypt = true;
            }

            // Encrypt the packet using F8 Mode encryption
            else if (policy.EncType == SrtpPolicy.AESF8_ENCRYPTION || policy.EncType == SrtpPolicy.TWOFISHF8_ENCRYPTION)
            {
                ProcessPacketAESF8(pkt, sentIndex);
                encrypt = true;
            }

            int index = 0;
            if (encrypt)
            {
                index = (int)(sentIndex | 0x80000000);
            }

            // Authenticate the packet
            // The authenticate method gets the index via parameter and stores
            // it in network order in rbStore variable. 
            if (policy.AuthType != SrtpPolicy.NULL_AUTHENTICATION)
            {
                AuthenticatePacket(pkt, index);
                pkt.Append(rbStore, 4);
                pkt.Append(tagStore, policy.AuthTagLength);
            }
            sentIndex++;
            sentIndex &= (int)(~0x80000000);       // clear possible overflow
        }

        /**
         * Transform a SRTCP packet into a RTCP packet.
         * This method is called when a SRTCP packet was received.
         * 
         * Operations done by the this operation include:
         * Authentication check, Packet replay check and decryption.
         * 
         * Both encryption and authentication functionality can be turned off
         * as long as the SRTPPolicy used in this SRTPCryptoContext requires no
         * encryption and no authentication. Then the packet will be sent out
         * untouched. However this is not encouraged. If no SRTCP feature is enabled,
         * then we shall not use SRTP TransformConnector. We should use the original
         * method (RTPManager managed transportation) instead.  
         * 
         * @param pkt the received RTCP packet 
         * @return true if the packet can be accepted
         *         false if authentication or replay check failed 
         */
        public bool ReverseTransformPacket(RawPacket pkt)
        {
            bool decrypt = false;
            int tagLength = policy.AuthTagLength;
            int indexEflag = pkt.GetSRTCPIndex(tagLength);

            if ((indexEflag & 0x80000000) == 0x80000000)
            {
                decrypt = true;
            }

            int index = (int)(indexEflag & ~0x80000000);

            /* Replay control */
            if (!CheckReplay(index))
            {
                return false;
            }

            /* Authenticate the packet */
            if (policy.AuthType != SrtpPolicy.NULL_AUTHENTICATION)
            {
                // get original authentication data and store in tempStore
                pkt.ReadRegionToBuff(pkt.GetLength() - tagLength, tagLength, tempStore);

                // Shrink packet to remove the authentication tag and index
                // because this is part of authenticated data
                pkt.shrink(tagLength + 4);

                // compute, then save authentication in tagStore
                AuthenticatePacket(pkt, indexEflag);

                for (int i = 0; i < tagLength; i++)
                {
                    if ((tempStore[i] & 0xff) == (tagStore[i] & 0xff))
                    {
                        continue;
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            if (decrypt)
            {
                /* Decrypt the packet using Counter Mode encryption */
                if (policy.EncType == SrtpPolicy.AESCM_ENCRYPTION || policy.EncType == SrtpPolicy.TWOFISH_ENCRYPTION)
                {
                    ProcessPacketAESCM(pkt, index);
                }

                /* Decrypt the packet using F8 Mode encryption */
                else if (policy.EncType == SrtpPolicy.AESF8_ENCRYPTION || policy.EncType == SrtpPolicy.TWOFISHF8_ENCRYPTION)
                {
                    ProcessPacketAESF8(pkt, index);
                }
            }
            Update(index);
            return true;
        }

        /**
         * Perform Counter Mode AES encryption / decryption 
         * @param pkt the RTP packet to be encrypted / decrypted
         */
        public void ProcessPacketAESCM(RawPacket pkt, int index)
        {
            long ssrc = pkt.GetRTCPSSRC();

            /* Compute the CM IV (refer to chapter 4.1.1 in RFC 3711):
            *
            * k_s   XX XX XX XX XX XX XX XX XX XX XX XX XX XX
            * SSRC              XX XX XX XX
            * index                               XX XX XX XX
            * ------------------------------------------------------XOR
            * IV    XX XX XX XX XX XX XX XX XX XX XX XX XX XX 00 00
            *        0  1  2  3  4  5  6  7  8  9 10 11 12 13 14 15
            */
            ivStore[0] = saltKey[0];
            ivStore[1] = saltKey[1];
            ivStore[2] = saltKey[2];
            ivStore[3] = saltKey[3];

            // The shifts transform the ssrc and index into network order
            ivStore[4] = (byte)(((ssrc >> 24) & 0xff) ^ this.saltKey[4]);
            ivStore[5] = (byte)(((ssrc >> 16) & 0xff) ^ this.saltKey[5]);
            ivStore[6] = (byte)(((ssrc >> 8) & 0xff) ^ this.saltKey[6]);
            ivStore[7] = (byte)((ssrc & 0xff) ^ this.saltKey[7]);

            ivStore[8] = saltKey[8];
            ivStore[9] = saltKey[9];

            ivStore[10] = (byte)(((index >> 24) & 0xff) ^ this.saltKey[10]);
            ivStore[11] = (byte)(((index >> 16) & 0xff) ^ this.saltKey[11]);
            ivStore[12] = (byte)(((index >> 8) & 0xff) ^ this.saltKey[12]);
            ivStore[13] = (byte)((index & 0xff) ^ this.saltKey[13]);

            ivStore[14] = ivStore[15] = 0;

            // Encrypted part excludes fixed header (8 bytes)  
            int payloadOffset = 8;
            int payloadLength = pkt.GetLength() - payloadOffset;
            cipherCtr.Process(cipher, pkt.GetBuffer(), payloadOffset, payloadLength, ivStore);
        }

        /**
         * Perform F8 Mode AES encryption / decryption
         *
         * @param pkt the RTP packet to be encrypted / decrypted
         */
        public void ProcessPacketAESF8(RawPacket pkt, int index)
        {
            // byte[] iv = new byte[16];

            // 4 bytes of the iv are zero
            // the first byte of the RTP header is not used.
            ivStore[0] = 0;
            ivStore[1] = 0;
            ivStore[2] = 0;
            ivStore[3] = 0;

            // Need the encryption flag
            index = (int)(index | 0x80000000);

            // set the index and the encrypt flag in network order into IV
            ivStore[4] = (byte)(index >> 24);
            ivStore[5] = (byte)(index >> 16);
            ivStore[6] = (byte)(index >> 8);
            ivStore[7] = (byte)index;

            // The fixed header follows and fills the rest of the IV
            MemoryStream buf = pkt.GetBuffer();
            buf.Position = 0;
            buf.Read(ivStore, 8, 8);

            // Encrypted part excludes fixed header (8 bytes), index (4 bytes), and
            // authentication tag (variable according to policy)  
            int payloadOffset = 8;
            int payloadLength = pkt.GetLength() - (4 + policy.AuthTagLength);
            SrtpCipherF8.Process(cipher, pkt.GetBuffer(), payloadOffset, payloadLength, ivStore, cipherF8);
        }

        byte[] tempBuffer = new byte[RawPacket.RTP_PACKET_MAX_SIZE];

        /**
         * Authenticate a packet.
         * 
         * Calculated authentication tag is stored in tagStore area.
         *
         * @param pkt the RTP packet to be authenticated
         */
        private void AuthenticatePacket(RawPacket pkt, int index)
        {
            MemoryStream buf = pkt.GetBuffer();
            buf.Position = 0;
            int len = pkt.GetLength();
            buf.Read(tempBuffer, 0, len);

            mac.BlockUpdate(tempBuffer, 0, len);
            rbStore[0] = (byte)(index >> 24);
            rbStore[1] = (byte)(index >> 16);
            rbStore[2] = (byte)(index >> 8);
            rbStore[3] = (byte)index;
            mac.BlockUpdate(rbStore, 0, rbStore.Length);
            mac.DoFinal(tagStore, 0);
        }

        /**
         * Checks if a packet is a replayed on based on its sequence number.
         * 
         * This method supports a 64 packet history relative to the given
         * sequence number.
         *
         * Sequence Number is guaranteed to be real (not faked) through 
         * authentication.
         * 
         * @param index index number of the SRTCP packet
         * @return true if this sequence number indicates the packet is not a
         * replayed one, false if not
         */
        bool CheckReplay(int index)
        {
            // compute the index of previously received packet and its
            // delta to the new received packet
            long delta = index - receivedIndex;

            if (delta > 0)
            {
                /* Packet not yet received */
                return true;
            }
            else
            {
                if (-delta > REPLAY_WINDOW_SIZE)
                {
                    /* Packet too old */
                    return false;
                }
                else
                {
                    if (((this.replayWindow >> ((int)-delta)) & 0x1) != 0)
                    {
                        /* Packet already received ! */
                        return false;
                    }
                    else
                    {
                        /* Packet not yet received */
                        return true;
                    }
                }
            }
        }

        /**
         * Compute the initialization vector, used later by encryption algorithms,
         * based on the label.
         * 
         * @param label label specified for each type of iv 
         */
        private void ComputeIv(byte label)
        {
            for (int i = 0; i < 14; i++)
            {
                ivStore[i] = masterSalt[i];
            }
            ivStore[7] ^= label;
            ivStore[14] = ivStore[15] = 0;
        }

        /**
         * Derives the srtcp session keys from the master key.
         * 
         */
        public void DeriveSrtcpKeys()
        {
            // compute the session encryption key
            byte label = 3;
            ComputeIv(label);

            KeyParameter encryptionKey = new KeyParameter(masterKey);
            cipher.Init(true, encryptionKey);
            Arrays.Fill(masterKey, (byte)0);

            cipherCtr.GetCipherStream(cipher, encKey, policy.EncKeyLength, ivStore);

            if (authKey != null)
            {
                label = 4;
                ComputeIv(label);
                cipherCtr.GetCipherStream(cipher, authKey, policy.AuthKeyLength, ivStore);

                switch ((policy.AuthType))
                {
                    case SrtpPolicy.HMACSHA1_AUTHENTICATION:
                        KeyParameter key = new KeyParameter(authKey);
                        mac.Init(key);
                        break;

                    default:
                        break;
                }
            }
            Arrays.Fill(authKey, (byte)0);

            // compute the session salt
            label = 5;
            ComputeIv(label);
            cipherCtr.GetCipherStream(cipher, saltKey, policy.SaltKeyLength, ivStore);
            Arrays.Fill(masterSalt, (byte)0);

            // As last step: initialize cipher with derived encryption key.
            if (cipherF8 != null)
            {
                SrtpCipherF8.DeriveForIV(cipherF8, encKey, saltKey);
            }
            encryptionKey = new KeyParameter(encKey);
            cipher.Init(true, encryptionKey);
            Arrays.Fill(encKey, (byte)0);
        }


        /**
         * Update the SRTP packet index.
         * 
         * This method is called after all checks were successful. 
         * 
         * @param index index number of the accepted packet
         */
        private void Update(int index)
        {
            int delta = receivedIndex - index;

            /* update the replay bit mask */
            if (delta > 0)
            {
                replayWindow = replayWindow << delta;
                replayWindow |= 1;
            }
            else
            {
#pragma warning disable CS0675 // Bitwise-or operator used on a sign-extended operand
                replayWindow |= 1 << delta;
#pragma warning restore CS0675 // Bitwise-or operator used on a sign-extended operand
            }

            receivedIndex = index;
        }

        /**
         * Derive a new SRTPCryptoContext for use with a new SSRC
         * 
         * This method returns a new SRTPCryptoContext initialized with the data of
         * this SRTPCryptoContext. Replacing the SSRC, Roll-over-Counter, and the
         * key derivation rate the application cab use this SRTPCryptoContext to
         * encrypt / decrypt a new stream (Synchronization source) inside one RTP
         * session.
         * 
         * Before the application can use this SRTPCryptoContext it must call the
         * deriveSrtpKeys method.
         * 
         * @param ssrc
         *            The SSRC for this context
         * @return a new SRTPCryptoContext with all relevant data set.
         */
        public SrtcpCryptoContext DeriveContext(long ssrc)
        {
            SrtcpCryptoContext pcc = null;
            pcc = new SrtcpCryptoContext(ssrc, masterKey,
                    masterSalt, policy);
            return pcc;
        }
    }
}
