//-----------------------------------------------------------------------------
// Filename: IceSession.cs
//
// Description: Represents a ICE Session as described in the Interactive
// Connectivity Establishment RFC8445 https://tools.ietf.org/html/rfc8445.
//
// Additionally support for the following standards or proposed standards 
// is included:
// - "Trickle ICE" as per draft RFC
//    https://tools.ietf.org/html/draft-ietf-ice-trickle-21.
// - "WebRTC IP Address Handling Requirements" as per draft RFC
//   https://tools.ietf.org/html/draft-ietf-rtcweb-ip-handling-12
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 15 Mar 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

[assembly: InternalsVisibleToAttribute("SIPSorcery.UnitTests")]

namespace SIPSorcery.Net
{
    /// <summary>
    /// An ICE session carries out connectivity checks with a remote peer in an
    /// attempt to determine the best destination end point to communicate with the
    /// remote party.
    /// </summary>
    /// <remarks>
    /// Limitations:
    ///  - To reduce complexity only a single checklist is used. This is based on the main 
    ///    webrtc use case where RTP (audio and video) and RTCP are all multiplexed on a 
    ///    single socket pair. Therefore  there only needs to be a single component and single 
    ///    data stream. If an additional use case occurs then multiple checklists could be added.
    /// </remarks>
    public class IceSession
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

        private const int ICE_UFRAG_LENGTH = 4;
        private const int ICE_PASSWORD_LENGTH = 24;
        private const int MAX_CHECKLIST_ENTRIES = 25;   // Maximum number of entries that can be added to the checklist of candidate pairs.

        /// <summary>
        /// ICE transaction spacing interval in milliseconds.
        /// </summary>
        /// <remarks>
        /// See https://tools.ietf.org/html/rfc8445#section-14.
        /// </remarks>
        private const int Ta = 50;

        /// <summary>
        /// The number of connectivity checks to carry out.
        /// </summary>
        private const int N = 5;

        private static readonly ILogger logger = Log.Logger;

        private RTPChannel _rtpChannel;

        public RTCIceComponent Component { get; private set; }

        public RTCIceGatheringState GatheringState { get; private set; } = RTCIceGatheringState.@new;

        public RTCIceConnectionState ConnectionState { get; private set; } = RTCIceConnectionState.@new;

        /// <summary>
        /// True if we are the "controlling" ICE agent (we initiated the communications) or
        /// false if we are the "controlled" agent.
        /// </summary>
        public bool IsController { get; internal set; }

        /// <summary>
        /// The list of host ICE candidates that have been gathered for this peer.
        /// </summary>
        public List<RTCIceCandidate> Candidates
        {
            get
            {
                if (_candidates == null)
                {
                    _candidates = GetHostCandidates();
                }

                return _candidates;
            }
        }

        private List<RTCIceCandidate> _candidates;
        private List<RTCIceCandidate> _remoteCandidates = new List<RTCIceCandidate>();

        /// <summary>
        /// The state of the checklist as the ICE checks are carried out.
        /// </summary>
        internal ChecklistState _checklistState = ChecklistState.Running;

        /// <summary>
        /// The checklist of local and remote candidate pairs
        /// </summary>
        internal List<ChecklistEntry> _checklist = new List<ChecklistEntry>();

        /// <summary>
        /// For local candidates this implementation takes a shortcut to reduce complexity. 
        /// The RTP socket will always be bound to one of:
        ///  - IPAddress.IPv6Any [::], 
        ///  - IPAddress.Any 0.0.0.0, or,
        ///  - a specific single IP address. 
        /// As such it's only necessary to create a single checklist entry for each remote 
        /// candidate. 
        /// Real host candidates must still be generated based on all local IP addresses. Those
        /// local candidates need to be transmitted to the remote peer but they don't need to
        /// be used when populating the checklist.
        /// </summary>
        internal readonly RTCIceCandidate _localChecklistCandidate;

        /// <summary>
        /// If the connectivity checks are successful this will hold the nominated
        /// remote candidate.
        /// </summary>
        public RTCIceCandidate NominatedCandidate { get; private set; }

        /// <summary>
        /// If the session has successfully connected this returns the remote ned point of
        /// the nominate candidate.
        /// </summary>
        public IPEndPoint ConnectedRemoteEndPoint
        {
            get { return (NominatedCandidate != null) ? NominatedCandidate.GetEndPoint() : null; }
        }

