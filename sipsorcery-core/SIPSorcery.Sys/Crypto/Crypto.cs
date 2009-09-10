//-----------------------------------------------------------------------------
// Filename: Crypto.cs
//
// Description: Encrypts and decrypts data.
//
// History:
// 16 Jul 2005	Aaron Clauson	Created.
// 10 Sep 2009  Aaron Clauson   Updated to use RNGCryptoServiceProvider in place of Random.
//
// License:
// Aaron Clauson
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Security.Cryptography;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Sys {
    public class Crypto {
        public const int DEFAULT_RANDOM_LENGTH = 10;    // Number of digits to return for default random numbers.

        private static ILog logger = AppState.logger;

        private static RNGCryptoServiceProvider m_randomProvider = new RNGCryptoServiceProvider();

#if !SILVERLIGHT

        public static string RSAEncrypt(string xmlKey, string plainText) {
            return Convert.ToBase64String(RSAEncryptRaw(xmlKey, plainText));
        }

        public static byte[] RSAEncryptRaw(string xmlKey, string plainText) {
            try {
                RSACryptoServiceProvider key = GetKey(xmlKey);

                return key.Encrypt(Encoding.ASCII.GetBytes(plainText), true);
            }
            catch (Exception excp) {
                logger.Error("Exception RSAEncrypt. " + excp.Message);
                throw excp;
            }
        }

        public static string RSADecrypt(string xmlKey, string cypherText) {
            return Encoding.ASCII.GetString(RSADecryptRaw(xmlKey, Convert.FromBase64String((cypherText))));
        }

        public static byte[] RSADecryptRaw(string xmlKey, byte[] cypher) {
            try {
                RSACryptoServiceProvider key = GetKey(xmlKey);

                return key.Decrypt(cypher, true);
            }
            catch (Exception excp) {
                logger.Error("Exception RSADecrypt. " + excp.Message);
                throw excp;
            }
        }

        private static RSACryptoServiceProvider GetKey(string xmlKey) {
            CspParameters cspParam = new CspParameters();
            cspParam.Flags = CspProviderFlags.UseMachineKeyStore;
            RSACryptoServiceProvider key = new RSACryptoServiceProvider(cspParam);
            key.FromXmlString(xmlKey);

            return key;
        }

#endif

        public static string GetRandomString() {
            return GetRandomInt(DEFAULT_RANDOM_LENGTH).ToString();
        }

        /// <summary>
        /// Returns a 10 digit random number.
        /// </summary>
        /// <returns></returns>
        public static int GetRandomInt() {
            return GetRandomInt(DEFAULT_RANDOM_LENGTH);
        }

        /// <summary>
        /// Returns a random number of a specified length.
        /// </summary>
        public static int GetRandomInt(int length) {
            int randomStart = 1000000000;
            int randomEnd = Int32.MaxValue;

            if (length > 0 && length < DEFAULT_RANDOM_LENGTH) {
                randomStart = Convert.ToInt32(Math.Pow(10, length - 1));
                randomEnd = Convert.ToInt32(Math.Pow(10, length) - 1);
            }

            return GetRandomInt(randomStart, randomEnd);
        }

        public static Int32 GetRandomInt(Int32 minValue, Int32 maxValue) {

            if (minValue > maxValue) {
                throw new ArgumentOutOfRangeException("minValue");
            }
            else if (minValue == maxValue) {
                return minValue;
            }

            Int64 diff = maxValue - minValue + 1;
            int attempts = 0;
            while (attempts < 10) {
                byte[] uint32Buffer = new byte[4];
                m_randomProvider.GetBytes(uint32Buffer);
                UInt32 rand = BitConverter.ToUInt32(uint32Buffer, 0);

                Int64 max = (1 + (Int64)UInt32.MaxValue);
                Int64 remainder = max % diff;
                if (rand <= max - remainder) {
                    return (Int32)(minValue + (rand % diff));
                }
                attempts++;
            }
            throw new ApplicationException("GetRandomInt did not return an appropriate random number within 10 attempts.");
        }

        public static string GetRandomString(int length) {
            string randomStr = GetRandomInt(length).ToString();
            return (randomStr.Length > length) ? randomStr.Substring(0, length) : randomStr;
        }

        public static byte[] createRandomSalt(int length) {
            byte[] randBytes = new byte[length];
            m_randomProvider.GetBytes(randBytes);
            return randBytes;
        }

        /// <summary>
        /// This vesion reads the whole file in at once. This is not great since it can consume
        /// a lot of memory if the file is large. However a buffered approach generates
        /// diferrent hashes across different platforms.
        /// </summary>
        /// <param name="filepath"></param>
        /// <returns></returns>
        public static string GetHash(string filepath) {
            // Check and then attempt to open the plaintext stream.
            FileStream fileStream = GetFileStream(filepath);

            // Encrypt the file using its hash as the key.
            SHA1 shaM = new SHA1Managed();

            // Buffer to read in plain text blocks.
            byte[] fileBuffer = new byte[fileStream.Length];
            fileStream.Read(fileBuffer, 0, (int)fileStream.Length);
            fileStream.Close();

            byte[] overallHash = shaM.ComputeHash(fileBuffer);

            return Convert.ToBase64String(overallHash);
        }

        /// <summary>
        /// Used by methods wishing to perform a hash operation on a file. This method
        /// will perform a number of checks and if happy return a read only file stream.
        /// </summary>
        /// <param name="filepath">The path to the input file for the hash operation.</param>
        /// <returns>A read only file stream for the file or throws an exception if there is a problem.</returns>
        private static FileStream GetFileStream(string filepath) {
            // Check that the file exists.
            if (!File.Exists(filepath)) {
                logger.Error("Cannot open a non-existent file for a hash operation, " + filepath + ".");
                throw new IOException("Cannot open a non-existent file for a hash operation, " + filepath + ".");
            }

            // Open the file.
            FileStream inputStream = File.OpenRead(filepath);

            if (inputStream.Length == 0) {
                inputStream.Close();
                logger.Error("Cannot perform a hash operation on an empty file, " + filepath + ".");
                throw new IOException("Cannot perform a hash operation on an empty file, " + filepath + ".");
            }

            return inputStream;
        }

        /// <summary>
        /// Gets an "X2" string representation of a random number.
        /// </summary>
        /// <param name="byteLength">The byte length of the random number string to obtain.</param>
        /// <returns>A string representation of the random number. It will be twice the length of byteLength.</returns>
        public static string GetRandomByteString(int byteLength) {
            byte[] myKey = new byte[byteLength];
            m_randomProvider.GetBytes(myKey);
            string sessionID = null;
            myKey.ToList().ForEach(b => sessionID += b.ToString("x2"));
            return sessionID;
        }

        public static byte[] GetSHAHash(params string[] values) {
            SHA1 sha = SHA1.Create();
            string plainText = null;
            foreach (string value in values) {
                plainText += value;
            }
            return sha.ComputeHash(Encoding.UTF8.GetBytes(plainText));
        }

        public static string GetSHAHashAsString(params string[] values) {
            return Convert.ToBase64String(GetSHAHash(values));
        }

        /// <summary>
        /// Returns the hash with each byte as an X2 string. This is useful for situations where
        /// the hash needs to only contain safe ASCII characters.
        /// </summary>
        /// <param name="values">The list of string to concantenate and hash.</param>
        /// <returns>A string with "safe" (0-9 and A-F) characters representing the hash.</returns>
        public static string GetSHAHashAsHex(params string[] values) {
            byte[] hash = GetSHAHash(values);
            string hashStr = null;
            hash.ToList().ForEach(b => hashStr += b.ToString("x2"));
            return hashStr;
        }

         #region Unit testing.

        #if UNITTEST

        [TestFixture]
        public class CryptoUnitTest {
            [TestFixtureSetUp]
            public void Init() {

            }

            [TestFixtureTearDown]
            public void Dispose() {

            }

            [Test]
            public void SampleTest() {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
                int initRandomNumber = Crypto.GetRandomInt();
                Console.WriteLine("Random int = " + initRandomNumber + ".");
                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void CallRandomNumberWebServiceUnitTest() {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                Console.WriteLine("Random number = " + GetRandomInt());

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void GetRandomNumberTest() {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                Console.WriteLine("Random number = " + GetRandomInt());

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void GetOneHundredRandomNumbersTest() {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                for (int index = 0; index < 100; index++) {
                    Console.WriteLine("Random number = " + GetRandomInt());
                }

                Console.WriteLine("-----------------------------------------");
            }
        }

        #endif

        #endregion
    }
}
