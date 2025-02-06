using System;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using Xunit;

namespace SIPSorcery.UnitTests.Net
{
    internal class DatetimeProvider : IDateTime
    {
        public DateTime Time { get; set; }
    }

    [Trait("Category", "unit")]
    public class ReorderBufferUnitTest
    {
        private ILogger logger = null;

        public ReorderBufferUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void ShouldReorder()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);
            var buffer = new RTPReorderBuffer(TimeSpan.FromMilliseconds(300));
            var packets = new[] { CreatePacket(1), CreatePacket(3), CreatePacket(4), CreatePacket(2) };

            foreach (var rtpPacket in packets)
            {
                buffer.Add(rtpPacket);
            }

            for (ushort i = 1; i <= 4; i++)
            {
                AssertSequenceNumber(buffer, i);
            }
        }

        [Fact]
        public void ShouldReorder2()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);
            var buffer = new RTPReorderBuffer(TimeSpan.FromMilliseconds(300));
            var packets = new[] { CreatePacket(1), CreatePacket(3), CreatePacket(2), CreatePacket(0) };

            foreach (var rtpPacket in packets)
            {
                buffer.Add(rtpPacket);
            }

            for (ushort i = 1; i <= 3; i++)
            {
                AssertSequenceNumber(buffer, i);
            }
        }

        [Fact]
        public void ShouldReorderWithWrapAround()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);
            var buffer = new RTPReorderBuffer(TimeSpan.FromMilliseconds(300));
            var packets = new[] { CreatePacket(65534), CreatePacket(3), CreatePacket(2), CreatePacket(0), CreatePacket(65535) };

            foreach (var rtpPacket in packets)
            {
                buffer.Add(rtpPacket);
            }
            AssertSequenceNumber(buffer, 65534);
            AssertSequenceNumber(buffer, 65535);
            AssertSequenceNumber(buffer, 0);
            AssertSequenceNumber(buffer, 2);
            AssertSequenceNumber(buffer, 3);
        }

        [Fact]
        public void ShouldReturnPacketsInOrder()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);
            var provider = new DatetimeProvider();
            var baseTime = DateTime.Now;
            var buffer = new RTPReorderBuffer(TimeSpan.FromMilliseconds(300), provider);

            provider.Time = baseTime;
            buffer.Add(CreatePacket(1));
            buffer.Add(CreatePacket(3));
            buffer.Add(CreatePacket(2));

            AssertSequenceNumber(buffer, 1);
            AssertSequenceNumber(buffer, 2);
            AssertSequenceNumber(buffer, 3);

            buffer.Add(CreatePacket(4));

            AssertSequenceNumber(buffer, 4);
        }

        [Fact]
        public void ShouldRemoveDuplicate()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);
            var provider = new DatetimeProvider();
            var baseTime = DateTime.Now;
            var buffer = new RTPReorderBuffer(TimeSpan.FromMilliseconds(300), provider);

            provider.Time = baseTime;
            buffer.Add(CreatePacket(1));
            buffer.Add(CreatePacket(2));
            buffer.Add(CreatePacket(2));

            AssertSequenceNumber(buffer, 1);
            AssertSequenceNumber(buffer, 2);

            Assert.False(buffer.Get(out _));
        }

        [Fact]
        public void ShouldWaitForMissingPacket()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);
            var provider = new DatetimeProvider();
            var baseTime = DateTime.Now;
            var buffer = new RTPReorderBuffer(TimeSpan.FromMilliseconds(300), provider);
   
            provider.Time = baseTime;
            buffer.Add(CreatePacket(1, baseTime));
            buffer.Add(CreatePacket(3, baseTime + TimeSpan.FromMilliseconds(100)));

            AssertSequenceNumber(buffer, 1);
            Assert.False(buffer.Get(out _));

            buffer.Add(CreatePacket(2, baseTime + TimeSpan.FromMilliseconds(200)));

            AssertSequenceNumber(buffer, 2);
            AssertSequenceNumber(buffer, 3);
        }

        [Fact]
        public void ShouldSkipPacketAfterSpecifiedTimeout()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);
            var provider = new DatetimeProvider();
            var baseTime = DateTime.Now;
            var buffer = new RTPReorderBuffer(TimeSpan.FromMilliseconds(300), provider);

            provider.Time = baseTime;
            buffer.Add(CreatePacket(1, baseTime));
            buffer.Add(CreatePacket(3, baseTime + TimeSpan.FromMilliseconds(100)));

            AssertSequenceNumber(buffer, 1);
            Assert.False(buffer.Get(out var p1));

            provider.Time = baseTime + TimeSpan.FromMilliseconds(400);
            
            AssertSequenceNumber(buffer, 3);
        }

        [Fact]
        public void ShouldSkipPacketAfterSpecifiedTimeoutWithWrapAround()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);
            var provider = new DatetimeProvider();
            var baseTime = DateTime.Now;
            var buffer = new RTPReorderBuffer(TimeSpan.FromMilliseconds(300), provider);

            provider.Time = baseTime;
            buffer.Add(CreatePacket(65534, baseTime));
            buffer.Add(CreatePacket(0, baseTime + TimeSpan.FromMilliseconds(100)));

            AssertSequenceNumber(buffer, 65534);
            Assert.False(buffer.Get(out var p1));

            buffer.Add(CreatePacket(65535, baseTime + TimeSpan.FromMilliseconds(200)));

            AssertSequenceNumber(buffer, 65535);
            AssertSequenceNumber(buffer, 0);
        }

        private void AssertSequenceNumber(RTPReorderBuffer buffer, ushort expected) {
            Assert.True(buffer.Get(out var p1));
            Assert.Equal(expected, p1.Header.SequenceNumber);
        }

        private RTPPacket CreatePacket(ushort seq, DateTime datetime = default) {
            return new RTPPacket() { Header = new RTPHeader() { SequenceNumber = seq, ReceivedTime = datetime } };
        }
    }
}