        /// <summary>
        /// Retransmission timer for STUN transactions, measured in milliseconds.
        /// </summary>
        /// <remarks>
        /// As specified in https://tools.ietf.org/html/rfc8445#section-14.
        /// </remarks>
        internal int RTO
        {
            get
            {
                if (GatheringState == RTCIceGatheringState.gathering)
                {
                    return Math.Max(500, Ta * Candidates.Count(x => x.type == RTCIceCandidateType.srflx || x.type == RTCIceCandidateType.relay));
                }
                else
                {
                    return Math.Max(500, Ta * N * (_checklist.Count(x => x.State == ChecklistEntryState.Waiting) + _checklist.Count(x => x.State == ChecklistEntryState.InProgress)));
                }
            }
        }

        public readonly string LocalIceUser;
        public readonly string LocalIcePassword;
        public string RemoteIceUser { get; private set; }
        public string RemoteIcePassword { get; private set; }

        private bool _closed = false;
        private Timer _stunChecksTimer;

        public event Action<RTCIceCandidate> OnIceCandidate;
        public event Action<RTCIceConnectionState> OnIceConnectionStateChange;
        public event Action<RTCIceGatheringState> OnIceGatheringStateChange;
        public event Action OnIceCandidateError;

        /// <summary>
        /// Creates a new instance of an ICE session.
        /// </summary>
        /// <param name="rtpChannel">The RTP channel is the object managing the socket
        /// doing the media sending and receiving. Its the same socket the ICE session
        /// will need to initiate all the connectivity checks on.</param>
        /// <param name="component">The component (RTP or RTCP) the channel is being used for. Note
        /// for cases where RTP and RTCP are multiplexed the component is set to RTP.</param>
        public IceSession(RTPChannel rtpChannel, RTCIceComponent component)
        {
            if (rtpChannel == null)
            {
                throw new ArgumentNullException("rtpChannel");
            }

            _rtpChannel = rtpChannel;
            Component = component;

            LocalIceUser = Crypto.GetRandomString(ICE_UFRAG_LENGTH);
            LocalIcePassword = Crypto.GetRandomString(ICE_PASSWORD_LENGTH);

            _localChecklistCandidate = new RTCIceCandidate(new RTCIceCandidateInit { sdpMid = "0", sdpMLineIndex = 0, usernameFragment = LocalIceUser });
            _localChecklistCandidate.SetAddressProperties(
                RTCIceProtocol.udp,
                _rtpChannel.RTPLocalEndPoint.Address,
                (ushort)_rtpChannel.RTPLocalEndPoint.Port,
                RTCIceCandidateType.host,
                null,
                0);
        }

        /// <summary>
        /// We've been given the green light to start the ICE candidate gathering process.
        /// This could include contacting external STUN and TURN servers. Events will 
        /// be fired as each ICE is identified and as the gathering state machine changes
        /// state.
        /// </summary>
        public void StartGathering()
        {
            GatheringState = RTCIceGatheringState.gathering;
            OnIceGatheringStateChange?.Invoke(RTCIceGatheringState.gathering);

            _stunChecksTimer = new Timer(ProcessChecklist, null, 0, Ta);
        }

        /// <summary>
        /// Set the ICE credentials that have been supplied by the remote peer. Once these
        /// are set the connectivity checks should be able to commence.
        /// </summary>
        /// <param name="username">The remote peer's ICE username.</param>
        /// <param name="password">The remote peer's ICE password.</param>
        public void SetRemoteCredentials(string username, string password)
        {
            logger.LogDebug("ICE session remote credentials set.");

            RemoteIceUser = username;
            RemoteIcePassword = password;

            // Once the remote party's ICE credentials are known connection checking can 
            // commence immediately as candidates trickle in.
            ConnectionState = RTCIceConnectionState.checking;
            OnIceConnectionStateChange?.Invoke(ConnectionState);
        }

        /// <summary>
        /// Closes the ICE session and stops any further connectivity checks.
        /// </summary>
        /// <param name="reason">Reason for the close. Informational only.</param>
        public void Close(string reason)
        {
            if (!_closed)
            {
                _closed = true;
                _stunChecksTimer.Dispose();
            }
        }

