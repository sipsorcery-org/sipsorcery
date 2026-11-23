using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class UlpfecTrackNegotiationUnitTest
    {
        private const RtpRedundancyOptions VideoFec = RtpRedundancyOptions.Red | RtpRedundancyOptions.Ulpfec;

        [Fact]
        public void DefaultTrackAndTuningAloneDoNotAdvertiseRedundancy()
        {
            var stream = new VideoStream(new RtpSessionConfig(), 0)
            {
                LocalTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false,
                    new List<SDPAudioVideoMediaFormat> { Format(SDPMediaTypesEnum.video, 96, "VP8") })
            };
            stream.LocalTrack.FecConfig = new RtpUlpfecConfig { TargetOverheadFraction = 0.75f };
            var allocator = new RtpFecPayloadTypes();

            allocator.PrepareOffer(new MediaStream[] { stream }, false);

            Assert.Equal(RtpRedundancyOptions.None, stream.LocalTrack.Redundancy);
            Assert.Equal(96, Assert.Single(stream.LocalTrack.Capabilities).ID);
            Assert.DoesNotContain(stream.LocalTrack.Capabilities, format => format.IsRed || format.IsUlpfec);
        }

        [Theory]
        [InlineData(RtpRedundancyOptions.Red)]
        [InlineData(RtpRedundancyOptions.Ulpfec)]
        public void VideoRequiresTheCompleteRedAndUlpfecFlagPair(RtpRedundancyOptions options)
        {
            var track = Track("VP8", 96, false);

            Assert.Throws<ArgumentException>(() => track.Redundancy = options);

            Assert.Equal(RtpRedundancyOptions.None, track.Redundancy);
            Assert.Equal(96, Assert.Single(track.Capabilities).ID);
        }

        [Fact]
        public void UnknownRedundancyFlagsAreRejectedWithoutChangingTrack()
        {
            var track = Track("VP8", 96, false);
            Assert.Throws<ArgumentOutOfRangeException>(() => track.Redundancy = (RtpRedundancyOptions)4);
            Assert.Equal(RtpRedundancyOptions.None, track.Redundancy);
        }

        [Theory]
        [InlineData(SDPMediaTypesEnum.audio, false)]
        [InlineData(SDPMediaTypesEnum.text, false)]
        [InlineData(SDPMediaTypesEnum.video, true)]
        public void RedundancyIsRestrictedToLocalVideoTracks(SDPMediaTypesEnum kind, bool remote)
        {
            var track = new MediaStreamTrack(kind, remote, new List<SDPAudioVideoMediaFormat>
            {
                Format(kind, 96, kind == SDPMediaTypesEnum.video ? "VP8" : kind == SDPMediaTypesEnum.audio ? "opus" : "t140")
            });

            Assert.Throws<InvalidOperationException>(() => track.Redundancy = VideoFec);

            track.Redundancy = RtpRedundancyOptions.None;
            Assert.Equal(RtpRedundancyOptions.None, track.Redundancy);
        }

        [Fact]
        public void ChangingIsRemoteAfterOptInIsRevalidatedAtOfferAndNegotiation()
        {
            var stream = Video(0, 96);
            var before = Snapshot(stream);
            stream.LocalTrack.IsRemote = true;
            var allocator = new RtpFecPayloadTypes();

            Assert.Throws<InvalidOperationException>(() => allocator.PrepareOffer(new MediaStream[] { stream }, false));
            Assert.Throws<InvalidOperationException>(() => allocator.GetNegotiationCapabilities(stream, PeerFormats(), true));

            Assert.Equal(before, Snapshot(stream));
        }

        [Fact]
        public void FirstOfferAllocatesTheLowestFreeDynamicPayloadTypes()
        {
            var stream = Video(0, 96);
            new RtpFecPayloadTypes().PrepareOffer(new MediaStream[] { stream }, false);

            AssertPair(stream, 97, 98);
            Assert.Equal(new[] { 96, 97, 98 }, stream.LocalTrack.Capabilities.Select(format => format.ID));
        }

        [Fact]
        public void FormatsAddedAfterTrackCreationReserveTheirIdsBeforeFirstOffer()
        {
            var stream = Video(0, 96);
            stream.LocalTrack.Capabilities.Add(Format(SDPMediaTypesEnum.video, 97, "VP9"));
            stream.LocalTrack.Capabilities.Add(Format(SDPMediaTypesEnum.video, 122, "H264"));
            stream.LocalTrack.Capabilities.Add(Format(SDPMediaTypesEnum.video, 127, "rtx", "apt=96"));

            new RtpFecPayloadTypes().PrepareOffer(new MediaStream[] { stream }, false);

            AssertPair(stream, 98, 99);
            Assert.Equal("H264", stream.LocalTrack.Capabilities.Single(format => format.ID == 122).Name());
            Assert.Equal("rtx", stream.LocalTrack.Capabilities.Single(format => format.ID == 127).Name());
        }

        [Fact]
        public void AutomaticallyAllocatedVideoRed101IsNotConsumedAsAnAudioRtpEvent()
        {
            var stream = Video(0, 96, 97, 98, 99, 100);
            var clock = new FecTestClock();
            var frames = new List<byte[]>();
            var packets = new List<RTPPacket>();
            var mediaTypes = new List<SDPMediaTypesEnum>();
            int rtpEvents = 0;
            new RtpFecPayloadTypes().PrepareOffer(new MediaStream[] { stream }, false);
            AssertPair(stream, 101, 102);
            stream.RemoteTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, true,
                new List<SDPAudioVideoMediaFormat>(stream.LocalTrack.Capabilities));
            stream.RtcpSession = new RTCPSession(SDPMediaTypesEnum.video, stream.LocalTrack.Ssrc);
            stream.SetFecReceiveTiming(() => clock.Now, clock.CreateTimer);
            stream.EnableUlpfec(101, 102, 96);
            // The public RTP-event field must never reserve an otherwise valid video PT.
            stream.NegotiatedRtpEventPayloadID = RTPSession.DEFAULT_DTMF_EVENT_PAYLOAD_ID;
            stream.OnRtpEventByIndex += (index, endpoint, rtpEvent, header) => rtpEvents++;
            stream.OnVideoFrameReceivedByIndex += (index, endpoint, timestamp, frame, format) => frames.Add(frame);
            stream.OnRtpPacketReceivedByIndex += (index, endpoint, mediaType, packet) =>
            {
                mediaTypes.Add(mediaType);
                packets.Add(packet);
            };
            var red = RtpRedPayload.EncodeSingleBlock(new byte[] { 0x10, 0xA1, 0xB2 }, 96);
            var wire = new RTPPacket(new ArraySegment<byte>(red), 0);
            wire.Header.PayloadType = 101;
            wire.Header.SequenceNumber = 500;
            wire.Header.Timestamp = 3000;
            wire.Header.SyncSource = 0x1234;
            wire.Header.MarkerBit = 1;
            var remote = new IPEndPoint(IPAddress.Loopback, 5004);

            try
            {
                var bytes = wire.GetBytes();
                stream.OnReceiveRTPPacket(new RTPHeader(bytes), remote.Port, remote, bytes);

                Assert.Equal(new byte[] { 0xA1, 0xB2 }, Assert.Single(frames));
                Assert.Equal(SDPMediaTypesEnum.video, Assert.Single(mediaTypes));
                var delivered = Assert.Single(packets);
                Assert.Equal(96, delivered.Header.PayloadType);
                Assert.Equal(500, delivered.Header.SequenceNumber);
                Assert.Equal(new byte[] { 0x10, 0xA1, 0xB2 }, delivered.GetPayloadBytes());
                Assert.Equal(0, rtpEvents);
                Assert.Equal(1u, stream.RtcpSession.PacketsReceivedCount);
                Assert.Equal(wire.GetPayloadLength(), stream.RtcpSession.OctetsReceivedCount);
                Assert.Equal(0, clock.PendingCount);
            }
            finally
            {
                stream.IsClosed = true;
            }
        }

        [Fact]
        public void BundledVideoTracksShareOnePairWithoutUsingAudioTextOrRtxIds()
        {
            var audio = new AudioStream(new RtpSessionConfig(), 0)
            {
                LocalTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false,
                    new List<SDPAudioVideoMediaFormat> { Format(SDPMediaTypesEnum.audio, 96, "opus") })
            };
            var text = new TextStream(new RtpSessionConfig(), 0)
            {
                LocalTrack = new MediaStreamTrack(SDPMediaTypesEnum.text, false,
                    new List<SDPAudioVideoMediaFormat> { Format(SDPMediaTypesEnum.text, 97, "t140") })
            };
            var first = Video(0, 98);
            first.LocalTrack.Capabilities.Add(Format(SDPMediaTypesEnum.video, 99, "rtx", "apt=98"));
            var second = Video(1, 100);
            var audioBefore = Snapshot(audio);
            var textBefore = Snapshot(text);

            new RtpFecPayloadTypes().PrepareOffer(new MediaStream[] { audio, text, first, second }, true);

            AssertPair(first, 102, 103);
            AssertPair(second, 102, 103);
            Assert.Equal(audioBefore, Snapshot(audio));
            Assert.Equal(textBefore, Snapshot(text));
            Assert.Contains(audio.LocalTrack.Capabilities, format => format.ID == 101 && format.Name() == "telephone-event");
            Assert.Equal("rtx", first.LocalTrack.Capabilities.Single(format => format.ID == 99).Name());
        }

        [Fact]
        public void OptedOutBundledVideoStillReservesItsCodecPayloadTypes()
        {
            var enabled = Video(0, 96);
            var disabled = Video(1, 97);
            disabled.LocalTrack.Redundancy = RtpRedundancyOptions.None;

            new RtpFecPayloadTypes().PrepareOffer(new MediaStream[] { enabled, disabled }, true);

            AssertPair(enabled, 98, 99);
            Assert.Equal(97, Assert.Single(disabled.LocalTrack.Capabilities).ID);
        }

        [Theory]
        [InlineData("VP8", true)]
        [InlineData("H264", false)]
        public void BundledTracksReuseMatchingBindingsAndRejectDifferentCodecsAtTheSameId(string secondCodec, bool compatible)
        {
            var first = Video(0, 96);
            var second = Video(1, 96);
            second.LocalTrack.Capabilities[0] = Format(SDPMediaTypesEnum.video, 96, secondCodec);
            var firstBefore = Snapshot(first);
            var secondBefore = Snapshot(second);
            var allocator = new RtpFecPayloadTypes();

            if (compatible)
            {
                allocator.PrepareOffer(new MediaStream[] { first, second }, true);
                AssertPair(first, 97, 98);
                AssertPair(second, 97, 98);
            }
            else
            {
                var error = Assert.Throws<InvalidOperationException>(() => allocator.PrepareOffer(new MediaStream[] { first, second }, true));
                Assert.Contains("96", error.Message);
                Assert.Equal(firstBefore, Snapshot(first));
                Assert.Equal(secondBefore, Snapshot(second));
            }
        }

        [Fact]
        public void UnbundledVideoTracksAllocateInSeparateNamespaces()
        {
            var first = Video(0, 96);
            var second = Video(1, 97);

            new RtpFecPayloadTypes().PrepareOffer(new MediaStream[] { first, second }, false);

            AssertPair(first, 97, 98);
            AssertPair(second, 96, 98);
        }

        [Fact]
        public void ExactlyTwoFreeDynamicIdsAreEnoughEvenWhenNonAdjacent()
        {
            var stream = Video(0, Enumerable.Range(96, 32).Where(id => id != 101 && id != 117).ToArray());
            new RtpFecPayloadTypes().PrepareOffer(new MediaStream[] { stream }, false);

            AssertPair(stream, 101, 117);
            Assert.Equal(32, stream.LocalTrack.Capabilities.Count);
            Assert.Equal(32, stream.LocalTrack.Capabilities.Select(format => format.ID).Distinct().Count());
        }

        [Theory]
        [InlineData(31)]
        [InlineData(32)]
        public void PayloadExhaustionDoesNotPartiallyModifyAnyTrack(int occupiedCount)
        {
            var first = Video(0, 96);
            var exhausted = Video(1, Enumerable.Range(96, occupiedCount).ToArray());
            var firstBefore = Snapshot(first);
            var exhaustedBefore = Snapshot(exhausted);
            var allocator = new RtpFecPayloadTypes();

            var error = Assert.Throws<InvalidOperationException>(() => allocator.PrepareOffer(new MediaStream[] { first, exhausted }, false));

            Assert.Contains("96-127", error.Message);
            Assert.Equal(firstBefore, Snapshot(first));
            Assert.Equal(exhaustedBefore, Snapshot(exhausted));
            exhausted.LocalTrack.Capabilities.RemoveAll(format => format.ID >= 126);
            allocator.PrepareOffer(new MediaStream[] { first, exhausted }, false);
            AssertPair(first, 97, 98);
            AssertPair(exhausted, 126, 127);
        }

        [Fact]
        public void RepeatedOffersKeepEstablishedIdsAndDoNotDuplicateAuxiliaryFormats()
        {
            var stream = Video(0, 96);
            var allocator = new RtpFecPayloadTypes();
            allocator.PrepareOffer(new MediaStream[] { stream }, false);
            var first = Snapshot(stream);

            allocator.PrepareOffer(new MediaStream[] { stream }, false);
            allocator.PrepareOffer(new MediaStream[] { stream }, false);

            Assert.Equal(first, Snapshot(stream));
            AssertPair(stream, 97, 98);
            Assert.Equal(3, stream.LocalTrack.Capabilities.Count);
        }

        [Fact]
        public void CodecCollidingWithAnEstablishedOfferBindingFailsWithoutRenumbering()
        {
            var stream = Video(0, 96);
            var allocator = new RtpFecPayloadTypes();
            allocator.PrepareOffer(new MediaStream[] { stream }, false);
            stream.LocalTrack.Capabilities.Add(Format(SDPMediaTypesEnum.video, 97, "H264"));
            var before = Snapshot(stream);

            var error = Assert.Throws<InvalidOperationException>(() => allocator.PrepareOffer(new MediaStream[] { stream }, false));

            Assert.Contains("97", error.Message);
            Assert.Equal(before, Snapshot(stream));
            Assert.Contains(stream.LocalTrack.Capabilities, format => format.ID == 97 && format.IsH264);
        }

        [Fact]
        public void NegotiatedRemoteBindingsAreReusedAndRemainReserved()
        {
            var stream = Video(0, 96);
            var allocator = new RtpFecPayloadTypes();
            allocator.RecordNegotiatedFormats(stream, PeerFormats(), false);
            allocator.PrepareOffer(new MediaStream[] { stream }, false);
            AssertPair(stream, 114, 116);

            stream.LocalTrack.Capabilities.Add(Format(SDPMediaTypesEnum.video, 114, "VP9"));
            var before = Snapshot(stream);
            Assert.Throws<InvalidOperationException>(() => allocator.PrepareOffer(new MediaStream[] { stream }, false));
            Assert.Equal(before, Snapshot(stream));
        }

        [Fact]
        public void RemoteCodecCannotReuseAnEarlierOfferedFecBinding()
        {
            var stream = Video(0, 96);
            var allocator = new RtpFecPayloadTypes();
            allocator.PrepareOffer(new MediaStream[] { stream }, false);
            var remote = new List<SDPAudioVideoMediaFormat>
            {
                Format(SDPMediaTypesEnum.video, 97, "VP8"),
                SDPAudioVideoMediaFormat.CreateRedFormat(114),
                SDPAudioVideoMediaFormat.CreateUlpfecFormat(116)
            };
            var before = Snapshot(stream);

            var error = Assert.Throws<InvalidOperationException>(() => allocator.RecordNegotiatedFormats(stream, remote, false));

            Assert.Contains("97", error.Message);
            Assert.Equal(before, Snapshot(stream));
            AssertPair(stream, 97, 98);
        }

        [Fact]
        public void BundledRemoteFecBindingCannotCollideWithAudioPayloadType()
        {
            var stream = Video(0, 96);
            var allocator = new RtpFecPayloadTypes();
            var video = PeerFormats();
            allocator.SetNegotiationScope(new[]
            {
                new SDPMediaAnnouncement(SDPMediaTypesEnum.audio, 9000,
                    new List<SDPAudioVideoMediaFormat> { Format(SDPMediaTypesEnum.audio, 114, "opus") }),
                new SDPMediaAnnouncement(SDPMediaTypesEnum.video, 9002, video)
            }, true);
            var before = Snapshot(stream);

            var error = Assert.Throws<InvalidOperationException>(() => allocator.RecordNegotiatedFormats(stream, video, true));

            Assert.Contains("114", error.Message);
            Assert.Equal(before, Snapshot(stream));
        }

        [Fact]
        public void RejectedBundledAudioDoesNotReserveItsPayloadTypes()
        {
            var stream = Video(0, 96);
            var allocator = new RtpFecPayloadTypes();
            var negotiated = new List<SDPAudioVideoMediaFormat>
            {
                Format(SDPMediaTypesEnum.video, 96, "VP8"),
                SDPAudioVideoMediaFormat.CreateRedFormat(97),
                SDPAudioVideoMediaFormat.CreateUlpfecFormat(98)
            };
            allocator.SetNegotiationScope(new[]
            {
                new SDPMediaAnnouncement(SDPMediaTypesEnum.audio, 0,
                    new List<SDPAudioVideoMediaFormat> { Format(SDPMediaTypesEnum.audio, 97, "opus") }),
                new SDPMediaAnnouncement(SDPMediaTypesEnum.video, 9002, negotiated)
            }, true);

            allocator.RecordNegotiatedFormats(stream, negotiated, true);
            allocator.PrepareOffer(new MediaStream[] { stream }, true);

            AssertPair(stream, 97, 98);
            Assert.Equal(96, Assert.Single(stream.LocalTrack.Capabilities, format => format.IsVideoMediaCodec).ID);
        }

        [Fact]
        public void OptingOutDoesNotReleaseEstablishedBundleBindingsForAnotherCodec()
        {
            var stream = Video(0, 96);
            var allocator = new RtpFecPayloadTypes();
            allocator.PrepareOffer(new MediaStream[] { stream }, true);
            stream.LocalTrack.Redundancy = RtpRedundancyOptions.None;
            allocator.PrepareOffer(new MediaStream[] { stream }, true);
            Assert.Equal(96, Assert.Single(stream.LocalTrack.Capabilities).ID);
            var other = Video(1, 97);

            var error = Assert.Throws<InvalidOperationException>(() => allocator.PrepareOffer(new MediaStream[] { stream, other }, true));

            Assert.Contains("97", error.Message);
            Assert.Equal(96, Assert.Single(stream.LocalTrack.Capabilities).ID);
            Assert.Equal(97, Assert.Single(other.LocalTrack.Capabilities).ID);
        }

        [Fact]
        public void AnswerMatchingUsesRemoteIdsWithoutAllocatingOrChangingStoredCapabilities()
        {
            var stream = Video(0, 96);
            var allocator = new RtpFecPayloadTypes();
            var before = Snapshot(stream);

            var matching = allocator.GetNegotiationCapabilities(stream, PeerFormats(), true);
            var compatible = SDPAudioVideoMediaFormat.GetCompatibleFormats(PeerFormats(), matching);

            Assert.Equal(114, Assert.Single(compatible, format => format.IsRed).ID);
            Assert.Equal(116, Assert.Single(compatible, format => format.IsUlpfec).ID);
            Assert.Equal(102, Assert.Single(compatible, format => format.IsVideoMediaCodec).ID);
            Assert.Equal(before, Snapshot(stream));
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public void IncompletePeerPairFallsBackWithoutClearingTrackIntent(bool red, bool ulpfec)
        {
            using var session = new RTPSession(new RtpSessionConfig());
            var track = Track();
            session.addTrack(track);

            Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.offer, Offer(PeerFormats(red, ulpfec))));

            Assert.Equal(VideoFec, track.Redundancy);
            Assert.Null(session.VideoStream.FecEncoder);
            Assert.Null(session.VideoStream.FecDecoder);
            Assert.DoesNotContain(session.CreateAnswer(IPAddress.Loopback).Media.Single().MediaFormats.Values,
                format => format.IsRed || format.IsUlpfec);
            var reoffer = session.CreateOffer(IPAddress.Loopback);
            Assert.Contains(reoffer.Media.Single().MediaFormats.Values, format => format.IsRed);
            Assert.Contains(reoffer.Media.Single().MediaFormats.Values, format => format.IsUlpfec);
        }

        [Fact]
        public void PeerCanEnableFecOnALaterOfferAfterAnEarlierPlainVideoFallback()
        {
            using var session = new RTPSession(new RtpSessionConfig());
            var track = Track();
            session.addTrack(track);
            Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.offer, Offer(PeerFormats(false, false))));
            Assert.Null(session.VideoStream.FecEncoder);

            Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.offer, Offer(PeerFormats())));

            Assert.Equal(VideoFec, track.Redundancy);
            Assert.Equal(114, session.VideoStream.FecEncoder.RedPayloadType);
            Assert.Equal(116, session.VideoStream.FecEncoder.UlpfecPayloadType);
            Assert.Equal(102, session.VideoStream.FecEncoder.MediaPayloadType);
        }

        [Fact]
        public void RemoteAnswerCannotIntroduceACompletePairThatWasNotOffered()
        {
            var stream = Video(0, 96);
            var allocator = new RtpFecPayloadTypes();
            allocator.PrepareOffer(new MediaStream[] { stream }, false);

            var matching = allocator.GetNegotiationCapabilities(stream, PeerFormats(), false);

            Assert.DoesNotContain(matching, format => format.IsRed || format.IsUlpfec);
            AssertPair(stream, 97, 98);
        }

        [Fact]
        public void RemoteAnswerCanAcceptTheExactPairFromTheLocalOffer()
        {
            var stream = Video(0, 96);
            var allocator = new RtpFecPayloadTypes();
            allocator.PrepareOffer(new MediaStream[] { stream }, false);
            var answer = new List<SDPAudioVideoMediaFormat>(stream.LocalTrack.Capabilities);

            var matching = allocator.GetNegotiationCapabilities(stream, answer, false);

            Assert.Equal(97, Assert.Single(matching, format => format.IsRed).ID);
            Assert.Equal(98, Assert.Single(matching, format => format.IsUlpfec).ID);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ActualAnswerKeepsOnlyTheOfferedPairDespiteEarlierAuxiliaryAliases(bool surroundingWhitespace)
        {
            using var session = new RTPSession(new RtpSessionConfig());
            session.addTrack(Track());
            var offered = session.CreateOffer(IPAddress.Loopback).Media.Single().MediaFormats.Values;
            int red = Assert.Single(offered, format => format.IsRed).ID;
            int ulpfec = Assert.Single(offered, format => format.IsUlpfec).ID;
            string padding = surroundingWhitespace ? " " : string.Empty;
            var answer = Offer(new List<SDPAudioVideoMediaFormat>
            {
                SDPAudioVideoMediaFormat.CreateRedFormat(114),
                SDPAudioVideoMediaFormat.CreateUlpfecFormat(116),
                Format(SDPMediaTypesEnum.video, 96, "VP8"),
                new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, red, padding + "red/90000" + padding, padding),
                new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, ulpfec, padding + "ulpfec/90000" + padding, padding)
            });

            Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.answer, answer));

            Assert.Equal(red, session.VideoStream.FecEncoder.RedPayloadType);
            Assert.Equal(ulpfec, session.VideoStream.FecEncoder.UlpfecPayloadType);
            Assert.Equal(96, session.VideoStream.FecEncoder.MediaPayloadType);
            AssertPair(session.VideoStream, red, ulpfec);
            Assert.DoesNotContain(session.VideoStream.LocalTrack.Capabilities, format => format.ID == 114 || format.ID == 116);
            Assert.DoesNotContain(session.VideoStream.RemoteTrack.Capabilities, format => format.ID == 114 || format.ID == 116);
        }

        [Fact]
        public void PerTrackOptInAndTuningSnapshotsAreIndependentAcrossRenegotiation()
        {
            using var session = new RTPSession(new RtpSessionConfig());
            var first = Track();
            first.FecConfig = new RtpUlpfecConfig { TargetOverheadFraction = 0.1f, MaxReceiveRepairWait = TimeSpan.FromMilliseconds(7) };
            var disabled = Track(enabled: false);
            disabled.FecConfig = new RtpUlpfecConfig { TargetOverheadFraction = 0.9f };
            var third = Track();
            third.FecConfig = new RtpUlpfecConfig { TargetOverheadFraction = 0.5f, MaxReceiveRepairWait = TimeSpan.FromMilliseconds(47) };
            session.addTrack(first);
            session.addTrack(disabled);
            session.addTrack(third);

            Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.offer, Offer(PeerFormats(), PeerFormats(), PeerFormats())));
            Assert.Equal(3, session.VideoStreamList.Count);
            Assert.Equal(0.1f, session.VideoStreamList[0].FecEncoder.Config.TargetOverheadFraction);
            Assert.Equal(TimeSpan.FromMilliseconds(7), session.VideoStreamList[0].FecConfig.MaxReceiveRepairWait);
            Assert.Null(session.VideoStreamList[1].FecEncoder);
            Assert.Null(session.VideoStreamList[1].FecDecoder);
            Assert.Equal(0.5f, session.VideoStreamList[2].FecEncoder.Config.TargetOverheadFraction);
            Assert.Equal(TimeSpan.FromMilliseconds(47), session.VideoStreamList[2].FecConfig.MaxReceiveRepairWait);
            Assert.NotSame(first.FecConfig, session.VideoStreamList[0].FecConfig);

            first.FecConfig.TargetOverheadFraction = 0.3f;
            first.FecConfig.MaxReceiveRepairWait = TimeSpan.FromMilliseconds(17);
            Assert.Equal(0.1f, session.VideoStreamList[0].FecEncoder.Config.TargetOverheadFraction);
            Assert.Equal(TimeSpan.FromMilliseconds(7), session.VideoStreamList[0].FecConfig.MaxReceiveRepairWait);
            Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.offer, Offer(PeerFormats(), PeerFormats(), PeerFormats())));
            Assert.Equal(0.3f, session.VideoStreamList[0].FecEncoder.Config.TargetOverheadFraction);
            Assert.Equal(TimeSpan.FromMilliseconds(17), session.VideoStreamList[0].FecConfig.MaxReceiveRepairWait);
            Assert.Equal(0.5f, session.VideoStreamList[2].FecEncoder.Config.TargetOverheadFraction);
            Assert.Null(session.VideoStreamList[1].FecEncoder);
        }

        [Fact]
        public void TrackOptOutAppliesAtRenegotiationWithoutDisablingAnotherTrack()
        {
            using var session = new RTPSession(new RtpSessionConfig());
            var first = Track();
            var second = Track();
            session.addTrack(first);
            session.addTrack(second);
            Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.offer, Offer(PeerFormats(), PeerFormats())));
            Assert.NotNull(session.VideoStreamList[0].FecEncoder);
            Assert.NotNull(session.VideoStreamList[1].FecEncoder);

            first.Redundancy = RtpRedundancyOptions.None;
            Assert.NotNull(session.VideoStreamList[0].FecEncoder);
            Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.offer, Offer(PeerFormats(), PeerFormats())));

            Assert.Null(session.VideoStreamList[0].FecEncoder);
            Assert.Null(session.VideoStreamList[0].FecDecoder);
            Assert.Equal(114, session.VideoStreamList[1].FecEncoder.RedPayloadType);
            Assert.Equal(116, session.VideoStreamList[1].FecEncoder.UlpfecPayloadType);
            first.Redundancy = VideoFec;
            Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.offer, Offer(PeerFormats(), PeerFormats())));
            Assert.Equal(114, session.VideoStreamList[0].FecEncoder.RedPayloadType);
            Assert.Equal(116, session.VideoStreamList[0].FecEncoder.UlpfecPayloadType);
            Assert.NotNull(session.VideoStreamList[1].FecEncoder);
        }

        [Fact]
        public void BothRtpAndWebRtcOfferBuildersGenerateTheAutomaticPair()
        {
            using (var session = new RTPSession(new RtpSessionConfig()))
            {
                session.addTrack(Track());
                var offer = session.CreateOffer(IPAddress.Loopback);
                var formats = offer.Media.Single().MediaFormats.Values;
                Assert.Equal(97, Assert.Single(formats, format => format.IsRed).ID);
                Assert.Equal(98, Assert.Single(formats, format => format.IsUlpfec).ID);
            }
            using (var pc = new RTCPeerConnection(null))
            {
                pc.addTrack(Track());
                var offer = SDP.ParseSDPDescription(pc.createOffer(new RTCOfferOptions
                {
                    X_ExcludeIceCandidates = true,
                    X_WaitForIceGatheringToComplete = false
                }).sdp);
                var formats = offer.Media.Single().MediaFormats.Values;
                Assert.Equal(97, Assert.Single(formats, format => format.IsRed).ID);
                Assert.Equal(98, Assert.Single(formats, format => format.IsUlpfec).ID);
            }
        }

        [Fact]
        public void AuxiliaryFormatsCannotBecomeTheSendingCodecOrReachVideoFormatEvents()
        {
            var formats = new List<SDPAudioVideoMediaFormat>
            {
                SDPAudioVideoMediaFormat.CreateRedFormat(114),
                SDPAudioVideoMediaFormat.CreateUlpfecFormat(116),
                Format(SDPMediaTypesEnum.video, 115, "rtx", "apt=102"),
                Format(SDPMediaTypesEnum.video, 118, "flexfec-03"),
                Format(SDPMediaTypesEnum.video, 102, "VP8"),
                Format(SDPMediaTypesEnum.video, 103, "H264")
            };
            var stream = new VideoStream(new RtpSessionConfig(), 0)
            {
                LocalTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, formats),
                RemoteTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, true, formats)
            };
            var events = new List<List<VideoFormat>>();
            stream.OnVideoFormatsNegotiatedByIndex += (index, negotiated) => events.Add(negotiated);

            Assert.Equal(102, stream.GetSendingFormat().ID);
            Assert.Equal("VP8", stream.GetSendingFormat().Name());
            stream.CheckVideoFormatsNegotiation();

            Assert.Equal(new[] { 102, 103 }, Assert.Single(events).Select(format => format.FormatID));
            Assert.Equal(new[] { VideoCodecsEnum.VP8, VideoCodecsEnum.H264 }, Assert.Single(events).Select(format => format.Codec));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AuxiliaryOnlyCapabilitiesCannotSelectASendingCodec(bool remoteOnly)
        {
            var formats = new List<SDPAudioVideoMediaFormat>
            {
                SDPAudioVideoMediaFormat.CreateRedFormat(114),
                SDPAudioVideoMediaFormat.CreateUlpfecFormat(116),
                Format(SDPMediaTypesEnum.video, 115, "rtx", "apt=102"),
                Format(SDPMediaTypesEnum.video, 118, "flexfec-03")
            };
            var stream = new VideoStream(new RtpSessionConfig(), 0);
            if (remoteOnly) stream.RemoteTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, true, formats);
            else stream.LocalTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, formats);

            Assert.Throws<ApplicationException>(() => stream.GetSendingFormat());
        }

        private static VideoStream Video(int index, params int[] ids)
        {
            return new VideoStream(new RtpSessionConfig(), index)
            {
                LocalTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false,
                    ids.Select(id => Format(SDPMediaTypesEnum.video, id, id == 96 ? "VP8" : "codec" + id)).ToList())
                {
                    Redundancy = VideoFec
                }
            };
        }

        private static MediaStreamTrack Track(string codec = "VP8", int id = 96, bool enabled = true)
            => new MediaStreamTrack(SDPMediaTypesEnum.video, false,
                new List<SDPAudioVideoMediaFormat> { Format(SDPMediaTypesEnum.video, id, codec) })
            {
                Redundancy = enabled ? VideoFec : RtpRedundancyOptions.None
            };

        private static SDPAudioVideoMediaFormat Format(SDPMediaTypesEnum kind, int id, string codec, string fmtp = null)
            => new SDPAudioVideoMediaFormat(kind, id, codec,
                kind == SDPMediaTypesEnum.audio ? 48000 : kind == SDPMediaTypesEnum.text ? 1000 : 90000,
                fmtp: fmtp);

        private static List<SDPAudioVideoMediaFormat> PeerFormats(bool red = true, bool ulpfec = true)
        {
            var formats = new List<SDPAudioVideoMediaFormat> { Format(SDPMediaTypesEnum.video, 102, "VP8") };
            if (red) formats.Add(SDPAudioVideoMediaFormat.CreateRedFormat(114));
            if (ulpfec) formats.Add(SDPAudioVideoMediaFormat.CreateUlpfecFormat(116));
            return formats;
        }

        private static SDP Offer(params List<SDPAudioVideoMediaFormat>[] formats)
        {
            var offer = new SDP(IPAddress.Loopback) { Connection = new SDPConnectionInformation(IPAddress.Loopback) };
            for (int i = 0; i < formats.Length; i++)
            {
                offer.Media.Add(new SDPMediaAnnouncement(SDPMediaTypesEnum.video, 9000 + i * 2, formats[i])
                {
                    Transport = "RTP/AVP",
                    MediaID = i.ToString(),
                    MLineIndex = i
                });
            }
            return offer;
        }

        private static string[] Snapshot(MediaStream stream)
            => stream.LocalTrack.Capabilities.Select(format => format.ID + ":" + format.Rtpmap + ":" + format.Fmtp).ToArray();

        private static void AssertPair(MediaStream stream, int red, int ulpfec)
        {
            var formats = stream.LocalTrack.Capabilities;
            Assert.Equal(red, Assert.Single(formats, format => format.IsRed).ID);
            Assert.Equal(ulpfec, Assert.Single(formats, format => format.IsUlpfec).ID);
            Assert.Equal(formats.Count, formats.Select(format => format.ID).Distinct().Count());
        }
    }
}
