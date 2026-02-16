//-----------------------------------------------------------------------------
// Filename: PooledSegmentedBufferUnitTest.cs
//
// Description: Unit tests for the PooledSegmentedBuffer<T> class.
//
// Author(s):
// GitHub Copilot
// 
// History:
// 2025   GitHub Copilot   Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Sys.UnitTests
{
    [Trait("Category", "unit")]
    public class PooledSegmentedBufferUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public PooledSegmentedBufferUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void Constructor_DefaultValues_InitializeLengthCorrectly()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();

            Assert.Equal(0, buffer.Length);
        }

        [Fact]
        public void Advance_WithValidCountAvailableCapacity_UpdatesLength()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>(16);
            var mem = buffer.GetMemory(8);

            buffer.Advance(8);

            Assert.Equal(8, buffer.Length);
        }

        [Fact]
        public void Advance_WithNegativeCount_ThrowsArgumentOutOfRangeException()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>(8);
            buffer.GetSpan(4);

            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Advance(-1));
        }

        [Fact]
        public void Advance_WithZeroCount_Succeeds()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>(8);
            buffer.GetSpan(4);

            buffer.Advance(0);

            Assert.Equal(0, buffer.Length);
        }

        [Fact]
        public void Advance_WithCountLargerThanAvailable_ThrowsInvalidOperationException()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>(8);
            buffer.GetSpan(4);

            Assert.Throws<InvalidOperationException>(() => buffer.Advance(100));
        }

        [Fact]
        public void GetReadOnlySequence_WithEmptyBuffer_ReturnsEmptySequence()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>(4);

            var sequence = buffer.GetReadOnlySequence();

            Assert.True(sequence.IsEmpty);
            Assert.Equal(0, sequence.Length);
        }

        [Fact]
        public void GetReadOnlySequence_WithSingleWrite_ReturnsSingleSegment()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>(4);
            var data = Guid.NewGuid().ToByteArray();

            data.CopyTo(buffer.GetSpan(data.Length));
            buffer.Advance(data.Length);

            var sequence = buffer.GetReadOnlySequence();

            Assert.True(sequence.IsSingleSegment);
#if NET5_0_OR_GREATER
            Assert.True(sequence.FirstSpan.SequenceEqual(data));
#else
            Assert.True(sequence.First.Span.SequenceEqual(data));
