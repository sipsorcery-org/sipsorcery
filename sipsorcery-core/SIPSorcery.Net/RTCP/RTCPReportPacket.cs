//-----------------------------------------------------------------------------
// Filename: RTCPReportPacket.cs
//
//      Custom RTCP Report Packet
//        0                   1                   2                   3
//        0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//header |      Report Type              |             Length            |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                         Report                                |
//       +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
// 
// History:
// 23 Feb 2007	Aaron Clauson	Created.
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
	public class RTCPReportPacket
	{
        public RTCPReportHeader Header;                               // 32 bits.
        public byte[] Report;

        public RTCPReportPacket(RTCPReportTypesEnum reportType, byte[] report)
		{
            Header = new RTCPReportHeader(reportType, Convert.ToUInt16(report.Length));
            Report = report;
		}

        public RTCPReportPacket(byte[] packet)
		{
            Header = new RTCPReportHeader(packet);
            Report = new byte[Header.Length];
            Array.Copy(packet, RTCPReportHeader.HEADER_BYTES_LENGTH, Report, 0, Report.Length);
		}

		public byte[] GetBytes()
		{
            byte[] packet = new byte[RTCPReportHeader.HEADER_BYTES_LENGTH + Report.Length];
            byte[] headerBytes = Header.GetBytes();

            Array.Copy(headerBytes, packet, headerBytes.Length);
            Array.Copy(Report, 0, packet, headerBytes.Length, Report.Length);

			return packet;
		}

        #region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class RTCPReportPacketUnitTest
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
