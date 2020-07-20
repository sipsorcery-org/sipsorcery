//-----------------------------------------------------------------------------
// Filename: SrtpCrtpyoContext.cs
//
// Description: SrtpCryptoContext class is the core class of SRTP implementation. 
// There can be multiple SRTP sources in one SRTP session.And each SRTP stream 
// has a corresponding SrtpCryptoContext object, identified by SSRC.In this way,
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

/**
 * SRTPCryptoContext class is the core class of SRTP implementation. There can
 * be multiple SRTP sources in one SRTP session. And each SRTP stream has a
 * corresponding SRTPCryptoContext object, identified by SSRC. In this way,
 * different sources can be protected independently.
 * 
 * SRTPCryptoContext class acts as a manager class and maintains all the
 * information used in SRTP transformation. It is responsible for deriving
 * encryption keys / salting keys / authentication keys from master keys. And it
 * will invoke certain class to encrypt / decrypt (transform / reverse
 * transform) RTP packets. It will hold a replay check db and do replay check
 * against incoming packets.
 * 
 * Refer to section 3.2 in RFC3711 for detailed description of cryptographic
 * context.
 * 
 * Cryptographic related parameters, i.e. encryption mode / authentication mode,
 * master encryption key and master salt key are determined outside the scope of
 * SRTP implementation. They can be assigned manually, or can be assigned
 * automatically using some key management protocol, such as MIKEY (RFC3830),
 * SDES (RFC4568) or Phil Zimmermann's ZRTP protocol (RFC6189).
 * 
 * @author Bing SU (nova.su@gmail.com)
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
    public class SrtpCryptoContext
    {
        /**
         * The replay check windows size
         */
        private readonly long REPLAY_WINDOW_SIZE = 64;

        /**
         * RTP SSRC of this cryptographic context
         */
        private long ssrcCtx;

        /**
         * Master key identifier
         */
        private byte[] mki;

        /**
         * Roll-Over-Counter, see RFC3711 section 3.2.1 for detailed description
         */
        private int roc;

        /**
         * Roll-Over-Counter guessed from packet
         */
        private int guessedROC;

        /**
         * RTP sequence number of the packet current processing
         */
        private int seqNum;

        /**
         * Whether we have the sequence number of current packet
         */
        private bool seqNumSet;

        /**
         * Key Derivation Rate, used to derive session keys from master keys
         */
        private long keyDerivationRate;

        /**
         * Bit mask for replay check
         */
        private long replayWindow;

        /**
         * Master encryption key
         */
        private byte[] masterKey;

        /**
         * Master salting key
         */
        private byte[] masterSalt;

        /**
         * Derived session encryption key
         */
        private byte[] encKey;

        /**
         * Derived session authentication key
         */
        private byte[] authKey;

        /**
         * Derived session salting key
         */
        private byte[] saltKey;

        /**
         * Encryption / Authentication policy for this session
         */
        private SrtpPolicy policy;

        /**
         * The HMAC object we used to do packet authentication
         */
        private IMac mac;

        /**
         * The symmetric cipher engines we need here
         */
        private IBlockCipher cipher = null;

        /**
         * Used inside F8 mode only
         */
        private IBlockCipher cipherF8 = null;

        /**
         * implements the counter cipher mode for RTP according to RFC 3711
         */
        private SrtpCipherCTR cipherCtr = new SrtpCipherCTR();

        /**
         * Temp store.
         */
        private byte[] tagStore;

        /**
         * Temp store.
         */
        private byte[] ivStore = new byte[16];

        /**
         * Temp store.
         */
        private byte[] rbStore = new byte[4];

        /**
         * this is a working store, used by some methods to avoid new operations the
         * methods must use this only to store results for immediate processing
         */
        private byte[] tempStore = new byte[100];

        /**
         * Construct an empty SRTPCryptoContext using ssrc. The other parameters are
         * set to default null value.
         * 
         * @param ssrcIn
         *            SSRC of this SRTPCryptoContext
         */
        public SrtpCryptoContext(long ssrcIn)
        {
            ssrcCtx = ssrcIn;
            mki = null;
            roc = 0;
            guessedROC = 0;
            seqNum = 0;
            keyDerivationRate = 0;
            masterKey = null;
            masterSalt = null;
            encKey = null;
            authKey = null;
            saltKey = null;
            seqNumSet = false;
            policy = null;
            tagStore = null;
        }

        /**
         * Construct a normal SRTPCryptoContext based on the given parameters.
         * 
         * @param ssrcIn
         *            the RTP SSRC that this SRTP cryptographic context protects.
         * @param rocIn
         *            the initial Roll-Over-Counter according to RFC 3711. These are
         *            the upper 32 bit of the overall 48 bit SRTP packet index.
         *            Refer to chapter 3.2.1 of the RFC.
         * @param kdr
         *            the key derivation rate defines when to recompute the SRTP
         *            session keys. Refer to chapter 4.3.1 in the RFC.
         * @param masterK
         *            byte array holding the master key for this SRTP cryptographic
         *            context. Refer to chapter 3.2.1 of the RFC about the role of
         *            the master key.
         * @param masterS
         *            byte array holding the master salt for this SRTP cryptographic
         *            context. It is used to computer the initialization vector that
         *            in turn is input to compute the session key, session
         *            authentication key and the session salt.
         * @param policyIn
         *            SRTP policy for this SRTP cryptographic context, defined the
         *            encryption algorithm, the authentication algorithm, etc
         */

        public SrtpCryptoContext(long ssrcIn, int rocIn, long kdr, byte[] masterK,
                byte[] masterS, SrtpPolicy policyIn)
        {
            ssrcCtx = ssrcIn;
            mki = null;
            roc = rocIn;
            guessedROC = 0;
            seqNum = 0;
            keyDerivationRate = kdr;
            seqNumSet = false;

            policy = policyIn;

            masterKey = new byte[policy.EncKeyLength];
            System.Array.Copy(masterK, 0, masterKey, 0, masterK.Length);

            masterSalt = new byte[policy.SaltKeyLength];
            System.Array.Copy(masterS, 0, masterSalt, 0, masterS.Length);

            mac = new HMac(new Sha1Digest());

            switch (policy.EncType)
            {
                case SrtpPolicy.NULL_ENCRYPTION:
                    encKey = null;
                    saltKey = null;
                    break;

                case SrtpPolicy.AESF8_ENCRYPTION:
                    cipherF8 = new AesEngine();
                    cipher = new AesEngine();
                    encKey = new byte[policy.EncKeyLength];
                    saltKey = new byte[policy.SaltKeyLength];
                    break;
                //$FALL-THROUGH$

                case SrtpPolicy.AESCM_ENCRYPTION:
                    cipher = new AesEngine();
                    encKey = new byte[policy.EncKeyLength];
                    saltKey = new byte[policy.SaltKeyLength];
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

                default:
                    tagStore = null;
                    break;
            }
        }

        /**
         * Close the crypto context.
         * 
         * The close functions deletes key data and performs a cleanup of the crypto
         * context.
         * 
         * Clean up key data, maybe this is the second time however, sometimes we
         * cannot know if the CryptoCOntext was used and the application called
         * deriveSrtpKeys(...) .
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
            return this.mki == null ? 0 : this.mki.Length;
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
         * Get the Roll-Over-Counter of this SRTP cryptographic context
         * 
         * @return the Roll-Over-Counter of this SRTP cryptographic context
         */
        public int GetROC()
        {
            return roc;
        }

        /**
         * Set the Roll-Over-Counter of this SRTP cryptographic context
         * 
         * @param rocIn
         *            the Roll-Over-Counter of this SRTP cryptographic context
         */
        public void SetROC(int rocIn)
        {
            roc = rocIn;
        }

        /**
         * Transform a RTP packet into a SRTP packet. This method is called when a
         * normal RTP packet ready to be sent.
         * 
         * Operations done by the transformation may include: encryption, using
         * either Counter Mode encryption, or F8 Mode encryption, adding
         * authentication tag, currently HMC SHA1 method.
         * 
         * Both encryption and authentication functionality can be turned off as
         * long as the SRTPPolicy used in this SRTPCryptoContext is requires no
         * encryption and no authentication. Then the packet will be sent out
         * untouched. However this is not encouraged. If no SRTP feature is enabled,
         * then we shall not use SRTP TransformConnector. We should use the original
         * method (RTPManager managed transportation) instead.
         * 
         * @param pkt
         *            the RTP packet that is going to be sent out
         */
        public void TransformPacket(RawPacket pkt)
        {
            /* Encrypt the packet using Counter Mode encryption */
            if (policy.EncType == SrtpPolicy.AESCM_ENCRYPTION || policy.EncType == SrtpPolicy.TWOFISH_ENCRYPTION)
            {
                ProcessPacketAESCM(pkt);
            }
            else if (policy.EncType == SrtpPolicy.AESF8_ENCRYPTION || policy.EncType == SrtpPolicy.TWOFISHF8_ENCRYPTION)
            {
                /* Encrypt the packet using F8 Mode encryption */
                ProcessPacketAESF8(pkt);
            }

            /* Authenticate the packet */
            if (policy.AuthType != SrtpPolicy.NULL_AUTHENTICATION)
            {
                AuthenticatePacketHMCSHA1(pkt, roc);
                pkt.Append(tagStore, policy.AuthTagLength);
            }

            /* Update the ROC if necessary */
            int seqNo = pkt.GetSequenceNumber();
            if (seqNo == 0xFFFF)
            {
                roc++;
            }
        }

        /**
         * Transform a SRTP packet into a RTP packet. This method is called when a
         * SRTP packet is received.
         * 
         * Operations done by the this operation include: Authentication check,
         * Packet replay check and Decryption.
         * 
         * Both encryption and authentication functionality can be turned off as
         * long as the SRTPPolicy used in this SRTPCryptoContext requires no
         * encryption and no authentication. Then the packet will be sent out
         * untouched. However this is not encouraged. If no SRTP feature is enabled,
         * then we shall not use SRTP TransformConnector. We should use the original
         * method (RTPManager managed transportation) instead.
         * 
         * @param pkt
         *            the RTP packet that is just received
         * @return true if the packet can be accepted false if the packet failed
         *         authentication or failed replay check
         */
        public bool ReverseTransformPacket(RawPacket pkt)
        {
            int seqNo = pkt.GetSequenceNumber();

            if (!seqNumSet)
            {
                seqNumSet = true;
                seqNum = seqNo;
            }

            // Guess the SRTP index (48 bit), see rFC 3711, 3.3.1
            // Stores the guessed roc in this.guessedROC
            long guessedIndex = GuessIndex(seqNo);

            // Replay control
            if (!CheckReplay(seqNo, guessedIndex))
            {
                return false;
            }

            // Authenticate packet
            if (policy.AuthType != SrtpPolicy.NULL_AUTHENTICATION)
            {
                int tagLength = policy.AuthTagLength;

                // get original authentication and store in tempStore
                pkt.ReadRegionToBuff(pkt.GetLength() - tagLength, tagLength, tempStore);
                pkt.shrink(tagLength);

                // save computed authentication in tagStore
                AuthenticatePacketHMCSHA1(pkt, guessedROC);

                for (int i = 0; i < tagLength; i++)
                {
                    if ((tempStore[i] & 0xff) != (tagStore[i] & 0xff))
                    {
                        return false;
                    }
                }
            }

            // Decrypt packet
            switch (policy.EncType)
            {
                case SrtpPolicy.AESCM_ENCRYPTION:
                case SrtpPolicy.TWOFISH_ENCRYPTION:
                    // using Counter Mode encryption
                    ProcessPacketAESCM(pkt);
                    break;

                case SrtpPolicy.AESF8_ENCRYPTION:
                case SrtpPolicy.TWOFISHF8_ENCRYPTION:
                    // using F8 Mode encryption
                    ProcessPacketAESF8(pkt);
                    break;

                default:
                    return false;
            }
            Update(seqNo, guessedIndex);
            return true;
        }

        /**
         * Perform Counter Mode AES encryption / decryption
         * 
         * @param pkt
         *            the RTP packet to be encrypted / decrypted
         */
        public void ProcessPacketAESCM(RawPacket pkt)
        {
            long ssrc = pkt.GetSSRC();
            int seqNo = pkt.GetSequenceNumber();
#pragma warning disable CS0675 // Bitwise-or operator used on a sign-extended operand
            long index = ((long)roc << 16) | seqNo;
#pragma warning restore CS0675 // Bitwise-or operator used on a sign-extended operand

            ivStore[0] = saltKey[0];
            ivStore[1] = saltKey[1];
            ivStore[2] = saltKey[2];
            ivStore[3] = saltKey[3];

            int i;
            for (i = 4; i < 8; i++)
            {
                ivStore[i] = (byte)((0xFF & (ssrc >> ((7 - i) * 8))) ^ this.saltKey[i]);
            }

            for (i = 8; i < 14; i++)
            {
                ivStore[i] = (byte)((0xFF & (byte)(index >> ((13 - i) * 8))) ^ this.saltKey[i]);
            }

            ivStore[14] = ivStore[15] = 0;

            int payloadOffset = pkt.GetHeaderLength();
            int payloadLength = pkt.GetPayloadLength();

            cipherCtr.Process(cipher, pkt.GetBuffer(), payloadOffset, payloadLength, ivStore);
        }

        /**
         * Perform F8 Mode AES encryption / decryption
         * 
         * @param pkt
         *            the RTP packet to be encrypted / decrypted
         */
        public void ProcessPacketAESF8(RawPacket pkt)
        {
            // 11 bytes of the RTP header are the 11 bytes of the iv
            // the first byte of the RTP header is not used.
            MemoryStream buf = pkt.GetBuffer();
            buf.Read(ivStore, (int)buf.Position, 12);
            ivStore[0] = 0;

            // set the ROC in network order into IV
            ivStore[12] = (byte)(this.roc >> 24);
            ivStore[13] = (byte)(this.roc >> 16);
            ivStore[14] = (byte)(this.roc >> 8);
            ivStore[15] = (byte)this.roc;

            int payloadOffset = pkt.GetHeaderLength();
            int payloadLength = pkt.GetPayloadLength();

            SrtpCipherF8.Process(cipher, pkt.GetBuffer(), payloadOffset, payloadLength, ivStore, cipherF8);
        }

        byte[] tempBuffer = new byte[RawPacket.RTP_PACKET_MAX_SIZE];

        /**
         * Authenticate a packet. Calculated authentication tag is returned.
         * 
         * @param pkt
         *            the RTP packet to be authenticated
         * @param rocIn
         *            Roll-Over-Counter
         */
        private void AuthenticatePacketHMCSHA1(RawPacket pkt, int rocIn)
        {
            MemoryStream buf = pkt.GetBuffer();
            buf.Position = 0;
            int len = (int)buf.Length;
            buf.Read(tempBuffer, 0, len);
            mac.BlockUpdate(tempBuffer, 0, len);
            rbStore[0] = (byte)(rocIn >> 24);
            rbStore[1] = (byte)(rocIn >> 16);
            rbStore[2] = (byte)(rocIn >> 8);
            rbStore[3] = (byte)rocIn;
            mac.BlockUpdate(rbStore, 0, rbStore.Length);
            mac.DoFinal(tagStore, 0);
        }

        /**
         * Checks if a packet is a replayed on based on its sequence number.
         * 
         * This method supports a 64 packet history relative the the given sequence
         * number.
         * 
         * Sequence Number is guaranteed to be real (not faked) through
         * authentication.
         * 
         * @param seqNo
         *            sequence number of the packet
         * @param guessedIndex
         *            guessed roc
         * @return true if this sequence number indicates the packet is not a
         *         replayed one, false if not
         */
        bool CheckReplay(int seqNo, long guessedIndex)
        {
            // compute the index of previously received packet and its
            // delta to the new received packet
#pragma warning disable CS0675 // Bitwise-or operator used on a sign-extended operand
            long localIndex = (((long)roc) << 16) | this.seqNum;
#pragma warning restore CS0675 // Bitwise-or operator used on a sign-extended operand
            long delta = guessedIndex - localIndex;

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
         * based on the lable, the packet index, key derivation rate and master salt
         * key.
         * 
         * @param label
         *            label specified for each type of iv
         * @param index
         *            48bit RTP packet index
         */
        private void ComputeIv(long label, long index)
        {
            long key_id;

            if (keyDerivationRate == 0)
            {
                key_id = label << 48;
            }
            else
            {
                key_id = ((label << 48) | (index / keyDerivationRate));
            }
            for (int i = 0; i < 7; i++)
            {
                ivStore[i] = masterSalt[i];
            }
            for (int i = 7; i < 14; i++)
            {
                ivStore[i] = (byte)((byte)(0xFF & (key_id >> (8 * (13 - i)))) ^ masterSalt[i]);
            }
            ivStore[14] = ivStore[15] = 0;
        }

        /**
         * Derives the srtp session keys from the master key
         * 
         * @param index
         *            the 48 bit SRTP packet index
         */
        public void DeriveSrtpKeys(long index)
        {
            // compute the session encryption key
            long label = 0;
            ComputeIv(label, index);

            KeyParameter encryptionKey = new KeyParameter(masterKey);
            cipher.Init(true, encryptionKey);
            Arrays.Fill(masterKey, (byte)0);

            cipherCtr.GetCipherStream(cipher, encKey, policy.EncKeyLength, ivStore);

            // compute the session authentication key
            if (authKey != null)
            {
                label = 0x01;
                ComputeIv(label, index);
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
            label = 0x02;
            ComputeIv(label, index);
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
         * Compute (guess) the new SRTP index based on the sequence number of a
         * received RTP packet.
         * 
         * @param seqNo
         *            sequence number of the received RTP packet
         * @return the new SRTP packet index
         */
        private long GuessIndex(int seqNo)
        {
            if (this.seqNum < 32768)
            {
                if (seqNo - this.seqNum > 32768)
                {
                    guessedROC = roc - 1;
                }
                else
                {
                    guessedROC = roc;
                }
            }
            else
            {
                if (seqNum - 32768 > seqNo)
                {
                    guessedROC = roc + 1;
                }
                else
                {
                    guessedROC = roc;
                }
            }

#pragma warning disable CS0675 // Bitwise-or operator used on a sign-extended operand
            return ((long)guessedROC) << 16 | seqNo;
#pragma warning restore CS0675 // Bitwise-or operator used on a sign-extended operand
        }

        /**
         * Update the SRTP packet index.
         * 
         * This method is called after all checks were successful. See section 3.3.1
         * in RFC3711 for detailed description.
         * 
         * @param seqNo
         *            sequence number of the accepted packet
         * @param guessedIndex
         *            guessed roc
         */
        private void Update(int seqNo, long guessedIndex)
        {
#pragma warning disable CS0675 // Bitwise-or operator used on a sign-extended operand
            long delta = guessedIndex - (((long)this.roc) << 16 | this.seqNum);
#pragma warning restore CS0675 // Bitwise-or operator used on a sign-extended operand

            /* update the replay bit mask */
            if (delta > 0)
            {
                replayWindow = replayWindow << (int)delta;
                replayWindow |= 1;
            }
            else
            {
#pragma warning disable CS0675 // Bitwise-or operator used on a sign-extended operand
                replayWindow |= (1 << (int)delta);
#pragma warning restore CS0675 // Bitwise-or operator used on a sign-extended operand
            }

            if (seqNo > seqNum)
            {
                seqNum = seqNo & 0xffff;
            }
            if (this.guessedROC > this.roc)
            {
                roc = guessedROC;
                seqNum = seqNo & 0xffff;
            }
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
         * @param roc
         *            The Roll-Over-Counter for this context
         * @param deriveRate
         *            The key derivation rate for this context
         * @return a new SRTPCryptoContext with all relevant data set.
         */
        public SrtpCryptoContext deriveContext(long ssrc, int roc, long deriveRate)
        {
            return new SrtpCryptoContext(ssrc, roc, deriveRate, masterKey, masterSalt, policy);
        }
    }
}