#endif
        }

        [Fact]
        public void GetReadOnlySequence_WithMultipleWrites_ReturnsMultipleSegments()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            const int increaseSize = 31;
            var lengths = new int[] {
                increaseSize / 2,
                increaseSize,
                increaseSize * 2,
                increaseSize * 3,
                increaseSize * 4,
                increaseSize * 5
            };
            var totalLength = lengths.Sum();
            var totalData = new byte[totalLength];

            using var buffer = new PooledSegmentedBuffer<byte>(increaseSize);

            var totalDataWriter = totalData.AsSpan();
            var random = new Random(42); // Use deterministic seed
            foreach (var length in lengths)
            {
                var span = buffer.GetSpan(length).Slice(0, length);
                FillRandomBytes(random, span);
                span.CopyTo(totalDataWriter);
                totalDataWriter = totalDataWriter.Slice(length);
                buffer.Advance(length);
            }

            var sequence = buffer.GetReadOnlySequence();

            Assert.False(sequence.IsEmpty);
            Assert.False(sequence.IsSingleSegment);

            var lengthsIndex = 0;
            foreach (var segment in sequence)
            {
                Assert.Equal(lengths[lengthsIndex++], segment.Length);
            }

            Assert.Equal(totalData, SequenceToArray(sequence));
        }

        [Fact]
        public void GetReadOnlySequence_WithMultipleWrites_WritesAcrossSegments()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            const int increaseSize = 33;
            var lengths = new int[] {
                increaseSize / 3,
                increaseSize / 2,
                increaseSize,
                increaseSize * 5
            };
            var totalLength = lengths.Sum();
            var totalData = new byte[totalLength];

            using var buffer = new PooledSegmentedBuffer<byte>(increaseSize);

            var totalDataWriter = totalData.AsSpan();
            var random = new Random(42); // Use deterministic seed
            foreach (var length in lengths)
            {
                var span = totalDataWriter.Slice(0, length);
                FillRandomBytes(random, span);
                WriteToBuffer(buffer, span); // Helper method to write span to buffer
                totalDataWriter = totalDataWriter.Slice(length);
            }

            var sequence = buffer.GetReadOnlySequence();

            Assert.False(sequence.IsEmpty);
            Assert.False(sequence.IsSingleSegment);
            Assert.Equal(totalData, SequenceToArray(sequence));
        }

        [Fact]
        public void Dispose_WhenCalled_ResetsStateAndDoesNotAllowFurtherWrites()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var buffer = new PooledSegmentedBuffer<byte>(4);
            buffer.GetSpan(4)[0] = 1;
            buffer.Advance(1);

            buffer.Dispose();

            buffer.Dispose(); // Should not throw

            Assert.Equal(0, buffer.Length);
            Assert.Throws<ObjectDisposedException>(() => buffer.GetSpan(2));
        }

        [Fact]
        public void Dispose_WhenCalledMultipleTimes_NoExceptionAndBuffersReleased()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var buffer = new PooledSegmentedBuffer<byte>(4);
            buffer.GetSpan(4)[0] = 1;
            buffer.Advance(1);

            buffer.Dispose();

            buffer.Dispose(); // Should not throw

            Assert.Equal(0, buffer.Length);
        }

        [Fact]
        public void Slice_EntireBuffer_ReturnsAllData()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            const int segmentSize = 16;
            var segmentSizes = new[] { segmentSize, segmentSize * 2, (int)(segmentSize * 1.5) };
            var totalLength = segmentSizes.Sum();
            var allData = new byte[totalLength];
            using var buffer = new PooledSegmentedBuffer<byte>(segmentSize);
            var offset = 0;
            for (var i = 0; i < segmentSizes.Length; i++)
            {
                var span = buffer.GetSpan(segmentSizes[i]).Slice(0, segmentSizes[i]);
                for (var j = 0; j < segmentSizes[i]; j++)
                {
                    span[j] = (byte)(offset + j);
                }

                span.CopyTo(allData.AsSpan(offset, segmentSizes[i]));
                buffer.Advance(segmentSizes[i]);
                offset += segmentSizes[i];
            }

            Assert.Equal(totalLength, buffer.Length);

            buffer.Slice(0, totalLength);

            Assert.Equal(totalLength, buffer.Length);
            Assert.Equal(allData, SequenceToArray(buffer.GetReadOnlySequence()));
        }

        [Fact]
        public void Slice_AtStart_ReturnsStartData()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            const int segmentSize = 16;
            var segmentSizes = new[] { segmentSize, segmentSize * 2, (int)(segmentSize * 1.5) };
            var totalLength = segmentSizes.Sum();
            var allData = new byte[totalLength];
            using var buffer = new PooledSegmentedBuffer<byte>(segmentSize);
            var offset = 0;
            for (var i = 0; i < segmentSizes.Length; i++)
            {
                var span = buffer.GetSpan(segmentSizes[i]).Slice(0, segmentSizes[i]);
                for (var j = 0; j < segmentSizes[i]; j++)
                {
                    span[j] = (byte)(offset + j);
                }

                span.CopyTo(allData.AsSpan(offset, segmentSizes[i]));
                buffer.Advance(segmentSizes[i]);
                offset += segmentSizes[i];
            }

            Assert.Equal(totalLength, buffer.Length);

            const int sliceLength = 8;

            buffer.Slice(0, sliceLength);

            Assert.Equal(sliceLength, buffer.Length);
            Assert.Equal(allData.AsSpan(0, sliceLength).ToArray(), SequenceToArray(buffer.GetReadOnlySequence()));
        }

        [Fact]
        public void Slice_InMiddle_ReturnsMiddleData()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            const int segmentSize = 16;
            var segmentSizes = new[] { segmentSize, segmentSize * 2, (int)(segmentSize * 1.5) };
            var totalLength = segmentSizes.Sum();
            var allData = new byte[totalLength];
            using var buffer = new PooledSegmentedBuffer<byte>(segmentSize);
            var offset = 0;
            for (var i = 0; i < segmentSizes.Length; i++)
            {
                var span = buffer.GetSpan(segmentSizes[i]).Slice(0, segmentSizes[i]);
                for (var j = 0; j < segmentSizes[i]; j++)
                {
                    span[j] = (byte)(offset + j);
                }

                span.CopyTo(allData.AsSpan(offset, segmentSizes[i]));
                buffer.Advance(segmentSizes[i]);
                offset += segmentSizes[i];
            }

            Assert.Equal(totalLength, buffer.Length);

            var sliceStart = segmentSizes[0] / 2;
            var sliceLength = segmentSizes[1];

            buffer.Slice(sliceStart, sliceLength);

            Assert.Equal(sliceLength, buffer.Length);
            Assert.Equal(allData.AsSpan(sliceStart, sliceLength).ToArray(), SequenceToArray(buffer.GetReadOnlySequence()));
        }

        [Fact]
        public void Slice_AtEnd_ReturnsEndData()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            const int segmentSize = 16;
            var segmentSizes = new[] { segmentSize, segmentSize * 2, (int)(segmentSize * 1.5) };
            var totalLength = segmentSizes.Sum();
            var allData = new byte[totalLength];
            using var buffer = new PooledSegmentedBuffer<byte>(segmentSize);
            var offset = 0;
            for (var i = 0; i < segmentSizes.Length; i++)
            {
                var span = buffer.GetSpan(segmentSizes[i]).Slice(0, segmentSizes[i]);
                for (var j = 0; j < segmentSizes[i]; j++)
                {
                    span[j] = (byte)(offset + j);
                }

                span.CopyTo(allData.AsSpan(offset, segmentSizes[i]));
                buffer.Advance(segmentSizes[i]);
                offset += segmentSizes[i];
            }

            var sliceLength = segmentSizes[2] / 2;
            var sliceStart = buffer.Length - sliceLength;

            buffer.Slice(sliceStart, sliceLength);

            Assert.Equal(sliceLength, buffer.Length);
            Assert.Equal(allData.AsSpan(sliceStart, sliceLength).ToArray(), SequenceToArray(buffer.GetReadOnlySequence()));
        }

        [Fact]
        public void Slice_ZeroLength_ReturnsEmpty()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            const int segmentSize = 16;
            using var buffer = new PooledSegmentedBuffer<byte>(segmentSize);
            for (var i = 0; i < segmentSize; i++)
            {
                buffer.GetSpan(segmentSize).Slice(0, segmentSize).Fill((byte)i);
                buffer.Advance(segmentSize);
            }

            buffer.Slice(0, 0);

            Assert.Equal(0, buffer.Length);
            Assert.True(buffer.GetReadOnlySequence().IsEmpty);
        }

        [Fact]
        public void Slice_OutOfBounds_ThrowsArgumentOutOfRangeException()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            const int segmentSize = 16;
            using var buffer = new PooledSegmentedBuffer<byte>(segmentSize);
            buffer.GetSpan(segmentSize).Fill(1);
            buffer.Advance(segmentSize);

            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Slice(1, buffer.Length + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Slice(-1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Slice(0, -1));
        }

        [Fact]
        public void Slice_OnlyCurrentBuffer_ReturnsCorrectData()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            const int segmentSize = 16;
            using var buffer = new PooledSegmentedBuffer<byte>(segmentSize);
            var data = new byte[segmentSize];
            for (var i = 0; i < segmentSize; i++)
            {
                data[i] = (byte)(i + 1);
            }

            data.CopyTo(buffer.GetSpan(segmentSize));
            buffer.Advance(segmentSize);

            var sliceStart = segmentSize / 4;
            var sliceLength = segmentSize - sliceStart;

            buffer.Slice(sliceStart, sliceLength);

            Assert.Equal(sliceLength, buffer.Length);
            Assert.Equal(data.AsSpan(sliceStart, sliceLength).ToArray(), SequenceToArray(buffer.GetReadOnlySequence()));
        }

        [Fact]
        public void Slice_OnlySegmentedBuffers_ReturnsCorrectData()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            const int segmentSize = 16;
            using var buffer = new PooledSegmentedBuffer<byte>(segmentSize);
            var segmentCount = 3;
            var actualSegmentSize = segmentSize * 4 / 3;
            var dataLength = actualSegmentSize * segmentCount;
            var data = new byte[dataLength];
            for (var i = 0; i < dataLength; i++)
            {
                data[i] = (byte)(i + 10);
            }

            for (var i = 0; i < segmentCount; i++)
            {
                var segment = data.AsSpan(i * actualSegmentSize, actualSegmentSize);
                segment.CopyTo(buffer.GetSpan(actualSegmentSize).Slice(0, actualSegmentSize));
                buffer.Advance(actualSegmentSize);
            }

            var sliceStart = actualSegmentSize;
            var sliceLength = segmentSize;

            buffer.Slice(sliceStart, sliceLength);

            Assert.Equal(sliceLength, buffer.Length);
            Assert.Equal(data.AsSpan(sliceStart, sliceLength).ToArray(), SequenceToArray(buffer.GetReadOnlySequence()));
        }

        [Fact]
        public void Slice_TrimBuffersFromStart_RemovesEarlierBuffers()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            const int segmentSize = 16;
            var segmentSizes = new[] { segmentSize, segmentSize * 2, (int)(segmentSize * 1.5) };
            var totalLength = segmentSizes.Sum();
            var allData = new byte[totalLength];
            using var buffer = new PooledSegmentedBuffer<byte>(segmentSize);
            var offset = 0;
            for (var i = 0; i < segmentSizes.Length; i++)
            {
                var span = buffer.GetSpan(segmentSizes[i]).Slice(0, segmentSizes[i]);
                for (var j = 0; j < segmentSizes[i]; j++)
                {
                    span[j] = (byte)(offset + j);
                }

                span.CopyTo(allData.AsSpan(offset, segmentSizes[i]));
                buffer.Advance(segmentSizes[i]);
                offset += segmentSizes[i];
            }

            var sliceOffset = segmentSizes[0] + segmentSizes[1] / 2;
            var sliceLength = totalLength - sliceOffset;

            buffer.Slice(sliceOffset, sliceLength);

            Assert.Equal(sliceLength, buffer.Length);
            Assert.Equal(allData.AsSpan(sliceOffset, sliceLength).ToArray(), SequenceToArray(buffer.GetReadOnlySequence()));
        }

        [Fact]
        public void Slice_TrimBuffersFromEnd_RemovesLaterBuffers()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            const int segmentSize = 16;
            var segmentSizes = new[] { segmentSize, segmentSize * 2, (int)(segmentSize * 1.5) };
            var totalLength = segmentSizes.Sum();
            var allData = new byte[totalLength];
            using var buffer = new PooledSegmentedBuffer<byte>(segmentSize);
            var offset = 0;
            for (var i = 0; i < segmentSizes.Length; i++)
            {
                var span = buffer.GetSpan(segmentSizes[i]).Slice(0, segmentSizes[i]);
                for (var j = 0; j < segmentSizes[i]; j++)
                {
                    span[j] = (byte)(offset + j);
                }

                span.CopyTo(allData.AsSpan(offset, segmentSizes[i]));
                buffer.Advance(segmentSizes[i]);
                offset += segmentSizes[i];
            }

            var sliceOffset = 2; // start at offset 2
            var sliceLength = segmentSizes[0] + 4; // ends in second buffer

            buffer.Slice(sliceOffset, sliceLength);

            Assert.Equal(sliceLength, buffer.Length);
            Assert.Equal(allData.AsSpan(sliceOffset, sliceLength).ToArray(), SequenceToArray(buffer.GetReadOnlySequence()));
        }

        /// <summary>
        /// Helper method to write a span to the buffer, similar to a Write method
        /// </summary>
        private static void WriteToBuffer<T>(PooledSegmentedBuffer<T> buffer, ReadOnlySpan<T> data)
        {
            var remaining = data;
            while (!remaining.IsEmpty)
            {
                var span = buffer.GetSpan(remaining.Length);
                var bytesToCopy = Math.Min(remaining.Length, span.Length);
                remaining.Slice(0, bytesToCopy).CopyTo(span);
                buffer.Advance(bytesToCopy);
                remaining = remaining.Slice(bytesToCopy);
            }
        }

        /// <summary>
        /// Helper method to fill span with random bytes, compatible with older .NET versions
        /// </summary>
        private static void FillRandomBytes(Random random, Span<byte> span)
        {
#if NET5_0_OR_GREATER
            random.NextBytes(span);
#else
            var buffer = new byte[span.Length];
            random.NextBytes(buffer);
            buffer.CopyTo(span);
#endif
        }

        /// <summary>
        /// Helper method to convert ReadOnlySequence to array
        /// </summary>
        private static T[] SequenceToArray<T>(ReadOnlySequence<T> sequence)
        {
            if (sequence.IsEmpty)
            {
                return Array.Empty<T>();
            }

            if (sequence.IsSingleSegment)
            {
                return sequence.First.Span.ToArray();
            }

            var result = new T[sequence.Length];
            var destinationSpan = result.AsSpan();
            sequence.CopyTo(destinationSpan);
            return result;
        }

        [Fact]
        public void Clear_AfterWrite_ResetsStateAndBuffers()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>(4);
            buffer.GetSpan(4)[0] = 1;
            buffer.Advance(1);

            buffer.Clear();

            Assert.Equal(0, buffer.Length);
            Assert.True(buffer.GetReadOnlySequence().IsEmpty);
        }

        [Fact]
        public void MultipleBuffers_Segmentation_WritesAcrossSegments()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>(4);

            for (var i = 0; i < 10; i++)
            {
                var span = buffer.GetSpan(2);
                span[0] = (byte)i;
                span[1] = (byte)(i + 1);
                buffer.Advance(2);
            }

            Assert.Equal(20, buffer.Length);
        }

        [Fact]
        public void ToArrayTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();
            var testData = Encoding.UTF8.GetBytes("Hello, World! This is a test of the PooledSegmentedBuffer ToArray method.");

            // Write test data to buffer
            var memory = buffer.GetMemory(testData.Length);
            testData.CopyTo(memory);
            buffer.Advance(testData.Length);

            // Get array and verify
            var result = buffer.ToArray();

            Assert.Equal(testData.Length, result.Length);
            Assert.Equal(testData, result);
        }

        [Fact]
        public void ToArrayWithSegmentedDataTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>(minimalIncrease: 10); // Small segments to test multi-buffer scenario

            // Write data across multiple segments
            var expectedData = new List<byte>();
            for (int i = 0; i < 3; i++)
            {
                var segmentData = Encoding.UTF8.GetBytes($"Segment{i}:");
                var memory = buffer.GetMemory(segmentData.Length);
                segmentData.CopyTo(memory);
                buffer.Advance(segmentData.Length);
                expectedData.AddRange(segmentData);
            }

            // Get array and verify it contains all data in correct order
            var result = buffer.ToArray();
            var expected = expectedData.ToArray();

            Assert.Equal(expected.Length, result.Length);
            Assert.Equal(expected, result);
            Assert.Equal("Segment0:Segment1:Segment2:", Encoding.UTF8.GetString(result));
        }

        [Fact]
        public void ToArrayEmptyBufferTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();

            var result = buffer.ToArray();

            Assert.Empty(result);
            Assert.Same(Array.Empty<byte>(), result);
        }

        [Fact]
        public void ToArrayGenericTypeTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<int>();
            var testData = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

            // Write test data to buffer
            var memory = buffer.GetMemory(testData.Length);
            testData.CopyTo(memory);
            buffer.Advance(testData.Length);

            // Get array and verify
            var result = buffer.ToArray();

            Assert.Equal(testData.Length, result.Length);
            Assert.Equal(testData, result);
        }

        [Fact]
        public void ToArrayUnintializedOptimizationTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();
            var testData = new byte[8192]; // Large enough to benefit from uninitialized allocation

            // Fill with specific pattern to verify all bytes are properly copied
            for (int i = 0; i < testData.Length; i++)
            {
                testData[i] = (byte)(i % 256);
            }

            // Write test data
            var memory = buffer.GetMemory(testData.Length);
            testData.CopyTo(memory);
            buffer.Advance(testData.Length);

            // Get array - this should use GC.AllocateUninitializedArray on .NET 5+
            var result = buffer.ToArray();

            // Verify all data is correctly copied despite using uninitialized allocation
            Assert.Equal(testData.Length, result.Length);
            Assert.Equal(testData, result);

            // Verify the pattern is preserved
            for (int i = 0; i < result.Length; i++)
            {
                Assert.Equal((byte)(i % 256), result[i]);
            }
        }

        [Fact]
        public void AsStreamWithByteBufferTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();
            var testData = Encoding.UTF8.GetBytes("Hello, World! This is a test of the PooledSegmentedBuffer AsStream method.");

            // Write test data to buffer
            var memory = buffer.GetMemory(testData.Length);
            testData.CopyTo(memory);
            buffer.Advance(testData.Length);

            // Get stream and read data back
            using var stream = buffer.AsStream();

            Assert.True(stream.CanRead, "Stream should be readable.");
            Assert.True(stream.CanSeek, "Stream should be seekable.");
            Assert.False(stream.CanWrite, "Stream should not be writable.");
            Assert.Equal(testData.Length, stream.Length);
            Assert.Equal(0, stream.Position);

            // Read all data
            var readBuffer = new byte[testData.Length];
            var bytesRead = stream.Read(readBuffer, 0, readBuffer.Length);

            Assert.Equal(testData.Length, bytesRead);
            Assert.Equal(testData, readBuffer);
            Assert.Equal(testData.Length, stream.Position);
        }

        [Fact]
        public void AsStreamSeekTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();
            var testData = Encoding.UTF8.GetBytes("0123456789");

            // Write test data to buffer
            var memory = buffer.GetMemory(testData.Length);
            testData.CopyTo(memory);
            buffer.Advance(testData.Length);

            using var stream = buffer.AsStream();

            // Test seeking from beginning
            stream.Seek(5, SeekOrigin.Begin);
            Assert.Equal(5, stream.Position);

            var readByte = stream.ReadByte();
            Assert.Equal((byte)'5', readByte);

            // Test seeking from current position
            stream.Seek(-1, SeekOrigin.Current);
            Assert.Equal(5, stream.Position);

            readByte = stream.ReadByte();
            Assert.Equal((byte)'5', readByte);

            // Test seeking from end
            stream.Seek(-3, SeekOrigin.End);
            Assert.Equal(7, stream.Position);

            readByte = stream.ReadByte();
            Assert.Equal((byte)'7', readByte);
        }

        [Fact]
        public void AsStreamPartialReadTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();
            var testData = Encoding.UTF8.GetBytes("Hello, World!");

            // Write test data to buffer
            var memory = buffer.GetMemory(testData.Length);
            testData.CopyTo(memory);
            buffer.Advance(testData.Length);

            using var stream = buffer.AsStream();

            // Read in chunks
            var readBuffer = new byte[5];
            var bytesRead = stream.Read(readBuffer, 0, 5);

            Assert.Equal(5, bytesRead);
            Assert.Equal("Hello", Encoding.UTF8.GetString(readBuffer));
            Assert.Equal(5, stream.Position);

            // Read remaining bytes
            readBuffer = new byte[20]; // Larger than remaining data
            bytesRead = stream.Read(readBuffer, 0, 20);

            Assert.Equal(8, bytesRead); // ", World!" is 8 bytes
            Assert.Equal(", World!", Encoding.UTF8.GetString(readBuffer, 0, bytesRead));
            Assert.Equal(testData.Length, stream.Position);

            // Try to read past end
            bytesRead = stream.Read(readBuffer, 0, 5);
            Assert.Equal(0, bytesRead);
        }

        [Fact]
        public void AsStreamWithSegmentedDataTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>(minimalIncrease: 5); // Small buffer to force segmentation

            // Write data in multiple segments
            var segment1 = Encoding.UTF8.GetBytes("Hello");
            var segment2 = Encoding.UTF8.GetBytes(", ");
            var segment3 = Encoding.UTF8.GetBytes("World!");

            // Force multiple segments by writing in chunks
            var memory1 = buffer.GetMemory(segment1.Length);
            segment1.CopyTo(memory1);
            buffer.Advance(segment1.Length);

            var memory2 = buffer.GetMemory(segment2.Length);
            segment2.CopyTo(memory2);
            buffer.Advance(segment2.Length);

            var memory3 = buffer.GetMemory(segment3.Length);
            segment3.CopyTo(memory3);
            buffer.Advance(segment3.Length);

            using var stream = buffer.AsStream();

            // Read all data at once
            var expectedData = Encoding.UTF8.GetBytes("Hello, World!");
            var readBuffer = new byte[expectedData.Length];
            var bytesRead = stream.Read(readBuffer, 0, readBuffer.Length);

            Assert.Equal(expectedData.Length, bytesRead);
            Assert.Equal(expectedData, readBuffer);
        }

        [Fact]
        public void AsStreamWithNonByteBufferThrowsTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<int>();

            var exception = Assert.Throws<InvalidOperationException>(() => buffer.AsStream());
            Assert.Contains("AsStream is only supported for PooledSegmentedBuffer<byte>", exception.Message);
            Assert.Contains("PooledSegmentedBuffer<Int32>", exception.Message);
        }

        [Fact]
        public void AsStreamWriteOperationsThrowTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();
            var testData = Encoding.UTF8.GetBytes("Test");

            var memory = buffer.GetMemory(testData.Length);
            testData.CopyTo(memory);
            buffer.Advance(testData.Length);

            using var stream = buffer.AsStream();

            // Test Write method throws
            Assert.Throws<NotSupportedException>(() => stream.Write(new byte[1], 0, 1));

            // Test SetLength throws
            Assert.Throws<NotSupportedException>(() => stream.SetLength(10));
        }

        [Fact]
        public void AsStreamEmptyBufferTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();

            using var stream = buffer.AsStream();

            Assert.Equal(0, stream.Length);
            Assert.Equal(0, stream.Position);

            var readBuffer = new byte[10];
            var bytesRead = stream.Read(readBuffer, 0, 10);

            Assert.Equal(0, bytesRead);
        }

        [Fact]
        public void AsStreamReadOnlyTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();
            var testData = Encoding.UTF8.GetBytes("ReadOnly stream test.");

            // Write test data to buffer
            var memory = buffer.GetMemory(testData.Length);
            testData.CopyTo(memory);
            buffer.Advance(testData.Length);

            using var stream = buffer.AsStream();

            // Test stream properties
            Assert.True(stream.CanRead, "Stream should be readable.");
            Assert.True(stream.CanSeek, "Stream should be seekable.");
            Assert.False(stream.CanWrite, "Stream should not be writable.");

            // Read and verify data
            var readBuffer = new byte[testData.Length];
            var bytesRead = stream.Read(readBuffer, 0, readBuffer.Length);

            Assert.Equal(testData.Length, bytesRead);
            Assert.Equal(testData, readBuffer);

            // Attempt to write to stream (should throw)
            Assert.Throws<NotSupportedException>(() => stream.WriteByte(0));
        }

        [Fact]
        public void AsStreamReadSpanBasicTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();
            var testData = Encoding.UTF8.GetBytes("Hello, World! This is a test of Stream.Read(Span<byte>) method.");

            // Write test data to buffer
            var memory = buffer.GetMemory(testData.Length);
            testData.CopyTo(memory);
            buffer.Advance(testData.Length);

            // Get stream and read data using Span<byte> overload
            using var stream = buffer.AsStream();

            Assert.True(stream.CanRead, "Stream should be readable.");
            Assert.True(stream.CanSeek, "Stream should be seekable.");
            Assert.False(stream.CanWrite, "Stream should not be writable.");
            Assert.Equal(testData.Length, stream.Length);
            Assert.Equal(0, stream.Position);

