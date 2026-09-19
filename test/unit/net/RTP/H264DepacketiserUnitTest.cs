//-----------------------------------------------------------------------------
// Filename: H264DepacketiserUnitTest.cs
//
// Description: Unit tests for the H264 depacketiser's Fragmentation Unit (FU-A)
// reassembly. A fragment run that lost its start fragment, or that has a hole in
// it, does not reconstitute the original NAL. The reassembler used to concatenate
// whatever fragments survived, so the first byte of slice data was parsed as the
// NAL header (typically giving the reserved type 31) and the garbage NAL was
// handed downstream as though the frame were complete.
//
// History:
// 18 Sep 2026  Aaron Clauson   Created, Dublin, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class H264DepacketiserUnitTest
    {
        private const int FU_A = 28;
        private const int STAP_A = 24;
        private const int NON_IDR_NAL_TYPE = 1;
        private const int IDR_NAL_TYPE = 5;
        private const int SPS_NAL_TYPE = 7;
        private const int PPS_NAL_TYPE = 8;

        private readonly ILogger logger;

        public H264DepacketiserUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Builds an FU-A packet payload carrying a slice of an IDR NAL.
        /// </summary>
        private static byte[] CreateFuA(bool start, bool end, byte[] fragment)
        {
            byte fuIndicator = (byte)((3 << 5) | FU_A);     // F=0, NRI=3, the F and NRI bits get copied to the reconstructed NAL header.
            byte fuHeader = (byte)((start ? 0x80 : 0) | (end ? 0x40 : 0) | IDR_NAL_TYPE);

            return new byte[] { fuIndicator, fuHeader }.Concat(fragment).ToArray();
        }

        /// <summary>
        /// Builds a single NAL unit packet payload, the "normal" non aggregated, non
        /// fragmented case.
        /// </summary>
        private static byte[] CreateSingleNal(int nalType, params byte[] data)
        {
            return new byte[] { (byte)((3 << 5) | nalType) }.Concat(data).ToArray();
        }

        /// <summary>
        /// Builds a STAP-A packet payload carrying the supplied NAL units, each prefixed with
        /// its 16 bit size.
        /// </summary>
        private static byte[] CreateStapA(params byte[][] nals)
        {
            var payload = new List<byte> { (byte)((3 << 5) | STAP_A) };

            foreach (var nal in nals)
            {
                payload.Add((byte)(nal.Length >> 8));
                payload.Add((byte)(nal.Length & 0xFF));
                payload.AddRange(nal);
            }

            return payload.ToArray();
        }

        /// <summary>
        /// The undamaged case. Three fragments with consecutive sequence numbers must
        /// reassemble into the original NAL with its header reconstructed.
        /// </summary>
        [Fact]
        public void CompleteFragmentRun_ReassemblesNal()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var depacketiser = new H264Depacketiser();

            Assert.Null(depacketiser.ProcessRTPPayloadAsNals(CreateFuA(true, false, new byte[] { 0x11, 0x22 }), 100, 90000, 0, out _));
            Assert.Null(depacketiser.ProcessRTPPayloadAsNals(CreateFuA(false, false, new byte[] { 0x33, 0x44 }), 101, 90000, 0, out _));

            var nals = depacketiser.ProcessRTPPayloadAsNals(CreateFuA(false, true, new byte[] { 0x55 }), 102, 90000, 1, out bool isKeyFrame);

            Assert.NotNull(nals);
            var nal = Assert.Single(nals);
            Assert.Equal(IDR_NAL_TYPE, nal[0] & 0x1F);
            Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55 }, nal.Skip(1).ToArray());
            Assert.True(isKeyFrame);
        }

        /// <summary>
        /// The failure from issue #1797. When the start fragment is lost the remaining
        /// fragments carry no NAL header, so they must be discarded rather than handed on
        /// as a NAL whose type is read out of slice data.
        /// </summary>
        [Fact]
        public void FragmentRunWithoutStartFragment_IsDiscarded()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var depacketiser = new H264Depacketiser();

            // Sequence number 100, the start fragment, never arrives.
            Assert.Null(depacketiser.ProcessRTPPayloadAsNals(CreateFuA(false, false, new byte[] { 0x33, 0x44 }), 101, 90000, 0, out _));

            var nals = depacketiser.ProcessRTPPayloadAsNals(CreateFuA(false, true, new byte[] { 0x55 }), 102, 90000, 1, out _);

            Assert.NotNull(nals);
            Assert.Empty(nals);
        }

        /// <summary>
        /// A hole in the middle of a fragment run also produces a corrupt NAL, so the run
        /// must be abandoned.
        /// </summary>
        [Fact]
        public void FragmentRunWithMissingMiddleFragment_IsDiscarded()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var depacketiser = new H264Depacketiser();

            Assert.Null(depacketiser.ProcessRTPPayloadAsNals(CreateFuA(true, false, new byte[] { 0x11, 0x22 }), 100, 90000, 0, out _));

            // Sequence number 101 is lost.
            var nals = depacketiser.ProcessRTPPayloadAsNals(CreateFuA(false, true, new byte[] { 0x55 }), 102, 90000, 1, out _);

            Assert.NotNull(nals);
            Assert.Empty(nals);
        }

        /// <summary>
        /// A frame whose only NAL was discarded must not be delivered as a zero length
        /// frame, the caller has to be able to tell there is nothing to decode.
        /// </summary>
        [Fact]
        public void FrameWithNoUsableNals_ReturnsNull()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var depacketiser = new H264Depacketiser();

            Assert.Null(depacketiser.ProcessRTPPayload(CreateFuA(false, false, new byte[] { 0x33, 0x44 }), 101, 90000, 0, out _));
            Assert.Null(depacketiser.ProcessRTPPayload(CreateFuA(false, true, new byte[] { 0x55 }), 102, 90000, 1, out _));
        }

        /// <summary>
        /// A NAL that was lost must not poison the fragment run that follows it in the next
        /// frame.
        /// </summary>
        [Fact]
        public void FragmentRunAfterDiscardedRun_ReassemblesNal()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var depacketiser = new H264Depacketiser();

            // First frame, start fragment lost.
            depacketiser.ProcessRTPPayloadAsNals(CreateFuA(false, false, new byte[] { 0x33 }), 101, 90000, 0, out _);
            depacketiser.ProcessRTPPayloadAsNals(CreateFuA(false, true, new byte[] { 0x44 }), 102, 90000, 1, out _);

            // Second frame, complete.
            depacketiser.ProcessRTPPayloadAsNals(CreateFuA(true, false, new byte[] { 0xAA }), 103, 93000, 0, out _);
            var nals = depacketiser.ProcessRTPPayloadAsNals(CreateFuA(false, true, new byte[] { 0xBB }), 104, 93000, 1, out _);

            Assert.NotNull(nals);
            var nal = Assert.Single(nals);
            Assert.Equal(IDR_NAL_TYPE, nal[0] & 0x1F);
            Assert.Equal(new byte[] { 0xAA, 0xBB }, nal.Skip(1).ToArray());
        }

        /// <summary>
        /// An access unit of SPS, PPS and an IDR slice is the canonical opening key frame and
        /// must be reported as one. The nal_unit_type constants used to have IDR and non-IDR
        /// the wrong way round, so the IDR slice was read as proof the frame was not a key
        /// frame and this returned false.
        /// </summary>
        [Fact]
        public void SpsPpsAndIdrSlice_IsKeyFrame()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var depacketiser = new H264Depacketiser();

            depacketiser.ProcessRTPPayloadAsNals(CreateSingleNal(SPS_NAL_TYPE, 0x11, 0x22), 100, 90000, 0, out _);
            depacketiser.ProcessRTPPayloadAsNals(CreateSingleNal(PPS_NAL_TYPE, 0x33), 101, 90000, 0, out _);
            var nals = depacketiser.ProcessRTPPayloadAsNals(CreateSingleNal(IDR_NAL_TYPE, 0x44), 102, 90000, 1, out bool isKeyFrame);

            Assert.Equal(3, nals.Count);
            Assert.True(isKeyFrame);
        }

        /// <summary>
        /// A mid stream IDR with no parameter sets in front of it is still a key frame, it is
        /// decodable without reference to anything earlier.
        /// </summary>
        [Fact]
        public void IdrSliceOnly_IsKeyFrame()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var depacketiser = new H264Depacketiser();

            depacketiser.ProcessRTPPayloadAsNals(CreateSingleNal(IDR_NAL_TYPE, 0x11), 100, 90000, 1, out bool isKeyFrame);

            Assert.True(isKeyFrame);
        }

        /// <summary>
        /// A fragmented IDR slice must be reported the same way as an unfragmented one.
        /// </summary>
        [Fact]
        public void FragmentedIdrSlice_IsKeyFrame()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var depacketiser = new H264Depacketiser();

            depacketiser.ProcessRTPPayloadAsNals(CreateFuA(true, false, new byte[] { 0x11 }), 100, 90000, 0, out _);
            depacketiser.ProcessRTPPayloadAsNals(CreateFuA(false, true, new byte[] { 0x22 }), 101, 90000, 1, out bool isKeyFrame);

            Assert.True(isKeyFrame);
        }

        /// <summary>
        /// The aggregated form of an opening key frame must be reported the same way.
        /// </summary>
        [Fact]
        public void StapAWithSpsPpsAndIdrSlice_IsKeyFrame()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var depacketiser = new H264Depacketiser();

            var stapA = CreateStapA(
                CreateSingleNal(SPS_NAL_TYPE, 0x11, 0x22),
                CreateSingleNal(PPS_NAL_TYPE, 0x33),
                CreateSingleNal(IDR_NAL_TYPE, 0x44));

            var nals = depacketiser.ProcessRTPPayloadAsNals(stapA, 100, 90000, 1, out bool isKeyFrame);

            Assert.Equal(3, nals.Count);
            Assert.True(isKeyFrame);
        }

        /// <summary>
        /// A coded slice of a non-IDR picture needs an earlier frame to decode, so it is not a
        /// key frame.
        /// </summary>
        [Fact]
        public void NonIdrSliceOnly_IsNotKeyFrame()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var depacketiser = new H264Depacketiser();

            depacketiser.ProcessRTPPayloadAsNals(CreateSingleNal(NON_IDR_NAL_TYPE, 0x11), 100, 90000, 1, out bool isKeyFrame);

            Assert.False(isKeyFrame);
        }

        /// <summary>
        /// A non-IDR slice outranks parameter sets appearing in the same access unit, whichever
        /// order they are seen in. Repeating SPS/PPS ahead of a predicted frame does not make
        /// that frame a key frame.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NonIdrSliceWithParameterSets_IsNotKeyFrame(bool sliceFirst)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var depacketiser = new H264Depacketiser();

            var slice = CreateSingleNal(NON_IDR_NAL_TYPE, 0x44);
            var sps = CreateSingleNal(SPS_NAL_TYPE, 0x11, 0x22);
            var pps = CreateSingleNal(PPS_NAL_TYPE, 0x33);

            var first = sliceFirst ? slice : sps;
            var second = sliceFirst ? sps : pps;
            var last = sliceFirst ? pps : slice;

            depacketiser.ProcessRTPPayloadAsNals(first, 100, 90000, 0, out _);
            depacketiser.ProcessRTPPayloadAsNals(second, 101, 90000, 0, out _);
            depacketiser.ProcessRTPPayloadAsNals(last, 102, 90000, 1, out bool isKeyFrame);

            Assert.False(isKeyFrame);
        }

        /// <summary>
        /// Fragments that arrive out of order are sorted on the marker bit, so the run is
        /// still contiguous by the time it is reassembled.
        /// </summary>
        [Fact]
        public void OutOfOrderFragments_ReassembleNal()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var depacketiser = new H264Depacketiser();

            depacketiser.ProcessRTPPayloadAsNals(CreateFuA(false, false, new byte[] { 0x33 }), 101, 90000, 0, out _);
            depacketiser.ProcessRTPPayloadAsNals(CreateFuA(true, false, new byte[] { 0x11 }), 100, 90000, 0, out _);
            var nals = depacketiser.ProcessRTPPayloadAsNals(CreateFuA(false, true, new byte[] { 0x55 }), 102, 90000, 1, out _);

            Assert.NotNull(nals);
            var nal = Assert.Single(nals);
            Assert.Equal(new byte[] { 0x11, 0x33, 0x55 }, nal.Skip(1).ToArray());
        }
    }
}
