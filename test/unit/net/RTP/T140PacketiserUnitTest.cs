//-----------------------------------------------------------------------------
// Filename: T140PacketiserUnitTest.cs
//
// Description: Unit tests for RFC 4103 T.140 real-time text packetisation,
// RED redundancy and the RttEndPoint.
//
// Author(s):
// Claude Code
//
// History:
// 20 Oct 2026  Claude Code    Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using SIPSorcery.Media;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class T140PacketiserUnitTest
    {
        private const int T140_PT = 98;
        private const int RED_PT = 100;

        /// <summary>
        /// Checks the RED header layout against a hand built payload: two redundant blocks of
        /// "a" and "bc" 300ms and 600ms old, then a primary of "d".
        /// </summary>
        [Fact]
        public void EncodeRedPayloadMatchesRfc2198LayoutUnitTest()
        {
            var redundant = new List<T140RedBlock>
            {
                new T140RedBlock(T140_PT, 600, Encoding.UTF8.GetBytes("a")),
                new T140RedBlock(T140_PT, 300, Encoding.UTF8.GetBytes("bc")),
            };

            var payload = T140RedPayload.Encode(redundant, new T140RedBlock(T140_PT, 0, Encoding.UTF8.GetBytes("d")));

            // F=1 PT=98, offset 600 (0x258) as 14 bits, length 1 as 10 bits.
            // 600 << 10 | 1 = 0x096001 -> 0x09 0x60 0x01.
            var expected = new byte[]
            {
                0x80 | T140_PT, 0x09, 0x60, 0x01,
                0x80 | T140_PT, 0x04, 0xB0, 0x02,
                T140_PT,
                (byte)'a', (byte)'b', (byte)'c', (byte)'d'
            };

            Assert.Equal(expected, payload);

            Assert.True(T140RedPayload.TryDecode(payload, out var decodedRedundant, out var primary));
            Assert.Equal(2, decodedRedundant.Count);
            Assert.Equal(600, decodedRedundant[0].TimestampOffset);
            Assert.Equal("a", Encoding.UTF8.GetString(decodedRedundant[0].Data));
            Assert.Equal(300, decodedRedundant[1].TimestampOffset);
            Assert.Equal("bc", Encoding.UTF8.GetString(decodedRedundant[1].Data));
            Assert.Equal(T140_PT, primary.PayloadType);
            Assert.Equal("d", Encoding.UTF8.GetString(primary.Data));
        }

        [Fact]
        public void DecodeTruncatedRedPayloadFailsUnitTest()
        {
            // Header claims a 5 byte redundant block but only 1 byte follows.
            var payload = new byte[] { 0x80 | T140_PT, 0x00, 0x00, 0x05, T140_PT, (byte)'x' };

            Assert.False(T140RedPayload.TryDecode(payload, out _, out _));
            Assert.False(T140RedPayload.TryDecode(new byte[] { 0x80 | T140_PT, 0x00 }, out _, out _));
        }

        [Theory]
        [InlineData("98/98/98", 98, 2)]
        [InlineData(" 96/96 ", 96, 1)]
        [InlineData("98", 98, 0)]
        public void ParseRedFmtpUnitTest(string fmtp, int expectedPayloadType, int expectedRedundancy)
        {
            Assert.True(T140RedPayload.TryParseFmtp(fmtp, out int pt, out int redundancy));
            Assert.Equal(expectedPayloadType, pt);
            Assert.Equal(expectedRedundancy, redundancy);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("98/99")]
        [InlineData("abc")]
        public void ParseInvalidRedFmtpUnitTest(string fmtp)
        {
            Assert.False(T140RedPayload.TryParseFmtp(fmtp, out _, out _));
        }

        [Fact]
        public void PlainT140SendsOnlyWhenThereIsTextUnitTest()
        {
            var packetiser = new T140Packetiser(T140_PT);

            Assert.Null(packetiser.GetNextPacket(0));

            packetiser.Enqueue("hi");
            var first = packetiser.GetNextPacket(300);
            Assert.NotNull(first);
            Assert.True(first.Value.Marker);
            Assert.Equal("hi", Encoding.UTF8.GetString(first.Value.Payload));

            packetiser.Enqueue("!");
            var second = packetiser.GetNextPacket(600);
            Assert.False(second.Value.Marker);

            Assert.Null(packetiser.GetNextPacket(900));

            // First packet after the idle period gets the marker again.
            packetiser.Enqueue("x");
            Assert.True(packetiser.GetNextPacket(5000).Value.Marker);
        }

        /// <summary>
        /// After the last text the sender keeps transmitting empty primaries until the text has
        /// been sent in every redundant generation, then stops.
        /// </summary>
        [Fact]
        public void RedFlushesRedundancyThenStopsUnitTest()
        {
            var packetiser = new T140Packetiser(T140_PT, RED_PT, 2);

            packetiser.Enqueue("a");
            var p1 = packetiser.GetNextPacket(0).Value;
            var p2 = packetiser.GetNextPacket(300).Value;
            var p3 = packetiser.GetNextPacket(600).Value;
            Assert.Null(packetiser.GetNextPacket(900));

            Assert.True(p1.Marker);
            Assert.False(p2.Marker);

            Assert.True(T140RedPayload.TryDecode(p1.Payload, out var r1, out var primary1));
            Assert.Equal(new[] { "", "" }, r1.Select(x => Encoding.UTF8.GetString(x.Data)));
            Assert.Equal("a", Encoding.UTF8.GetString(primary1.Data));

            Assert.True(T140RedPayload.TryDecode(p2.Payload, out var r2, out var primary2));
            Assert.Equal(new[] { "", "a" }, r2.Select(x => Encoding.UTF8.GetString(x.Data)));
            Assert.Equal(300, r2[1].TimestampOffset);
            Assert.Empty(primary2.Data);

            Assert.True(T140RedPayload.TryDecode(p3.Payload, out var r3, out _));
            Assert.Equal(new[] { "a", "" }, r3.Select(x => Encoding.UTF8.GetString(x.Data)));
            Assert.Equal(600, r3[0].TimestampOffset);
        }

        [Fact]
        public void CpsLimitSpreadsTextOverIntervalsUnitTest()
        {
            // 10 cps with a 300ms interval allows 3 characters per packet.
            var packetiser = new T140Packetiser(T140_PT, charactersPerSecond: 10);
            packetiser.Enqueue("abcdefg");

            Assert.Equal("abc", Encoding.UTF8.GetString(packetiser.GetNextPacket(0).Value.Payload));
            Assert.Equal("def", Encoding.UTF8.GetString(packetiser.GetNextPacket(300).Value.Payload));
            Assert.Equal("g", Encoding.UTF8.GetString(packetiser.GetNextPacket(600).Value.Payload));
        }

        [Fact]
        public void SurrogatePairIsNotSplitUnitTest()
        {
            // With a 1 character per interval limit the emoji must still go out whole.
            var packetiser = new T140Packetiser(T140_PT, charactersPerSecond: 1);
            packetiser.Enqueue("\U0001F600x");

            Assert.Equal("\U0001F600", Encoding.UTF8.GetString(packetiser.GetNextPacket(0).Value.Payload));
            Assert.Equal("x", Encoding.UTF8.GetString(packetiser.GetNextPacket(300).Value.Payload));
        }

        [Fact]
        public void LargeTextIsSplitIntoRedSizedBlocksUnitTest()
        {
            var packetiser = new T140Packetiser(T140_PT, RED_PT, 2);
            var text = new string('z', 2500);
            packetiser.Enqueue(text);

            var received = new StringBuilder();
            var depacketiser = new T140Depacketiser(T140_PT, RED_PT);
            ushort seq = 0;
            T140Packet? packet;

            while ((packet = packetiser.GetNextPacket(seq * 300)) != null)
            {
                received.Append(depacketiser.ProcessPayload(1, seq++, RED_PT, packet.Value.Payload));
            }

            Assert.Equal(text, received.ToString());
        }

        [Fact]
        public void DepacketiserRecoversFromRedundancyUnitTest()
        {
            var packets = BuildRedPackets(new[] { "H", "e", "l", "l", "o" });
            var depacketiser = new T140Depacketiser(T140_PT, RED_PT);
            var received = new StringBuilder();

            // Lose packets 1 and 2 ("e" and "l"), both are within the redundancy of packet 3.
            foreach (int i in new[] { 0, 3, 4 })
            {
                received.Append(depacketiser.ProcessPayload(1, (ushort)i, RED_PT, packets[i]));
            }

            Assert.Equal("Hello", received.ToString());
            Assert.Equal(2, depacketiser.RecoveredPacketCount);
            Assert.Equal(0, depacketiser.LostPacketCount);
        }

        [Fact]
        public void DepacketiserInsertsLossMarkerBeyondRedundancyUnitTest()
        {
            var packets = BuildRedPackets(new[] { "a", "b", "c", "d", "e", "f" });
            var depacketiser = new T140Depacketiser(T140_PT, RED_PT);
            var received = new StringBuilder();

            // Lose packets 1, 2 and 3, only 2 and 3 can be recovered from packet 4.
            foreach (int i in new[] { 0, 4, 5 })
            {
                received.Append(depacketiser.ProcessPayload(1, (ushort)i, RED_PT, packets[i]));
            }

            Assert.Equal($"a{T140Depacketiser.LOSS_MARKER}cdef", received.ToString());
            Assert.Equal(1, depacketiser.LostPacketCount);
        }

        [Fact]
        public void DepacketiserDropsDuplicateAndLatePacketsUnitTest()
        {
            var packets = BuildRedPackets(new[] { "a", "b", "c" });
            var depacketiser = new T140Depacketiser(T140_PT, RED_PT);
            var received = new StringBuilder();

            foreach (int i in new[] { 0, 2, 1, 2 })
            {
                received.Append(depacketiser.ProcessPayload(1, (ushort)i, RED_PT, packets[i]));
            }

            Assert.Equal("abc", received.ToString());
        }

        [Fact]
        public void DepacketiserHandlesSequenceWrapUnitTest()
        {
            var depacketiser = new T140Depacketiser(T140_PT);

            Assert.Equal("a", depacketiser.ProcessPayload(1, ushort.MaxValue, T140_PT, Encoding.UTF8.GetBytes("a")));
            Assert.Equal("b", depacketiser.ProcessPayload(1, 0, T140_PT, Encoding.UTF8.GetBytes("b")));
            Assert.Equal($"{T140Depacketiser.LOSS_MARKER}d", depacketiser.ProcessPayload(1, 2, T140_PT, Encoding.UTF8.GetBytes("d")));
        }

        [Fact]
        public void DepacketiserRemovesByteOrderMarkUnitTest()
        {
            var depacketiser = new T140Depacketiser(T140_PT);

            Assert.Equal("hi", depacketiser.ProcessPayload(1, 0, T140_PT, Encoding.UTF8.GetBytes("\uFEFFhi")));
            Assert.Equal(string.Empty, depacketiser.ProcessPayload(1, 1, T140_PT, Encoding.UTF8.GetBytes("\uFEFF")));
        }

        [Fact]
        public void DepacketiserAcceptsPlainT140WhenRedNegotiatedUnitTest()
        {
            var depacketiser = new T140Depacketiser(T140_PT, RED_PT);

            Assert.Equal("ok", depacketiser.ProcessPayload(1, 0, T140_PT, Encoding.UTF8.GetBytes("ok")));
        }

        [Fact]
        public void TextFormatCodecIsCaseInsensitiveUnitTest()
        {
            Assert.Equal(TextCodecsEnum.T140, new TextFormat(98, "t140").Codec);
            Assert.Equal(TextCodecsEnum.RED, new TextFormat(100, "red", parameters: "98/98/98").Codec);
        }

        [Fact]
        public async Task CanSendTextFollowsSourceStateUnitTest()
        {
            using var rtt = new RttEndPoint();

            Assert.False(rtt.CanSendText);

            await rtt.StartText();
            Assert.True(rtt.CanSendText);

            await rtt.PauseText();
            Assert.False(rtt.CanSendText);

            await rtt.ResumeText();
            Assert.True(rtt.CanSendText);

            await rtt.CloseText();
            Assert.False(rtt.CanSendText);
        }

        /// <summary>
        /// The remote party can send text before this side starts its media session. That text
        /// must be held and delivered when the sink starts rather than dropped.
        /// </summary>
        [Fact]
        public async Task TextReceivedBeforeSinkStartIsDeliveredOnStartUnitTest()
        {
            using var rtt = new RttEndPoint(useRedundancy: false);
            var received = new StringBuilder();
            rtt.OnTextReceived += textData => received.Append(textData.Text);
            var ep = new IPEndPoint(IPAddress.Loopback, 5000);

            rtt.GotTextRtp(ep, 1, 0, 0, T140_PT, 1, Encoding.UTF8.GetBytes("He"));
            rtt.GotTextRtp(ep, 1, 1, 300, T140_PT, 0, Encoding.UTF8.GetBytes("llo"));

            Assert.Equal(string.Empty, received.ToString());

            await rtt.StartTextSink();
            Assert.Equal("Hello", received.ToString());

            rtt.GotTextRtp(ep, 1, 2, 600, T140_PT, 0, Encoding.UTF8.GetBytes("!"));
            Assert.Equal("Hello!", received.ToString());
        }

        [Fact]
        public async Task TextReceivedAfterSinkCloseIsDroppedUnitTest()
        {
            using var rtt = new RttEndPoint(useRedundancy: false);
            var received = new StringBuilder();
            rtt.OnTextReceived += text => received.Append(text);
            var ep = new IPEndPoint(IPAddress.Loopback, 5000);

            await rtt.StartTextSink();
            await rtt.CloseTextSink();
            rtt.GotTextRtp(ep, 1, 0, 0, T140_PT, 1, Encoding.UTF8.GetBytes("late"));

            Assert.Equal(string.Empty, received.ToString());
        }

        [Fact]
        public async Task HoldPausesAndResumesTextSourceUnitTest()
        {
            using var rtt = new RttEndPoint();
            var session = new VoIPMediaSession(new MediaEndPoints { TextSource = rtt, TextSink = rtt });

            try
            {
                await rtt.StartText();
                Assert.True(rtt.CanSendText);

                await session.PutOnHold();
                Assert.False(rtt.CanSendText);

                await session.TakeOffHold();
                Assert.True(rtt.CanSendText);
            }
            finally
            {
                session.Close(null);
            }
        }

        /// <summary>
        /// The local offer from a session with an RttEndPoint must include both RED and the
        /// t140 format it references.
        /// </summary>
        [Fact]
        public void OfferIncludesRedAndT140UnitTest()
        {
            var rtt = new RttEndPoint();
            var session = new VoIPMediaSession(new MediaEndPoints { TextSource = rtt, TextSink = rtt });

            var offer = session.CreateOffer(IPAddress.Loopback);
            var text = offer.Media.Single(x => x.Media == SDPMediaTypesEnum.text);

            Assert.Equal("red/1000", text.MediaFormats[RED_PT].Rtpmap, ignoreCase: true);
            Assert.Equal("98/98/98", text.MediaFormats[RED_PT].Fmtp);
            Assert.Equal("t140/1000", text.MediaFormats[T140_PT].Rtpmap, ignoreCase: true);

            session.Close(null);
        }

        /// <summary>
        /// Answering an offer that uses different payload types must switch the RttEndPoint to the
        /// remote party's payload types.
        /// </summary>
        [Fact]
        public void AnswerUsesRemotePayloadTypesUnitTest()
        {
            string remoteSdp =
            @"v=0
o=- 1986548327 0 IN IP4 127.0.0.1
s=-
c=IN IP4 127.0.0.1
t=0 0
m=text 11000 RTP/AVP 101 96
a=rtpmap:101 red/1000
a=fmtp:101 96/96/96
a=rtpmap:96 t140/1000";

            var rtt = new RttEndPoint();
            var session = new VoIPMediaSession(new MediaEndPoints { TextSource = rtt, TextSink = rtt });

            var result = session.SetRemoteDescription(SdpType.offer, SDP.ParseSDPDescription(remoteSdp));

            Assert.Equal(SetDescriptionResultEnum.OK, result);
            Assert.Equal(TextCodecsEnum.RED, rtt.GetTextSourceFormat().Codec);
            Assert.Equal(101, rtt.GetTextSourceFormat().FormatID);
            Assert.Equal("96/96/96", rtt.GetTextSourceFormat().Parameters);
            Assert.Equal(101, rtt.GetTextSinkFormat().FormatID);

            session.Close(null);
        }

        /// <summary>
        /// Two sessions on the loopback interface exchange text end to end.
        /// </summary>
        [Fact]
        public async Task LoopbackRttCallUnitTest()
        {
            var aliceRtt = new RttEndPoint(bufferTimeMilliseconds: 50);
            var bobRtt = new RttEndPoint(bufferTimeMilliseconds: 50);
            var alice = new VoIPMediaSession(new MediaEndPoints { TextSource = aliceRtt, TextSink = aliceRtt }, IPAddress.Loopback);
            var bob = new VoIPMediaSession(new MediaEndPoints { TextSource = bobRtt, TextSink = bobRtt }, IPAddress.Loopback);

            try
            {
                var offer = alice.CreateOffer(IPAddress.Loopback);
                Assert.Equal(SetDescriptionResultEnum.OK, bob.SetRemoteDescription(SdpType.offer, offer));
                var answer = bob.CreateAnswer(IPAddress.Loopback);
                Assert.Equal(SetDescriptionResultEnum.OK, alice.SetRemoteDescription(SdpType.answer, answer));

                await alice.Start();
                await bob.Start();

                var bobReceived = new StringBuilder();
                var aliceReceived = new StringBuilder();
                var bobDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var aliceDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                bobRtt.OnTextReceived += e =>
                {
                    lock (bobReceived)
                    {
                        bobReceived.Append(e.Text);
                        if (bobReceived.ToString() == "Hello Bob\u2028") { bobDone.TrySetResult(true); }
                    }
                };
                aliceRtt.OnTextReceived += e =>
                {
                    lock (aliceReceived)
                    {
                        aliceReceived.Append(e.Text);
                        if (aliceReceived.ToString() == "Hi") { aliceDone.TrySetResult(true); }
                    }
                };

                foreach (var c in "Hello Bob\u2028")
                {
                    aliceRtt.SendText(c.ToString());
                }
                bobRtt.SendText("Hi");

                var all = Task.WhenAll(bobDone.Task, aliceDone.Task);
                Assert.Same(all, await Task.WhenAny(all, Task.Delay(5000)));
                Assert.Equal(0, bobRtt.LostPacketCount);
            }
            finally
            {
                alice.Close(null);
                bob.Close(null);
                aliceRtt.Dispose();
                bobRtt.Dispose();
            }
        }

        private static List<byte[]> BuildRedPackets(string[] blocks)
        {
            var packetiser = new T140Packetiser(T140_PT, RED_PT, 2);
            var packets = new List<byte[]>();

            for (int i = 0; i < blocks.Length; i++)
            {
                packetiser.Enqueue(blocks[i]);
                packets.Add(packetiser.GetNextPacket(i * 300).Value.Payload);
            }

            return packets;
        }
    }
}