#if NET5_0_OR_GREATER
            // Read all data using Span<byte>
            var readBuffer = new byte[testData.Length];
            var readSpan = readBuffer.AsSpan();
            var bytesRead = stream.Read(readSpan);

            Assert.Equal(testData.Length, bytesRead);
            Assert.Equal(testData, readBuffer);
            Assert.Equal(testData.Length, stream.Position);
#else
            // For older frameworks, use the byte array overload
            var readBuffer = new byte[testData.Length];
            var bytesRead = stream.Read(readBuffer, 0, readBuffer.Length);

            Assert.Equal(testData.Length, bytesRead);
            Assert.Equal(testData, readBuffer);
            Assert.Equal(testData.Length, stream.Position);
#endif
        }

        [Fact]
        public void AsStreamReadSpanPartialTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();
            var testData = Encoding.UTF8.GetBytes("Hello, Span World!");

            // Write test data to buffer
            var memory = buffer.GetMemory(testData.Length);
            testData.CopyTo(memory);
            buffer.Advance(testData.Length);

            using var stream = buffer.AsStream();

#if NET5_0_OR_GREATER
            // Read in chunks using Span<byte>
            var readBuffer = new byte[7];
            var readSpan = readBuffer.AsSpan();
            var bytesRead = stream.Read(readSpan);
            
            Assert.Equal(7, bytesRead);
            Assert.Equal("Hello, ", Encoding.UTF8.GetString(readBuffer));
            Assert.Equal(7, stream.Position);

            // Read remaining bytes using Span<byte>
            readBuffer = new byte[20]; // Larger than remaining data
            readSpan = readBuffer.AsSpan();
            bytesRead = stream.Read(readSpan);
            
            Assert.Equal(11, bytesRead); // "Span World!" is 11 bytes
            Assert.Equal("Span World!", Encoding.UTF8.GetString(readBuffer, 0, bytesRead));
            Assert.Equal(testData.Length, stream.Position);

            // Try to read past end using Span<byte>
            readBuffer = new byte[5];
            readSpan = readBuffer.AsSpan();
            bytesRead = stream.Read(readSpan);
            Assert.Equal(0, bytesRead);
