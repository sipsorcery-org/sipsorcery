using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using SIPSorcery.Sys;
using Xunit;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;

namespace SIPSorcery.Sys.UnitTests
{
    [Trait("Category", "unit")]
    public class SequenceReaderUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SequenceReaderUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void SequenceReader_Constructor_WithEmptySequence_ShouldInitializeCorrectly()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            
            var emptySequence = ReadOnlySequence<byte>.Empty;
            var reader = new SequenceReader<byte>(emptySequence);

            Assert.Equal(0, reader.Consumed);
            Assert.Equal(0, reader.Remaining);

            logger.LogDebug("Empty sequence reader initialized correctly");
        }

        [Fact]
        public void SequenceReader_Constructor_WithSingleSegment_ShouldInitializeCorrectly()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            
            var data = new byte[] { 1, 2, 3, 4, 5 };
            var sequence = new ReadOnlySequence<byte>(data);
            var reader = new SequenceReader<byte>(sequence);

            Assert.Equal(0, reader.Consumed);
            Assert.Equal(5, reader.Remaining);

            logger.LogDebug("Single segment reader initialized correctly");
        }

        [Fact]
        public void SequenceReader_TryCopyTo_WithSufficientData_ShouldSucceed()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            
            var data = new byte[] { 10, 20, 30, 40, 50 };
            var sequence = new ReadOnlySequence<byte>(data);
            var reader = new SequenceReader<byte>(sequence);

            var buffer = new byte[3];
            bool result = reader.TryCopyTo(buffer.AsSpan());

            Assert.True(result);
            Assert.Equal(10, buffer[0]);
            Assert.Equal(20, buffer[1]);
            Assert.Equal(30, buffer[2]);
            Assert.Equal(0, reader.Consumed); // Consumed should not change after TryCopyTo
            Assert.Equal(5, reader.Remaining); // Remaining should not change after TryCopyTo

            logger.LogDebug("TryCopyTo with sufficient data succeeded");
        }

        [Fact]
        public void SequenceReader_TryCopyTo_WithInsufficientData_ShouldFail()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            
            var data = new byte[] { 10, 20 };
            var sequence = new ReadOnlySequence<byte>(data);
            var reader = new SequenceReader<byte>(sequence);

            var buffer = new byte[5];
            bool result = reader.TryCopyTo(buffer.AsSpan());

            Assert.False(result);
            Assert.Equal(0, reader.Consumed);
            Assert.Equal(2, reader.Remaining);

            logger.LogDebug("TryCopyTo with insufficient data failed correctly");
        }

        [Fact]
        public void SequenceReader_TryCopyTo_WithEmptyDestination_ShouldSucceed()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            
            var data = new byte[] { 10, 20, 30 };
            var sequence = new ReadOnlySequence<byte>(data);
            var reader = new SequenceReader<byte>(sequence);

            var buffer = new byte[0];
            bool result = reader.TryCopyTo(buffer.AsSpan());

            Assert.True(result);
            Assert.Equal(0, reader.Consumed);
            Assert.Equal(3, reader.Remaining);

            logger.LogDebug("TryCopyTo with empty destination succeeded");
        }

        [Fact]
        public void SequenceReader_Advance_WithValidCount_ShouldUpdatePosition()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            
            var data = new byte[] { 10, 20, 30, 40, 50 };
            var sequence = new ReadOnlySequence<byte>(data);
            var reader = new SequenceReader<byte>(sequence);

            reader.Advance(2);

            Assert.Equal(2, reader.Consumed);
            Assert.Equal(3, reader.Remaining);

            // Test that subsequent operations work correctly
            var buffer = new byte[2];
            bool result = reader.TryCopyTo(buffer.AsSpan());

            Assert.True(result);
            Assert.Equal(30, buffer[0]);
            Assert.Equal(40, buffer[1]);

            logger.LogDebug("Advance with valid count updated position correctly");
        }

        [Fact]
        public void SequenceReader_Advance_WithZeroCount_ShouldNotChangePosition()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            
            var data = new byte[] { 10, 20, 30 };
            var sequence = new ReadOnlySequence<byte>(data);
            var reader = new SequenceReader<byte>(sequence);

            reader.Advance(0);

            Assert.Equal(0, reader.Consumed);
            Assert.Equal(3, reader.Remaining);

            logger.LogDebug("Advance with zero count did not change position");
        }

        [Fact]
        public void SequenceReader_Advance_WithNegativeCount_ShouldThrowException()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            
            var data = new byte[] { 10, 20, 30 };
            var sequence = new ReadOnlySequence<byte>(data);
            var reader = new SequenceReader<byte>(sequence);

            try
            {
                reader.Advance(-1);
                Assert.Fail("Expected ArgumentOutOfRangeException was not thrown");
            }
            catch (ArgumentOutOfRangeException)
            {
                // Expected exception
                logger.LogDebug("Advance with negative count threw exception correctly");
            }
        }

        [Fact]
        public void SequenceReader_Advance_BeyondSequenceLength_ShouldThrowException()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            
            var data = new byte[] { 10, 20, 30 };
            var sequence = new ReadOnlySequence<byte>(data);
            var reader = new SequenceReader<byte>(sequence);

            try
            {
                reader.Advance(10); // Advance beyond sequence length
                Assert.Fail("Expected ArgumentOutOfRangeException was not thrown");
            }
            catch (ArgumentOutOfRangeException)
            {
                // Expected exception
                logger.LogDebug("Advance beyond sequence length threw exception correctly");
            }
        }

        [Fact]
        public void SequenceReader_Advance_ExactlyToEnd_ShouldWork()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            
            var data = new byte[] { 10, 20, 30 };
            var sequence = new ReadOnlySequence<byte>(data);
            var reader = new SequenceReader<byte>(sequence);

            reader.Advance(3); // Advance exactly to end

            Assert.Equal(3, reader.Consumed);
            Assert.Equal(0, reader.Remaining);

            // TryCopyTo should fail now
            var buffer = new byte[1];
            bool result = reader.TryCopyTo(buffer.AsSpan());
            Assert.False(result);

            logger.LogDebug("Advance exactly to end worked correctly");
        }

        [Fact]
        public void SequenceReader_MultipleAdvance_ShouldAccumulate()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            
            var data = new byte[] { 10, 20, 30, 40, 50, 60 };
            var sequence = new ReadOnlySequence<byte>(data);
            var reader = new SequenceReader<byte>(sequence);

            reader.Advance(2);
            Assert.Equal(2, reader.Consumed);
            Assert.Equal(4, reader.Remaining);

            reader.Advance(1);
            Assert.Equal(3, reader.Consumed);
            Assert.Equal(3, reader.Remaining);

            reader.Advance(2);
            Assert.Equal(5, reader.Consumed);
            Assert.Equal(1, reader.Remaining);

            // Verify final position with TryCopyTo
            var buffer = new byte[1];
            bool result = reader.TryCopyTo(buffer.AsSpan());
            Assert.True(result);
            Assert.Equal(60, buffer[0]);

            // Verify that trying to advance beyond remaining elements throws exception
            try
            {
                reader.Advance(2); // Only 1 element remaining, this should throw
                Assert.Fail("Expected ArgumentOutOfRangeException was not thrown");
            }
            catch (ArgumentOutOfRangeException)
            {
                // Expected exception
                logger.LogDebug("Advance beyond remaining elements threw exception correctly");
            }

            logger.LogDebug("Multiple advances accumulated correctly");
        }

        [Fact]
        public void SequenceReader_WithMultipleSegments_ShouldWorkCorrectly()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            
            // Create a multi-segment sequence
            var segment1 = new byte[] { 1, 2 };
            var segment2 = new byte[] { 3, 4, 5 };
            var segment3 = new byte[] { 6 };

            var firstSegment = new TestSegment(segment1);
            var secondSegment = firstSegment.Append(segment2);
            var thirdSegment = secondSegment.Append(segment3);

            var sequence = new ReadOnlySequence<byte>(firstSegment, 0, thirdSegment, segment3.Length);
            var reader = new SequenceReader<byte>(sequence);

            Assert.Equal(0, reader.Consumed);
            Assert.Equal(6, reader.Remaining);

            // Test TryCopyTo across segments
            var buffer = new byte[4];
            bool result = reader.TryCopyTo(buffer.AsSpan());
            Assert.True(result);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, buffer);

            // Test Advance across segments
            reader.Advance(3);
            Assert.Equal(3, reader.Consumed);
            Assert.Equal(3, reader.Remaining);

            var buffer2 = new byte[2];
            result = reader.TryCopyTo(buffer2.AsSpan());
            Assert.True(result);
            Assert.Equal(new byte[] { 4, 5 }, buffer2);

            logger.LogDebug("Multi-segment sequence worked correctly");
        }

        [Fact]
        public void SequenceReader_AdvanceAcrossSegmentBoundaries_ShouldWorkCorrectly()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            
            // Create segments of different sizes
            var segment1 = new byte[] { 1, 2, 3 };
            var segment2 = new byte[] { 4, 5 };
            var segment3 = new byte[] { 6, 7, 8, 9 };

            var firstSegment = new TestSegment(segment1);
            var secondSegment = firstSegment.Append(segment2);
            var thirdSegment = secondSegment.Append(segment3);

            var sequence = new ReadOnlySequence<byte>(firstSegment, 0, thirdSegment, segment3.Length);
            var reader = new SequenceReader<byte>(sequence);

            // Advance exactly to segment boundary
            reader.Advance(3);
            Assert.Equal(3, reader.Consumed);
            Assert.Equal(6, reader.Remaining);

            var buffer = new byte[1];
            bool result = reader.TryCopyTo(buffer.AsSpan());
            Assert.True(result);
            Assert.Equal(4, buffer[0]); // First byte of second segment

            // Advance across segment boundary
            reader.Advance(2);
            Assert.Equal(5, reader.Consumed);
            Assert.Equal(4, reader.Remaining);

            result = reader.TryCopyTo(buffer.AsSpan());
            Assert.True(result);
            Assert.Equal(6, buffer[0]); // First byte of third segment

            logger.LogDebug("Advance across segment boundaries worked correctly");
        }

        [Fact]
        public void SequenceReader_TryCopyToAcrossSegments_ShouldWorkCorrectly()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            
            var segment1 = new byte[] { 1, 2 };
            var segment2 = new byte[] { 3, 4 };
            var segment3 = new byte[] { 5, 6 };

            var firstSegment = new TestSegment(segment1);
            var secondSegment = firstSegment.Append(segment2);
            var thirdSegment = secondSegment.Append(segment3);

            var sequence = new ReadOnlySequence<byte>(firstSegment, 0, thirdSegment, segment3.Length);
            var reader = new SequenceReader<byte>(sequence);

            // TryCopyTo spanning multiple segments
            var buffer = new byte[5];
            bool result = reader.TryCopyTo(buffer.AsSpan());
            Assert.True(result);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, buffer);

            logger.LogDebug("TryCopyTo across segments worked correctly");
        }

        [Fact]
        public void SequenceReader_WithDifferentTypes_ShouldWork()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            
            // Test with int type
            var intData = new int[] { 100, 200, 300, 400 };
            var intSequence = new ReadOnlySequence<int>(intData);
            var intReader = new SequenceReader<int>(intSequence);

            Assert.Equal(0, intReader.Consumed);
            Assert.Equal(4, intReader.Remaining);

            var intBuffer = new int[2];
            bool result = intReader.TryCopyTo(intBuffer.AsSpan());
            Assert.True(result);
            Assert.Equal(new int[] { 100, 200 }, intBuffer);

            intReader.Advance(1);
            Assert.Equal(1, intReader.Consumed);
            Assert.Equal(3, intReader.Remaining);

            result = intReader.TryCopyTo(intBuffer.AsSpan());
            Assert.True(result);
            Assert.Equal(new int[] { 200, 300 }, intBuffer);

            logger.LogDebug("SequenceReader with different types worked correctly");
        }

        [Fact]
        public void SequenceReader_EmptySequence_Operations_ShouldHandleCorrectly()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            
            var emptySequence = ReadOnlySequence<byte>.Empty;
            var reader = new SequenceReader<byte>(emptySequence);

            // All operations should handle empty sequence gracefully
            Assert.Equal(0, reader.Consumed);
            Assert.Equal(0, reader.Remaining);

            var buffer = new byte[1];
            bool result = reader.TryCopyTo(buffer.AsSpan());
            Assert.False(result);

            reader.Advance(0);
            Assert.Equal(0, reader.Consumed);
            Assert.Equal(0, reader.Remaining);

            // Should throw when trying to advance beyond empty sequence
            try
            {
                reader.Advance(5);
                Assert.Fail("Expected ArgumentOutOfRangeException was not thrown");
            }
            catch (ArgumentOutOfRangeException)
            {
                // Expected exception
                logger.LogDebug("Advance beyond empty sequence threw exception correctly");
            }

            logger.LogDebug("Empty sequence operations handled correctly");
        }

        [Fact]
        public void SequenceReader_Properties_ShouldBeReadonly()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            
            var data = new byte[] { 1, 2, 3, 4, 5 };
            var sequence = new ReadOnlySequence<byte>(data);
            var reader = new SequenceReader<byte>(sequence);

            // Verify initial state
            Assert.Equal(0, reader.Consumed);
            Assert.Equal(5, reader.Remaining);

            // Advance and verify properties update correctly
            reader.Advance(2);
            Assert.Equal(2, reader.Consumed);
            Assert.Equal(3, reader.Remaining);

            reader.Advance(1);
            Assert.Equal(3, reader.Consumed);
            Assert.Equal(2, reader.Remaining);

            logger.LogDebug("Readonly properties worked correctly");
        }

        // Helper class for getting method names
        private static class TestHelper
        {
            public static string GetCurrentMethodName([CallerMemberName] string methodName = default) => methodName;
        }

        // Helper class for creating multi-segment sequences
        private class TestSegment : ReadOnlySequenceSegment<byte>
        {
            public TestSegment(ReadOnlyMemory<byte> memory)
            {
                Memory = memory;
            }

            public TestSegment Append(ReadOnlyMemory<byte> memory)
            {
                var segment = new TestSegment(memory)
                {
                    RunningIndex = RunningIndex + Memory.Length
                };
                Next = segment;
                return segment;
            }
        }
    }
}
