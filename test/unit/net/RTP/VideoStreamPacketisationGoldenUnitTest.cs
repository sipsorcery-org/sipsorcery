//-----------------------------------------------------------------------------
// Filename: VideoStreamPacketisationGoldenUnitTest.cs
//
// Description: Pins the exact RTP packets VideoStream puts on the wire for a set
// of deterministic video frames, covering every codec's packetisation, header
// extensions, SRTP protection and ULPFEC, so that a change to the send path that
// is not meant to change its output can be shown not to.
//
// The expected digests were produced by running this file against the send path
// as it was at commit c331140ed, before the VP8 send path changed. It only uses
// API that existed then, so it can be re-run there. The exceptions are the
// scenarios marked "packetisation fix": their output changed on purpose with the
// H264/H265 fragmentation, H265 aggregation and Annex B parsing fixes, which
// H26XPacketiserUnitTest checks in detail.
//
// Author(s):
// SIPSorcery contributors
//
// History:
// 06 Nov 2026  SIPSorcery contributors  Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using SIPSorcery.net.RTP.Packetisation;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class VideoStreamPacketisationGoldenUnitTest
    {
        private readonly Xunit.Abstractions.ITestOutputHelper _output;

        public VideoStreamPacketisationGoldenUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            _output = output;
        }

        public static IEnumerable<object[]> GoldenScenarios() =>
            VideoRtpGoldenScenario.All.Select(s => new object[] { s.Name, s.ExpectedDigest });

        /// <summary>
        /// The array based send methods put exactly the expected packets on the wire.
        /// </summary>
        [Theory]
        [MemberData(nameof(GoldenScenarios))]
        public void SendVideo_MatchesGoldenWireOutput(string scenarioName, string expectedDigest)
        {
            var scenario = VideoRtpGoldenScenario.Get(scenarioName);

            var packets = scenario.Run((stream, timestamp, frame) =>
            {
                if (timestamp.HasValue)
                {
                    stream.SendVideoAt(timestamp.Value, frame);
                }
                else
                {
                    stream.SendVideo(VideoRtpGoldenScenario.FRAME_DURATION, frame);
                }
            });

            string digest = VideoRtpGoldenScenario.Digest(packets);
            _output.WriteLine($"{scenarioName}: {packets.Count} packets, {packets.Sum(p => p.Length)} bytes, digest {digest}");

            Assert.Equal(expectedDigest, digest);
        }
    }

    /// <summary>
    /// A deterministic sequence of encoded video frames sent through a freshly set up <see cref="VideoStream"/>, with
    /// the wire packets captured.
    /// </summary>
    public sealed class VideoRtpGoldenScenario
    {
        public const uint FRAME_DURATION = 3000;
        public const uint SSRC = 0x11223344;
        public const ushort FIRST_SEQUENCE_NUMBER = 65530; // Wraps during most scenarios.

        public delegate void SendFrame(VideoStream stream, uint? explicitTimestamp, byte[] frame);

        public string Name { get; private set; }
        public string ExpectedDigest { get; private set; }
        public string Codec { get; private set; }
        public int PayloadType { get; private set; } = 96;
        public string Fmtp { get; private set; }
        public Func<List<byte[]>> CreateFrames { get; private set; }
        public uint[] ExplicitTimestamps { get; private set; }
        public bool HeaderExtensions { get; private set; }
        public bool Srtp { get; private set; }
        public bool Ulpfec { get; private set; }

        public static readonly VideoRtpGoldenScenario[] All =
        {
            new VideoRtpGoldenScenario { Name = "VP8_Sizes", Codec = "VP8",
                // Either side of the 1200 byte packet boundary, an empty frame (no packets, but the timestamp still
                // advances), and a large key frame sized frame.
                CreateFrames = () => Frames(Random(1, 1), Random(1199, 2), Random(1200, 3), Random(1201, 4), Random(0, 5), Random(2400, 6), Random(2401, 7), Random(25_000, 8), Random(60_000, 9)),
                ExpectedDigest = "DB71ED0FE84B106E791603C3B1CFC90EE15B00E90AF06E3E4A2F6D711B01C383" },
            new VideoRtpGoldenScenario { Name = "VP8_ExplicitTimestamps", Codec = "VP8",
                CreateFrames = () => Frames(Random(3000, 10), Random(800, 11), Random(5000, 12)),
                ExplicitTimestamps = new uint[] { 1_000, 7_000, 7_001 },
                ExpectedDigest = "6D94D209595661BAFFCE2F6393302A1B2B862908100E2EEAF0F6D6BF1A4DE4C1" },
            new VideoRtpGoldenScenario { Name = "VP8_HeaderExtensions_Srtp", Codec = "VP8", HeaderExtensions = true, Srtp = true,
                CreateFrames = () => Frames(Random(4000, 13), Random(900, 14), Random(1200, 15)),
                ExpectedDigest = "418A02DE0D8F72AB8ECD90A3F9B2D1F2A1E8DCDBF657A0132D2ACC84E59ECA76" },
            new VideoRtpGoldenScenario { Name = "VP8_Ulpfec", Codec = "VP8", Ulpfec = true,
                CreateFrames = () => Frames(Random(6000, 16), Random(700, 17), Random(2500, 18)),
                ExpectedDigest = "2AC14D8E11DFF7809EDEEE6C89546A00B7E869C3A8B3C80736350AFCB0BEED54" },

            // Packetisation fix: the 1201 byte NAL is sent as two fragmentation units rather than one.
            new VideoRtpGoldenScenario { Name = "H264_SpsPpsIdr", Codec = "H264", Fmtp = "packetization-mode=1",
                CreateFrames = () => Frames(
                    Au(Sc4, H264Nal(0x67, 12, 20), Sc4, H264Nal(0x68, 4, 21), Sc4, H264Nal(0x65, 5000, 22)),
                    Au(Sc4, H264Nal(0x41, 900, 23)),
                    Au(Sc4, H264Nal(0x41, 1200, 24)),
                    Au(Sc4, H264Nal(0x41, 1201, 25)),
                    Au(Sc4, H264Nal(0x41, 2401, 26))),
                ExpectedDigest = "A8B7E41DD3346286A4BE9EA690CE37188F53A615E60CDD482FA7A385344CE5DF" },
            new VideoRtpGoldenScenario { Name = "H264_ThreeByteStartCodes", Codec = "H264", Fmtp = "packetization-mode=1",
                CreateFrames = () => Frames(
                    Au(Sc3, H264Nal(0x09, 2, 30), Sc3, H264Nal(0x41, 1500, 31), Sc3, H264Nal(0x41, 40, 32)),
                    Au(Sc3, H264Nal(0x65, 3601, 33))),
                ExpectedDigest = "AF37A5863ADD6D09FCD92A4FDFEB42989F9661A9C299C75DC4E463BD578E09FD" },
            // Packetisation fix. The Annex B parser's handling of the awkward cases: data before the first start code,
            // a one byte NAL before a three byte start code, extra zero bytes before a start code, a NAL whose header
            // byte is 0x01, back to back start codes and trailing zeros.
            new VideoRtpGoldenScenario { Name = "H264_ParserEdgeCases", Codec = "H264", Fmtp = "packetization-mode=1",
                CreateFrames = () => Frames(
                    Au(H264Nal(0x65, 10, 40), Sc4, H264Nal(0x41, 50, 41)),
                    Au(Sc3, new byte[] { 0x09 }, Sc3, H264Nal(0x41, 60, 42)),
                    Au(Sc4, H264Nal(0x41, 70, 43), new byte[] { 0x00, 0x00, 0x00, 0x00, 0x01 }, H264Nal(0x41, 80, 44)),
                    Au(Sc3, new byte[] { 0x01 }, H264Nal(0x41, 90, 45)),
                    Au(Sc4, Sc4, H264Nal(0x41, 100, 46), new byte[] { 0x00, 0x00 }),
                    new byte[0]),
                ExpectedDigest = "56526BD40814479672D892062FB822A49DF841AEBA496EFF8C05940049698B99" },
            // Packetisation fix. Random bytes, zeros included, so the parser finds start codes wherever they happen
            // to occur.
            new VideoRtpGoldenScenario { Name = "H264_RandomBytes", Codec = "H264", Fmtp = "packetization-mode=1",
                CreateFrames = () => Frames(RandomWithStartCodes(20_000, 50), RandomWithStartCodes(3_000, 51)),
                ExpectedDigest = "A135A2AF963845D7720FF1448BDEE96AA873B0E4B1C65657BFB26A449153D98D" },
            new VideoRtpGoldenScenario { Name = "H264_LargeIdr_HeaderExtensions_Srtp", Codec = "H264", Fmtp = "packetization-mode=1", HeaderExtensions = true, Srtp = true,
                CreateFrames = () => Frames(Au(Sc4, H264Nal(0x67, 12, 60), Sc4, H264Nal(0x68, 4, 61), Sc4, H264Nal(0x65, 60_000, 62)), Au(Sc4, H264Nal(0x41, 4000, 63))),
                ExpectedDigest = "EED6112E1A464AF585E6063DFF055DE0C5FB086D99B034DA0D809BDC79AFCB14" },
            new VideoRtpGoldenScenario { Name = "H264_Ulpfec", Codec = "H264", Fmtp = "packetization-mode=1", Ulpfec = true,
                CreateFrames = () => Frames(Au(Sc4, H264Nal(0x67, 12, 70), Sc4, H264Nal(0x68, 4, 71), Sc4, H264Nal(0x65, 3000, 72)), Au(Sc4, H264Nal(0x41, 500, 73))),
                ExpectedDigest = "F73FD161A87051800AEFB75A0FBF6FCF412F7D91044D2AECB9F32DFF51653573" },
            new VideoRtpGoldenScenario { Name = "H264_ExplicitTimestamps", Codec = "H264", Fmtp = "packetization-mode=1",
                CreateFrames = () => Frames(Au(Sc4, H264Nal(0x41, 20, 80), Sc4, H264Nal(0x41, 3000, 81)), Au(Sc4, H264Nal(0x41, 1300, 82))),
                ExplicitTimestamps = new uint[] { 90_000, 96_000 },
                ExpectedDigest = "626CF647ECC20DFF6D76829B10E510100575B4664542DC3B0E763FF0BB4405BB" },

            // Packetisation fix. Small NALs are aggregated and large ones fragmented, every fragmentation unit
            // carrying the NAL's type. An aggregation packet holds two or more NALs, so in the third frame the 700
            // and 200 byte NALs share one, where the 700 byte NAL was sent alone and the 200 byte one in an
            // aggregation packet of its own.
            new VideoRtpGoldenScenario { Name = "H265_ParameterSetsAndIdr", Codec = "H265",
                CreateFrames = () => Frames(
                    Au(Sc4, H265Nal(32, 24, 90), Sc4, H265Nal(33, 40, 91), Sc4, H265Nal(34, 8, 92), Sc4, H265Nal(19, 4000, 93)),
                    Au(Sc4, H265Nal(1, 500, 94)),
                    Au(Sc4, H265Nal(1, 300, 95), Sc3, H265Nal(1, 400, 96), Sc4, H265Nal(1, 700, 97), Sc4, H265Nal(1, 200, 98))),
                ExpectedDigest = "B34894A5154357EE3E6A0781D968B6FACCED0EA94B53BDE6F8C0F001B6486BD5" },
            // Packetisation fix: the FU types, and the 1201 byte NAL is sent as two fragmentation units.
            new VideoRtpGoldenScenario { Name = "H265_Large_HeaderExtensions", Codec = "H265", HeaderExtensions = true,
                CreateFrames = () => Frames(Au(Sc4, H265Nal(19, 30_000, 100)), Au(Sc4, H265Nal(1, 1201, 101))),
                ExpectedDigest = "F51F32A4E863FA3203E6299A37A88CC8EAD62A5438F0470A575F4CA4D8B62678" },

            new VideoRtpGoldenScenario { Name = "VP9_KeyAndInterFrames", Codec = "VP9", PayloadType = 98,
                CreateFrames = () => Frames(Vp9Frame(true, 5000, 110), Vp9Frame(false, 800, 111), Vp9Frame(false, 1200, 112), Vp9Frame(false, 2500, 113)),
                ExpectedDigest = "27310AD8D1AADA4D52A4EFF6F5E4C73E4D42338D68E17C0F7DCF3848EE85015C" },

            new VideoRtpGoldenScenario { Name = "AV1_TemporalUnits", Codec = "AV1", PayloadType = 45,
                CreateFrames = () => Frames(
                    Au(Obu(2, 0, 120), Obu(1, 12, 121), Obu(6, 3000, 122)),
                    Au(Obu(2, 0, 123), Obu(6, 900, 124))),
                ExpectedDigest = "D83367407A14AC689402D2E804FC00BE80CA68A976FBBBF857D42154891ABCBF" },

            new VideoRtpGoldenScenario { Name = "MJPEG_Frames", Codec = "JPEG", PayloadType = 26,
                CreateFrames = () => Frames(Jpeg(100, 130), Jpeg(2500, 131)),
                ExpectedDigest = "383326972DA4B5EB1112BCBB689DA69D37534428321DA401FFCCAB1B0EE4CB5E" },
        };

        public static VideoRtpGoldenScenario Get(string name) => All.Single(s => s.Name == name);

        /// <summary>
        /// Sends the scenario's frames through a new video stream and returns the packets that were put on the wire.
        /// </summary>
        public List<byte[]> Run(SendFrame send)
        {
            var format = new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, PayloadType, Codec, 90000, 0, Fmtp);

            Dictionary<int, RTPHeaderExtension> headerExtensions = null;
            if (HeaderExtensions)
            {
                headerExtensions = new Dictionary<int, RTPHeaderExtension>
                {
                    { 3, new TransportWideCCExtension(3) },
                    { 5, new CVOExtension(5) },
                };
            }

            var stream = new VideoStream(new RtpSessionConfig(), 0);
            stream.LocalTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { format },
                MediaStreamStatusEnum.SendOnly, null, headerExtensions);
            stream.LocalTrack.Ssrc = SSRC;
            stream.LocalTrack.SeqNum = FIRST_SEQUENCE_NUMBER;
            stream.DestinationEndPoint = new IPEndPoint(IPAddress.Loopback, 5004);

            if (Srtp)
            {
                stream.SetSecurityContext(FakeProtect, FakeProtect, FakeProtect, FakeProtect);
            }

            if (Ulpfec)
            {
                stream.EnableUlpfec(122, 127, PayloadType, new RtpUlpfecConfig { TargetOverheadFraction = 0.25f, MinGroupPackets = 1 });
            }

            var channel = new CapturingRtpChannel();
            stream.AddRtpChannel(channel);

            try
            {
                var frames = CreateFrames();
                for (int i = 0; i < frames.Count; i++)
                {
                    send(stream, ExplicitTimestamps?[i], frames[i]);
                }

                return channel.Packets;
            }
            finally
            {
                channel.Close("test complete");
            }
        }

        /// <summary>
        /// A deterministic stand in for SRTP protection: scrambles everything after the fixed header in place and
        /// appends a ten byte tag that depends on the whole packet, as an authentication tag would.
        /// </summary>
        internal static int FakeProtect(byte[] packet, int length, out int outputBufferLength)
        {
            uint tag = 2166136261;
            for (int i = 0; i < length; i++)
            {
                if (i >= 12)
                {
                    packet[i] ^= (byte)(0x5A + i);
                }
                tag = (tag ^ packet[i]) * 16777619;
            }

            for (int i = 0; i < 10; i++)
            {
                packet[length + i] = (byte)(tag >> ((i % 4) * 8));
            }

            outputBufferLength = length + 10;
            return 0;
        }

        /// <summary>
        /// SHA-256 over the packets, each prefixed with its length, as upper case hex.
        /// </summary>
        public static string Digest(IList<byte[]> packets)
        {
            using (var ms = new MemoryStream())
            {
                foreach (var packet in packets)
                {
                    ms.WriteByte((byte)(packet.Length >> 8));
                    ms.WriteByte((byte)packet.Length);
                    ms.Write(packet, 0, packet.Length);
                }

                using (var sha = SHA256.Create())
                {
                    return BitConverter.ToString(sha.ComputeHash(ms.ToArray())).Replace("-", string.Empty);
                }
            }
        }

        private static readonly byte[] Sc4 = { 0x00, 0x00, 0x00, 0x01 };
        private static readonly byte[] Sc3 = { 0x00, 0x00, 0x01 };

        private static List<byte[]> Frames(params byte[][] frames) => frames.ToList();

        private static byte[] Au(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

        private static byte[] Random(int length, uint seed)
        {
            var bytes = new byte[length];
            uint state = seed * 2654435761u + 1;
            for (int i = 0; i < length; i++)
            {
                bytes[i] = (byte)Next(ref state);
            }
            return bytes;
        }

        private static byte[] RandomWithStartCodes(int length, uint seed)
        {
            var bytes = Random(length, seed);
            // Bias towards zeros so start codes occur often.
            uint state = seed + 7;
            for (int i = 0; i < length; i++)
            {
                if ((Next(ref state) & 0x07) < 3)
                {
                    bytes[i] = 0;
                }
                else if ((Next(ref state) & 0x1F) == 0)
                {
                    bytes[i] = 1;
                }
            }
            return bytes;
        }

        /// <summary>
        /// An H264 NAL: the header byte then payload bytes that can never form a start code.
        /// </summary>
        private static byte[] H264Nal(byte header, int length, uint seed)
        {
            var nal = Random(length, seed);
            nal[0] = header;
            for (int i = 1; i < length; i++)
            {
                nal[i] = (byte)(2 + nal[i] % 254);
            }
            return nal;
        }

        /// <summary>
        /// An H265 NAL: the two byte header for <paramref name="type"/> then payload bytes that can never form a start code.
        /// </summary>
        private static byte[] H265Nal(int type, int length, uint seed)
        {
            var nal = H264Nal(0x00, length, seed);
            nal[0] = (byte)(type << 1);
            nal[1] = 0x01;
            return nal;
        }

        private static byte[] Vp9Frame(bool keyFrame, int length, uint seed)
        {
            var frame = Random(length, seed);
            // frame_marker 0b10, profile 0, show_existing_frame 0, frame_type 0 for key and 1 for inter.
            frame[0] = keyFrame ? (byte)0x82 : (byte)0x86;
            return frame;
        }

        /// <summary>
        /// An AV1 OBU with a size field.
        /// </summary>
        private static byte[] Obu(int type, int payloadLength, uint seed)
        {
            byte header = (byte)((type << 3) | 0x02);
            return new[] { header }.Concat(AV1Packetiser.WriteLeb128(payloadLength)).Concat(Random(payloadLength, seed)).ToArray();
        }

        /// <summary>
        /// A minimal baseline JPEG: SOI, DQT, SOF0 (16x16), SOS, entropy coded data and EOI.
        /// </summary>
        private static byte[] Jpeg(int entropyLength, uint seed)
        {
            var frame = new List<byte> { 0xFF, 0xD8 };
            frame.AddRange(new byte[] { 0xFF, 0xDB, 0x00, 0x43, 0x00 });
            frame.AddRange(Enumerable.Range(1, 64).Select(x => (byte)x));
            frame.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x10, 0x00, 0x10, 0x03, 0x01, 0x21, 0x00, 0x02, 0x11, 0x00, 0x03, 0x11, 0x00 });
            frame.AddRange(new byte[] { 0xFF, 0xDA, 0x00, 0x0C, 0x03, 0x01, 0x00, 0x02, 0x11, 0x03, 0x11, 0x00, 0x3F, 0x00 });
            frame.AddRange(Random(entropyLength, seed).Select(b => b == 0xFF ? (byte)0xFE : b));
            frame.AddRange(new byte[] { 0xFF, 0xD9 });
            return frame.ToArray();
        }

        private static uint Next(ref uint state)
        {
            // xorshift32
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }
    }

    /// <summary>
    /// An RTP channel that records a copy of every packet it is asked to send instead of sending it.
    /// </summary>
    internal sealed class CapturingRtpChannel : RTPChannel
    {
        public List<byte[]> Packets { get; } = new List<byte[]>();

        public CapturingRtpChannel() : base(false, IPAddress.Loopback) { }

        public override SocketError Send(RTPChannelSocketsEnum sendOn, IPEndPoint dstEndPoint, byte[] buffer)
        {
            Packets.Add((byte[])buffer.Clone());
            return SocketError.Success;
        }
    }
}