#else
            // For older frameworks, use the byte array overload
            var readBuffer = new byte[7];
            var bytesRead = stream.Read(readBuffer, 0, 7);
            
            Assert.Equal(7, bytesRead);
            Assert.Equal("Hello, ", Encoding.UTF8.GetString(readBuffer));
            Assert.Equal(7, stream.Position);

            // Read remaining bytes using byte array
            readBuffer = new byte[20]; // Larger than remaining data
            bytesRead = stream.Read(readBuffer, 0, 20);
            
            Assert.Equal(11, bytesRead); // "Span World!" is 11 bytes
            Assert.Equal("Span World!", Encoding.UTF8.GetString(readBuffer, 0, bytesRead));
            Assert.Equal(testData.Length, stream.Position);

            // Try to read past end using byte array
            readBuffer = new byte[5];
            bytesRead = stream.Read(readBuffer, 0, 5);
            Assert.Equal(0, bytesRead);
#endif
        }

        [Fact]
        public void AsStreamReadSpanWithSegmentedDataTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>(minimalIncrease: 6); // Small buffer to force segmentation

            // Write data in multiple segments
            var segment1 = Encoding.UTF8.GetBytes("Span");
            var segment2 = Encoding.UTF8.GetBytes(", ");
            var segment3 = Encoding.UTF8.GetBytes("Read");
            var segment4 = Encoding.UTF8.GetBytes(" Test!");

            // Force multiple segments by writing in chunks
            var memory1 = buffer.GetMemory(segment1.Length);
            segment1.CopyTo(memory1);
            buffer.Advance(segment1.Length);

            var memory2 = buffer.GetMemory(segment2.Length);
            segment2.CopyTo(memory2);
            buffer.Advance(segment2.Length);

            var memory3 = buffer.GetMemory(segment3.Length);
            segment3.CopyTo(memory3);
            buffer.Advance(segment3.Length);

            var memory4 = buffer.GetMemory(segment4.Length);
            segment4.CopyTo(memory4);
            buffer.Advance(segment4.Length);

            using var stream = buffer.AsStream();

            // Read all data at once
            var expectedData = Encoding.UTF8.GetBytes("Span, Read Test!");

