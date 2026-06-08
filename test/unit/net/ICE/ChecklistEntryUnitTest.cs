//-----------------------------------------------------------------------------
// Filename: ChecklistEntryUnitTest.cs
//
// Description: Characterization tests for ChecklistEntry, the ICE candidate-pair
// abstraction. These pin the current RFC 8445 section 6.1.2.3 pair-priority
// formula and the priority-descending sort order so that an upcoming refactor of
// the ICE checklist logic out of RtpIceChannel cannot silently change candidate
// pair ordering (which determines the order connectivity checks are attempted and
// therefore which path a session selects).
//
// These tests are deliberately network-free: ChecklistEntry is public and its
// priority is a pure function of the local/remote candidate priorities and the
// controlling role.
//
// Author(s):
// Aaron Clauson
//
// History:
// 08 Jun 2026	Aaron Clauson	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class ChecklistEntryUnitTest
    {
        private const ulong TWO_POW_32 = 4294967296UL; // 2^32, the multiplier on MIN(G,D) in the pair priority.

        private readonly Microsoft.Extensions.Logging.ILogger logger = null;

        public ChecklistEntryUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        private static RTCIceCandidate Candidate(uint priority)
        {
            // The pair priority only depends on the candidate priority, so the other fields are irrelevant.
            return new RTCIceCandidate(new RTCIceCandidateInit()) { priority = priority };
        }

        /// <summary>
        /// Pins the RFC 8445 section 6.1.2.3 pair priority for the controlling agent:
        /// 2^32*MIN(G,D) + 2*MAX(G,D) + (G&gt;D ? 1 : 0), where G is the controlling (local, here) priority.
        /// </summary>
        [Fact]
        public void PairPriorityForControllerMatchesRfc8445Formula()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            // Local (G) lower than remote (D): tiebreak bit (G>D) is 0.
            var entry = new ChecklistEntry(Candidate(100), Candidate(200), isLocalController: true);
            Assert.Equal(TWO_POW_32 * 100 + 2 * 200 + 0, entry.Priority);

            // Local (G) higher than remote (D): tiebreak bit (G>D) is 1.
            var entry2 = new ChecklistEntry(Candidate(200), Candidate(100), isLocalController: true);
            Assert.Equal(TWO_POW_32 * 100 + 2 * 200 + 1, entry2.Priority);
        }

        /// <summary>
        /// Pins the pair priority for the controlled agent: the tiebreak bit is (D&gt;G), i.e. it depends on
        /// the remote (controlling) priority being the larger one.
        /// </summary>
        [Fact]
        public void PairPriorityForControlledMatchesRfc8445Formula()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            // Controlled agent, remote (G) higher than local (D): tiebreak bit (D... remote>local) is 1.
            var entry = new ChecklistEntry(Candidate(100), Candidate(200), isLocalController: false);
            Assert.Equal(TWO_POW_32 * 100 + 2 * 200 + 1, entry.Priority);

            // Controlled agent, remote lower than local: tiebreak bit is 0.
            var entry2 = new ChecklistEntry(Candidate(200), Candidate(100), isLocalController: false);
            Assert.Equal(TWO_POW_32 * 100 + 2 * 200 + 0, entry2.Priority);
        }

        /// <summary>
        /// When the local and remote priorities are equal the tiebreak bit is 0 regardless of role
        /// (neither G&gt;D nor D&gt;G holds).
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void PairPriorityWithEqualPrioritiesHasNoTiebreak(bool isLocalController)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var entry = new ChecklistEntry(Candidate(150), Candidate(150), isLocalController);
            Assert.Equal(TWO_POW_32 * 150 + 2 * 150 + 0, entry.Priority);
        }

        /// <summary>
        /// The same pair has the same priority whichever way round the (local, remote) priorities are
        /// supplied, apart from the single tiebreak bit. This is the property that makes both agents agree
        /// on the ordering.
        /// </summary>
        [Fact]
        public void PairPriorityIsSymmetricApartFromTiebreak()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var ab = new ChecklistEntry(Candidate(100), Candidate(200), isLocalController: true);
            var ba = new ChecklistEntry(Candidate(200), Candidate(100), isLocalController: true);

            // Differ only by the tiebreak bit (1) since one direction has G>D and the other does not.
            Assert.Equal(1UL, Math.Max(ab.Priority, ba.Priority) - Math.Min(ab.Priority, ba.Priority));
        }

        /// <summary>
        /// Pins the sort order used to drive connectivity checks: ChecklistEntry.CompareTo sorts entries in
        /// DESCENDING priority order (highest priority pair first), so List.Sort() puts the most preferred
        /// pair at index 0.
        /// </summary>
        [Fact]
        public void SortOrdersByDescendingPriority()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var low = new ChecklistEntry(Candidate(10), Candidate(10), isLocalController: true);
            var mid = new ChecklistEntry(Candidate(100), Candidate(100), isLocalController: true);
            var high = new ChecklistEntry(Candidate(1000), Candidate(1000), isLocalController: true);

            var list = new List<ChecklistEntry> { low, high, mid };
            list.Sort();

            Assert.Equal(new[] { high.Priority, mid.Priority, low.Priority }, list.Select(e => e.Priority).ToArray());
            Assert.True(list[0].Priority > list[1].Priority && list[1].Priority > list[2].Priority);
        }

        /// <summary>
        /// The constructor records the supplied local/remote priorities and controlling role verbatim.
        /// </summary>
        [Fact]
        public void ConstructorRecordsPrioritiesAndRole()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var entry = new ChecklistEntry(Candidate(42), Candidate(99), isLocalController: true);

            Assert.Equal(42u, entry.LocalPriority);
            Assert.Equal(99u, entry.RemotePriority);
            Assert.True(entry.IsLocalController);
            Assert.Equal(ChecklistEntryState.Frozen, entry.State); // default initial state.
        }
    }
}
