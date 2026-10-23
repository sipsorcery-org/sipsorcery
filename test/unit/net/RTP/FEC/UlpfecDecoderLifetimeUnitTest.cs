//-----------------------------------------------------------------------------
// Filename: UlpfecDecoderLifetimeUnitTest.cs
//
// Description: Decoder lifetime, retention, wraparound and duplicate regressions.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class UlpfecDecoderLifetimeUnitTest
    {
        private const uint SyncSource = 0x87654321;

        private static RTPPacket Media(ushort sequence, uint syncSource = SyncSource)
        {
            return new RTPPacket(new ArraySegment<byte>(new byte[] { (byte)sequence, (byte)(sequence >> 8), 0x45 }), 0)
            {
                Header = new RTPHeader
                {
                    SyncSource = syncSource,
                    SequenceNumber = sequence,
                    Timestamp = 90000,
                    PayloadType = 96
                }
            };
        }

        private static RTPPacket Repair(ushort sequence, params RTPPacket[] media)
        {
            return new RtpUlpfecEncoder(122, 127, 96).CreateFecPacket(media, () => sequence);
        }

        [Fact]
        public void LosslessThousandMediaPacketsNeverBecomeRecoveredAfterEviction()
        {
            var decoder = new RtpUlpfecDecoder(122, 127);
            ushort sequence = 100;
            for (int groupIndex = 0; groupIndex < 200; groupIndex++)
            {
                var group = new List<RTPPacket>();
                for (int mediaIndex = 0; mediaIndex < 5; mediaIndex++)
                {
                    var packet = Media(sequence++);
                    group.Add(packet);
                    Assert.True(decoder.TryAddReceivedMedia(packet, out var recovered));
                    Assert.Empty(recovered);
                }
                decoder.AddReceivedFec(Repair(sequence++, group.ToArray()), out var fecRecovered);
                Assert.Empty(fecRecovered);
                Assert.Equal(0, decoder.PendingFecCount);
                Assert.InRange(decoder.MediaHistoryCount, 1, RtpUlpfecDecoder.MAX_MEDIA_HISTORY);
            }
        }

        [Fact]
        public void PendingRepairExpiresBeforeEvictedHistoryCanBeReinterpreted()
        {
            var decoder = new RtpUlpfecDecoder(122, 127);
            var oldRepair = Repair(3, Media(1), Media(2));
            decoder.AddReceivedFec(oldRepair, out var initialRecovered);
            Assert.Empty(initialRecovered);
            Assert.Equal(1, decoder.PendingFecCount);

            Assert.True(decoder.TryAddReceivedMedia(Media(130), out var recovered));
            Assert.Empty(recovered);
            Assert.Equal(0, decoder.PendingFecCount);
            Assert.False(decoder.TryAddReceivedMedia(Media(2), out recovered));
            Assert.Empty(recovered);

            Assert.False(decoder.TryAddReceivedFec(Repair(131, Media(2), Media(3)), out recovered));
            Assert.Empty(recovered);
            Assert.Equal(0, decoder.PendingFecCount);
            Assert.Equal(1, decoder.MediaHistoryCount);
        }

        [Theory]
        [InlineData(65534)]
        [InlineData(65535)]
        [InlineData(0)]
        [InlineData(1)]
        public void StatefulRecoveryAndDuplicateSuppressionCrossSequenceWrap(int lostSequence)
        {
            var decoder = new RtpUlpfecDecoder(122, 127);
            var media = new[] { Media(65534), Media(65535), Media(0), Media(1) };
            // Repair arrives first, so pre-wrap originals must extend into the previous cycle.
            decoder.AddReceivedFec(Repair(2, media), out var recovered);
            Assert.Empty(recovered);
            var allRecovered = new List<RTPPacket>();
            foreach (var packet in media.Reverse())
            {
                if (packet.Header.SequenceNumber != lostSequence)
                {
                    Assert.True(decoder.TryAddReceivedMedia(packet, out recovered));
                    allRecovered.AddRange(recovered);
                }
            }

            var expected = media.Single(x => x.Header.SequenceNumber == lostSequence);
            var actual = Assert.Single(allRecovered);
            Assert.Equal(expected.Header.SequenceNumber, actual.Header.SequenceNumber);
            Assert.Equal(expected.GetPayloadBytes(), actual.GetPayloadBytes());
            Assert.Equal(0, decoder.PendingFecCount);
            Assert.False(decoder.TryAddReceivedMedia(expected, out recovered));
            Assert.Empty(recovered);
        }

        [Fact]
        public void DuplicateRepairAndLateOriginalDoNotProduceSecondDelivery()
        {
            var decoder = new RtpUlpfecDecoder(122, 127);
            var first = Media(10);
            var second = Media(11);
            var repair = Repair(12, first, second);
            Assert.True(decoder.TryAddReceivedFec(repair, out var recovered));
            Assert.False(decoder.TryAddReceivedFec(repair, out recovered));
            Assert.Empty(recovered);
            Assert.Equal(1, decoder.PendingFecCount);

            Assert.True(decoder.TryAddReceivedMedia(second, out recovered));
            Assert.Equal(first.GetPayloadBytes(), Assert.Single(recovered).GetPayloadBytes());
            Assert.False(decoder.TryAddReceivedMedia(first, out recovered));
            Assert.Empty(recovered);
            Assert.False(decoder.TryAddReceivedMedia(second, out recovered));
            Assert.Empty(recovered);
            Assert.False(decoder.TryAddReceivedFec(repair, out recovered));
            Assert.Empty(recovered);
            Assert.Equal(0, decoder.PendingFecCount);
            Assert.Equal(2, decoder.MediaHistoryCount);
        }

        [Fact]
        public void CompletedRepairIsAdmittedOnceAndNeverQueued()
        {
            var decoder = new RtpUlpfecDecoder(122, 127);
            var first = Media(10);
            var second = Media(11);
            decoder.AddReceivedMedia(first, out _);
            decoder.AddReceivedMedia(second, out _);
            var repair = Repair(12, first, second);
            Assert.True(decoder.TryAddReceivedFec(repair, out var recovered));
            Assert.Empty(recovered);
            Assert.False(decoder.TryAddReceivedFec(repair, out recovered));
            Assert.Empty(recovered);
            Assert.Equal(0, decoder.PendingFecCount);
        }

        [Fact]
        public void OverlappingPendingGroupsRecoverIterativelyWithoutDuplicateHistory()
        {
            var decoder = new RtpUlpfecDecoder(122, 127);
            var first = Media(1);
            var second = Media(2);
            var third = Media(3);
            decoder.AddReceivedFec(Repair(4, first, second), out var recovered);
            Assert.Empty(recovered);
            decoder.AddReceivedFec(Repair(5, second, third), out recovered);
            Assert.Empty(recovered);

            Assert.True(decoder.TryAddReceivedMedia(third, out recovered));
            Assert.Equal(new ushort[] { 2, 1 }, recovered.Select(x => x.Header.SequenceNumber).ToArray());
            Assert.Equal(second.GetPayloadBytes(), recovered[0].GetPayloadBytes());
            Assert.Equal(first.GetPayloadBytes(), recovered[1].GetPayloadBytes());
            Assert.Equal(0, decoder.PendingFecCount);
            Assert.Equal(3, decoder.MediaHistoryCount);
        }

        [Fact]
        public void PendingRepairAndMediaHistoryRemainBoundedOnEveryAdmissionPath()
        {
            var decoder = new RtpUlpfecDecoder(122, 127);
            for (ushort sequence = 3; sequence < 43; sequence++)
            {
                decoder.AddReceivedFec(Repair(sequence, Media(1), Media(2)), out var recovered);
                Assert.Empty(recovered);
                Assert.InRange(decoder.PendingFecCount, 1, RtpUlpfecDecoder.MAX_PENDING_FEC);
            }
            Assert.Equal(RtpUlpfecDecoder.MAX_PENDING_FEC, decoder.PendingFecCount);
            Assert.True(decoder.TryAddReceivedMedia(Media(2), out var finalRecovered));
            Assert.Equal((ushort)1, Assert.Single(finalRecovered).Header.SequenceNumber);
            Assert.Equal(0, decoder.PendingFecCount);

            for (ushort sequence = 43; sequence < 300; sequence++)
            {
                Assert.True(decoder.TryAddReceivedMedia(Media(sequence), out var recovered));
                Assert.Empty(recovered);
                Assert.InRange(decoder.MediaHistoryCount, 1, RtpUlpfecDecoder.MAX_MEDIA_HISTORY);
            }
            Assert.Equal(RtpUlpfecDecoder.MAX_MEDIA_HISTORY, decoder.MediaHistoryCount);
            Assert.False(decoder.TryAddReceivedMedia(Media(1), out finalRecovered));
            Assert.Empty(finalRecovered);
        }

        [Fact]
        public void DistinctRepairBlocksSharingOuterSequenceAreNotDeduplicatedTogether()
        {
            var decoder = new RtpUlpfecDecoder(122, 127);
            decoder.AddReceivedFec(Repair(10, Media(1), Media(2)), out var recovered);
            Assert.Empty(recovered);
            decoder.AddReceivedFec(Repair(10, Media(3), Media(4)), out recovered);
            Assert.Empty(recovered);
            Assert.Equal(2, decoder.PendingFecCount);

            Assert.True(decoder.TryAddReceivedMedia(Media(2), out recovered));
            Assert.Equal((ushort)1, Assert.Single(recovered).Header.SequenceNumber);
            Assert.Equal(1, decoder.PendingFecCount);
            Assert.True(decoder.TryAddReceivedMedia(Media(4), out recovered));
            Assert.Equal((ushort)3, Assert.Single(recovered).Header.SequenceNumber);
            Assert.Equal(0, decoder.PendingFecCount);
        }

        [Fact]
        public void RedPiggybackRepairCanProtectItsAlreadyRegisteredPrimarySequence()
        {
            var decoder = new RtpUlpfecDecoder(122, 127);
            var first = Media(1);
            var second = Media(2);
            var primary = Media(3);
            Assert.True(decoder.TryAddReceivedMedia(primary, out var recovered));
            Assert.Empty(recovered);

            decoder.AddReceivedFec(Repair(3, first, second, primary), out recovered);
            Assert.Empty(recovered);
            Assert.Equal(1, decoder.PendingFecCount);
            Assert.True(decoder.TryAddReceivedMedia(second, out recovered));
            Assert.Equal(first.GetPayloadBytes(), Assert.Single(recovered).GetPayloadBytes());
            Assert.Equal(0, decoder.PendingFecCount);
        }

        [Fact]
        public void RepairOnlyPacketCannotInventItsOwnOrFutureMediaSequence()
        {
            var decoder = new RtpUlpfecDecoder(122, 127);
            decoder.AddReceivedFec(Repair(3, Media(3)), out var recovered);
            Assert.Empty(recovered);
            decoder.AddReceivedFec(Repair(3, Media(4)), out recovered);
            Assert.Empty(recovered);
            Assert.Equal(0, decoder.MediaHistoryCount);
            Assert.Equal(0, decoder.PendingFecCount);
            Assert.True(decoder.TryAddReceivedMedia(Media(1), out recovered));
            Assert.Empty(recovered);
        }

        [Theory]
        [InlineData("truncated")]
        [InlineData("empty-mask")]
        [InlineData("extension-bit")]
        [InlineData("invalid-recovered-header")]
        public void MalformedRepairsDoNotEnterPendingHistoryOrChangeSource(string corruption)
        {
            var decoder = new RtpUlpfecDecoder(122, 127);
            var repair = Repair(2, Media(1, 0x11111111));
            var payload = repair.GetPayloadBytes();
            if (corruption == "truncated")
            {
                repair.Payload = new byte[] { 127, 0 };
            }
            else if (corruption == "empty-mask")
            {
                payload[13] = 0;
                payload[14] = 0;
            }
            else if (corruption == "extension-bit")
            {
                payload[1] |= 0x80;
            }
            else
            {
                payload[1] |= 0x10;
            }

            Assert.False(decoder.TryAddReceivedFec(repair, out var recovered));
            Assert.Empty(recovered);
            Assert.Equal(0, decoder.PendingFecCount);
            Assert.Equal(0, decoder.MediaHistoryCount);
            Assert.True(decoder.TryAddReceivedMedia(Media(1), out recovered));
            Assert.Empty(recovered);
        }

        [Fact]
        public void ResetClearsSourceWindowAndPendingRepairIdentity()
        {
            var decoder = new RtpUlpfecDecoder(122, 127);
            decoder.AddReceivedFec(Repair(12, Media(10), Media(11)), out var recovered);
            Assert.Empty(recovered);
            Assert.False(decoder.TryAddReceivedMedia(Media(10, 0x11111111), out recovered));
            Assert.Empty(recovered);
            Assert.Equal(1, decoder.PendingFecCount);

            decoder.Reset();
            Assert.Equal(0, decoder.PendingFecCount);
            Assert.Equal(0, decoder.MediaHistoryCount);
            Assert.True(decoder.TryAddReceivedMedia(Media(65535, 0x11111111), out recovered));
            Assert.Empty(recovered);
            Assert.True(decoder.TryAddReceivedMedia(Media(0, 0x11111111), out recovered));
            Assert.Empty(recovered);
            Assert.Equal(2, decoder.MediaHistoryCount);
        }
    }
}