        /// <summary>
        /// Adds a remote ICE candidate to the ICE session.
        /// </summary>
        /// <param name="candidate">An ICE candidate from the remote party.</param>
        public void AddRemoteCandidate(RTCIceCandidate candidate)
        {
            if (candidate.component == Component)
            {
                // Have a remote candidate. Connectivity checks can start. Note because we support ICE trickle
                // we may also still be gathering candidates. Connectivity checks and gathering can be done in parallel.

                logger.LogDebug($"ICE session adding remote candidate: {candidate.ToString()}");

                _remoteCandidates.Add(candidate);
                UpdateChecklist(candidate);
            }
            else
            {
                // This occurs if the remote party made an offer and assumed we couldn't multiplex the audio and video streams.
                // It will offer the same ICE candidates separately for the audio and video announcements.
                logger.LogWarning($"ICE session omitting remote candidate with unsupported component: {candidate.ToString()}");
            }
        }

        /// <summary>
        /// Acquires an ICE candidate for each IP address that this host has except for:
        /// - Loopback addresses must not be included.
        /// - Deprecated IPv4-compatible IPv6 addresses and IPv6 site-local unicast addresses
        ///   must not be included,
        /// - IPv4-mapped IPv6 address should not be included.
        /// - If a non-location tracking IPv6 address is available use it and do not included 
        ///   location tracking enabled IPv6 addresses (i.e. prefer temporary IPv6 addresses over 
        ///   permanent addresses), see RFC6724.
        /// </summary>
        /// <remarks>See https://tools.ietf.org/html/rfc8445#section-5.1.1.1</remarks>
        /// <returns>A list of "host" ICE candidates for the local machine.</returns>
        private List<RTCIceCandidate> GetHostCandidates()
        {
            List<RTCIceCandidate> hostCandidates = new List<RTCIceCandidate>();
            RTCIceCandidateInit init = new RTCIceCandidateInit { usernameFragment = LocalIceUser };

            var rtpBindAddress = _rtpChannel.RTPLocalEndPoint.Address;

            // We get a list of local addresses that can be used with the address the RTP socket is bound on.
            List<IPAddress> localAddresses = null;
            if (IPAddress.IPv6Any.Equals(rtpBindAddress))
            {
                if (_rtpChannel.RtpSocket.DualMode)
                {
                    // IPv6 dual mode listening on [::] means we can use all valid local addresses.
                    localAddresses = NetServices.LocalIPAddresses.Where(x =>
                        !IPAddress.IsLoopback(x) && !x.IsIPv4MappedToIPv6 && !x.IsIPv6SiteLocal).ToList();
                }
                else
                {
                    // IPv6 but not dual mode on [::] means can use all valid local IPv6 addresses.
                    localAddresses = NetServices.LocalIPAddresses.Where(x => x.AddressFamily == AddressFamily.InterNetworkV6
                        && !IPAddress.IsLoopback(x) && !x.IsIPv4MappedToIPv6 && !x.IsIPv6SiteLocal).ToList();
                }
            }
            else if (IPAddress.Any.Equals(rtpBindAddress))
            {
                // IPv4 on 0.0.0.0 means can use all valid local IPv4 addresses.
                localAddresses = NetServices.LocalIPAddresses.Where(x => x.AddressFamily == AddressFamily.InterNetwork
                        && !IPAddress.IsLoopback(x)).ToList();
            }
            else
            {
                // If not bound on a [::] or 0.0.0.0 means we're only listening on a specific IP address
                // and that's the only one that can be used for the host candidate.
                localAddresses = new List<IPAddress> { rtpBindAddress };
            }

            foreach (var localAddress in localAddresses)
            {
                var hostCandidate = new RTCIceCandidate(init);
                hostCandidate.SetAddressProperties(RTCIceProtocol.udp, localAddress, (ushort)_rtpChannel.RTPPort, RTCIceCandidateType.host, null, 0);

                // We currently only support a single multiplexed connection for all data streams and RTCP.
                if (hostCandidate.component == RTCIceComponent.rtp && hostCandidate.sdpMLineIndex == 0)
                {
                    hostCandidates.Add(hostCandidate);

                    OnIceCandidate?.Invoke(hostCandidate);
                }
            }

            return hostCandidates;
        }

