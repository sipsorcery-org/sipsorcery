using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.Net.UnitTests.RTP
{
    [TestClass]
    public class RTPVP8HeaderUnitTest
    {
        /// <summary>
        ///  Tests getting the VP8 header for an intermediate (non-key) frame.
        /// </summary>
        [TestMethod]
        public void GeIntermediateFrameHeaderTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPVP8Header rtpVP8Header = new RTPVP8Header()
            {
                StartOfVP8Partition = true,
                FirstPartitionSize = 54
            };

            byte[] headerBuffer = rtpVP8Header.GetBytes();
            
            Console.WriteLine(BitConverter.ToString(headerBuffer, 0));
        }
    }
}
