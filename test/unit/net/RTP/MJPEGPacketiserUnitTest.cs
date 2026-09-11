//-----------------------------------------------------------------------------
// Filename: MJPEGPacketiserUnitTest.cs
//
// Description: Unit tests for the MJPEGPacketiser class. These pin the marker
// scanning behaviour of GetFrameData, in particular that the marker byte
// slices are tolerant of segments that run to the end of the frame. The SOS
// (start of scan) segment always runs to the end of the frame so any slicing
// change that throws on an out of range length breaks every MJPEG frame.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 11 Jun 2026  Aaron Clauson   Created, Dublin, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.net.RTP.Packetisation;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class MJPEGPacketiserUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public MJPEGPacketiserUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        private static readonly byte[] _entropyData = Enumerable.Range(0x10, 24).Select(x => (byte)x).ToArray();

        /// <summary>
        /// Builds a minimal synthetic baseline JPEG frame: SOI, DQT (one 8 bit quantisation
        /// table), SOF0 (16x16, 3 components with 4:2:2 sampling) and SOS followed by entropy
        /// coded data, optionally terminated with an EOI marker.
        /// </summary>
        private static byte[] CreateTestJpegFrame(bool includeEoi)
        {
            var frame = new List<byte>();

            // SOI.
            frame.AddRange(new byte[] { 0xFF, 0xD8 });

            // DQT: length 67 (2 length bytes + 1 precision/table id byte + 64 table bytes).
            frame.AddRange(new byte[] { 0xFF, 0xDB, 0x00, 0x43, 0x00 });
            frame.AddRange(Enumerable.Range(1, 64).Select(x => (byte)x));

            // SOF0: length 17, 8 bit precision, 16x16, 3 components with 4:2:2 sampling factors.
            frame.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x10, 0x00, 0x10, 0x03,
                0x01, 0x21, 0x00,
                0x02, 0x11, 0x00,
                0x03, 0x11, 0x00 });

            // SOS: length 12, 3 components, then the entropy coded data.
            frame.AddRange(new byte[] { 0xFF, 0xDA, 0x00, 0x0C, 0x03, 0x01, 0x00, 0x02, 0x11, 0x03, 0x11, 0x00, 0x3F, 0x00 });
            frame.AddRange(_entropyData);

            if (includeEoi)
            {
                frame.AddRange(new byte[] { 0xFF, 0xD9 });
            }

            return frame.ToArray();
        }

        /// <summary>
        /// Tests that a frame terminated with an EOI marker is scanned without an exception and
        /// that the quantisation table, frame dimensions and scan data are extracted correctly.
        /// The SOS marker is closed by the trailing EOI marker, which exercises the slice whose
        /// nominal length extends past the end of the frame and must be clamped.
        /// </summary>
        [Fact]
        public void GetFrameDataWithEoiUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var frame = CreateTestJpegFrame(includeEoi: true);

            var data = MJPEGPacketiser.GetFrameData(frame, out var customData);

            Assert.NotNull(data);
            Assert.NotNull(data.Data);
            Assert.True(data.Data.Length >= _entropyData.Length);
            Assert.Equal(_entropyData, data.Data.Take(_entropyData.Length));

            Assert.Equal(255, customData.MjpegHeader.q);
            Assert.Equal(2, customData.MjpegHeader.width);   // 16 >> 3.
            Assert.Equal(2, customData.MjpegHeader.height);  // 16 >> 3.
            var qTable = Assert.Single(customData.QTables);
            Assert.Equal(Enumerable.Range(1, 64).Select(x => (byte)x), qTable);
        }

        /// <summary>
        /// Tests that a frame WITHOUT a trailing EOI marker is scanned without an exception. The
        /// SOS segment is then the final marker and runs to the end of the frame, exercising the
        /// end-of-scan slice whose nominal length always extends past the end of the frame.
        /// </summary>
        [Fact]
        public void GetFrameDataWithoutEoiUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var frame = CreateTestJpegFrame(includeEoi: false);

            var data = MJPEGPacketiser.GetFrameData(frame, out var customData);

            Assert.NotNull(data);
            Assert.Equal(_entropyData, data.Data);
            Assert.Single(customData.QTables);
        }
    }
}