        /// <summary>
        /// Attempts to get a list of server-reflexive candidates using the local "host" candidates
        /// and a STUN or TURN server.
        /// </summary>
        /// <remarks>See https://tools.ietf.org/html/rfc8445#section-5.1.1.2</remarks>
        /// <returns></returns>
        private List<RTCIceCandidate> GetServerRelexiveCandidates()
        {
            OnIceCandidateError?.Invoke();
            throw new NotImplementedException();
        }

        /// <summary>
        /// Attempts to get a list of relay candidates from a TURN server.
        /// </summary>
        /// <remarks>See https://tools.ietf.org/html/rfc8445#section-5.1.1.2</remarks>
        /// <returns></returns>
        private List<RTCIceCandidate> GetRelayCandidates()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Updates the checklist with new candidate pairs.
        /// </summary>
        /// <remarks>
        /// From https://tools.ietf.org/html/rfc8445#section-6.1.2.2:
        /// IPv6 link-local addresses MUST NOT be paired with other than link-local addresses.
        /// </remarks>
        private void UpdateChecklist(RTCIceCandidate remoteCandidate)
        {
            lock (_checklist)
            {
                // Local server reflexive candidates don't get added to the checklist since they are just local
                // "host" candidates with an extra NAT address mapping. The NAT address mapping is needed for the
                // remote ICE peer but locally a server reflexive candidate is always going to be represented by
                // a "host" candidate.

                bool supportsIPv4 = _rtpChannel.RtpSocket.AddressFamily == AddressFamily.InterNetwork || _rtpChannel.IsDualMode;
                bool supportsIPv6 = _rtpChannel.RtpSocket.AddressFamily == AddressFamily.InterNetworkV6 || _rtpChannel.IsDualMode;

                if (remoteCandidate.addressFamily == AddressFamily.InterNetwork && supportsIPv4 ||
                    remoteCandidate.addressFamily == AddressFamily.InterNetworkV6 && supportsIPv6)
                {
                    ChecklistEntry entry = new ChecklistEntry(_localChecklistCandidate, remoteCandidate, IsController);

                    // Because only ONE checklist is currently supported each candidate pair can be set to
                    // a "waiting" state. If an additional checklist is ever added then only one candidate
                    // pair with the same foundation should be set to waiting across all checklists.
                    // See https://tools.ietf.org/html/rfc8445#section-6.1.2.6 for a somewhat convoluted
                    // explanation and example.
                    entry.State = ChecklistEntryState.Waiting;

                    AddChecklistEntry(entry);
                }

                // Finally sort the checklist to put it in priority order and if necessary remove lower 
                // priority pairs.
                _checklist.Sort();

                while (_checklist.Count > MAX_CHECKLIST_ENTRIES)
                {
                    _checklist.RemoveAt(_checklist.Count - 1);
                }
            }
        }

        /// <summary>
        /// Attempts to add a checklist entry. If there is already an equivalent entry in the checklist
        /// the entry may not be added or may replace an existing entry.
        /// </summary>
        /// <param name="entry">The new entry to attempt to add to the checklist.</param>
        private void AddChecklistEntry(ChecklistEntry entry)
        {
            // Check if there is already an entry that matches the remote candidate.
            // Note: The implementation in this class relies binding the socket used for all
            // local candidates on a SINGLE address (typically 0.0.0.0 or [::]). Consequently
            // there is no need to check the local candidate when determining duplicates. As long
            // as there is one checklist entry with each remote candidate the connectivity check will
            // work. To put it another way the local candidate information is not used on the 
            // "Nominated" pair.

            var entryRemoteEP = entry.RemoteCandidate.GetEndPoint();

            var existingEntry = _checklist.Where(x => x.RemoteCandidate.GetEndPoint().Address.Equals(entryRemoteEP.Address)
                && x.RemoteCandidate.GetEndPoint().Port == entryRemoteEP.Port
                && x.RemoteCandidate.protocol == entry.RemoteCandidate.protocol).SingleOrDefault();

            if (existingEntry != null)
            {
                if (entry.Priority > existingEntry.Priority)
                {
                    logger.LogDebug($"Removing lower priority entry and adding candidate pair to checklist for: {entry.RemoteCandidate}");
                    _checklist.Remove(existingEntry);
                    _checklist.Add(entry);
                }
                else
                {
                    logger.LogDebug($"Existing checklist entry has higher priority, NOT adding entry for: {entry.RemoteCandidate}");
                }
            }
            else
            {
                // No existing entry.
                logger.LogDebug($"Adding new candidate pair to checklist for: {entry.RemoteCandidate}");
                _checklist.Add(entry);
            }
        }

