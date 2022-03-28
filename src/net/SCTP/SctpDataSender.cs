//-----------------------------------------------------------------------------
// Filename: SctpDataSender.cs
//
// Description: This class manages sending data chunks to an association's
// remote peer.
//
// Remarks: Most of the logic for this class is specified in
// https://tools.ietf.org/html/rfc4960#section-6.1.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// Easter Sunday 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class SctpDataSender
    {
        public const ushort DEFAULT_SCTP_MTU = 1300;

        public const uint CONGESTION_WINDOW_FACTOR = 4380;

        /// <summary>
        /// Used to limit the number of packets that are sent at any one time, i.e. when 
        /// the transmit timer fires do not send more than this many packets.
        /// </summary>
        public const int MAX_BURST = 4;

        /// <summary>
        /// Milliseconds to wait between bursts if no SACK chunks are received in the interim.
        /// Eventually if no SACK chunks are received the congestion or receiver windows
        /// will reach zero and enforce a longer period.
        /// </summary>
        public const int BURST_PERIOD_MILLISECONDS = 50;

        /// <summary>
        /// Retransmission timeout initial value.
        /// </summary>
        public const int RTO_INITIAL_SECONDS = 3;

        /// <summary>
        /// The minimum value for the Retransmission timeout.
        /// </summary>
        public const int RTO_MIN_SECONDS = 1;

        /// <summary>
        /// The maximum value for the Retransmission timeout.
        /// </summary>
        public const int RTO_MAX_SECONDS = 60;

        private static ILogger logger = LogFactory.CreateLogger<SctpDataSender>();

        /// <summary>
        /// Callback method that sends data chunks.
        /// </summary>
        internal Action<SctpDataChunk> _sendDataChunk;

        /// <summary>
        /// Callback method that sends forward-tsn chunks.
        /// </summary>
        internal Action<SctpForwardCumulativeTSNChunk> _sendForwardTsn;

        private string _associationID;
        private ushort _defaultMTU;
        private uint _initialTSN;
        private bool _gotFirstSACK;
        private bool _isStarted;
        private bool _isClosed;
        private int _lastAckedDataChunkSize;
        private bool _inRetransmitMode;
        protected ManualResetEventSlim _senderMre = new ManualResetEventSlim();

        /// <summary>
        /// Congestion control window (cwnd, in bytes), which is adjusted by
        /// the sender based on observed network conditions.
        /// </summary>
        internal uint _congestionWindow;

        /// <summary>
        /// The current Advertised Receiver Window Credit for the remote peer.
        /// This value represents the dedicated  buffer space on the remote peer, 
        /// in number of bytes, that will be used for the receive buffer for DATA 
        /// chunks sent to it.
        /// </summary>
        internal uint _receiverWindow;

        /// <summary>
        /// Slow-start threshold (ssthresh, in bytes), which is used by the
        /// sender to distinguish slow-start and congestion avoidance phases.
        /// </summary>
        private uint _slowStartThreshold;

        /// <summary>
        /// The initial Advertised Receiver Window Credit for the remote peer.
        /// This value represents the dedicated  buffer space on the remote peer, 
        /// in number of bytes, that will be used for the receive buffer for DATA 
        /// chunks sent to it.
        /// </summary>
        private uint _initialRemoteARwnd;

        internal int _burstPeriodMilliseconds = BURST_PERIOD_MILLISECONDS;
        internal int _rtoInitialMilliseconds = RTO_INITIAL_SECONDS * 1000;
        internal int _rtoMinimumMilliseconds = RTO_MIN_SECONDS * 1000;
        
        /// <summary>
        /// A count of the bytes currently in-flight to the remote peer.
        /// </summary>
        internal uint _outstandingBytes =>
            (uint)(_unconfirmedChunks.Sum(x => x.Value.UserData.Length));

        /// <summary>
        /// The TSN that the remote peer has acknowledged.
        /// </summary>
        private uint _cumulativeAckTSN;

        /// <summary>
        /// If true, FORWARD-TSN chunks are permitted as described in rfc3758
        /// </summary>
        internal bool _supportsPartialReliabilityExtension;

        /// <summary>
        /// A theoretical cumulative TSN point of the peer.  
        /// https://datatracker.ietf.org/doc/html/rfc3758#section-3.5
        /// </summary>
        internal uint _advancedPeerAckPoint { get; private set; }

        /// <summary>
        /// Keeps track of the sequence numbers for each of the streams being
        /// used by the association.
        /// </summary>
        private Dictionary<ushort, ushort> _streamSeqnums = new Dictionary<ushort, ushort>();

        /// <summary>
        /// Queue to hold SCTP frames that are waiting to be sent to the remote peer.
        /// </summary>
        protected ConcurrentQueue<SctpDataChunk> _sendQueue = new ConcurrentQueue<SctpDataChunk>();

        /// <summary>
        /// Chunks that have been sent to the remote peer but have yet to be acknowledged.
        /// </summary>
        private ConcurrentDictionary<uint, SctpDataChunk> _unconfirmedChunks = new ConcurrentDictionary<uint, SctpDataChunk>();

        /// <summary>
        /// Chunks that have been flagged by a gap report from the remote peer as missing
        /// and that need to be re-sent.
        /// </summary>
        internal ConcurrentDictionary<uint, int> _missingChunks = new ConcurrentDictionary<uint, int>();

        /// <summary>
        /// The total size (in bytes) of queued user data that will be sent to the peer.
        /// </summary>
        public ulong BufferedAmount => (ulong)_sendQueue.Sum(x => x.UserData?.Length ?? 0);

        /// <summary>
        /// The Transaction Sequence Number (TSN) that will be used in the next DATA chunk sent.
        /// </summary>
        public uint TSN { get; internal set; }


        public SctpDataSender(
            string associationID,
            Action<SctpChunk> sendChunk,
            ushort defaultMTU,
            uint initialTSN,
            uint remoteARwnd)
        {
            _associationID = associationID;
            _sendDataChunk = sendChunk;
            _sendForwardTsn = sendChunk;
            _defaultMTU = defaultMTU > 0 ? defaultMTU : DEFAULT_SCTP_MTU;
            _initialTSN = initialTSN;
            _advancedPeerAckPoint = initialTSN;
            TSN = initialTSN;
            _initialRemoteARwnd = remoteARwnd;
            _receiverWindow = remoteARwnd;

            // RFC4960 7.2.1 (point 1)
            _congestionWindow = (uint)(Math.Min(4 * _defaultMTU, Math.Max(2 * _defaultMTU, CONGESTION_WINDOW_FACTOR)));

            // RFC4960 7.2.1 (point 3)
            _slowStartThreshold = _initialRemoteARwnd;
        }

        public void SetReceiverWindow(uint remoteARwnd)
        {
            _initialRemoteARwnd = remoteARwnd;
        }

        /// <summary>
        /// Handler for SACK chunks received from the remote peer.
        /// </summary>
        /// <param name="sack">The SACK chunk from the remote peer.</param>
        public void GotSack(SctpSackChunk sack)
        {
            if (sack != null)
            {
                _inRetransmitMode = false;
                bool updateCwnd = true;

                unchecked
                {
                    uint maxTSNDistance = SctpDataReceiver.GetDistance(_cumulativeAckTSN, TSN);
                    bool processGapReports = true;

                    if (_unconfirmedChunks.TryGetValue(sack.CumulativeTsnAck, out var result))
                    {
                        if (!result.Abandoned)
                        {
                            _lastAckedDataChunkSize = result.UserData.Length;
                        }
                        else
                        {
                            updateCwnd = false;
                        }
                    }

                    if (!_gotFirstSACK)
                    {
                        if (SctpDataReceiver.IsNewerOrEqual(_initialTSN, sack.CumulativeTsnAck))
                        {
                            logger.LogTrace($"SCTP first SACK remote peer TSN ACK {sack.CumulativeTsnAck} next sender TSN {TSN}, arwnd {sack.ARwnd} (gap reports {sack.GapAckBlocks.Count}).");
                            _gotFirstSACK = true;
                            _cumulativeAckTSN = _initialTSN;
                            RemoveAckedUnconfirmedChunks(sack.CumulativeTsnAck);
                        }
                    }
                    else
                    {
                        if (_cumulativeAckTSN != sack.CumulativeTsnAck)
                        {
                            if (SctpDataReceiver.GetDistance(_cumulativeAckTSN, sack.CumulativeTsnAck) > maxTSNDistance)
                            {
                                logger.LogWarning($"SCTP SACK TSN from remote peer of {sack.CumulativeTsnAck} was too distant from the expected {_cumulativeAckTSN}, ignoring.");
                                processGapReports = false;
                            }
                            else if (!SctpDataReceiver.IsNewer(_cumulativeAckTSN, sack.CumulativeTsnAck))
                            {
                                logger.LogWarning($"SCTP SACK TSN from remote peer of {sack.CumulativeTsnAck} was behind expected {_cumulativeAckTSN}, ignoring.");
                                processGapReports = false;
                            }
                            else
                            {
                                logger.LogTrace($"SCTP SACK remote peer TSN ACK {sack.CumulativeTsnAck}, next sender TSN {TSN}, arwnd {sack.ARwnd} (gap reports {sack.GapAckBlocks.Count}).");
                                RemoveAckedUnconfirmedChunks(sack.CumulativeTsnAck);
                            }
                        }
                        else
                        {
                            logger.LogTrace($"SCTP SACK remote peer TSN ACK no change {_cumulativeAckTSN}, next sender TSN {TSN}, arwnd {sack.ARwnd} (gap reports {sack.GapAckBlocks.Count}).");
                            RemoveAckedUnconfirmedChunks(sack.CumulativeTsnAck);
                        }
                    }

                    // Check gap reports. Only process them if the cumulative ACK TSN was acceptable.
                    if (processGapReports && sack.GapAckBlocks.Count > 0)
                    {
                        ProcessGapReports(sack.GapAckBlocks, maxTSNDistance);
                    }
                }

                _receiverWindow = CalculateReceiverWindow(sack.ARwnd);
                // RFC 3758 3.5 A2 The sender MUST NOT credit an "abandoned" data chunk to the
                // partial_bytes_acked  and MUST NOT advance the cwnd based on this "abandoned" data chunk.
                if (updateCwnd)
                {
                    _congestionWindow = CalculateCongestionWindow(_lastAckedDataChunkSize);
                }


                if (_supportsPartialReliabilityExtension)
                {
                    // RFC 3758 3.5 C1
                    if (SctpDataReceiver.IsNewer(_advancedPeerAckPoint, sack.CumulativeTsnAck))
                    {
                        _advancedPeerAckPoint = sack.CumulativeTsnAck;
                    }

                    // RFC 3758 3.5 C2
                    UpdateAdvancedPeerAckPoint();
                }
                // SACK's will normally allow more data to be sent.
                _senderMre.Set();
            }
        }

        /// <summary>
        /// RFC 3758 3.5 C2) Try to further advance the "Advanced.Peer.Ack.Point" locally,
        /// that is, to move "Advanced.Peer.Ack.Point" up as long as the chunk next in
        /// the out-queue space is marked as "abandoned"
        /// </summary>
        private void UpdateAdvancedPeerAckPoint()
        {
            var unconfirmed = _unconfirmedChunks.GetEnumerator();
            while (unconfirmed.MoveNext())
            {
                CheckForAbandonedChunk(unconfirmed.Current.Value);
            }

            unchecked
            {
                int safety = _unconfirmedChunks.Count();
                do
                {
                    var nextTsn = _advancedPeerAckPoint + 1;
                    safety--;

                    if (_unconfirmedChunks.TryGetValue(nextTsn, out var chunk))
                    {
                        if (chunk.Abandoned)
                        {
                            _advancedPeerAckPoint = nextTsn;
                        }
                        else
                        {
                            break;
                        }
                    }
                } while (safety >= 0);
            }

            // RFC 3758 3.5 C3
            if (SctpDataReceiver.IsNewer(_gotFirstSACK ? _cumulativeAckTSN : _initialTSN, _advancedPeerAckPoint))
            {
                logger.LogTrace($"SCTP AdvancedPeerAckPoint moved from {_cumulativeAckTSN} to {_advancedPeerAckPoint}");
                
                _sendForwardTsn(GetForwardTSN());

                RemoveAckedUnconfirmedChunks(_advancedPeerAckPoint);
            }
        }

        /// <summary>
        /// Creates a FORWARD-TSN chunk that represents the current state of the sender
        /// </summary>
        /// <returns>A ForwardCumulativeTSN chunk</returns>
        internal SctpForwardCumulativeTSNChunk GetForwardTSN()
        {
            var forwardTsn = new SctpForwardCumulativeTSNChunk(_advancedPeerAckPoint);
            foreach (var chunk in _unconfirmedChunks.Values)
            {
                if (!chunk.Unordered && chunk.Abandoned)
                {
                    if (forwardTsn.StreamSequenceAssociations.TryGetValue(chunk.StreamID, out ushort currentStreamSeq))
                    {
                        if (SctpDataReceiver.IsNewerOrEqual(currentStreamSeq, chunk.StreamSeqNum))
                        {
                            forwardTsn.StreamSequenceAssociations[chunk.StreamID] = chunk.StreamSeqNum;
                        }
                    }
                    else
                    {
                        forwardTsn.StreamSequenceAssociations.Add(chunk.StreamID, chunk.StreamSeqNum);
                    }
                }
            }

            // TODO: rfc3758 C4: If the total size of the FORWARD TSN does NOT fit in a single MTU, then the sender of
            // the FORWARD TSN SHOULD lower the Advanced.Peer.Ack.Point to the last TSN that will fit in a single MTU.

            return forwardTsn;
        }

        /// <summary>
        /// Sends a DATA chunk to the remote peer.
        /// </summary>
        /// <param name="streamID">The stream ID to sent the data on.</param>
        /// <param name="ppid">The payload protocol ID for the data.</param>
        /// <param name="data">The byte data to send.</param>
        /// <param name="ordered">If true, messages will be received in order.</param>
        /// <param name="maxLifetime">The maximum lifetime in milliseconds before the message is abandoned.</param>
        /// <param name="maxRetransmits">The maximum number of retranmissions before the message is abandoned.</param>
        public void SendData(ushort streamID, uint ppid, byte[] data, bool ordered=true, uint maxLifetime=uint.MaxValue, uint maxRetransmits=uint.MaxValue)
        {
            lock (_sendQueue)
            {
                ushort seqnum = 0;

                if (_streamSeqnums.ContainsKey(streamID))
                {
                    unchecked
                    {
                        _streamSeqnums[streamID] = (ushort)(_streamSeqnums[streamID] + 1);
                        seqnum = _streamSeqnums[streamID];
                    }
                }
                else
                {
                    _streamSeqnums.Add(streamID, 0);
                }

                for (int index = 0; index * _defaultMTU < data.Length; index++)
                {
                    int offset = (index == 0) ? 0 : (index * _defaultMTU);
                    int payloadLength = (offset + _defaultMTU < data.Length) ? _defaultMTU : data.Length - offset;

                    // Future TODO: Replace with slice when System.Memory is introduced as a dependency.
                    byte[] payload = new byte[payloadLength];
                    Buffer.BlockCopy(data, offset, payload, 0, payloadLength);

                    bool isBegining = index == 0;
                    bool isEnd = ((offset + payloadLength) >= data.Length) ? true : false;

                    SctpDataChunk dataChunk = new SctpDataChunk(
                        !ordered,
                        isBegining,
                        isEnd,
                        streamID,
                        seqnum,
                        ppid,
                        payload,
                        maxLifetime,
                        maxRetransmits);

                    _sendQueue.Enqueue(dataChunk);
                }

                _senderMre.Set();
            }
        }

        /// <summary>
        /// Start the sending thread to process the new DATA chunks from the application and
        /// any retransmits or timed out chunks.
        /// </summary>
        public void StartSending()
        {
            if (!_isStarted)
            {
                _isStarted = true;
                var sendThread = new Thread(DoSend);
                sendThread.IsBackground = true;
                sendThread.Start();
            }
        }

        /// <summary>
        /// Stops the sending thread.
        /// </summary>
        public void Close()
        {
            _isClosed = true;
        }

        /// <summary>
        /// Updates the sender state for the gap reports received in a SACH chunk from the
        /// remote peer.
        /// </summary>
        /// <param name="sackGapBlocks">The gap reports from the remote peer.</param>
        /// <param name="maxTSNDistance">The maximum distance any valid TSN should be from the current
        /// ACK'ed TSN. If this distance gets exceeded by a gap report then it's likely something has been
        /// miscalculated.</param>
        private void ProcessGapReports(List<SctpTsnGapBlock> sackGapBlocks, uint maxTSNDistance)
        {
            uint lastAckTSN = _cumulativeAckTSN;

            foreach (var gapBlock in sackGapBlocks)
            {
                uint goodTSNStart = _cumulativeAckTSN + gapBlock.Start;

                if (SctpDataReceiver.GetDistance(lastAckTSN, goodTSNStart) > maxTSNDistance)
                {
                    logger.LogWarning($"SCTP SACK gap report had a start TSN of {goodTSNStart} too distant from last good TSN {lastAckTSN}, ignoring rest of SACK.");
                    break;
                }
                else if (!SctpDataReceiver.IsNewer(lastAckTSN, goodTSNStart))
                {
                    logger.LogWarning($"SCTP SACK gap report had a start TSN of {goodTSNStart} behind last good TSN {lastAckTSN}, ignoring rest of SACK.");
                    break;
                }
                else
                {
                    uint missingTSN = lastAckTSN + 1;

                    logger.LogTrace($"SCTP SACK gap report start TSN {goodTSNStart} gap report end TSN {_cumulativeAckTSN + gapBlock.End} " +
                        $"first missing TSN {missingTSN}.");

                    while (missingTSN != goodTSNStart)
                    {
                        if (!_missingChunks.ContainsKey(missingTSN))
                        {
                            if (!_unconfirmedChunks.ContainsKey(missingTSN))
                            {
                                // What to do? Can't retransmit a chunk that's no longer available. 
                                // Hope it's a transient error from a duplicate or out of order SACK.
                                // TODO: Maybe keep count of how many time this occurs and send an ABORT if it
                                // gets to a certain threshold.
                                logger.LogWarning($"SCTP SACK gap report reported missing TSN of {missingTSN} but no matching unconfirmed chunk available.");
                                break;
                            }
                            else
                            {
                                logger.LogTrace($"SCTP SACK gap adding retransmit entry for TSN {missingTSN}.");
                                _missingChunks.TryAdd(missingTSN, 0);
                            }
                        }

                        missingTSN++;
                    }
                }

                lastAckTSN = _cumulativeAckTSN + gapBlock.End;
            }
        }

        /// <summary>
        /// Removes the chunks waiting for a SACK confirmation from the unconfirmed queue.
        /// </summary>
        /// <param name="sackTSN">The acknowledged TSN received from in a SACK from the remote peer.</param>
        private void RemoveAckedUnconfirmedChunks(uint sackTSN)
        {
            logger.LogTrace($"SCTP data sender removing unconfirmed chunks cumulative ACK TSN {_cumulativeAckTSN}, SACK TSN {sackTSN}.");

            if (_cumulativeAckTSN == sackTSN)
            {
                // This is normal for the first SACK received.
                _unconfirmedChunks.TryRemove(_cumulativeAckTSN, out _);
                _missingChunks.TryRemove(_cumulativeAckTSN, out _);
            }
            else
            {
                _unconfirmedChunks.TryRemove(_cumulativeAckTSN, out _);
                _missingChunks.TryRemove(_cumulativeAckTSN, out _);

                int safety = _unconfirmedChunks.Count() + 1;

                do
                {
                    _cumulativeAckTSN++;
                    safety--;

                    if (!_unconfirmedChunks.TryRemove(_cumulativeAckTSN, out _))
                    {
                        if (!_supportsPartialReliabilityExtension)
                        {
                            logger.LogWarning($"SCTP data sender could not remove unconfirmed chunk for {_cumulativeAckTSN}.");
                        }
                    }

                    _missingChunks.TryRemove(_cumulativeAckTSN, out _);

                } while (_cumulativeAckTSN != sackTSN && safety >= 0);
            }
        }

        /// <summary>
        /// Worker thread to process the send and retransmit queues.
        /// </summary>
        private void DoSend(object state)
        {
            logger.LogDebug($"SCTP association data send thread started for association {_associationID}.");

            while (!_isClosed)
            {
                // DateTime.Now calls have been a tiny bit expensive in the past so get a small saving by only
                // calling once per loop.
                DateTime now = DateTime.Now;

                int burstSize = (_inRetransmitMode || _congestionWindow < _outstandingBytes || _receiverWindow == 0) ? 1 : MAX_BURST;
                int chunksSent = 0;

                //logger.LogTrace($"SCTP sender burst size {burstSize}, in retransmit mode {_inRetransmitMode}, cwnd {_congestionWindow}, arwnd {_receiverWindow}.");

                if (_supportsPartialReliabilityExtension)
                {
                    // // RFC 3758 3.5 A5
                    UpdateAdvancedPeerAckPoint();
                }

                // Missing chunks from a SACK gap report take priority.
                if (_missingChunks.Count > 0)
                {
                    var misses = _missingChunks.GetEnumerator();
                    bool haveMissing = misses.MoveNext();

                    while (chunksSent < burstSize && haveMissing)
                    {
                        if (_unconfirmedChunks.TryGetValue(misses.Current.Key, out var missingChunk))
                        {
                            if (!missingChunk.Abandoned)
                            {
                                missingChunk.LastSentAt = now;
                                missingChunk.SendCount += 1;

                                logger.LogTrace($"SCTP resending missing data chunk for TSN {missingChunk.TSN}, data length {missingChunk.UserData.Length}, " +
                                    $"flags {missingChunk.ChunkFlags:X2}, send count {missingChunk.SendCount}.");

                                _sendDataChunk(missingChunk);
                                chunksSent++;
                            }
                            else
                            {
                                logger.LogTrace($"SCTP abandoned resend of missing chunk for TSN {missingChunk.TSN}");
                            }
                        }

                        haveMissing = misses.MoveNext();
                    }
                }

                // Check if there are any unconfirmed transactions that are due for a retransmit.
                if (chunksSent < burstSize && _unconfirmedChunks.Count > 0)
                {
                    foreach (var chunk in _unconfirmedChunks.Values
                        .Where(x => now.Subtract(x.LastSentAt).TotalSeconds > RTO_MIN_SECONDS)
                        .Take(burstSize - chunksSent))
                    {
                        if (!chunk.Abandoned)
                        {
                            chunk.LastSentAt = DateTime.Now;
                            chunk.SendCount += 1;

                            logger.LogTrace($"SCTP retransmitting data chunk for TSN {chunk.TSN}, data length {chunk.UserData.Length}, " +
                                $"flags {chunk.ChunkFlags:X2}, send count {chunk.SendCount}.");

                            _sendDataChunk(chunk);
                            chunksSent++;
                        }
                        else
                        {
                            logger.LogTrace($"SCTP abandoned unconfirmed chunk for TSN {chunk.TSN}");
                        }
                        
                        if (!_inRetransmitMode)
                        {
                            _inRetransmitMode = true;

                            // When the T3-rtx timer expires on an address, SCTP should perform slow start.
                            // RFC4960 7.2.3
                            _slowStartThreshold = (uint)Math.Max(_congestionWindow / 2, 4 * _defaultMTU);
                            _congestionWindow = _defaultMTU;
                        }
                    }
                }

                // Finally send any new data chunks that have not yet been sent.
                if (chunksSent < burstSize && _sendQueue.Count > 0)
                {
                    while (chunksSent < burstSize && _sendQueue.TryDequeue(out var dataChunk))
                    {
                        if (CheckForAbandonedChunk(dataChunk))
                        {
                            logger.LogTrace($"SCTP abandoned chunk prior to sending");
                            continue;
                        }

                        if (!dataChunk._tsnAssigned)
                        {
                            dataChunk.SetTSN(TSN);
                            TSN = (TSN == UInt32.MaxValue) ? 0 : TSN + 1;
                        }

                        dataChunk.LastSentAt = now;
                        dataChunk.SendCount = 1;

                        logger.LogTrace($"SCTP sending data chunk for TSN {dataChunk.TSN}, data length {dataChunk.UserData.Length}, " +
                            $"flags {dataChunk.ChunkFlags:X2}, send count {dataChunk.SendCount}.");

                        _unconfirmedChunks.TryAdd(dataChunk.TSN, dataChunk);
                        _sendDataChunk(dataChunk);
                        chunksSent++;
                    }
                }

                _senderMre.Reset();

                int wait = GetSendWaitMilliseconds();
                //logger.LogTrace($"SCTP sender wait period {wait}ms, arwnd {_receiverWindow}, cwnd {_congestionWindow} " +
                //    $"outstanding bytes {_outstandingBytes}, send queue {_sendQueue.Count}, missing {_missingChunks.Count} "
                //    + $"unconfirmed {_unconfirmedChunks.Count}.");

                _senderMre.Wait(wait);
            }

            logger.LogDebug($"SCTP association data send thread stopped for association {_associationID}.");
        }

        /// <summary>
        /// Determines how many milliseconds the send thread should wait before the next send attempt.
        /// </summary>
        private int GetSendWaitMilliseconds()
        {
            if (_sendQueue.Count > 0 || _missingChunks.Count > 0)
            {
                if (_receiverWindow > 0 && _congestionWindow > _outstandingBytes)
                {
                    return _burstPeriodMilliseconds;
                }
                else
                {
                    return _rtoMinimumMilliseconds;
                }
            }
            else if (_unconfirmedChunks.Count > 0)
            {
                // TODO use Round Trip Time (RTT) measurements to adjust the Retransmission Timeout (RTO) value.
                // https://tools.ietf.org/html/rfc4960#section-6.3.1
                return _rtoMinimumMilliseconds;
            }
            else
            {
                return _rtoInitialMilliseconds;
            }
        }

        /// <summary>
        /// Calculates the receiver window based on the value supplied from a SACK chunk.
        /// Note the receive window in the SACK chunk does not take account for in flight
        /// DATA chunks hence the need for this calculation.
        /// </summary>
        /// <param name="advertisedReceiveWindow">The last receive window value supplied by the remote peer 
        /// either in the INIT handshake or in a SACK chunk.</param>
        /// <remarks>
        /// See https://tools.ietf.org/html/rfc4960#section-6.2.1.
        /// </remarks>
        /// <returns>The new value to use for the receiver window.</returns>
        private uint CalculateReceiverWindow(uint advertisedReceiveWindow)
        {
            return (advertisedReceiveWindow > _outstandingBytes) ? advertisedReceiveWindow - _outstandingBytes : 0;
        }

        /// <summary>
        /// Calculates an updated value for the congestion window.
        /// </summary>
        /// <param name="lastAckDataChunkSize">The size of last ACK'ed DATA chunk.</param>
        /// <returns>A congestion window value.</returns>
        private uint CalculateCongestionWindow(int lastAckDataChunkSize)
        {
            if (_congestionWindow < _slowStartThreshold)
            {
                // In Slow-Start mode, see RFC4960 7.2.1.

                if (_congestionWindow < _outstandingBytes)
                {
                    // When cwnd is less than or equal to ssthresh, an SCTP endpoint MUST
                    // use the slow - start algorithm to increase cwnd only if the current
                    // congestion window is being fully utilized.
                    uint increasedCwnd = (uint)(_congestionWindow + Math.Min(lastAckDataChunkSize, _defaultMTU));

                    logger.LogTrace($"SCTP sender congestion window in slow-start increased from {_congestionWindow} to {increasedCwnd}.");

                    return increasedCwnd;
                }
                else
                {
                    return _congestionWindow;
                }
            }
            else
            {
                // In Congestion Avoidance mode, see RFC4960 7.2.2.

                if (_congestionWindow < _outstandingBytes)
                {
                    logger.LogTrace($"SCTP sender congestion window in congestion avoidance increased from {_congestionWindow} to {_congestionWindow + _defaultMTU}.");

                    return _congestionWindow + _defaultMTU;
                }
                else
                {
                    return _congestionWindow;
                }
            }
        }

        /// <summary>
        /// Checks if a chunk is either already abandoned, or should be abandoned.
        /// </summary>
        /// <returns>True if the chunk is abandoned.</returns>
        protected bool CheckForAbandonedChunk(SctpDataChunk chunk)
        {
            if (_supportsPartialReliabilityExtension)
            {
                if (chunk.Abandoned)
                {
                    return true;
                }

                // Abandon messages that have exceeded their lifetime
                if (chunk.MaxLifetime < uint.MaxValue && (DateTime.Now - chunk.CreatedAt).TotalMilliseconds > chunk.MaxLifetime)
                {
                    AbandonChunk(chunk);
                    return true;
                }
                if (chunk.MaxRetransmissions < uint.MaxValue && chunk.MaxRetransmissions < chunk.SendCount)
                {
                    AbandonChunk(chunk);
                    return true;
                }

            }
            return false;

        }

        /// <summary>
        /// Marks a chunk as abandoned for the PR-SCTP extension.  
        /// When a data chunk is "abandoned", the sender MUST treat the data
        /// chunk as being finally acked and no longer outstanding.
        /// https://datatracker.ietf.org/doc/html/rfc3758#section-3.5 A2
        /// </summary>
        /// <param name="chunk">The chunk to flag as abandoned</param>
        protected void AbandonChunk(SctpDataChunk chunk)
        {
            if (!_supportsPartialReliabilityExtension)
            {
                logger.LogWarning("SCTP Chunk can not be abandoned as FORWARDTSN is not supported on this sender");
                return;
            }

            if (chunk.Abandoned)
            {
                return;
            }

            chunk.Abandoned = true;
            logger.LogDebug("SCTP sender abandoned chunk " + chunk.TSN);


            if (chunk.Begining != chunk.Ending || !chunk.Begining)
            {
                // RFC 3758 3.5 A3) When a TSN is "abandoned", if it is part of a fragmented message,
                // all other TSN's within that fragmented message MUST be abandoned at the same time.
                foreach (var chunkFragment in _unconfirmedChunks.Where(x => x.Value.StreamID == chunk.StreamID && x.Value.StreamSeqNum == chunk.StreamSeqNum))
                {
                    chunkFragment.Value.Abandoned = true;
                }
                foreach (var chunkFragment in _sendQueue.Where(x => x.StreamID == chunk.StreamID && x.StreamSeqNum == chunk.StreamSeqNum))
                {
                    chunkFragment.Abandoned = true;
                }
            }
        }
    }
}
