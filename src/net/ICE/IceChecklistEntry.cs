//-----------------------------------------------------------------------------
// Filename: IceChecklistEntry.cs
//
// Description: Represents an entry that gets added to an ICE session checklist.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 23 Jun 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.Net
{
    /// <summary>
    /// List of state conditions for a check list entry as the connectivity checks are 
    /// carried out.
    /// </summary>
    internal enum ChecklistEntryState
    {
        /// <summary>
        /// A check has not been sent for this pair, but the pair is not Frozen.
        /// </summary>
        Waiting,

        /// <summary>
        /// A check has been sent for this pair, but the transaction is in progress.
        /// </summary>
        InProgress,

        /// <summary>
        /// A check has been sent for this pair, and it produced a successful result.
        /// </summary>
        Succeeded,

        /// <summary>
        /// A check has been sent for this pair, and it failed (a response to the 
        /// check was never received, or a failure response was received).
        /// </summary>
        Failed,

        /// <summary>
        /// A check for this pair has not been sent, and it cannot be sent until the 
        /// pair is unfrozen and moved into the Waiting state.
        /// </summary>
        Frozen
    }

    /// <summary>
    /// Represents the state of the ICE checks for a checklist.
    /// </summary>
    /// <remarks>
    /// As specified in https://tools.ietf.org/html/rfc8445#section-6.1.2.1.
    /// </remarks>
    internal enum ChecklistState
    {
        /// <summary>
        /// The checklist is neither Completed nor Failed yet.
        /// Checklists are initially set to the Running state.
        /// </summary>
        Running,

        /// <summary>
        /// The checklist contains a nominated pair for each
        /// component of the data stream.
        /// </summary>
        Completed,

        /// <summary>
        /// The checklist does not have a valid pair for each component
        /// of the data stream, and all of the candidate pairs in the
        /// checklist are in either the Failed or the Succeeded state.  In
        /// other words, at least one component of the checklist has candidate
        /// pairs that are all in the Failed state, which means the component
        /// has failed, which means the checklist has failed.
        /// </summary>
        Failed
    }

    /// <summary>
    /// A check list entry represents an ICE candidate pair (local candidate + remote candidate)
    /// that is being checked for connectivity. If the overall ICE session does succeed it will
    /// be due to one of these checklist entries successfully completing the ICE checks.
    /// </summary>
    internal class ChecklistEntry : IComparable
    {
        public RTCIceCandidate LocalCandidate;
        public RTCIceCandidate RemoteCandidate;

        /// <summary>
        /// The current state of this checklist entry. Indicates whether a STUN check has been
        /// sent, responded to, timed out etc.
        /// </summary>
        /// <remarks>
        /// See https://tools.ietf.org/html/rfc8445#section-6.1.2.6 for the state
        /// transition diagram for a check list entry.
        /// </remarks>
        public ChecklistEntryState State = ChecklistEntryState.Frozen;

        /// <summary>
        /// The candidate pairs whose local and remote candidates are both the
        /// default candidates for a particular component is called the "default
        /// candidate pair" for that component.  This is the pair that would be
        /// used to transmit data if both agents had not been ICE aware.
        /// </summary>
        public bool Default;

        /// <summary>
        /// Gets set to true when the connectivity checks for the candidate pair are
        /// successful. Valid entries are eligible to be set as nominated.
        /// </summary>
        public bool Valid;

        /// <summary>
        /// Gets set to true if this entry is selected as the single nominated entry to be
        /// used for the session communications. Setting a check list entry as nominated
        /// indicates the ICE checks have been successful and the application can begin
        /// normal communications.
        /// </summary>
        public bool Nominated;

        /// <summary>
        /// The priority for the candidate pair:
        ///  - Let G be the priority for the candidate provided by the controlling agent.
        ///  - Let D be the priority for the candidate provided by the controlled agent.
        /// Pair Priority = 2^32*MIN(G,D) + 2*MAX(G,D) + (G>D?1:0)
        /// </summary>
        /// <remarks>
        /// See https://tools.ietf.org/html/rfc8445#section-6.1.2.3.
        /// </remarks>
        public ulong Priority { get; private set; }

        /// <summary>
        /// Timestamp the last connectivity check (STUN binding request) was sent at.
        /// </summary>
        public DateTime LastCheckSentAt = DateTime.MinValue;

        /// <summary>
        /// The number of checks that have been sent without a response.
        /// </summary>
        public int ChecksSent;

        /// <summary>
        /// The transaction ID that was set in the last STUN request connectivity check.
        /// </summary>
        public string RequestTransactionID;

        /// <summary>
        /// Before a remote peer will be able to use the relay it's IP address needs
        /// to be authorised by sending a Create Permissions request to the TURN server.
        /// This field records the number of Create Permissions requests that have been
        /// sent for this entry.
        /// </summary>
        public int TurnPermissionsRequestSent { get; set; } = 0;

        /// <summary>
        /// This field records the time a Create Permissions response was received.
        /// </summary>
        public DateTime TurnPermissionsResponseAt { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Creates a new entry for the ICE session checklist.
        /// </summary>
        /// <param name="localCandidate">The local candidate for the checklist pair.</param>
        /// <param name="remoteCandidate">The remote candidate for the checklist pair.</param>
        /// <param name="isLocalController">True if we are acting as the controlling agent in the ICE session.</param>
        public ChecklistEntry(RTCIceCandidate localCandidate, RTCIceCandidate remoteCandidate, bool isLocalController)
        {
            LocalCandidate = localCandidate;
            RemoteCandidate = remoteCandidate;

            var controllingCandidate = (isLocalController) ? localCandidate : remoteCandidate;
            var controlledCandidate = (isLocalController) ? remoteCandidate : localCandidate;

            Priority = (2 << 32) * Math.Min(controllingCandidate.priority, controlledCandidate.priority) +
                (ulong)2 * Math.Max(controllingCandidate.priority, controlledCandidate.priority) +
                (ulong)((controllingCandidate.priority > controlledCandidate.priority) ? 1 : 0);
        }

        /// <summary>
        /// Compare method to allow the checklist to be sorted in priority order.
        /// </summary>
        public int CompareTo(Object other)
        {
            if (other is ChecklistEntry)
            {
                //return Priority.CompareTo((other as ChecklistEntry).Priority);
                return (other as ChecklistEntry).Priority.CompareTo(Priority);
            }
            else
            {
                throw new ApplicationException("CompareTo is not implemented for ChecklistEntry and arbitrary types.");
            }
        }
    }
}