        /// <summary>
        /// Processes the checklist and sends any required STUN requests to perform connectivity checks.
        /// </summary>
        /// <remarks>
        /// The scheduling mechanism for ICE is specified in https://tools.ietf.org/html/rfc8445#section-6.1.4.
        /// </remarks>
        private void ProcessChecklist(Object stateInfo)
        {
            try
            {
                if (ConnectionState == RTCIceConnectionState.checking && _checklist != null && _checklist.Count > 0)
                {
                    if (RemoteIceUser == null || RemoteIcePassword == null)
                    {
                        logger.LogWarning("ICE session checklist processing cannot occur as either the remote ICE user or password are not set.");
                        ConnectionState = RTCIceConnectionState.failed;
                    }
                    else
                    {
                        lock (_checklist)
                        {
                            // The checklist gets sorted into priority order whenever a remote candidate and its corresponding candidate pairs
                            // are added. At this point it can be relied upon that the checklist is correctly sorted by candidate pair priority.

                            // Do a check for any timed out entries.
                            var failedEntries = _checklist.Where(x => x.State == ChecklistEntryState.InProgress
                                   && DateTime.Now.Subtract(x.LastCheckSentAt).TotalMilliseconds > RTO
                                   && x.ChecksSent >= N).ToList();

                            foreach (var failedEntry in failedEntries)
                            {
                                logger.LogDebug($"Checks for checklist entry have timed out, state being set to failed: {failedEntry.LocalCandidate} -> {failedEntry.RemoteCandidate}.");
                                failedEntry.State = ChecklistEntryState.Failed;
                            }

                            // Move on to checking for  checklist entries that need an initial check sent.
                            var nextEntry = _checklist.Where(x => x.State == ChecklistEntryState.Waiting).FirstOrDefault();

                            if (nextEntry != null)
                            {
                                SendConnectivityCheck(nextEntry, false);
                                return;
                            }

                            // No waiting entries so check for ones requiring a retransmit.
                            var retransmitEntry = _checklist.Where(x => x.State == ChecklistEntryState.InProgress
                                && DateTime.Now.Subtract(x.LastCheckSentAt).TotalMilliseconds > RTO).FirstOrDefault();

                            if (retransmitEntry != null)
                            {
                                SendConnectivityCheck(retransmitEntry, false);
                                return;
                            }

                            // If this point is reached and all entries are in a failed state then the overall result 
                            // of the ICE check is a failure.
                            if (_checklist.All(x => x.State == ChecklistEntryState.Failed))
                            {
                                _stunChecksTimer.Dispose();
                                _checklistState = ChecklistState.Failed;
                                ConnectionState = RTCIceConnectionState.failed;
                                OnIceConnectionStateChange?.Invoke(ConnectionState);
                            }
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception ProcessChecklist. " + excp);
            }
        }

        /// <summary>
        /// Sets the nominated checklist entry. This action completes the checklist processing and 
        /// indicates the connection checks were successful.
        /// </summary>
        /// <param name="entry">The checklist entry that was nominated.</param>
        private void SetNominatedEntry(ChecklistEntry entry)
        {
            entry.Nominated = true;
            _checklistState = ChecklistState.Completed;
            NominatedCandidate = entry.RemoteCandidate;
            ConnectionState = RTCIceConnectionState.connected;
            OnIceConnectionStateChange?.Invoke(RTCIceConnectionState.connected);
        }

        /// <summary>
        /// Performs a connectivity check for a single candidate pair entry.
        /// </summary>
        /// <param name="candidatePair">The candidate pair to perform a connectivity check for.</param>
        /// <param name="setUseCandidate">If true indicates we are acting as the "controlling" ICE agent
        /// and are nominating this candidate as the chosen one.</param>
        /// <remarks>As specified in https://tools.ietf.org/html/rfc8445#section-7.2.4.</remarks>
        private void SendConnectivityCheck(ChecklistEntry candidatePair, bool setUseCandidate)
        {
            candidatePair.State = ChecklistEntryState.InProgress;
            candidatePair.LastCheckSentAt = DateTime.Now;
            candidatePair.ChecksSent++;
            candidatePair.RequestTransactionID = Crypto.GetRandomString(STUNv2Header.TRANSACTION_ID_LENGTH);

            IPEndPoint remoteEndPoint = candidatePair.RemoteCandidate.GetEndPoint();

            logger.LogDebug($"Sending ICE connectivity check from {_rtpChannel.RTPLocalEndPoint} to {remoteEndPoint} (use candidate {setUseCandidate}).");

            STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
            stunRequest.Header.TransactionId = Encoding.ASCII.GetBytes(candidatePair.RequestTransactionID);
            stunRequest.AddUsernameAttribute(RemoteIceUser + ":" + LocalIceUser);
            stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Priority, BitConverter.GetBytes(candidatePair.Priority)));

            if (setUseCandidate)
            {
                stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.UseCandidate, null));
            }