#if NET5_0_OR_GREATER
            var readBuffer = new byte[expectedData.Length];
            var readSpan = readBuffer.AsSpan();
            var bytesRead = stream.Read(readSpan);

            Assert.Equal(expectedData.Length, bytesRead);
            Assert.Equal(expectedData, readBuffer);
            Assert.Equal("Span, Read Test!", Encoding.UTF8.GetString(readBuffer));
            Assert.Equal(expectedData.Length, stream.Position);
#else
            var readBuffer = new byte[expectedData.Length];
            var bytesRead = stream.Read(readBuffer, 0, readBuffer.Length);

            Assert.Equal(expectedData.Length, bytesRead);
            Assert.Equal(expectedData, readBuffer);
            Assert.Equal("Span, Read Test!", Encoding.UTF8.GetString(readBuffer));
            Assert.Equal(expectedData.Length, stream.Position);
#endif
        }

        [Fact]
        public void AsStreamReadSpanVsArrayComparisonTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer1 = new PooledSegmentedBuffer<byte>();
            using var buffer2 = new PooledSegmentedBuffer<byte>();
            var testData = new byte[1000];
            new Random(42).NextBytes(testData);

            // Write same test data to both buffers
            var memory1 = buffer1.GetMemory(testData.Length);
            testData.CopyTo(memory1);
            buffer1.Advance(testData.Length);

            var memory2 = buffer2.GetMemory(testData.Length);
            testData.CopyTo(memory2);
            buffer2.Advance(testData.Length);

            using var stream1 = buffer1.AsStream();
            using var stream2 = buffer2.AsStream();

            // Read using array-based method
            var arrayBuffer = new byte[testData.Length];
            var arrayBytesRead = stream1.Read(arrayBuffer, 0, arrayBuffer.Length);

