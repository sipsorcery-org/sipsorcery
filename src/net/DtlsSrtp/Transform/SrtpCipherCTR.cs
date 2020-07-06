//-----------------------------------------------------------------------------
// Filename: SrtpCipherCTR.cs
//
// Description: Implements SRTP Counter Mode Encryption.
//
// Derived From:
// https://github.com/jitsi/jitsi-srtp/blob/master/src/main/java/org/jitsi/srtp/crypto/SrtpCipherCtr.java
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
 * Copyright @ 2016 - present 8x8, Inc
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
 */

/**
 * SRTPCipherF8 implements SRTP F8 Mode AES Encryption (AES-f8).
 * F8 Mode AES Encryption algorithm is defined in RFC3711, section 4.1.2.
 * 
 * Other than Null Cipher, RFC3711 defined two encryption algorithms:
 * Counter Mode AES Encryption and F8 Mode AES encryption. Both encryption
 * algorithms are capable to encrypt / decrypt arbitrary length data, and the
 * size of packet data is not required to be a multiple of the AES block 
 * size (128bit). So, no padding is needed.
 * 
 * Please note: these two encryption algorithms are specially defined by SRTP.
 * They are not common AES encryption modes, so you will not be able to find a 
 * replacement implementation in common cryptographic libraries. 
 * 
 * As defined by RFC3711: F8 mode encryption is optional.
 *
 *                        mandatory to impl     optional      default
 * -------------------------------------------------------------------------
 *   encryption           AES-CM, NULL          AES-f8        AES-CM
 *   message integrity    HMAC-SHA1                -          HMAC-SHA1
 *   key derivation       (PRF) AES-CM             -          AES-CM 
 *
 * We use AESCipher to handle basic AES encryption / decryption.
 * 
 * @author Bing SU (nova.su@gmail.com)
 * @author Werner Dittmann <werner.dittmann@t-online.de>
 */

using System.IO;
using Org.BouncyCastle.Crypto;

namespace SIPSorcery.Net
{
    /**
     * SRTPCipherCTR implements SRTP Counter Mode AES Encryption (AES-CM).
     * Counter Mode AES Encryption algorithm is defined in RFC3711, section 4.1.1.
     * 
     * Other than Null Cipher, RFC3711 defined two two encryption algorithms:
     * Counter Mode AES Encryption and F8 Mode AES encryption. Both encryption
     * algorithms are capable to encrypt / decrypt arbitrary length data, and the
     * size of packet data is not required to be a multiple of the AES block 
     * size (128bit). So, no padding is needed.
     * 
     * Please note: these two encryption algorithms are specially defined by SRTP.
     * They are not common AES encryption modes, so you will not be able to find a 
     * replacement implementation in common cryptographic libraries. 
     *
     * As defined by RFC3711: Counter Mode Encryption is mandatory..
     *
     *                        mandatory to impl     optional      default
     * -------------------------------------------------------------------------
     *   encryption           AES-CM, NULL          AES-f8        AES-CM
     *   message integrity    HMAC-SHA1                -          HMAC-SHA1
     *   key derivation       (PRF) AES-CM             -          AES-CM 
     *
     * We use AESCipher to handle basic AES encryption / decryption.
     * 
     * @author Werner Dittmann (Werner.Dittmann@t-online.de)
     * @author Bing SU (nova.su@gmail.com)
     */
    public class SrtpCipherCTR
    {
        private const int BLKLEN = 16;
        private const int MAX_BUFFER_LENGTH = 10 * 1024;
        private byte[] cipherInBlock = new byte[BLKLEN];
        private byte[] tmpCipherBlock = new byte[BLKLEN];
        private byte[] streamBuf = new byte[1024];

        public void Process(IBlockCipher cipher, MemoryStream data, int off, int len, byte[] iv)
        {
            // if data fits in inner buffer - use it. Otherwise allocate bigger
            // buffer store it to use it for later processing - up to a defined
            // maximum size.
            byte[] cipherStream = null;
            if (len > streamBuf.Length)
            {
                cipherStream = new byte[len];
                if (cipherStream.Length <= MAX_BUFFER_LENGTH)
                {
                    streamBuf = cipherStream;
                }
            }
            else
            {
                cipherStream = streamBuf;
            }

            GetCipherStream(cipher, cipherStream, len, iv);
            for (int i = 0; i < len; i++)
            {
                data.Position = i + off;
                var byteToWrite = data.ReadByte();
                data.Position = i + off;
                data.WriteByte((byte)(byteToWrite ^ cipherStream[i]));
            }
        }

        /**
         * Computes the cipher stream for AES CM mode. See section 4.1.1 in RFC3711
         * for detailed description.
         * 
         * @param out
         *            byte array holding the output cipher stream
         * @param length
         *            length of the cipher stream to produce, in bytes
         * @param iv
         *            initialization vector used to generate this cipher stream
         */
        public void GetCipherStream(IBlockCipher aesCipher, byte[] _out, int length, byte[] iv)
        {
            System.Array.Copy(iv, 0, cipherInBlock, 0, 14);

            int ctr;
            for (ctr = 0; ctr < length / BLKLEN; ctr++)
            {
                // compute the cipher stream
                cipherInBlock[14] = (byte)((ctr & 0xFF00) >> 8);
                cipherInBlock[15] = (byte)((ctr & 0x00FF));

                aesCipher.ProcessBlock(cipherInBlock, 0, _out, ctr * BLKLEN);
            }

            // Treat the last bytes:
            cipherInBlock[14] = (byte)((ctr & 0xFF00) >> 8);
            cipherInBlock[15] = (byte)((ctr & 0x00FF));

            aesCipher.ProcessBlock(cipherInBlock, 0, tmpCipherBlock, 0);
            System.Array.Copy(tmpCipherBlock, 0, _out, ctr * BLKLEN, length % BLKLEN);
        }
    }
}