            byte[] stunReqBytes = stunRequest.ToByteBufferStringKey(RemoteIcePassword, true);

            _rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, remoteEndPoint, stunReqBytes);
        }

        /// <summary>
        /// Processes a received STUN request or response.
        /// </summary>
        /// <param name="stunMessage">The STUN message received.</param>
        /// <param name="remoteEndPoint">The remote end point the STUN packet was received from.</param>
        public void ProcessStunMessage(STUNv2Message stunMessage, IPEndPoint remoteEndPoint)
        {
            remoteEndPoint = (!remoteEndPoint.Address.IsIPv4MappedToIPv6) ? remoteEndPoint : new IPEndPoint(remoteEndPoint.Address.MapToIPv4(), remoteEndPoint.Port);

            //logger.LogDebug($"STUN message received from remote {remoteEndPoint} {stunMessage.Header.MessageType}.");

            if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingRequest)
            {
                // TODO: The integrity check method needs to be implemented (currently just returns true).
                bool result = stunMessage.CheckIntegrity(System.Text.Encoding.UTF8.GetBytes(LocalIcePassword), LocalIceUser, RemoteIceUser);

                if (!result)
                {
                    // Send STUN error response.
                    STUNv2Message stunErrResponse = new STUNv2Message(STUNv2MessageTypesEnum.BindingErrorResponse);
                    stunErrResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                    _rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, remoteEndPoint, stunErrResponse.ToByteBuffer(null, false));
                }
                else
                {
                    var matchingCandidate = (_remoteCandidates != null) ? _remoteCandidates.Where(x => x.IsEquivalentEndPoint(RTCIceProtocol.udp, remoteEndPoint)).FirstOrDefault() : null;

                    if (matchingCandidate == null)
                    {
                        // This STUN request has come from a socket not in the remote ICE candidates list. 
                        // Add a new remote peer reflexive candidate.
                        RTCIceCandidate peerRflxCandidate = new RTCIceCandidate(new RTCIceCandidateInit());
                        peerRflxCandidate.SetAddressProperties(RTCIceProtocol.udp, remoteEndPoint.Address, (ushort)remoteEndPoint.Port, RTCIceCandidateType.prflx, null, 0);
                        logger.LogDebug($"Adding peer reflex ICE candidate for {remoteEndPoint}.");
                        _remoteCandidates.Add(peerRflxCandidate);

                        UpdateChecklist(peerRflxCandidate);

                        matchingCandidate = peerRflxCandidate;
                    }

                    // Find the checklist entry for this remote candidate and update its status.
                    var matchingChecklistEntry = _checklist.Where(x => x.RemoteCandidate.foundation == matchingCandidate.foundation).FirstOrDefault();

                    if (matchingChecklistEntry == null)
                    {
                        logger.LogWarning("ICE session STUN request matched a remote candidate but NOT a checklist entry.");
                    }
                    //else
                    //{
                    //    if (!IsController)
                    //    {
                    //        matchingChecklistEntry.State = ChecklistEntryState.Succeeded;
                    //    }
                    //}

                    // The UseCandidate attribute is only meant to be set by the "Controller" peer. This implementation
                    // will accept it irrespective of the peer roles. If the remote peer wants us to use a certain remote
                    // end point then so be it.
                    if (stunMessage.Attributes.Any(x => x.AttributeType == STUNv2AttributeTypesEnum.UseCandidate))
                    {
                        if (ConnectionState != RTCIceConnectionState.connected)
                        {
                            // If we are the "controlled" agent and get a "use candidate" attribute that sets the matching candidate as nominated 
                            // as per https://tools.ietf.org/html/rfc8445#section-7.3.1.5.

                            if (matchingChecklistEntry == null)
                            {
                                logger.LogWarning("ICE session STUN request had UseCandidate set but no matching checklist entry was found.");
                            }
                            else
                            {
                                logger.LogDebug($"ICE session remote peer nominated entry from binding request: {matchingChecklistEntry.RemoteCandidate}");
                                SetNominatedEntry(matchingChecklistEntry);
                            }
                        }
                    }

                    STUNv2Message stunResponse = new STUNv2Message(STUNv2MessageTypesEnum.BindingSuccessResponse);
                    stunResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                    stunResponse.AddXORMappedAddressAttribute(remoteEndPoint.Address, remoteEndPoint.Port);

                    string localIcePassword = LocalIcePassword;
                    byte[] stunRespBytes = stunResponse.ToByteBufferStringKey(localIcePassword, true);
                    _rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, remoteEndPoint, stunRespBytes);
                }
            }
            else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingSuccessResponse)
            {
                // Correlate with request using transaction ID as per https://tools.ietf.org/html/rfc8445#section-7.2.5.

                // Actions to take on a successful STUN response https://tools.ietf.org/html/rfc8445#section-7.2.5.3
                // - Discover peer reflexive remote candidates
                //   (TODO: According to https://tools.ietf.org/html/rfc8445#section-7.2.5.3.1 peer reflexive get added to the local candidates list?)
                // - Construct a valid pair which means match a candidate pair in the check list and mark it as valid (since a successful STUN exchange 
                //   has now taken place on it). A new entry may need to be created for this pair since peer reflexive candidates are not added to the connectivity
                //   check checklist.
                // - Update state of candidate pair that generated the check to Succeeded.
                // - If the controlling candidate set the USE_CANDIDATE attribute then the ICE agent that receives the successful response sets the nominated
                //   flag of the pair to true. Once the nominated flag is set it concludes the ICE processing for that component.

                if (_checklistState == ChecklistState.Running)
                {
                    string txID = Encoding.ASCII.GetString(stunMessage.Header.TransactionId);

                    // Attempt to find the checklist entry for this transaction ID.
                    var matchingChecklistEntry = _checklist.Where(x => x.RequestTransactionID == txID).FirstOrDefault();

                    if (matchingChecklistEntry == null)
                    {
                        logger.LogWarning("ICE session STUN response transaction ID did not match a checklist entry.");
                    }
                    else
                    {
                        matchingChecklistEntry.State = ChecklistEntryState.Succeeded;

                        if (matchingChecklistEntry.Nominated)
                        {
                            logger.LogDebug($"ICE session remote peer nominated entry from binding response: {matchingChecklistEntry.RemoteCandidate}");

                            // This is the response to a connectivity check that had the "UseCandidate" attribute set.
                            SetNominatedEntry(matchingChecklistEntry);
                        }
                        else if (this.IsController && !_checklist.Any(x => x.Nominated))
                        {
                            // If we are the controlling ICE agent it's up to us to decide when to nominate a candidate pair to use for the connection.
                            // To start with we'll just use whichever pair gets the first successful STUN exchange. If needs be the selection algorithm can
                            // improve over time.

                            matchingChecklistEntry.ChecksSent = 0;
                            matchingChecklistEntry.LastCheckSentAt = DateTime.MinValue;
                            matchingChecklistEntry.Nominated = true;

                            SendConnectivityCheck(matchingChecklistEntry, true);
                        }
                    }
                }
            }
            else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingErrorResponse)
            {
                logger.LogWarning($"A STUN binding error response was received from {remoteEndPoint}.");

                // Attempt to find the checklist entry for this transaction ID.
                string txID = Encoding.ASCII.GetString(stunMessage.Header.TransactionId);
                var matchingChecklistEntry = _checklist.Where(x => x.RequestTransactionID == txID).FirstOrDefault();

                if (matchingChecklistEntry == null)
                {
                    logger.LogWarning("ICE session STUN error response transaction ID did not match a checklist entry.");
                }
                else
                {
                    logger.LogWarning($"ICE session check list entry set to failed: {matchingChecklistEntry.RemoteCandidate}");
                    matchingChecklistEntry.State = ChecklistEntryState.Failed;
                }
            }
            else
            {
                logger.LogWarning($"An unrecognised STUN request was received from {remoteEndPoint}.");
            }
        }

        //private async Task SendTurnServerBindingRequest(IceCandidate iceCandidate)
        //{
        //    var rtpChannel = GetRtpChannel(SDPMediaTypesEnum.audio);

        //    int attempt = 1;

        //    while (attempt < INITIAL_STUN_BINDING_ATTEMPTS_LIMIT && !IsConnected && !IsClosed && !iceCandidate.IsGatheringComplete)
        //    {
        //        logger.LogDebug($"Sending STUN binding request {attempt} from {rtpChannel.RTPLocalEndPoint} to {iceCandidate.TurnServer.ServerEndPoint}.");

        //        STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
        //        stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
        //        byte[] stunReqBytes = stunRequest.ToByteBuffer(null, false);

        //        rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, iceCandidate.TurnServer.ServerEndPoint, stunReqBytes);

        //        await Task.Delay(INITIAL_STUN_BINDING_PERIOD_MILLISECONDS).ConfigureAwait(false);

        //        attempt++;
        //    }

        //    iceCandidate.IsGatheringComplete = true;
        //}

        //private void AllocateTurn(IceCandidate iceCandidate)
        //{
        //    try
        //    {
        //        var rtpChannel = GetRtpChannel(SDPMediaTypesEnum.audio);

        //        if (iceCandidate.TurnAllocateAttempts >= MAXIMUM_TURN_ALLOCATE_ATTEMPTS)
        //        {
        //            logger.LogDebug("TURN allocation for local socket " + iceCandidate.NetworkAddress + " failed after " + iceCandidate.TurnAllocateAttempts + " attempts.");

        //            iceCandidate.IsGatheringComplete = true;
        //        }
        //        else
        //        {
        //            iceCandidate.TurnAllocateAttempts++;

        //            //logger.LogDebug("Sending STUN connectivity check to client " + client.SocketAddress + ".");

        //            STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.Allocate);
        //            stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
        //            stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Lifetime, 3600));
        //            stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.RequestedTransport, STUNv2AttributeConstants.UdpTransportType));   // UDP
        //            byte[] stunReqBytes = stunRequest.ToByteBuffer(null, false);
        //            rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, iceCandidate.TurnServer.ServerEndPoint, stunReqBytes);
        //        }
        //    }
        //    catch (Exception excp)
        //    {
        //        logger.LogError("Exception AllocateTurn. " + excp);
        //    }
        //}

        //private void CreateTurnPermissions()
        //{
        //    try
        //    {
        //        var rtpChannel = GetRtpChannel(SDPMediaTypesEnum.audio);

        //        var localTurnIceCandidate = (from cand in LocalIceCandidates where cand.TurnRelayIPEndPoint != null select cand).First();
        //        var remoteTurnCandidate = (from cand in RemoteIceCandidates where cand.type == RTCIceCandidateType.relay select cand).First();

        //        // Send create permission request
        //        STUNv2Message turnPermissionRequest = new STUNv2Message(STUNv2MessageTypesEnum.CreatePermission);
        //        turnPermissionRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
        //        //turnBindRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.ChannelNumber, (ushort)3000));
        //        turnPermissionRequest.Attributes.Add(new STUNv2XORAddressAttribute(STUNv2AttributeTypesEnum.XORPeerAddress, remoteTurnCandidate.port, IPAddress.Parse(remoteTurnCandidate.NetworkAddress)));
        //        turnPermissionRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Username, Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Username)));
        //        turnPermissionRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Nonce, Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Nonce)));
        //        turnPermissionRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Realm, Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Realm)));

        //        MD5 md5 = new MD5CryptoServiceProvider();
        //        byte[] hmacKey = md5.ComputeHash(Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Username + ":" + localTurnIceCandidate.TurnServer.Realm + ":" + localTurnIceCandidate.TurnServer.Password));

        //        byte[] turnPermissionReqBytes = turnPermissionRequest.ToByteBuffer(hmacKey, false);
        //        //localTurnIceCandidate.LocalRtpSocket.SendTo(turnPermissionReqBytes, localTurnIceCandidate.TurnServer.ServerEndPoint);
        //        rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, localTurnIceCandidate.TurnServer.ServerEndPoint, turnPermissionReqBytes);
        //    }
        //    catch (Exception excp)
        //    {
        //        logger.LogError("Exception CreateTurnPermissions. " + excp);
        //    }
        //}
    }
}
