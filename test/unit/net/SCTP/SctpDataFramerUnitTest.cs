//-----------------------------------------------------------------------------
// Filename: SctpDataFramerUnitTest.cs
//
// Description: Unit tests for the SctpChunk class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 30 Mar 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class SctpDataFramerUnitTest
    {
        private ILogger logger = null;

        public SctpDataFramerUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a single packet frame gets processed correctly.
        /// </summary>
        [Fact]
        public void SinglePacketFrame()
        {
            SctpDataFramer framer = new SctpDataFramer(0, 0, 0);

            SctpDataChunk chunk = new SctpDataChunk(false, true, true, 0, 0, 0, 0, new byte[] { 0x00 });

            var sortedFrames = framer.OnDataChunk(chunk);

            Assert.Single(sortedFrames);
            Assert.Equal("00", sortedFrames.Single().UserData.HexStr());
            Assert.Equal(0U, framer.CumulativeAckTSN);
            Assert.Equal(0, framer.ForwardTSNCount);
        }

        /// <summary>
        /// Tests that a chunk fragmented across three packets gets processed correctly.
        /// </summary>
        [Fact]
        public void ThreeFragments()
        {
            SctpDataFramer framer = new SctpDataFramer(0, 0, 0);

            SctpDataChunk chunk1 = new SctpDataChunk(false, true, false, 0, 0, 0, 0, new byte[] { 0x00 });
            SctpDataChunk chunk2 = new SctpDataChunk(false, false, false, 1, 0, 0, 0, new byte[] { 0x01 });
            SctpDataChunk chunk3 = new SctpDataChunk(false, false, true, 2, 0, 0, 0, new byte[] { 0x02 });

            var sortFrames1 = framer.OnDataChunk(chunk1);
            Assert.Equal(0U, framer.CumulativeAckTSN);
            var sortFrames2 = framer.OnDataChunk(chunk2);
            Assert.Equal(1U, framer.CumulativeAckTSN);
            var sortFrames3 = framer.OnDataChunk(chunk3);
            Assert.Equal(2U, framer.CumulativeAckTSN);

            Assert.Empty(sortFrames1);
            Assert.Empty(sortFrames2);
            Assert.Single(sortFrames3);
            Assert.Equal("000102", sortFrames3.Single().UserData.HexStr());
            Assert.Equal(0, framer.ForwardTSNCount);
        }

        /// <summary>
        /// Tests that a fragmented chunk spread across three packets, and received out of order,
        /// gets processed correctly.
        /// </summary>
        [Fact]
        public void ThreeFragmentsOutOfOrder()
        {
            SctpDataFramer framer = new SctpDataFramer(0, 0, 0);

            SctpDataChunk chunk1 = new SctpDataChunk(false, true, false, 0, 0, 0, 0, new byte[] { 0x00 });
            SctpDataChunk chunk2 = new SctpDataChunk(false, false, false, 1, 0, 0, 0, new byte[] { 0x01 });
            SctpDataChunk chunk3 = new SctpDataChunk(false, false, true, 2, 0, 0, 0, new byte[] { 0x02 });

            var sortFrames1 = framer.OnDataChunk(chunk1);
            Assert.Equal(0U, framer.CumulativeAckTSN);
            var sortFrames2 = framer.OnDataChunk(chunk3);
            Assert.Equal(0U, framer.CumulativeAckTSN);
            var sortFrames3 = framer.OnDataChunk(chunk2);
            Assert.Equal(2U, framer.CumulativeAckTSN);

            Assert.Empty(sortFrames1);
            Assert.Empty(sortFrames2);
            Assert.Single(sortFrames3);
            Assert.Equal("000102", sortFrames3.Single().UserData.HexStr());
            Assert.Equal(0, framer.ForwardTSNCount);
        }

        /// <summary>
        /// Tests that a fragmented chunk with the beginning chunk received last
        /// gets processed correctly.
        /// </summary>
        [Fact]
        public void ThreeFragmentsBeginLast()
        {
            SctpDataFramer framer = new SctpDataFramer(0, 0, 0);

            SctpDataChunk chunk1 = new SctpDataChunk(false, true, false, 0, 0, 0, 0, new byte[] { 0x00 });
            SctpDataChunk chunk2 = new SctpDataChunk(false, false, false, 1, 0, 0, 0, new byte[] { 0x01 });
            SctpDataChunk chunk3 = new SctpDataChunk(false, false, true, 2, 0, 0, 0, new byte[] { 0x02 });

            var sortFrames1 = framer.OnDataChunk(chunk3);
            Assert.Null(framer.CumulativeAckTSN);
            var sortFrames2 = framer.OnDataChunk(chunk2);
            Assert.Null(framer.CumulativeAckTSN);
            var sortFrames3 = framer.OnDataChunk(chunk1);
            Assert.Equal(2U, framer.CumulativeAckTSN);

            Assert.Empty(sortFrames1);
            Assert.Empty(sortFrames2);
            Assert.Single(sortFrames3);
            Assert.Equal("000102", sortFrames3.Single().UserData.HexStr());
            Assert.Equal(0, framer.ForwardTSNCount);
        }

        /// <summary>
        /// Tests that a fragmented chunk that includes a TSN wrap gets processed correctly.
        /// </summary>
        [Fact]
        public void FragmentWithTSNWrap()
        {
            SctpDataFramer framer = new SctpDataFramer(0, 0, uint.MaxValue - 2);

            SctpDataChunk chunk1 = new SctpDataChunk(false, true, false, uint.MaxValue - 2, 0, 0, 0, new byte[] { 0x00 });
            SctpDataChunk chunk2 = new SctpDataChunk(false, false, false, uint.MaxValue - 1, 0, 0, 0, new byte[] { 0x01 });
            SctpDataChunk chunk3 = new SctpDataChunk(false, false, false, uint.MaxValue, 0, 0, 0, new byte[] { 0x02 });
            SctpDataChunk chunk4 = new SctpDataChunk(false, false, false, 0, 0, 0, 0, new byte[] { 0x03 });
            SctpDataChunk chunk5 = new SctpDataChunk(false, false, true, 1, 0, 0, 0, new byte[] { 0x04 });

            var sFrames1 = framer.OnDataChunk(chunk1);
            Assert.Equal(uint.MaxValue - 2, framer.CumulativeAckTSN);
            var sFrames2 = framer.OnDataChunk(chunk2);
            Assert.Equal(uint.MaxValue - 1, framer.CumulativeAckTSN);
            var sFrames3 = framer.OnDataChunk(chunk3);
            Assert.Equal(uint.MaxValue, framer.CumulativeAckTSN);
            var sFrames4 = framer.OnDataChunk(chunk4);
            Assert.Equal(0U, framer.CumulativeAckTSN);
            var sFrames5 = framer.OnDataChunk(chunk5);
            Assert.Equal(1U, framer.CumulativeAckTSN);

            Assert.Empty(sFrames1);
            Assert.Empty(sFrames2);
            Assert.Empty(sFrames3);
            Assert.Empty(sFrames4);
            Assert.Single(sFrames5);
            Assert.Equal("0001020304", sFrames5.Single().UserData.HexStr());
            Assert.Equal(0, framer.ForwardTSNCount);
        }

        /// <summary>
        /// Tests that a fragmented chunk that includes a TSN wrap, and with out of order 
        /// chunks, gets processed correctly.
        /// </summary>
        [Fact]
        public void FragmentWithTSNWrapAndOutOfOrder()
        {
            SctpDataFramer framer = new SctpDataFramer(0, 0, uint.MaxValue - 2);

            SctpDataChunk chunk1 = new SctpDataChunk(true, true, false, uint.MaxValue - 2, 0, 0, 0, new byte[] { 0x00 });
            SctpDataChunk chunk2 = new SctpDataChunk(true, false, false, uint.MaxValue - 1, 0, 0, 0, new byte[] { 0x01 });
            SctpDataChunk chunk3 = new SctpDataChunk(true, false, false, uint.MaxValue, 0, 0, 0, new byte[] { 0x02 });
            SctpDataChunk chunk4 = new SctpDataChunk(true, false, false, 0, 0, 0, 0, new byte[] { 0x03 });
            SctpDataChunk chunk5 = new SctpDataChunk(true, false, true, 1, 0, 0, 0, new byte[] { 0x04 });

            // Intersperse a couple of full chunks in the middle of the fragmented chunk.
            SctpDataChunk chunk6 = new SctpDataChunk(true, true, true, 6, 0, 0, 0, new byte[] { 0x06 });
            SctpDataChunk chunk9 = new SctpDataChunk(true, true, true, 9, 0, 0, 0, new byte[] { 0x09 });

            var sframes9 = framer.OnDataChunk(chunk9);
            Assert.Null(framer.CumulativeAckTSN);
            var sframes1 = framer.OnDataChunk(chunk1);
            Assert.Equal(uint.MaxValue - 2, framer.CumulativeAckTSN);
            var sframes2 = framer.OnDataChunk(chunk2);
            Assert.Equal(uint.MaxValue - 1, framer.CumulativeAckTSN);
            var sframes3 = framer.OnDataChunk(chunk3);
            Assert.Equal(uint.MaxValue, framer.CumulativeAckTSN);
            var sframes6 = framer.OnDataChunk(chunk6);
            Assert.Equal(uint.MaxValue, framer.CumulativeAckTSN);
            var sframes4 = framer.OnDataChunk(chunk4);
            Assert.Equal(0U, framer.CumulativeAckTSN);
            var sframes5 = framer.OnDataChunk(chunk5);
            Assert.Equal(1U, framer.CumulativeAckTSN);

            Assert.Empty(sframes1);
            Assert.Empty(sframes2);
            Assert.Empty(sframes3);
            Assert.Empty(sframes4);
            Assert.Single(sframes6);
            Assert.Single(sframes9);
            Assert.Single(sframes5);
            Assert.Equal("0001020304", sframes5.Single().UserData.HexStr());
            Assert.Equal(2, framer.ForwardTSNCount);
        }

        /// <summary>
        /// Tests that a fragmented chunk gets processed correctly when the expected TSN wraps
        /// within the fragment.
        /// </summary>
        [Fact]
        public void FragmentWithExpectedTSNWrap()
        {
            SctpDataFramer framer = new SctpDataFramer(0, 0, uint.MaxValue - 2);

            SctpDataChunk chunk1 = new SctpDataChunk(false, true, false, uint.MaxValue - 2, 0, 0, 0, new byte[] { 0x00 });
            SctpDataChunk chunk2 = new SctpDataChunk(false, false, false, uint.MaxValue - 1, 0, 0, 0, new byte[] { 0x01 });
            SctpDataChunk chunk3 = new SctpDataChunk(false, false, false, uint.MaxValue, 0, 0, 0, new byte[] { 0x02 });
            SctpDataChunk chunk4 = new SctpDataChunk(false, false, false, 0, 0, 0, 0, new byte[] { 0x03 });
            SctpDataChunk chunk5 = new SctpDataChunk(false, false, true, 1, 0, 0, 0, new byte[] { 0x04 });

            var sframes1 = framer.OnDataChunk(chunk1);
            var sframes2 = framer.OnDataChunk(chunk2);
            var sframes3 = framer.OnDataChunk(chunk3);
            var sframes4 = framer.OnDataChunk(chunk4);
            var sframes5 = framer.OnDataChunk(chunk5);

            Assert.Empty(sframes1);
            Assert.Empty(sframes2);
            Assert.Empty(sframes3);
            Assert.Empty(sframes4);
            Assert.Single(sframes5);
            Assert.Equal("0001020304", sframes5.Single().UserData.HexStr());
            Assert.Equal(0, framer.ForwardTSNCount);
        }

        /// <summary>
        /// Tests that get distance method returns the correct value for a series of start and end points.
        /// </summary>
        [Fact]
        public void CheckGetDistance()
        {
            Assert.Equal(55U, SctpDataFramer.GetDistance(95, 150));
            Assert.Equal(1U, SctpDataFramer.GetDistance(0, uint.MaxValue));
            Assert.Equal(1U, SctpDataFramer.GetDistance(uint.MaxValue, 0));
            Assert.Equal(11U, SctpDataFramer.GetDistance(5, uint.MaxValue - 5));
            Assert.Equal(11U, SctpDataFramer.GetDistance(uint.MaxValue - 5, 5));
            Assert.Equal(50U, SctpDataFramer.GetDistance(100, 50));
        }

        /// <summary>
        /// Tests that is current method returns the correct value for a series expected and received TSNs.
        /// </summary>
        [Fact]
        public void CheckIsCurrent()
        {
            Assert.True(SctpDataFramer.IsNewer(0, 1));
            Assert.True(SctpDataFramer.IsNewer(0, 0));
            Assert.True(SctpDataFramer.IsNewer(1, 1));
            Assert.True(SctpDataFramer.IsNewer(uint.MaxValue, uint.MaxValue));
            Assert.True(SctpDataFramer.IsNewer(uint.MaxValue - 1, uint.MaxValue - 1));
            Assert.True(SctpDataFramer.IsNewer(uint.MaxValue, 0));
            Assert.True(SctpDataFramer.IsNewer(uint.MaxValue, 1));
            Assert.True(SctpDataFramer.IsNewer(uint.MaxValue - 1, uint.MaxValue));
            Assert.True(SctpDataFramer.IsNewer(uint.MaxValue - 1, 0));
            Assert.True(SctpDataFramer.IsNewer(103040, 232933));

            Assert.False(SctpDataFramer.IsNewer(uint.MaxValue, uint.MaxValue - 1));
            Assert.False(SctpDataFramer.IsNewer(0, uint.MaxValue));
            Assert.False(SctpDataFramer.IsNewer(1, uint.MaxValue));
            Assert.False(SctpDataFramer.IsNewer(0, uint.MaxValue - 1));
            Assert.False(SctpDataFramer.IsNewer(1, uint.MaxValue - 1));
        }

        /// <summary>
        /// Checks that the expiry mechanism, removing old chunks, works correctly
        /// when single packet unordered chunks are being received in the correct order.
        /// </summary>
        [Fact]
        public void CheckExpiryWithSinglePacketChunksUnordered()
        {
            uint tsn = uint.MaxValue - 3;
            uint receiveWindow = 8;
            uint mtu = 1;
            SctpDataFramer framer = new SctpDataFramer(receiveWindow, mtu, tsn);

            for (int i = 0; i < 50; i++)
            {
                SctpDataChunk chunk = new SctpDataChunk(true, true, true, tsn++, 0, 0, 0, new byte[] { 0x55 });

                var sortedFrames = framer.OnDataChunk(chunk);

                Assert.Single(sortedFrames);
                Assert.Equal("55", sortedFrames.Single().UserData.HexStr());
                Assert.Equal(0, framer.ForwardTSNCount);
                Assert.Equal(tsn - 1, framer.CumulativeAckTSN);
            }
        }

        /// <summary>
        /// Checks that the expiry mechanism, removing old chunks, works correctly
        /// when single packet ordered chunks are being received in the correct order.
        /// </summary>
        [Fact]
        public void CheckExpiryWithSinglePacketChunksOrdered()
        {
            uint tsn = uint.MaxValue - 3;
            uint receiveWindow = 8;
            uint mtu = 1;
            ushort streamSeqnum = 0;
            SctpDataFramer framer = new SctpDataFramer(receiveWindow, mtu, tsn);

            for (int i = 0; i < 50; i++)
            {
                SctpDataChunk chunk = new SctpDataChunk(false, true, true, tsn++, 0, streamSeqnum++, 0, new byte[] { 0x55 });

                var sortedFrames = framer.OnDataChunk(chunk);

                Assert.Single(sortedFrames);
                Assert.Equal("55", sortedFrames.Single().UserData.HexStr());
                Assert.Equal(0, framer.ForwardTSNCount);
                Assert.Equal(tsn - 1, framer.CumulativeAckTSN);
            }
        }

        /// <summary>
        /// Tests that single packet ordered chunks get delivered correctly when received in order.
        /// </summary>
        [Fact]
        public void ThreeStreamPackets()
        {
            SctpDataFramer framer = new SctpDataFramer(0, 0, 0);

            SctpDataChunk chunk1 = new SctpDataChunk(false, true, true, 0, 0, 0, 0, new byte[] { 0x00 });
            SctpDataChunk chunk2 = new SctpDataChunk(false, true, true, 1, 0, 1, 0, new byte[] { 0x01 });
            SctpDataChunk chunk3 = new SctpDataChunk(false, true, true, 2, 0, 2, 0, new byte[] { 0x02 });

            var sortFrames1 = framer.OnDataChunk(chunk1);
            var sortFrames2 = framer.OnDataChunk(chunk2);
            var sortFrames3 = framer.OnDataChunk(chunk3);

            Assert.Single(sortFrames1);
            Assert.Equal(0, sortFrames1.Single().StreamSeqNum);
            Assert.Equal("00", sortFrames1.Single().UserData.HexStr());
            Assert.Single(sortFrames2);
            Assert.Equal(1, sortFrames2.Single().StreamSeqNum);
            Assert.Equal("01", sortFrames2.Single().UserData.HexStr());
            Assert.Single(sortFrames3);
            Assert.Equal(2, sortFrames3.Single().StreamSeqNum);
            Assert.Equal("02", sortFrames3.Single().UserData.HexStr());
            Assert.Equal(0, framer.ForwardTSNCount);
        }

        /// <summary>
        /// Tests that single packet ordered chunks get delivered correctly when received out
        /// of order.
        /// </summary>
        [Fact]
        public void StreamPacketsReceviedOutOfOrder()
        {
            SctpDataFramer framer = new SctpDataFramer(0, 0, uint.MaxValue);

            SctpDataChunk chunk0 = new SctpDataChunk(false, true, true, uint.MaxValue, 0, ushort.MaxValue, 0, new byte[] { 0x00 });
            SctpDataChunk chunk1 = new SctpDataChunk(false, true, true, 0, 0, 0, 0, new byte[] { 0x00 });
            SctpDataChunk chunk2 = new SctpDataChunk(false, true, true, 1, 0, 1, 0, new byte[] { 0x01 });
            SctpDataChunk chunk3 = new SctpDataChunk(false, true, true, 2, 0, 2, 0, new byte[] { 0x02 });

            var sortFrames0 = framer.OnDataChunk(chunk0);
            var sortFrames1 = framer.OnDataChunk(chunk3);
            var sortFrames2 = framer.OnDataChunk(chunk2);
            var sortFrames3 = framer.OnDataChunk(chunk1);

            Assert.Single(sortFrames0);
            Assert.Empty(sortFrames1);
            Assert.Empty(sortFrames2);
            Assert.Equal(3, sortFrames3.Count);
            Assert.Equal(0, sortFrames3.First().StreamSeqNum);
            Assert.Equal("00", sortFrames3.First().UserData.HexStr());
            Assert.Equal(2, sortFrames3.Last().StreamSeqNum);
            Assert.Equal("02", sortFrames3.Last().UserData.HexStr());
            Assert.Equal(0, framer.ForwardTSNCount);
        }

        /// <summary>
        /// Tests that a forward TSN list with only single entry generates the correct gap report.
        /// </summary>
        [Fact]
        public void GetSingleGapReport()
        {
            SctpDataFramer framer = new SctpDataFramer(0, 0, 25);
            framer.OnDataChunk(new SctpDataChunk(true, true, true, 30, 0, 0, 0, new byte[] { 0x33 }));

            var gapReports = framer.GetForwardTSNGaps();

            Assert.Single(gapReports);

            var report = gapReports.Single();

            Assert.Equal(5, report.Start);
            Assert.Equal(5, report.End);
        }

        /// <summary>
        /// Tests that a forward TSN list with only single entry generates the correct gap report
        /// when the TSN gaps occurs across a TNS wrap.
        /// </summary>
        [Fact]
        public void GetSingleGapReportWithWrap()
        {
            SctpDataFramer framer = new SctpDataFramer(0, 0, uint.MaxValue - 2);
            framer.OnDataChunk(new SctpDataChunk(true, true, true, 2, 0, 0, 0, new byte[] { 0x33 }));

            var gapReports = framer.GetForwardTSNGaps();

            Assert.Single(gapReports);

            var report = gapReports.Single();

            Assert.Equal(5, report.Start);
            Assert.Equal(5, report.End);
        }

        /// <summary>
        /// Tests that a forward TSN list with two gaps generates the correct reports.
        /// </summary>
        [Fact]
        public void GetTwoGapReports()
        {
            SctpDataFramer framer = new SctpDataFramer(0, 0, 15005);
            framer.OnDataChunk(new SctpDataChunk(true, true, true, 15007, 0, 0, 0, new byte[] { 0x33 }));
            framer.OnDataChunk(new SctpDataChunk(true, true, true, 15008, 0, 0, 0, new byte[] { 0x33 }));
            framer.OnDataChunk(new SctpDataChunk(true, true, true, 15010, 0, 0, 0, new byte[] { 0x33 }));

            var gapReports = framer.GetForwardTSNGaps();

            Assert.Equal(2, gapReports.Count);
            Assert.True(gapReports[0].Start == 2 && gapReports[0].End == 3);
            Assert.True(gapReports[1].Start == 5 && gapReports[1].End == 5);
        }

        /// <summary>
        /// Tests that a forward TSN list with three gaps generates the correct reports.
        /// </summary>
        [Fact]
        public void GetThreeGapReports()
        {
            SctpDataFramer framer = new SctpDataFramer(0, 0, 3);
            framer.OnDataChunk(new SctpDataChunk(true, true, true, 7, 0, 0, 0, new byte[] { 0x33 }));
            framer.OnDataChunk(new SctpDataChunk(true, true, true, 8, 0, 0, 0, new byte[] { 0x33 }));
            framer.OnDataChunk(new SctpDataChunk(true, true, true, 9, 0, 0, 0, new byte[] { 0x33 }));
            framer.OnDataChunk(new SctpDataChunk(true, true, true, 11, 0, 0, 0, new byte[] { 0x33 }));
            framer.OnDataChunk(new SctpDataChunk(true, true, true, 12, 0, 0, 0, new byte[] { 0x33 }));
            framer.OnDataChunk(new SctpDataChunk(true, true, true, 14, 0, 0, 0, new byte[] { 0x33 }));

            var gapReports = framer.GetForwardTSNGaps();

            Assert.Equal(3, gapReports.Count);
            Assert.True(gapReports[0].Start == 4 && gapReports[0].End == 6);
            Assert.True(gapReports[1].Start == 8 && gapReports[1].End == 9);
            Assert.True(gapReports[2].Start == 11 && gapReports[2].End == 11);
        }
    }
}
