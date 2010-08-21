//-----------------------------------------------------------------------------
// Filename: ByteBuffer.cs
//
// Description: Provides some useful methods for working with byte[] buffers.
//
// History:
// 04 May 2006	Aaron Clauson	Created.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Text;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Sys
{
	public class ByteBufferInfo
	{
		/// <summary>
		/// Searches a buffer for a string up until a specified end string.
		/// </summary>
		/// <param name="buffer">The byte array to search for an instance of the specified string.</param>
        /// <param name="startPosition">The position in the array that the search should be started from.</param>
        /// <param name="endPosition">An index that if reached indicates the search should be halted.</param>
		/// <param name="find">The string that is being searched for.</param>
		/// <param name="end">If the end string is found the search is halted and a negative result returned.</param>
		/// <returns>The start position in the buffer of the requested string or -1 if not found.</returns>
		public static int GetStringPosition(byte[] buffer, int startPosition, int endPosition, string find, string end)
		{
			if(buffer == null || buffer.Length == 0 || find == null)
			{
                return -1;
			}
			else
			{
				byte[] findArray = Encoding.UTF8.GetBytes(find);
				byte[] endArray = (end != null) ? Encoding.UTF8.GetBytes(end) : null;

				int findPosn = 0;
				int endPosn = 0;

                for (int index = startPosition; index < endPosition; index++)
				{
					if(buffer[index] == findArray[findPosn])
					{
						findPosn++;
					}
					else
					{
						findPosn = 0;
					}

					if(endArray != null && buffer[index] == endArray[endPosn])
					{
						endPosn++;
					}
					else
					{
						endPosn = 0;
					}

					if(findPosn == findArray.Length)
					{
						return index - findArray.Length + 1;
					}
					else if(endArray != null && endPosn == endArray.Length)
					{
						return -1;
					}
				}

				return -1;
			}
		}

        public static bool HasString(byte[] buffer, int startPosition, int endPosition, string find, string end)
        {
            return GetStringPosition(buffer, startPosition, endPosition, find, end) != -1;
        }

		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class SIPURIUnitTest
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
			public void HasStringUnitTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

				byte[] sample = Encoding.ASCII.GetBytes("The quick brown fox jumped over...");

				bool hasFox = ByteBufferInfo.HasString(sample, 0, Int32.MaxValue, "fox", null);
				
				Assert.IsTrue(hasFox, "The string was not found in the buffer.");
			}

			[Test]
			public void NotBeforeEndUnitTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

				byte[] sample = Encoding.ASCII.GetBytes("The quick brown fox jumped over...");

                bool hasFox = ByteBufferInfo.HasString(sample, 0, Int32.MaxValue, "fox", "brown");
				
				Assert.IsTrue(!hasFox, "The string was not found in the buffer.");
			}

            [Test]
            public void GetStringIndexUnitTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string sipMsg =
                    "REGISTER sip:Blue Face SIP/2.0\r\n" +
                    "Via: SIP/2.0/UDP 127.0.0.1:1720;branch=z9hG4bKlgnUQcaywCOaPcXR\r\n" +
                    "Max-Forwards: 70\r\n" +
                    "User-Agent: PA168S\r\n" +
                    "From: \"user\" <sip:user@Blue Face>;tag=81swjAV7dHG1yjd5\r\n" +
                    "To: \"user\" <sip:user@Blue Face>\r\n" +
                    "Call-ID: DHZVs1HFuMoTQ6LO@82.114.95.1\r\n" +
                    "CSeq: 15754 REGISTER\r\n" +
                    "Contact: <sip:user@127.0.0.1:1720>\r\n" +
                    "Expires: 30\r\n" +
                    "Content-Length: 0\r\n\r\n";
                
                byte[] sample = Encoding.ASCII.GetBytes(sipMsg);

                int endOfMsgIndex = ByteBufferInfo.GetStringPosition(sample, 0, Int32.MaxValue, "\r\n\r\n", null);

                Assert.IsTrue(endOfMsgIndex == sample.Length - 4, "The string position was not correctly found in the buffer.");
            }

            [Test]
            public void GetStringIndexSIPInviteUnitTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string sipMsg =
                     "INVITE sip:12345@sip.domain.com:5060;TCID-0 SIP/2.0\r\n" +
                     "From: UNAVAILABLE<sip:user@sip.domain.com:5060>;tag=c0a83dfe-13c4-26bf01-975a21d0-2d8a\r\n" +
                     "To: <sip:1234@sipdomain.com:5060>\r\n" +
                     "Call-ID: 94b6e3f8-c0a83dfe-13c4-26bf01-975a21ce-52c@sip.domain.com\r\n" +
                     "CSeq: 1 INVITE\r\n" +
                     "Via: SIP/2.0/UDP 86.9.84.23:5060;branch=z9hG4bK-26bf01-975a21d0-1ffb\r\n" +
                     "Max-Forwards: 70\r\n" +
                     "User-Agent: TA612V-V1.2_54\r\n" +
                     "Supported: timer,replaces\r\n" +
                     "Contact: <sip:user@88.8.88.88:5060>\r\n" +
                     "Content-Type: application/SDP\r\n" +
                     "Content-Length: 386\r\n" +
                     "\r\n" +
                     "v=0\r\n" +
                     "o=b0000 613 888 IN IP4 88.8.88.88\r\n" +
                     "s=SIP Call\r\n" +
                     "c=IN IP4 88.8.88.88\r\n" +
                     "t=0 0\r\n" +
                     "m=audio 10000 RTP/AVP 0 101 18 100 101 2 103 8\r\n" +
                     "a=fmtp:101 0-15\r\n" +
                     "a=fmtp:18 annexb=no\r\n" +
                     "a=sendrecv\r\n" +
                     "a=rtpmap:0 PCMU/8000\r\n" +
                     "a=rtpmap:101 telephone-event/8000\r\n" +
                     "a=rtpmap:18 G729/8000\r\n" +
                     "a=rtpmap:100 G726-16/8000\r\n" +
                     "a=rtpmap:101 G726-24/8000\r\n" +
                     "a=rtpmap:2 G726-32/8000\r\n" +
                     "a=rtpmap:103 G726-40/8000\r\n" +
                     "a=rtpmap:8 PCMA/8000";

                byte[] sample = Encoding.ASCII.GetBytes(sipMsg);

                int endOfMsgIndex = ByteBufferInfo.GetStringPosition(sample, 0, Int32.MaxValue, "\r\n\r\n", null);

                Assert.IsTrue(endOfMsgIndex == sipMsg.IndexOf("\r\n\r\n"), "The string position was not correctly found in the buffer. Index found was " + endOfMsgIndex + ", should have been " + sipMsg.IndexOf("\r\n\r\n") + ".");
            }

            [Test]
            public void GetStringIndexNotFoundUnitTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string sipMsg =
                    "REGISTER sip:Blue Face SIP/2.0\r\n" +
                    "Via: SIP/2.0/UDP 127.0.0.1:1720;branch=z9hG4bKlgnUQcaywCOaPcXR\r\n" +
                    "Max-Forwards: 70\r\n" +
                    "User-Agent: PA168S\r\n" +
                    "From: \"user\" <sip:user@Blue Face>;tag=81swjAV7dHG1yjd5\r\n" +
                    "To: \"user\" <sip:user@Blue Face>\r\n" +
                    "Call-ID: DHZVs1HFuMoTQ6LO@82.114.95.1\r\n" +
                    "CSeq: 15754 REGISTER\r\n" +
                    "Contact: <sip:user@127.0.0.1:1720>\r\n" +
                    "Expires: 30\r\n" +
                    "Content-Length: 0\r\n";

                byte[] sample = Encoding.ASCII.GetBytes(sipMsg);

                int endOfMsgIndex = ByteBufferInfo.GetStringPosition(sample, 0, Int32.MaxValue, "\r\n\r\n", null);

                Assert.IsTrue(endOfMsgIndex == -1, "The string position was not correctly found in the buffer.");
            }
		}

		#endif

		#endregion
	}
}
