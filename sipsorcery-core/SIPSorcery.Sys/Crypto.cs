//-----------------------------------------------------------------------------
// Filename: Crypto.cs
//
// Description: Encrypts and decrypts data.
//
// History:
// 16 Jul 2005	Aaron Clauson	Created.
//
// License:
// Aaron Clauson
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using log4net;

namespace Aza.Configuration
{
	public class Crypto
	{	
		private static ILog logger = AppState.logger;
		
		public static string RSAEncrypt(string xmlKey, string plainText)
		{
			return Convert.ToBase64String(RSAEncryptRaw(xmlKey, plainText));
		}

		public static byte[] RSAEncryptRaw(string xmlKey, string plainText)
		{
			try
			{
				RSACryptoServiceProvider key = GetKey(xmlKey);
				
				return key.Encrypt(Encoding.ASCII.GetBytes(plainText), true);
			}
			catch(Exception excp)
			{
				logger.Error("Exception RSAEncrypt. " + excp.Message);
				throw excp;
			}
		}

	
		public static string RSADecrypt(string xmlKey, string cypherText)
		{
			return Encoding.ASCII.GetString(RSADecryptRaw(xmlKey, Convert.FromBase64String((cypherText))));
		}

	
		public static byte[] RSADecryptRaw(string xmlKey, byte[] cypher)
		{
			try
			{
				RSACryptoServiceProvider key = GetKey(xmlKey);		

				return key.Decrypt(cypher, true);
			}
			catch(Exception excp)
			{
				logger.Error("Exception RSADecrypt. " + excp.Message);
				throw excp;
			}
		}

	
		private static RSACryptoServiceProvider GetKey(string xmlKey)
		{
			CspParameters cspParam = new CspParameters();
			cspParam.Flags = CspProviderFlags.UseMachineKeyStore;
			RSACryptoServiceProvider key = new RSACryptoServiceProvider(cspParam);
			key.FromXmlString(xmlKey);

			return key;
		}

	
		public static string GetRandomString()
		{
			Random rnd = new Random(Convert.ToInt32(DateTime.Now.Ticks % Int32.MaxValue));

			int randomStart = 1000000000; 
			int randomEnd = Int32.MaxValue;

			return rnd.Next(randomStart, randomEnd).ToString();
		}

		public static int GetRandomInt()
		{
			Random rnd = new Random(Convert.ToInt32(DateTime.Now.Ticks % Int32.MaxValue));

			int randomStart = 1000000000;
			int randomEnd = Int32.MaxValue;

			return rnd.Next(randomStart, randomEnd);
		}

		public static byte[] createRandomSalt(int Length)
		{
			// Create a buffer
			byte[] randBytes;

			if (Length >= 1)
			{
				randBytes = new byte[Length];
			}
			else
			{
				randBytes = new byte[1];
			}

			// Create a new RNGCryptoServiceProvider.
			RNGCryptoServiceProvider rand = new RNGCryptoServiceProvider();

			// Fill the buffer with random bytes.
			rand.GetBytes(randBytes);

			// return the bytes.
			return randBytes;
		}

		public static void clearBytes(byte[] Buffer)
		{
			// Check arguments.
			if (Buffer == null)
			{
				throw new ArgumentException("Buffer");
			}

			// Set each byte in the buffer to 0.
			for (int x = 0; x <= Buffer.Length - 1; x++)
			{
				Buffer[x] = 0;
			}
		}

		public static string GetSHAHash(string plainText)
		{
			SHA1Managed shaHash = new SHA1Managed();
			
			return Convert.ToBase64String(shaHash.ComputeHash(Encoding.ASCII.GetBytes(plainText)));
		}

		/// <summary>
		/// This vesion reads the whole file in at once. This is not great since it can consume
		/// a lot of memory if the file is large. However a buffered approach generates
		/// diferrent hashes across different platforms.
		/// </summary>
		/// <param name="filepath"></param>
		/// <returns></returns>
		public static string GetHash(string filepath)
		{
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
		private static FileStream GetFileStream(string filepath)
		{
			// Check that the file exists.
			if(!File.Exists(filepath))
			{
				logger.Error("Cannot open a non-existent file for a hash operation, " + filepath + ".");
				throw new IOException("Cannot open a non-existent file for a hash operation, " + filepath + ".");
			}

			// Open the file.
			FileStream inputStream = File.OpenRead(filepath);

			if(inputStream.Length == 0)
			{
				inputStream.Close();
				logger.Error("Cannot perform a hash operation on an empty file, " + filepath + ".");
				throw new IOException("Cannot perform a hash operation on an empty file, " + filepath + ".");
			}

			return inputStream;
		}
	}
}
