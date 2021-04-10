//-----------------------------------------------------------------------------
// Filename: SctpDataReceiverUnitTest.cs
//
// Description: Unit tests for the SctpDataReceiver class.
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
    public class SctpDataReceiverUnitTest
    {
        private ILogger logger = null;

        public SctpDataReceiverUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a single packet frame gets processed correctly.
        /// </summary>
        [Fact]
        public void SinglePacketFrame()
        {
            SctpDataReceiver receiver = new SctpDataReceiver(0, 0, 0);

            SctpDataChunk chunk = new SctpDataChunk(false, true, true, 0, 0, 0, 0, new byte[] { 0x00 });

            var sortedFrames = receiver.OnDataChunk(chunk);

            Assert.Single(sortedFrames);
            Assert.Equal("00", sortedFrames.Single().UserData.HexStr());
            Assert.Equal(0U, receiver.CumulativeAckTSN);
            Assert.Equal(0, receiver.ForwardTSNCount);
        }

        /// <summary>
        /// Tests that a chunk fragmented across three packets gets processed correctly.
        /// </summary>
        [Fact]
        public void ThreeFragments()
        {
            SctpDataReceiver receiver = new SctpDataReceiver(0, 0, 0);

            SctpDataChunk chunk1 = new SctpDataChunk(false, true, false, 0, 0, 0, 0, new byte[] { 0x00 });
            SctpDataChunk chunk2 = new SctpDataChunk(false, false, false, 1, 0, 0, 0, new byte[] { 0x01 });
            SctpDataChunk chunk3 = new SctpDataChunk(false, false, true, 2, 0, 0, 0, new byte[] { 0x02 });

            var sortFrames1 = receiver.OnDataChunk(chunk1);
            Assert.Equal(0U, receiver.CumulativeAckTSN);
            var sortFrames2 = receiver.OnDataChunk(chunk2);
            Assert.Equal(1U, receiver.CumulativeAckTSN);
            var sortFrames3 = receiver.OnDataChunk(chunk3);
            Assert.Equal(2U, receiver.CumulativeAckTSN);

            Assert.Empty(sortFrames1);
            Assert.Empty(sortFrames2);
            Assert.Single(sortFrames3);
            Assert.Equal("000102", sortFrames3.Single().UserData.HexStr());
            Assert.Equal(0, receiver.ForwardTSNCount);
        }

        /// <summary>
        /// Tests that a fragmented chunk spread across three packets, and received out of order,
        /// gets processed correctly.
        /// </summary>
        [Fact]
        public void ThreeFragmentsOutOfOrder()
        {
            SctpDataReceiver receiver = new SctpDataReceiver(0, 0, 0);

            SctpDataChunk chunk1 = new SctpDataChunk(false, true, false, 0, 0, 0, 0, new byte[] { 0x00 });
            SctpDataChunk chunk2 = new SctpDataChunk(false, false, false, 1, 0, 0, 0, new byte[] { 0x01 });
            SctpDataChunk chunk3 = new SctpDataChunk(false, false, true, 2, 0, 0, 0, new byte[] { 0x02 });

            var sortFrames1 = receiver.OnDataChunk(chunk1);
            Assert.Equal(0U, receiver.CumulativeAckTSN);
            var sortFrames2 = receiver.OnDataChunk(chunk3);
            Assert.Equal(0U, receiver.CumulativeAckTSN);
            var sortFrames3 = receiver.OnDataChunk(chunk2);
            Assert.Equal(2U, receiver.CumulativeAckTSN);

            Assert.Empty(sortFrames1);
            Assert.Empty(sortFrames2);
            Assert.Single(sortFrames3);
            Assert.Equal("000102", sortFrames3.Single().UserData.HexStr());
            Assert.Equal(0, receiver.ForwardTSNCount);
        }

        /// <summary>
        /// Tests that a fragmented chunk with the beginning chunk received last
        /// gets processed correctly.
        /// </summary>
        [Fact]
        public void ThreeFragmentsBeginLast()
        {
            SctpDataReceiver receiver = new SctpDataReceiver(0, 0, 0);

            SctpDataChunk chunk1 = new SctpDataChunk(false, true, false, 0, 0, 0, 0, new byte[] { 0x00 });
            SctpDataChunk chunk2 = new SctpDataChunk(false, false, false, 1, 0, 0, 0, new byte[] { 0x01 });
            SctpDataChunk chunk3 = new SctpDataChunk(false, false, true, 2, 0, 0, 0, new byte[] { 0x02 });

            var sortFrames1 = receiver.OnDataChunk(chunk3);
            Assert.Null(receiver.CumulativeAckTSN);
            var sortFrames2 = receiver.OnDataChunk(chunk2);
            Assert.Null(receiver.CumulativeAckTSN);
            var sortFrames3 = receiver.OnDataChunk(chunk1);
            Assert.Equal(2U, receiver.CumulativeAckTSN);

            Assert.Empty(sortFrames1);
            Assert.Empty(sortFrames2);
            Assert.Single(sortFrames3);
            Assert.Equal("000102", sortFrames3.Single().UserData.HexStr());
            Assert.Equal(0, receiver.ForwardTSNCount);
        }

        /// <summary>
        /// Tests that a fragmented chunk that includes a TSN wrap gets processed correctly.
        /// </summary>
        [Fact]
        public void FragmentWithTSNWrap()
        {
            SctpDataReceiver receiver = new SctpDataReceiver(0, 0, uint.MaxValue - 2);

            SctpDataChunk chunk1 = new SctpDataChunk(false, true, false, uint.MaxValue - 2, 0, 0, 0, new byte[] { 0x00 });
            SctpDataChunk chunk2 = new SctpDataChunk(false, false, false, uint.MaxValue - 1, 0, 0, 0, new byte[] { 0x01 });
            SctpDataChunk chunk3 = new SctpDataChunk(false, false, false, uint.MaxValue, 0, 0, 0, new byte[] { 0x02 });
            SctpDataChunk chunk4 = new SctpDataChunk(false, false, false, 0, 0, 0, 0, new byte[] { 0x03 });
            SctpDataChunk chunk5 = new SctpDataChunk(false, false, true, 1, 0, 0, 0, new byte[] { 0x04 });

            var sFrames1 = receiver.OnDataChunk(chunk1);
            Assert.Equal(uint.MaxValue - 2, receiver.CumulativeAckTSN);
            var sFrames2 = receiver.OnDataChunk(chunk2);
            Assert.Equal(uint.MaxValue - 1, receiver.CumulativeAckTSN);
            var sFrames3 = receiver.OnDataChunk(chunk3);
            Assert.Equal(uint.MaxValue, receiver.CumulativeAckTSN);
            var sFrames4 = receiver.OnDataChunk(chunk4);
            Assert.Equal(0U, receiver.CumulativeAckTSN);
            var sFrames5 = receiver.OnDataChunk(chunk5);
            Assert.Equal(1U, receiver.CumulativeAckTSN);

            Assert.Empty(sFrames1);
            Assert.Empty(sFrames2);
            Assert.Empty(sFrames3);
            Assert.Empty(sFrames4);
            Assert.Single(sFrames5);
            Assert.Equal("0001020304", sFrames5.Single().UserData.HexStr());
            Assert.Equal(0, receiver.ForwardTSNCount);
        }

        /// <summary>
        /// Tests that a fragmented chunk that includes a TSN wrap, and with out of order 
        /// chunks, gets processed correctly.
        /// </summary>
        [Fact]
        public void FragmentWithTSNWrapAndOutOfOrder()
        {
            SctpDataReceiver receiver = new SctpDataReceiver(0, 0, uint.MaxValue - 2);

            SctpDataChunk chunk1 = new SctpDataChunk(true, true, false, uint.MaxValue - 2, 0, 0, 0, new byte[] { 0x00 });
            SctpDataChunk chunk2 = new SctpDataChunk(true, false, false, uint.MaxValue - 1, 0, 0, 0, new byte[] { 0x01 });
            SctpDataChunk chunk3 = new SctpDataChunk(true, false, false, uint.MaxValue, 0, 0, 0, new byte[] { 0x02 });
            SctpDataChunk chunk4 = new SctpDataChunk(true, false, false, 0, 0, 0, 0, new byte[] { 0x03 });
            SctpDataChunk chunk5 = new SctpDataChunk(true, false, true, 1, 0, 0, 0, new byte[] { 0x04 });

            // Intersperse a couple of full chunks in the middle of the fragmented chunk.
            SctpDataChunk chunk6 = new SctpDataChunk(true, true, true, 6, 0, 0, 0, new byte[] { 0x06 });
            SctpDataChunk chunk9 = new SctpDataChunk(true, true, true, 9, 0, 0, 0, new byte[] { 0x09 });

            var sframes9 = receiver.OnDataChunk(chunk9);
            Assert.Null(receiver.CumulativeAckTSN);
            var sframes1 = receiver.OnDataChunk(chunk1);
            Assert.Equal(uint.MaxValue - 2, receiver.CumulativeAckTSN);
            var sframes2 = receiver.OnDataChunk(chunk2);
            Assert.Equal(uint.MaxValue - 1, receiver.CumulativeAckTSN);
            var sframes3 = receiver.OnDataChunk(chunk3);
            Assert.Equal(uint.MaxValue, receiver.CumulativeAckTSN);
            var sframes6 = receiver.OnDataChunk(chunk6);
            Assert.Equal(uint.MaxValue, receiver.CumulativeAckTSN);
            var sframes4 = receiver.OnDataChunk(chunk4);
            Assert.Equal(0U, receiver.CumulativeAckTSN);
            var sframes5 = receiver.OnDataChunk(chunk5);
            Assert.Equal(1U, receiver.CumulativeAckTSN);

            Assert.Empty(sframes1);
            Assert.Empty(sframes2);
            Assert.Empty(sframes3);
            Assert.Empty(sframes4);
            Assert.Single(sframes6);
            Assert.Single(sframes9);
            Assert.Single(sframes5);
            Assert.Equal("0001020304", sframes5.Single().UserData.HexStr());
            Assert.Equal(2, receiver.ForwardTSNCount);
        }

        /// <summary>
        /// Tests that a fragmented chunk gets processed correctly when the expected TSN wraps
        /// within the fragment.
        /// </summary>
        [Fact]
        public void FragmentWithExpectedTSNWrap()
        {
            SctpDataReceiver receiver = new SctpDataReceiver(0, 0, uint.MaxValue - 2);

            SctpDataChunk chunk1 = new SctpDataChunk(false, true, false, uint.MaxValue - 2, 0, 0, 0, new byte[] { 0x00 });
            SctpDataChunk chunk2 = new SctpDataChunk(false, false, false, uint.MaxValue - 1, 0, 0, 0, new byte[] { 0x01 });
            SctpDataChunk chunk3 = new SctpDataChunk(false, false, false, uint.MaxValue, 0, 0, 0, new byte[] { 0x02 });
            SctpDataChunk chunk4 = new SctpDataChunk(false, false, false, 0, 0, 0, 0, new byte[] { 0x03 });
            SctpDataChunk chunk5 = new SctpDataChunk(false, false, true, 1, 0, 0, 0, new byte[] { 0x04 });

            var sframes1 = receiver.OnDataChunk(chunk1);
            var sframes2 = receiver.OnDataChunk(chunk2);
            var sframes3 = receiver.OnDataChunk(chunk3);
            var sframes4 = receiver.OnDataChunk(chunk4);
            var sframes5 = receiver.OnDataChunk(chunk5);

            Assert.Empty(sframes1);
            Assert.Empty(sframes2);
            Assert.Empty(sframes3);
            Assert.Empty(sframes4);
            Assert.Single(sframes5);
            Assert.Equal("0001020304", sframes5.Single().UserData.HexStr());
            Assert.Equal(0, receiver.ForwardTSNCount);
        }

        /// <summary>
        /// Tests that get distance method returns the correct value for a series of start and end points.
        /// </summary>
        [Fact]
        public void CheckGetDistance()
        {
            Assert.Equal(55U, SctpDataReceiver.GetDistance(95, 150));
            Assert.Equal(1U, SctpDataReceiver.GetDistance(0, uint.MaxValue));
            Assert.Equal(1U, SctpDataReceiver.GetDistance(uint.MaxValue, 0));
            Assert.Equal(11U, SctpDataReceiver.GetDistance(5, uint.MaxValue - 5));
            Assert.Equal(11U, SctpDataReceiver.GetDistance(uint.MaxValue - 5, 5));
            Assert.Equal(50U, SctpDataReceiver.GetDistance(100, 50));
        }

        /// <summary>
        /// Tests that is current method returns the correct value for a series expected and received TSNs.
        /// </summary>
        [Fact]
        public void CheckIsCurrent()
        {
            Assert.True(SctpDataReceiver.IsNewer(0, 1));
            Assert.True(SctpDataReceiver.IsNewerOrEqual(0, 0));
            Assert.True(SctpDataReceiver.IsNewerOrEqual(1, 1));
            Assert.True(SctpDataReceiver.IsNewerOrEqual(uint.MaxValue, uint.MaxValue));
            Assert.True(SctpDataReceiver.IsNewerOrEqual(uint.MaxValue - 1, uint.MaxValue - 1));
            Assert.True(SctpDataReceiver.IsNewer(uint.MaxValue, 0));
            Assert.True(SctpDataReceiver.IsNewer(uint.MaxValue, 1));
            Assert.True(SctpDataReceiver.IsNewer(uint.MaxValue - 1, uint.MaxValue));
            Assert.True(SctpDataReceiver.IsNewer(uint.MaxValue - 1, 0));
            Assert.True(SctpDataReceiver.IsNewer(103040, 232933));

            Assert.False(SctpDataReceiver.IsNewer(uint.MaxValue, uint.MaxValue - 1));
            Assert.False(SctpDataReceiver.IsNewer(0, uint.MaxValue));
            Assert.False(SctpDataReceiver.IsNewer(1, uint.MaxValue));
            Assert.False(SctpDataReceiver.IsNewer(0, uint.MaxValue - 1));
            Assert.False(SctpDataReceiver.IsNewer(1, uint.MaxValue - 1));
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
            SctpDataReceiver receiver = new SctpDataReceiver(receiveWindow, mtu, tsn);

            for (int i = 0; i < 50; i++)
            {
                SctpDataChunk chunk = new SctpDataChunk(true, true, true, tsn++, 0, 0, 0, new byte[] { 0x55 });

                var sortedFrames = receiver.OnDataChunk(chunk);

                Assert.Single(sortedFrames);
                Assert.Equal("55", sortedFrames.Single().UserData.HexStr());
                Assert.Equal(0, receiver.ForwardTSNCount);
                Assert.Equal(tsn - 1, receiver.CumulativeAckTSN);
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
            SctpDataReceiver receiver = new SctpDataReceiver(receiveWindow, mtu, tsn);

            for (int i = 0; i < 50; i++)
            {
                SctpDataChunk chunk = new SctpDataChunk(false, true, true, tsn++, 0, streamSeqnum++, 0, new byte[] { 0x55 });

                var sortedFrames = receiver.OnDataChunk(chunk);

                Assert.Single(sortedFrames);
                Assert.Equal("55", sortedFrames.Single().UserData.HexStr());
                Assert.Equal(0, receiver.ForwardTSNCount);
                Assert.Equal(tsn - 1, receiver.CumulativeAckTSN);
            }
        }

        /// <summary>
        /// Tests that single packet ordered chunks get delivered correctly when received in order.
        /// </summary>
        [Fact]
        public void ThreeStreamPackets()
        {
            SctpDataReceiver receiver = new SctpDataReceiver(0, 0, 0);

            SctpDataChunk chunk1 = new SctpDataChunk(false, true, true, 0, 0, 0, 0, new byte[] { 0x00 });
            SctpDataChunk chunk2 = new SctpDataChunk(false, true, true, 1, 0, 1, 0, new byte[] { 0x01 });
            SctpDataChunk chunk3 = new SctpDataChunk(false, true, true, 2, 0, 2, 0, new byte[] { 0x02 });

            var sortFrames1 = receiver.OnDataChunk(chunk1);
            var sortFrames2 = receiver.OnDataChunk(chunk2);
            var sortFrames3 = receiver.OnDataChunk(chunk3);

            Assert.Single(sortFrames1);
            Assert.Equal(0, sortFrames1.Single().StreamSeqNum);
            Assert.Equal("00", sortFrames1.Single().UserData.HexStr());
            Assert.Single(sortFrames2);
            Assert.Equal(1, sortFrames2.Single().StreamSeqNum);
            Assert.Equal("01", sortFrames2.Single().UserData.HexStr());
            Assert.Single(sortFrames3);
            Assert.Equal(2, sortFrames3.Single().StreamSeqNum);
            Assert.Equal("02", sortFrames3.Single().UserData.HexStr());
            Assert.Equal(0, receiver.ForwardTSNCount);
        }

        /// <summary>
        /// Tests that single packet ordered chunks get delivered correctly when received out
        /// of order.
        /// </summary>
        [Fact]
        public void StreamPacketsReceviedOutOfOrder()
        {
            SctpDataReceiver receiver = new SctpDataReceiver(0, 0, uint.MaxValue);

            SctpDataChunk chunk0 = new SctpDataChunk(false, true, true, uint.MaxValue, 0, ushort.MaxValue, 0, new byte[] { 0x00 });
            SctpDataChunk chunk1 = new SctpDataChunk(false, true, true, 0, 0, 0, 0, new byte[] { 0x00 });
            SctpDataChunk chunk2 = new SctpDataChunk(false, true, true, 1, 0, 1, 0, new byte[] { 0x01 });
            SctpDataChunk chunk3 = new SctpDataChunk(false, true, true, 2, 0, 2, 0, new byte[] { 0x02 });

            var sortFrames0 = receiver.OnDataChunk(chunk0);
            var sortFrames1 = receiver.OnDataChunk(chunk3);
            var sortFrames2 = receiver.OnDataChunk(chunk2);
            var sortFrames3 = receiver.OnDataChunk(chunk1);

            Assert.Single(sortFrames0);
            Assert.Empty(sortFrames1);
            Assert.Empty(sortFrames2);
            Assert.Equal(3, sortFrames3.Count);
            Assert.Equal(0, sortFrames3.First().StreamSeqNum);
            Assert.Equal("00", sortFrames3.First().UserData.HexStr());
            Assert.Equal(2, sortFrames3.Last().StreamSeqNum);
            Assert.Equal("02", sortFrames3.Last().UserData.HexStr());
            Assert.Equal(0, receiver.ForwardTSNCount);
        }

        /// <summary>
        /// Tests that a forward TSN list with only single entry generates the correct gap report.
        /// </summary>
        [Fact]
        public void GetSingleGapReport()
        {
            SctpDataReceiver receiver = new SctpDataReceiver(0, 0, 25);
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, 25, 0, 0, 0, new byte[] { 0x33 }));
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, 30, 0, 0, 0, new byte[] { 0x33 }));

            var gapReports = receiver.GetForwardTSNGaps();

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
            uint initialTSN = uint.MaxValue - 2;
            SctpDataReceiver receiver = new SctpDataReceiver(0, 0, initialTSN);
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, initialTSN, 0, 0, 0, new byte[] { 0x33 }));
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, 2, 0, 0, 0, new byte[] { 0x33 }));

            var gapReports = receiver.GetForwardTSNGaps();

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
            SctpDataReceiver receiver = new SctpDataReceiver(0, 0, 15005);
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, 15005, 0, 0, 0, new byte[] { 0x33 }));
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, 15007, 0, 0, 0, new byte[] { 0x33 }));
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, 15008, 0, 0, 0, new byte[] { 0x33 }));
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, 15010, 0, 0, 0, new byte[] { 0x33 }));

            var gapReports = receiver.GetForwardTSNGaps();

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
            SctpDataReceiver receiver = new SctpDataReceiver(0, 0, 3);
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, 3, 0, 0, 0, new byte[] { 0x33 }));
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, 7, 0, 0, 0, new byte[] { 0x33 }));
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, 8, 0, 0, 0, new byte[] { 0x33 }));
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, 9, 0, 0, 0, new byte[] { 0x33 }));
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, 11, 0, 0, 0, new byte[] { 0x33 }));
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, 12, 0, 0, 0, new byte[] { 0x33 }));
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, 14, 0, 0, 0, new byte[] { 0x33 }));

            var gapReports = receiver.GetForwardTSNGaps();

            Assert.Equal(3, gapReports.Count);
            Assert.True(gapReports[0].Start == 4 && gapReports[0].End == 6);
            Assert.True(gapReports[1].Start == 8 && gapReports[1].End == 9);
            Assert.True(gapReports[2].Start == 11 && gapReports[2].End == 11);
        }

        /// <summary>
        /// Tests that a forward TSN list that has a duplicate TSN that's already been 
        /// received does not generate a gap report.
        /// </summary>
        [Fact]
        public void GeGapReportWithDuplicateForwardTSN()
        {
            uint initialTSN = Crypto.GetRandomUInt(true);
            SctpDataReceiver receiver = new SctpDataReceiver(0, 0, initialTSN);

            // Forward TSN.
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, initialTSN + 1, 0, 0, 0, new byte[] { 0x33 }));
            // Initial expected TSN.
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, initialTSN, 0, 0, 0, new byte[] { 0x33 }));
            // Duplicate of first received TSN.
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, initialTSN + 1, 0, 0, 0, new byte[] { 0x33 }));

            var gapReports = receiver.GetForwardTSNGaps();

            Assert.Empty(gapReports);
        }

        /// <summary>
        /// Checks that the receiver generates the correct SACK chunk for a single missing DATA chunk.
        /// </summary>
        [Fact]
        public void GetSackForSingleMissingChunk()
        {
            uint arwnd = 131072;
            ushort mtu = 1400;
            uint initialTSN = Crypto.GetRandomUInt(true);

            SctpDataReceiver receiver = new SctpDataReceiver(arwnd, mtu, initialTSN);

            receiver.OnDataChunk(new SctpDataChunk(true, true, true, initialTSN, 0, 0, 0, new byte[] { 0x44 }));
            Assert.Equal(initialTSN, receiver.CumulativeAckTSN);

            // Simulate a missing chunk by incrementing the TSN by 2.
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, initialTSN + 2, 0, 0, 0, new byte[] { 0x44 }));
            Assert.Equal(initialTSN, receiver.CumulativeAckTSN);

            var sack = receiver.GetSackChunk();
            Assert.Equal(initialTSN, sack.CumulativeTsnAck);
            Assert.Single(sack.GapAckBlocks);
            Assert.Equal(2, sack.GapAckBlocks[0].Start);
            Assert.Equal(2, sack.GapAckBlocks[0].End);
        }

        /// <summary>
        /// Checks that the receiver generates a null SACK if the first DATA chunk has not been received.
        /// </summary>
        [Fact]
        public void GetSackForInitialChunkMissing()
        {
            uint arwnd = 131072;
            ushort mtu = 1400;
            uint initialTSN = Crypto.GetRandomUInt(true);

            SctpDataReceiver receiver = new SctpDataReceiver(arwnd, mtu, initialTSN);

            receiver.OnDataChunk(new SctpDataChunk(true, true, true, initialTSN + 1, 0, 0, 0, new byte[] { 0x44 }));
            Assert.Null(receiver.CumulativeAckTSN);
            Assert.Null(receiver.GetSackChunk());
        }

        /// <summary>
        /// Checks that the receiver handles the initial chunk being delivered out of order.
        /// </summary>
        [Fact]
        public void InitialChunkOutOfOrder()
        {
            uint arwnd = 131072;
            ushort mtu = 1400;
            uint initialTSN = Crypto.GetRandomUInt(true);

            SctpDataReceiver receiver = new SctpDataReceiver(arwnd, mtu, initialTSN);

            // Skip initial DATA chunk.
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, initialTSN + 1, 0, 0, 0, new byte[] { 0x44 }));
            Assert.Null(receiver.CumulativeAckTSN);
            Assert.Null(receiver.GetSackChunk());

            // Give the receiver the initial DATA chunk.
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, initialTSN, 0, 0, 0, new byte[] { 0x44 }));
            Assert.Equal(initialTSN + 1, receiver.CumulativeAckTSN);

            var sack = receiver.GetSackChunk();

            Assert.NotNull(sack);
            Assert.Empty(sack.GapAckBlocks);
        }

        /// <summary>
        /// Checks that the receiver handles the initial chunk being delivered two chunks out of order.
        /// </summary>
        [Fact]
        public void InitialChunkTwoChunkDelay()
        {
            uint arwnd = 131072;
            ushort mtu = 1400;
            uint initialTSN = Crypto.GetRandomUInt(true);

            SctpDataReceiver receiver = new SctpDataReceiver(arwnd, mtu, initialTSN);

            // Skip initial DATA chunk.
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, initialTSN + 1, 0, 0, 0, new byte[] { 0x44 }));
            Assert.Null(receiver.CumulativeAckTSN);
            Assert.Null(receiver.GetSackChunk());

            receiver.OnDataChunk(new SctpDataChunk(true, true, true, initialTSN + 2, 0, 0, 0, new byte[] { 0x44 }));
            Assert.Null(receiver.CumulativeAckTSN);
            Assert.Null(receiver.GetSackChunk());

            // Give the receiver the initial DATA chunk.
            receiver.OnDataChunk(new SctpDataChunk(true, true, true, initialTSN, 0, 0, 0, new byte[] { 0x44 }));
            Assert.Equal(initialTSN + 2, receiver.CumulativeAckTSN);

            var sack = receiver.GetSackChunk();

            Assert.NotNull(sack);
            Assert.Empty(sack.GapAckBlocks);
        }
    }
}
