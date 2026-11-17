//-----------------------------------------------------------------------------
// Filename: IceCandidatePairStats.cs
//
// Description: A read-only copy of an ICE checklist entry, for diagnostics.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Nov 2026	Aaron Clauson	Created, Dublin, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.Net
{
    /// <summary>
    /// One candidate pair of the ICE checklist as it was when the copy was made
    /// (<see cref="RtpIceChannel.GetChecklistSnapshot"/>). It does not change after: a later
    /// snapshot is needed to see the pair move on.
    /// </summary>
    public sealed class IceCandidatePairStats
    {
        /// <summary>Our candidate.</summary>
        public RTCIceCandidate LocalCandidate { get; }

        /// <summary>The remote peer's candidate.</summary>
        public RTCIceCandidate RemoteCandidate { get; }

        /// <summary>Where the pair's checks have got to: frozen, waiting, in progress, succeeded or failed.</summary>
        public ChecklistEntryState State { get; }

        /// <summary>True if this is the pair in use (the nominated entry).</summary>
        public bool Selected { get; }

        /// <summary>True if the pair has been nominated, by us as the controlling peer.</summary>
        public bool Nominated { get; }

        /// <summary>True if the remote peer nominated the pair (a check with USE-CANDIDATE).</summary>
        public bool RemoteNominated { get; }

        /// <summary>The pair's priority, which orders the checklist.</summary>
        public ulong Priority { get; }

        /// <summary>Every connectivity check we have sent on the pair.</summary>
        public int ChecksSent { get; }

        /// <summary>The success responses to our checks on the pair.</summary>
        public int ResponsesReceived { get; }

        /// <summary>The checks the remote peer has sent on the pair.</summary>
        public int RequestsReceived { get; }

        /// <summary>The round trip time of the latest of our checks to be answered, or null if none has been.</summary>
        public TimeSpan? RoundTripTime { get; }

        /// <summary>When our first check on the pair was sent, or null if none has been.</summary>
        public DateTime? FirstCheckSentAt { get; }

        /// <summary>When a check of ours on the pair was last answered, or null if none has been.</summary>
        public DateTime? LastResponseAt { get; }

        /// <summary>When the remote peer last sent a check on the pair, or null if it hasn't.</summary>
        public DateTime? LastRequestReceivedAt { get; }

        internal IceCandidatePairStats(ChecklistEntry entry, bool selected)
        {
            LocalCandidate = entry.LocalCandidate;
            RemoteCandidate = entry.RemoteCandidate;
            State = entry.State;
            Selected = selected;
            Nominated = entry.Nominated;
            RemoteNominated = entry.RemoteNominated;
            Priority = entry.Priority;
            ChecksSent = entry.TotalChecksSent;
            ResponsesReceived = entry.ResponsesReceived;
            RequestsReceived = entry.BindingRequestsReceived;
            RoundTripTime = entry.RoundTripTime;
            FirstCheckSentAt = Time(entry.FirstCheckSentAt);
            LastResponseAt = Time(entry.LastResponseAt);
            LastRequestReceivedAt = Time(entry.LastBindingRequestReceivedAt);
        }

        private static DateTime? Time(DateTime t) => t == DateTime.MinValue || t == default ? (DateTime?)null : t;

        public override string ToString() =>
            $"{LocalCandidate?.type} {LocalCandidate?.address}:{LocalCandidate?.port} -> {RemoteCandidate?.type} {RemoteCandidate?.address}:{RemoteCandidate?.port} " +
            $"{State}{(Selected ? " (selected)" : "")}, checks {ChecksSent} sent, {ResponsesReceived} answered, {RequestsReceived} received" +
            (RoundTripTime.HasValue ? $", rtt {RoundTripTime.Value.TotalMilliseconds:0.#} ms" : "");
    }
}
