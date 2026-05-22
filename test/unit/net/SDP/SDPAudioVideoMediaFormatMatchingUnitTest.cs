//-----------------------------------------------------------------------------
// Filename: SDPAudioVideoMediaFormatMatchingUnitTest.cs
//
// Description: Direct characterization tests for the codec matching
// helpers on SDPAudioVideoMediaFormat:
//   - AreMatch              — pair match (rtpmap or well-known ID)
//   - GetCompatibleFormats  — intersection
//   - SortMediaCapability   — re-order to a priority list
//   - GetCommonRtpEventFormat — telephone-event common-format helper
//
// These functions sit underneath RTPSession.SetRemoteDescription and
// drive every codec-intersection decision the negotiation makes. Locking
// their behaviour down at the helper level is faster and more focused
// than only testing via the full session API.
//
// Category 4 in the SDP-refactor test plan. The integration-level
// counterpart is RTPSessionCodecMatchingUnitTest.
//
// History:
// 22 May 2026	Claude Code - Opus 4.7	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class SDPAudioVideoMediaFormatMatchingUnitTest
    {
        /// <summary>
        /// Two well-known formats with the same ID and name match. Sanity
        /// check for the well-known branch of AreMatch.
        /// </summary>
        [Fact]
        public void AreMatch_SamePcmu_ReturnsTrue()
        {
            var a = new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU);
            var b = new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU);

            Assert.True(SDPAudioVideoMediaFormat.AreMatch(a, b));
        }

        /// <summary>
        /// Two well-known formats with different IDs don't match, even
        /// though both are well-known.
        /// </summary>
        [Fact]
        public void AreMatch_PcmuVsPcma_ReturnsFalse()
        {
            var pcmu = new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU);
            var pcma = new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMA);

            Assert.False(SDPAudioVideoMediaFormat.AreMatch(pcmu, pcma));
        }

        /// <summary>
        /// Two dynamic-PT formats with the same rtpmap (codec name +
        /// clock rate) match even when the payload IDs differ. The rtpmap
        /// branch takes priority over ID equality for dynamic codecs.
        /// </summary>
        [Fact]
        public void AreMatch_DynamicSameRtpmapDifferentId_ReturnsTrue()
        {
            var a = new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000);
            var b = new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 97, "VP8", 90000);

            Assert.True(SDPAudioVideoMediaFormat.AreMatch(a, b));
        }

        /// <summary>
        /// Two dynamic-PT formats with the same ID but different codec
        /// names (different rtpmaps) don't match. The well-known branch
        /// only fires for IDs below the dynamic threshold (96), so the
        /// rtpmap inequality wins.
        /// </summary>
        [Fact]
        public void AreMatch_DynamicSameIdDifferentRtpmap_ReturnsFalse()
        {
            var vp8 = new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000);
            var h264 = new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "H264", 90000);

            Assert.False(SDPAudioVideoMediaFormat.AreMatch(vp8, h264));
        }

        /// <summary>
        /// Codec-name matching is case-insensitive. "opus" vs "OPUS"
        /// is the same rtpmap.
        /// </summary>
        [Fact]
        public void AreMatch_RtpmapCaseInsensitive_ReturnsTrue()
        {
            var lower = new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.audio, 111, "opus", 48000);
            var upper = new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.audio, 111, "OPUS", 48000);

            Assert.True(SDPAudioVideoMediaFormat.AreMatch(lower, upper));
        }

        /// <summary>
        /// GetCompatibleFormats returns the intersection of two lists,
        /// preserving the order of the FIRST list. Important: the order
        /// of "a" wins, not "b", because the offerer's order is the
        /// priority key.
        /// </summary>
        [Fact]
        public void GetCompatibleFormats_TwoLists_ReturnsIntersectionInFirstListOrder()
        {
            var a = new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMA),
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.G722),
            };
            var b = new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.G722),
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
            };

            var compatible = SDPAudioVideoMediaFormat.GetCompatibleFormats(a, b);

            Assert.Equal(2, compatible.Count);
            Assert.Equal("PCMU", compatible[0].Name());
            Assert.Equal("G722", compatible[1].Name());
        }

        /// <summary>
        /// GetCompatibleFormats with no overlap returns an empty list,
        /// not null. Callers can iterate safely without null-checks.
        /// </summary>
        [Fact]
        public void GetCompatibleFormats_NoOverlap_ReturnsEmptyListNotNull()
        {
            var a = new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
            };
            var b = new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMA),
            };

            var compatible = SDPAudioVideoMediaFormat.GetCompatibleFormats(a, b);

            Assert.NotNull(compatible);
            Assert.Empty(compatible);
        }

        /// <summary>
        /// GetCompatibleFormats with a null first argument returns empty
        /// (not throws). Documented in the source as "Preferable to
        /// return an empty list."
        /// </summary>
        [Fact]
        public void GetCompatibleFormats_FirstNull_ReturnsEmptyList()
        {
            var b = new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
            };

            var compatible = SDPAudioVideoMediaFormat.GetCompatibleFormats(null, b);

            Assert.NotNull(compatible);
            Assert.Empty(compatible);
        }

        /// <summary>
        /// GetCompatibleFormats with a null second argument also returns
        /// empty (mirror of the previous test).
        /// </summary>
        [Fact]
        public void GetCompatibleFormats_SecondNull_ReturnsEmptyList()
        {
            var a = new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
            };

            var compatible = SDPAudioVideoMediaFormat.GetCompatibleFormats(a, null);

            Assert.NotNull(compatible);
            Assert.Empty(compatible);
        }

        /// <summary>
        /// SortMediaCapability reorders the capability list to match the
        /// priority-order list's IDs. PriorityOrder is the offerer's
        /// preference; this is how the answerer respects the offerer's
        /// codec priority.
        /// </summary>
        [Fact]
        public void SortMediaCapability_ReordersByPriorityListIds()
        {
            // capabilities are in PCMA-first order
            var capabilities = new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMA),
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
            };
            // offerer prefers PCMU first
            var priority = new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMA),
            };

            SDPAudioVideoMediaFormat.SortMediaCapability(capabilities, priority);

            Assert.Equal("PCMU", capabilities[0].Name());
            Assert.Equal("PCMA", capabilities[1].Name());
        }

        /// <summary>
        /// SortMediaCapability puts items NOT in the priority list at
        /// the END of the resulting capabilities. The unknowns get
        /// int.MaxValue as their sort key (per the source).
        /// </summary>
        [Fact]
        public void SortMediaCapability_ItemsNotInPriorityList_GoToEnd()
        {
            var capabilities = new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.G722),
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMA),
            };
            var priority = new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMA),
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
            };

            SDPAudioVideoMediaFormat.SortMediaCapability(capabilities, priority);

            // PCMA, PCMU first (in priority order), then G722 last.
            Assert.Equal("PCMA", capabilities[0].Name());
            Assert.Equal("PCMU", capabilities[1].Name());
            Assert.Equal("G722", capabilities[2].Name());
        }

        /// <summary>
        /// SortMediaCapability with a null priority list is a no-op —
        /// the capabilities order is preserved.
        /// </summary>
        [Fact]
        public void SortMediaCapability_NullPriority_IsNoOp()
        {
            var capabilities = new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMA),
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
            };

            SDPAudioVideoMediaFormat.SortMediaCapability(capabilities, null);

            Assert.Equal("PCMA", capabilities[0].Name());
            Assert.Equal("PCMU", capabilities[1].Name());
        }

        /// <summary>
        /// GetCommonRtpEventFormat returns the FIRST list's
        /// telephone-event when both lists carry a telephone-event entry.
        /// "If using different format ID's choose the first one" per the
        /// source comment.
        /// </summary>
        [Fact]
        public void GetCommonRtpEventFormat_BothHaveTelephoneEvent_ReturnsFirstListsEntry()
        {
            var a = new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
                new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.audio, 101, "telephone-event", 8000),
            };
            var b = new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
                new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.audio, 100, "telephone-event", 8000),
            };

            var common = SDPAudioVideoMediaFormat.GetCommonRtpEventFormat(a, b);

            Assert.False(common.IsEmpty());
            Assert.Equal(101, common.ID);
            Assert.Equal("telephone-event", common.Name());
        }

        /// <summary>
        /// GetCommonRtpEventFormat returns Empty when the first list has
        /// no telephone-event entry.
        /// </summary>
        [Fact]
        public void GetCommonRtpEventFormat_FirstMissingTelephoneEvent_ReturnsEmpty()
        {
            var a = new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
            };
            var b = new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
                new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.audio, 101, "telephone-event", 8000),
            };

            var common = SDPAudioVideoMediaFormat.GetCommonRtpEventFormat(a, b);

            Assert.True(common.IsEmpty());
        }

        /// <summary>
        /// GetCommonRtpEventFormat returns Empty when the second list has
        /// no telephone-event entry (mirror).
        /// </summary>
        [Fact]
        public void GetCommonRtpEventFormat_SecondMissingTelephoneEvent_ReturnsEmpty()
        {
            var a = new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
                new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.audio, 101, "telephone-event", 8000),
            };
            var b = new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
            };

            var common = SDPAudioVideoMediaFormat.GetCommonRtpEventFormat(a, b);

            Assert.True(common.IsEmpty());
        }

        /// <summary>
        /// GetCommonRtpEventFormat handles a null/empty input gracefully —
        /// returns Empty rather than throwing.
        /// </summary>
        [Fact]
        public void GetCommonRtpEventFormat_NullOrEmpty_ReturnsEmpty()
        {
            var withEvent = new List<SDPAudioVideoMediaFormat>
            {
                new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.audio, 101, "telephone-event", 8000),
            };

            Assert.True(SDPAudioVideoMediaFormat.GetCommonRtpEventFormat(null, withEvent).IsEmpty());
            Assert.True(SDPAudioVideoMediaFormat.GetCommonRtpEventFormat(withEvent, null).IsEmpty());
            Assert.True(SDPAudioVideoMediaFormat.GetCommonRtpEventFormat(
                new List<SDPAudioVideoMediaFormat>(), withEvent).IsEmpty());
            Assert.True(SDPAudioVideoMediaFormat.GetCommonRtpEventFormat(
                withEvent, new List<SDPAudioVideoMediaFormat>()).IsEmpty());
        }
    }
}
