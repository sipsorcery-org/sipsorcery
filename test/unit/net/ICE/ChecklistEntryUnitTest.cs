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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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

        private static ChecklistEntry Entry() =>
            new ChecklistEntry(Candidate(100), Candidate(200), isLocalController: true);

        private static IPEndPoint RemoteEp() => new IPEndPoint(IPAddress.Loopback, 3478);

        private static STUNMessage Response(STUNMessageTypesEnum type) => new STUNMessage(type);

        // --- RequestTransactionID cache / IsTransactionIDMatch ---------------------------------------

        /// <summary>
        /// Setting RequestTransactionID pushes the id onto the front of the cache; the getter returns the
        /// most recent id and IsTransactionIDMatch recognises both the current and a previously cached id but
        /// not an unknown one. This pins the matching used to correlate STUN responses to in-flight checks.
        /// </summary>
        [Fact]
        public void RequestTransactionID_CachesAndMatchesPreviousIds()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var entry = Entry();
            entry.RequestTransactionID = "txn-first-000";
            entry.RequestTransactionID = "txn-second-00";

            Assert.Equal("txn-second-00", entry.RequestTransactionID); // most recent.
            Assert.True(entry.IsTransactionIDMatch("txn-second-00"));  // current.
            Assert.True(entry.IsTransactionIDMatch("txn-first-000"));  // previously cached.
            Assert.False(entry.IsTransactionIDMatch("never-set-000")); // unknown.
        }

        /// <summary>
        /// Setting RequestTransactionID to the value already at the front of the cache is a no-op (it is not
        /// duplicated), so repeated identical assignments do not grow or churn the cache.
        /// </summary>
        [Fact]
        public void RequestTransactionID_SettingSameValueDoesNotDuplicate()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var entry = Entry();
            entry.RequestTransactionID = "same-value-00";
            entry.RequestTransactionID = "same-value-00";

            // Only one cached id, so the prior id is unknown and only the single value matches.
            Assert.True(entry.IsTransactionIDMatch("same-value-00"));
            Assert.Equal("same-value-00", entry.RequestTransactionID);
        }

        // --- GotStunResponse state transitions ------------------------------------------------------

        /// <summary>
        /// A binding success response for a non-nominated pair moves the entry to Succeeded and resets the
        /// outstanding check counter.
        /// </summary>
        [Fact]
        public void GotStunResponse_BindingSuccess_NotNominated_SetsSucceeded()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var entry = Entry();
            entry.State = ChecklistEntryState.InProgress;
            entry.ChecksSent = 3;

            entry.GotStunResponse(Response(STUNMessageTypesEnum.BindingSuccessResponse), RemoteEp());

            Assert.Equal(ChecklistEntryState.Succeeded, entry.State);
            Assert.Equal(0, entry.ChecksSent);
        }

        /// <summary>
        /// A binding success response for an already-nominated pair is treated as a keep-alive: it records the
        /// connected-response time and rotates the request transaction id rather than changing state.
        /// </summary>
        [Fact]
        public void GotStunResponse_BindingSuccess_Nominated_UpdatesKeepAlive()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var entry = Entry();
            entry.Nominated = true;
            entry.State = ChecklistEntryState.Succeeded;
            entry.RequestTransactionID = "original-txn";

            entry.GotStunResponse(Response(STUNMessageTypesEnum.BindingSuccessResponse), RemoteEp());

            Assert.Equal(ChecklistEntryState.Succeeded, entry.State); // unchanged.
            Assert.True(entry.LastConnectedResponseAt > DateTime.MinValue);
            Assert.NotEqual("original-txn", entry.RequestTransactionID); // rotated.
        }

        /// <summary>
        /// A binding error response marks the pair Failed.
        /// </summary>
        [Fact]
        public void GotStunResponse_BindingError_SetsFailed()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var entry = Entry();
            entry.State = ChecklistEntryState.InProgress;

            entry.GotStunResponse(Response(STUNMessageTypesEnum.BindingErrorResponse), RemoteEp());

            Assert.Equal(ChecklistEntryState.Failed, entry.State);
        }

        /// <summary>
        /// A TURN Create Permission success response records that permission was granted and, if the entry was
        /// InProgress, moves it back to Waiting and clears the first-check timestamp so the binding check is
        /// re-sent now that the relay permission exists.
        /// </summary>
        [Fact]
        public void GotStunResponse_CreatePermissionSuccess_InProgress_MovesToWaiting()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var entry = Entry();
            entry.State = ChecklistEntryState.InProgress;
            entry.FirstCheckSentAt = DateTime.Now;

            entry.GotStunResponse(Response(STUNMessageTypesEnum.CreatePermissionSuccessResponse), RemoteEp());

            Assert.Equal(1, entry.TurnPermissionsRequestSent);
            Assert.True(entry.TurnPermissionsResponseAt > DateTime.MinValue);
            Assert.Equal(ChecklistEntryState.Waiting, entry.State);
            Assert.Equal(DateTime.MinValue, entry.FirstCheckSentAt);
        }

        /// <summary>
        /// A TURN Create Permission error response (with no auth error code triggering a retry) marks the pair
        /// Failed.
        /// </summary>
        [Fact]
        public void GotStunResponse_CreatePermissionError_SetsFailed()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var entry = Entry();
            entry.State = ChecklistEntryState.InProgress;

            entry.GotStunResponse(Response(STUNMessageTypesEnum.CreatePermissionErrorResponse), RemoteEp());

            Assert.Equal(ChecklistEntryState.Failed, entry.State);
        }

        /// <summary>
        /// A TURN Refresh success response carrying a LIFETIME attribute extends the relay's time-to-expiry on
        /// the associated ICE server by the advertised lifetime.
        /// </summary>
        [Fact]
        public void GotStunResponse_RefreshSuccess_WithLifetime_SetsTurnExpiry()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            STUNUri.TryParse("turn:turn.example.com:3478", out var uri);
            var iceServer = new IceServer(uri, 1, "user", "pass");
            var localCandidate = new RTCIceCandidate(new RTCIceCandidateInit()) { priority = 100, IceServer = iceServer };
            var entry = new ChecklistEntry(localCandidate, Candidate(200), isLocalController: true);

            var resp = Response(STUNMessageTypesEnum.RefreshSuccessResponse);
            byte[] lifetime = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(lifetime, 600);
            resp.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Lifetime, lifetime));

            var before = DateTime.Now;
            entry.GotStunResponse(resp, RemoteEp());

            // Expiry should be roughly now + 600s.
            Assert.True(iceServer.TurnTimeToExpiry > before.AddSeconds(590));
            Assert.True(iceServer.TurnTimeToExpiry < before.AddSeconds(610));
        }

        /// <summary>
        /// An unexpected/unhandled STUN response type leaves the entry state untouched (logged and ignored).
        /// </summary>
        [Fact]
        public void GotStunResponse_UnexpectedType_NoStateChange()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var entry = Entry();
            entry.State = ChecklistEntryState.Waiting;

            entry.GotStunResponse(Response(STUNMessageTypesEnum.BindingRequest), RemoteEp());

            Assert.Equal(ChecklistEntryState.Waiting, entry.State);
        }
    }
}