#if NET5_0_OR_GREATER
            // Read using Span<byte> method
            var spanBuffer = new byte[testData.Length];
            var spanBytesRead = stream2.Read(spanBuffer.AsSpan());

            // Results should be identical
            Assert.Equal(arrayBytesRead, spanBytesRead);
            Assert.Equal(testData.Length, arrayBytesRead);
            Assert.Equal(testData.Length, spanBytesRead);
            Assert.Equal(arrayBuffer, spanBuffer);
            Assert.Equal(testData, arrayBuffer);
            Assert.Equal(testData, spanBuffer);
            Assert.Equal(stream1.Position, stream2.Position);
#else
            // For older frameworks, just test the array method against itself
            stream2.Position = 0;
            var arrayBuffer2 = new byte[testData.Length];
            var arrayBytesRead2 = stream2.Read(arrayBuffer2, 0, arrayBuffer2.Length);

            // Results should be identical
            Assert.Equal(arrayBytesRead, arrayBytesRead2);
            Assert.Equal(testData.Length, arrayBytesRead);
            Assert.Equal(testData.Length, arrayBytesRead2);
            Assert.Equal(arrayBuffer, arrayBuffer2);
            Assert.Equal(testData, arrayBuffer);
            Assert.Equal(testData, arrayBuffer2);
