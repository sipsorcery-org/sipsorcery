//-----------------------------------------------------------------------------
// Filename: Utilities.cs
//
// Description: Useful functions for VoIP protocol implementation.
//
// History:
// 23 May 2005	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Sys
{
	public class NetConvert
	{
		public static UInt16 DoReverseEndian(UInt16 x) 
		{
			//return Convert.ToUInt16(x << 8 & 0x00ff00 | (x >> 8));
            return Convert.ToUInt16((x << 8 & 0xff00) | (x >> 8));
            //return Convert.ToUInt16((x << 8) | (x >> 8));
		}
		
		public static uint DoReverseEndian(uint x) 
		{
			return (x << 24 | (x & 0xff00) << 8 | (x & 0xff0000) >> 8 | x >> 24);
		}

        public static ulong DoReverseEndian(ulong x)
        {
            return (x << 56 | (x & 0xff00) << 40 | (x & 0xff0000) << 24 | (x & 0xff000000) << 8 | (x & 0xff00000000) >> 8 | (x & 0xff0000000000) >> 24 | (x & 0xff000000000000) >> 40 | x >> 56);
        }

		/*public static byte[] GetRandomPayload(int length)
		{
			byte[] randomPayload = new byte[length];
			
			Random rnd = new Random(DateTime.Now.Millisecond);
			
			int randomStart = 0;
			int randomEnd = int.MaxValue;
			
			while(length > 0)
			{
				if(length < 32)
				{
					randomStart = Math.Pow(10, length);
				}

				randomNum = rnd.Next(randomStart, randomEnd);
				byte[] randomBuffer = ConvertToBuffer(randomNum);

				Array.Copy(randomBuffer, 0, randomPayload, payloadIndex, randomBuffer.Length);
				
				length -= randomBuffer.Length;
				payloadIndex += randomBuffer.Length;
			}

			return randomPayload;
		}*/

		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class NetConvertUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{
				
			}

			[TestFixtureTearDown]
			public void Dispose()
			{			
				
			}

			[Test]
			public void SampleTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				Assert.IsTrue(true, "True was false.");
			}

            [Test]
            public void ReverseUInt16SampleTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                ushort testNum = 45677;
                byte[] testNumBytes = BitConverter.GetBytes(testNum);

                ushort reversed = NetConvert.DoReverseEndian(testNum);
                byte[] reversedNumBytes = BitConverter.GetBytes(reversed);

                ushort unReversed = NetConvert.DoReverseEndian(reversed);

                int testNumByteCount = 0;
                foreach (byte testNumByte in testNumBytes)
                {
                    Console.WriteLine("original " + testNumByteCount + ": " + testNumByte.ToString("x"));
                    testNumByteCount++;
                }

                int reverseNumByteCount = 0;
                foreach (byte reverseNumByte in reversedNumBytes)
                {
                    Console.WriteLine("reversed " + reverseNumByteCount + ": " + reverseNumByte.ToString("x"));
                    reverseNumByteCount++;
                }

                Console.WriteLine("Original=" + testNum);
                Console.WriteLine("Reversed=" + reversed);
                Console.WriteLine("Unreversed=" + unReversed);

                Assert.IsTrue(testNum == unReversed, "Reverse endian operation for uint16 did not work successfully.");
            }

            [Test]
            public void ReverseUInt32SampleTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                uint testNum = 123124;
                byte[] testNumBytes = BitConverter.GetBytes(testNum);

                uint reversed = NetConvert.DoReverseEndian(testNum);
                byte[] reversedNumBytes = BitConverter.GetBytes(reversed);

                uint unReversed = NetConvert.DoReverseEndian(reversed);

                int testNumByteCount = 0;
                foreach (byte testNumByte in testNumBytes)
                {
                    Console.WriteLine("original " + testNumByteCount + ": " + testNumByte.ToString("x"));
                    testNumByteCount++;
                }

                int reverseNumByteCount = 0;
                foreach (byte reverseNumByte in reversedNumBytes)
                {
                    Console.WriteLine("reversed " + reverseNumByteCount + ": " + reverseNumByte.ToString("x"));
                    reverseNumByteCount++;
                }

                Console.WriteLine("Original=" + testNum);
                Console.WriteLine("Reversed=" + reversed);
                Console.WriteLine("Unreversed=" + unReversed);

                Assert.IsTrue(testNum == unReversed, "Reverse endian operation for uint32 did not work successfully.");
            }

            [Test]
            public void ReverseUInt64SampleTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                ulong testNum = 1231265499856464;
                byte[] testNumBytes = BitConverter.GetBytes(testNum);

                ulong reversed = NetConvert.DoReverseEndian(testNum);
                byte[] reversedNumBytes = BitConverter.GetBytes(reversed);

                ulong unReversed = NetConvert.DoReverseEndian(reversed);

                int testNumByteCount = 0;
                foreach (byte testNumByte in testNumBytes)
                {
                    Console.WriteLine("original " + testNumByteCount + ": " + testNumByte.ToString("x"));
                    testNumByteCount++;
                }

                int reverseNumByteCount = 0;
                foreach (byte reverseNumByte in reversedNumBytes)
                {
                    Console.WriteLine("reversed " + reverseNumByteCount + ": " + reverseNumByte.ToString("x"));
                    reverseNumByteCount++;
                }

                Console.WriteLine("Original=" + testNum);
                Console.WriteLine("Reversed=" + reversed);
                Console.WriteLine("Unreversed=" + unReversed);

                Assert.IsTrue(testNum == unReversed, "Reverse endian operation for uint64 did not work successfully.");
            }
		}

		#endif

		#endregion

	}
}
