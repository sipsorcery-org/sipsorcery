//-----------------------------------------------------------------------------
// Filename: RTPPacket.cs
//
// Description: Encapsulation of an RTP packet.
// 
// History:
// 24 May 2005	Aaron Clauson	Created.
//
// License: 
// Aaron Clauson
//-----------------------------------------------------------------------------

using System;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Net
{
	public class RTPPacket
	{
		public RTPHeader Header;
		public byte[] Payload;

		public RTPPacket()
		{
			Header = new RTPHeader();
		}

		public RTPPacket(int payloadSize)
		{
			Header = new RTPHeader();
			Payload = new byte[payloadSize]; //GetNullPayload(payloadSize);
		}
		
		public RTPPacket(byte[] packet)
		{
			Header = new RTPHeader(packet);	
		}

		public byte[] GetBytes()
		{
			byte[] header = Header.GetBytes();
			byte[] payload = Payload;

			byte[] packet = new byte[header.Length + payload.Length];
			
			Array.Copy(header, packet, header.Length);
			Array.Copy(payload, 0, packet, header.Length, payload.Length);

			return packet;
		}

		private byte[] GetNullPayload(int numBytes)
		{
			byte[] payload = new byte[numBytes]; 
			
			for(int byteCount=0; byteCount<numBytes; byteCount++)
			{
				payload[byteCount] = 0xff;
			}

			return payload;
		}

        #region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class RTPPacketUnitTest
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
		}

		#endif

		#endregion
	}
}