#endif
        }

        [Fact]
        public void AsStreamWithNonByteBuffer_ThrowsInvalidOperationException()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<int>();
            buffer.GetSpan(10).Fill(42);
            buffer.Advance(10);

            Assert.Throws<InvalidOperationException>(() => buffer.AsStream());
        }

        [Fact]
        public void Constructor_WithCustomArrayPool_UsesCustomPool()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var customPool = ArrayPool<byte>.Create();

            using var buffer = new PooledSegmentedBuffer<byte>(1024, customPool);
            buffer.GetSpan(512).Fill(123);
            buffer.Advance(512);

            Assert.Equal(512, buffer.Length);
            Assert.True(buffer.GetReadOnlySequence().First.Span.ToArray().All(b => b == 123));
        }

        [Fact]
        public void GetMemory_WithNegativeSizeHint_DoesNotThrow()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();

            // It relies on Math.Max in AddNewBuffer which makes negative values become 0 or minimal increase
            var memory = buffer.GetMemory(-1);
            Assert.True(memory.Length > 0); // Should get some memory
        }

        [Fact]
        public void GetSpan_WithNegativeSizeHint_DoesNotThrow()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();

            var span = buffer.GetSpan(-1);
            Assert.True(span.Length > 0); // Should get some span
        }

        [Fact]
        public void GetMemory_AfterDispose_ThrowsObjectDisposedException()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var buffer = new PooledSegmentedBuffer<byte>();
            buffer.Dispose();

            Assert.Throws<ObjectDisposedException>(() => buffer.GetMemory(10));
        }

        [Fact]
        public void GetSpan_AfterDispose_ThrowsObjectDisposedException()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var buffer = new PooledSegmentedBuffer<byte>();
            buffer.Dispose();

            Assert.Throws<ObjectDisposedException>(() => buffer.GetSpan(10));
        }

        [Fact]
        public void Advance_AfterDispose_ThrowsInvalidOperationException()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var buffer = new PooledSegmentedBuffer<byte>();
            buffer.Dispose();

            Assert.Throws<InvalidOperationException>(() => buffer.Advance(1));
        }

        [Fact]
        public void GetReadOnlySequence_AfterDispose_Works()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var buffer = new PooledSegmentedBuffer<byte>();
            buffer.GetSpan(10).Fill(123);
            buffer.Advance(10);
            buffer.Dispose();

            var sequence = buffer.GetReadOnlySequence();

            Assert.True(sequence.IsEmpty); // After dispose, length should be 0
        }

        [Fact]
        public void AsStream_InvalidSeekOrigin_ThrowsArgumentException()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();
            var testData = Encoding.UTF8.GetBytes("Test");
            var memory = buffer.GetMemory(testData.Length);
            testData.CopyTo(memory);
            buffer.Advance(testData.Length);

            using var stream = buffer.AsStream();

            Assert.Throws<ArgumentException>(() => stream.Seek(0, (SeekOrigin)999));
        }

        [Fact]
        public void AsStream_SeekToNegativePosition_ThrowsIOException()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();
            var testData = Encoding.UTF8.GetBytes("Test");
            var memory = buffer.GetMemory(testData.Length);
            testData.CopyTo(memory);
            buffer.Advance(testData.Length);

            using var stream = buffer.AsStream();

            Assert.Throws<IOException>(() => stream.Seek(-10, SeekOrigin.Begin));
        }

        [Fact]
        public void AsStream_WriteToReadOnlyStream_ThrowsNotSupportedException()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();
            using var stream = buffer.AsStream();

            Assert.Throws<NotSupportedException>(() => stream.Write(new byte[1], 0, 1));
        }

        [Fact]
        public void AsStream_SetLengthOnReadOnlyStream_ThrowsNotSupportedException()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();
            using var stream = buffer.AsStream();

            Assert.Throws<NotSupportedException>(() => stream.SetLength(10));
        }

        [Fact]
        public void AsStream_DisposedBuffer_PropertiesReturnFalse()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();
            var stream = buffer.AsStream();

            buffer.Dispose();

            // Assert - After buffer disposal, stream properties should reflect this
            Assert.False(stream.CanRead);
            Assert.False(stream.CanSeek);
            Assert.False(stream.CanWrite);
        }

        [Fact]
        public void AsStream_ReadAfterBufferDispose_ThrowsObjectDisposedException()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();
            var testData = Encoding.UTF8.GetBytes("Test data");
            var memory = buffer.GetMemory(testData.Length);
            testData.CopyTo(memory);
            buffer.Advance(testData.Length);

            using var stream = buffer.AsStream();
            buffer.Dispose();

            var readBuffer = new byte[4];
            Assert.Throws<ObjectDisposedException>(() => stream.Read(readBuffer, 0, 4));
        }

        [Fact]
        public void Constructor_WithZeroMinimalIncrease_UsesSizeHint()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>(0);
            var memory = buffer.GetMemory(100);
            
            Assert.True(memory.Length >= 100);
        }

        [Fact]
        public void ToArray_WithSlicedBuffer_ReturnsOnlySlicedData()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();
            var testData = Encoding.UTF8.GetBytes("Hello, World! This is a test message.");
            
            var memory = buffer.GetMemory(testData.Length);
            testData.CopyTo(memory);
            buffer.Advance(testData.Length);
            
            // Slice to get only "World!"
            buffer.Slice(7, 6);

            var result = buffer.ToArray();
            var expected = Encoding.UTF8.GetBytes("World!");

            Assert.Equal(expected, result);
            Assert.Equal(6, buffer.Length);
        }

        [Fact]
        public void Clear_AfterSlice_ResetsToEmpty()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            using var buffer = new PooledSegmentedBuffer<byte>();
            var testData = Encoding.UTF8.GetBytes("Hello, World!");
            
            var memory = buffer.GetMemory(testData.Length);
            testData.CopyTo(memory);
            buffer.Advance(testData.Length);
            
            // Slice first
            buffer.Slice(0, 5);
            Assert.Equal(5, buffer.Length);
            
            // Then clear
            buffer.Clear();
            
            Assert.Equal(0, buffer.Length);
            Assert.True(buffer.GetReadOnlySequence().IsEmpty);
        }
    }
}
