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

        private string _associationID;
        private ushort _defaultMTU;
        private uint _initialTSN;
        private bool _gotFirstSACK;
        private bool _isStarted;
        private bool _isClosed;
        private int _lastAckedDataChunkSize;
        private bool _inRetransmitMode;
        private bool _inFastRecoveryMode;
        private uint _fastRecoveryExitPoint;
        private ManualResetEventSlim _senderMre = new ManualResetEventSlim();

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
        /// <summary>
        /// Retransmission timeout. 
        /// See https://datatracker.ietf.org/doc/html/rfc4960#section-6.3.1
        /// </summary>
        internal double _rto = RTO_INITIAL_SECONDS * 1000;
        internal int _rtoInitialMilliseconds = RTO_INITIAL_SECONDS * 1000;
        internal int _rtoMinimumMilliseconds = RTO_MIN_SECONDS * 1000;
        internal int _rtoMaximumMilliseconds = RTO_MAX_SECONDS * 1000;
        private bool _hasRoundTripTime;
        private double _smoothedRoundTripTime; // "SRTT"
        private double _roundTripTimeVariation; // "RTTVAR"
        private double _rtoAlpha = 0.125; // Suggested value in rfc4960#section-15
        private double _rtoBeta = 0.25; // Suggested value in rfc4960#section-15

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
        /// Keeps track of the sequence numbers for each of the streams being
        /// used by the association.
        /// </summary>
        private Dictionary<ushort, ushort> _streamSeqnums = new Dictionary<ushort, ushort>();

        /// <summary>
        /// Queue to hold SCTP frames that are waiting to be sent to the remote peer.
        /// </summary>
        private ConcurrentQueue<SctpDataChunk> _sendQueue = new ConcurrentQueue<SctpDataChunk>();

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
            Action<SctpDataChunk> sendDataChunk,
            ushort defaultMTU,
            uint initialTSN,
            uint remoteARwnd)
        {
            _associationID = associationID;
            _sendDataChunk = sendDataChunk;
            _defaultMTU = defaultMTU > 0 ? defaultMTU : DEFAULT_SCTP_MTU;
            _initialTSN = initialTSN;
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
                if (_inRetransmitMode)
                {
                    logger.LogTrace($"SCTP sender exiting retransmit mode.");
                    _inRetransmitMode = false;
                }

                unchecked
                {
                    uint maxTSNDistance = SctpDataReceiver.GetDistance(_cumulativeAckTSN, TSN);
                    bool processGapReports = true;
                    uint cumAckTSNBeforeSackProcessing = _cumulativeAckTSN;

                    if (_unconfirmedChunks.TryGetValue(sack.CumulativeTsnAck, out var result))
                    {
                        // Don't include retransmits in round trip calculation
                        if (result.SendCount == 1)
                        {
                            UpdateRoundTripTime(result);
                        }

                        _lastAckedDataChunkSize = result.UserData.Length;
                    }

                    if (!_gotFirstSACK)
                    {
                        if (SctpDataReceiver.GetDistance(_initialTSN, sack.CumulativeTsnAck) < maxTSNDistance
                            && SctpDataReceiver.IsNewerOrEqual(_initialTSN, sack.CumulativeTsnAck))
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

                    if (sack.DuplicateTSN.Count > 0)
                    {
                        // The remote is reporting that we have sent a duplicate TSN. 
                        // This is probably because a SACK chunk was dropped. 
                        // Ensure that we stop sending the duplicate.
                        foreach (uint duplicateTSN in sack.DuplicateTSN)
                        {
                            _unconfirmedChunks.TryRemove(duplicateTSN, out _);
                            _missingChunks.TryRemove(duplicateTSN, out _);
                        }
                    }


                    // Check gap reports. Only process them if the cumulative ACK TSN was acceptable.
                    if (processGapReports && sack.GapAckBlocks.Count > 0)
                    {
                        bool didIncrementCumAckTSN = SctpDataReceiver.IsNewer(cumAckTSNBeforeSackProcessing, _cumulativeAckTSN);
                        ProcessGapReports(sack.GapAckBlocks, maxTSNDistance, didIncrementCumAckTSN);
                    }

                    // rfc4960 6.2.1 D iv
                    // If the Cumulative TSN Ack matches or exceeds the Fast Recovery exitpoint(Section 7.2.4), Fast Recovery is exited.
                    if (_inFastRecoveryMode && SctpDataReceiver.IsNewerOrEqual(_fastRecoveryExitPoint, _cumulativeAckTSN))
                    {
                        logger.LogTrace($"SCTP sender exiting fast recovery at TSN {_fastRecoveryExitPoint}");
                        _inFastRecoveryMode = false;
                    }
                }

                _receiverWindow = CalculateReceiverWindow(sack.ARwnd);
                _congestionWindow = CalculateCongestionWindow(_lastAckedDataChunkSize);

                // SACK's will normally allow more data to be sent.
                _senderMre.Set();
            }
        }

        /// <summary>
        /// Sends a DATA chunk to the remote peer.
        /// </summary>
        /// <param name="streamID">The stream ID to sent the data on.</param>
        /// <param name="ppid">The payload protocol ID for the data.</param>
        /// <param name="message">The byte data to send.</param>
        public void SendData(ushort streamID, uint ppid, byte[] data)
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
                        false,
                        isBegining,
                        isEnd,
                        TSN,
                        streamID,
                        seqnum,
                        ppid,
                        payload);

                    _sendQueue.Enqueue(dataChunk);

                    TSN = (TSN == UInt32.MaxValue) ? 0 : TSN + 1;
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
        /// <param name="didSackIncrementTSN">If true, processing of the SACK incremented the <see cref="_cumulativeAckTSN"/></param>
        private void ProcessGapReports(List<SctpTsnGapBlock> sackGapBlocks, uint maxTSNDistance, bool didSackIncrementTSN)
        {
            uint lastAckTSN = _cumulativeAckTSN;

            // https://www.rfc-editor.org/rfc/rfc4960#section-7.2.4
            // For each incoming SACK, miss indications are incremented only for missing TSNs prior to the highest TSN newly acknowledged in the SACK.
            uint highestTsnNewlyAcknowledged = lastAckTSN;

            unchecked {

                // Parse the gap report to identify missing chunks that have now been acknowledged in the gap report
                foreach (var block in sackGapBlocks)
                {
                    for (ushort offset = block.Start; offset <= block.End; offset++)
                    {
                        uint goodTSN = _cumulativeAckTSN + offset;

                        _missingChunks.TryRemove(goodTSN, out _);
                        if (_unconfirmedChunks.TryRemove(goodTSN, out _))
                        {
                            logger.LogTrace($"SCTP acknowledged data chunk receipt in gap report for TSN {goodTSN}");
                            highestTsnNewlyAcknowledged = goodTSN;
                        }
                    }
                }

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
                            if (!_missingChunks.TryGetValue(missingTSN, out int missCount))
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
                                    _missingChunks.TryAdd(missingTSN, 1);
                                }
                            }
                            else if (
                                // If an endpoint is in Fast Recovery and a SACK arrives that advances the Cumulative TSN Ack
                                // Point, the miss indications are incremented for all TSNs reported missing in the SACK.
                                (_inFastRecoveryMode && didSackIncrementTSN) ||
                                //  For each incoming SACK, miss indications are incremented only
                                //  for missing TSNs prior to the highest TSN newly acknowledged in the SACK.
                                SctpDataReceiver.IsNewer(missingTSN, highestTsnNewlyAcknowledged))
                            {
                                _missingChunks.TryUpdate(missingTSN, missCount + 1, missCount);

                                // rfc 7.2.4: When the third consecutive miss indication is received for a TSN(s), the data sender shall do the following...
                                if (missCount + 1 == 3)
                                {
                                    if (!_inFastRecoveryMode) // RFC4960 7.2.4 (2)
                                    {
                                        _inFastRecoveryMode = true;
                                        // mark the highest outstanding TSN as the Fast Recovery exit point
                                        _fastRecoveryExitPoint = _cumulativeAckTSN + sackGapBlocks.Last().End;

                                        logger.LogTrace($"SCTP sender entering fast recovery mode due to missing TSN {missingTSN}. Fast recovery exit point {_fastRecoveryExitPoint}.");
                                        // RFC4960 7.2.3
                                        _slowStartThreshold = (uint)Math.Max(_congestionWindow / 2, 4 * _defaultMTU);
                                        _congestionWindow = _defaultMTU;
                                    }
                                }
                            }

                            missingTSN++;
                        }
                    }

                    lastAckTSN = _cumulativeAckTSN + gapBlock.End;
                }
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
                unchecked
                {
                    for (uint i = 0; i < SctpDataReceiver.GetDistance(_cumulativeAckTSN, sackTSN); i++)
                    {
                        uint ackd = _cumulativeAckTSN + i + 1;
                        _unconfirmedChunks.TryRemove(ackd, out _);
                        _missingChunks.TryRemove(ackd, out _);
                    }
                    _cumulativeAckTSN = sackTSN;
                }
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
                var outstandingBytes = _outstandingBytes;
                // DateTime.Now calls have been a tiny bit expensive in the past so get a small saving by only
                // calling once per loop.
                DateTime now = DateTime.Now;

                int burstSize = (_inRetransmitMode || _inFastRecoveryMode || _congestionWindow < outstandingBytes || _receiverWindow == 0) ? 1 : MAX_BURST;
                int chunksSent = 0;

                //logger.LogTrace($"SCTP sender burst size {burstSize}, in retransmit mode {_inRetransmitMode}, cwnd {_congestionWindow}, arwnd {_receiverWindow}.");

                // Missing chunks from a SACK gap report take priority.
                if (_missingChunks.Count > 0)
                {
                    foreach (var missing in _missingChunks)
                    {
                        if (missing.Value >= 3)  // RFC4960 7.2.4 Fast retransmission
                        {
                            if (_unconfirmedChunks.TryGetValue(missing.Key, out var missingChunk))
                            {
                                missingChunk.LastSentAt = now;
                                missingChunk.SendCount += 1;

                                logger.LogTrace($"SCTP resending missing data chunk for TSN {missingChunk.TSN}, data length {missingChunk.UserData.Length}, " +
                                    $"flags {missingChunk.ChunkFlags:X2}, send count {missingChunk.SendCount}.");

                                _sendDataChunk(missingChunk);
                                chunksSent++;
                                _missingChunks.TryUpdate(missing.Key, 0, missing.Value);
                            }
                        }
                        if (chunksSent >= burstSize)
                        {
                            break;
                        }
                    }
                }

                // Check if there are any unconfirmed transactions that are due for a retransmit.
                if (chunksSent < burstSize && _unconfirmedChunks.Count > 0)
                {
                    foreach (var chunk in _unconfirmedChunks.Values
                        .Where(x => now.Subtract(x.LastSentAt).TotalMilliseconds > (_hasRoundTripTime ? _rto : _rtoInitialMilliseconds))
                        .Take(burstSize - chunksSent))
                    {
                        chunk.LastSentAt = DateTime.Now;
                        chunk.SendCount += 1;

                        logger.LogTrace($"SCTP retransmitting data chunk for TSN {chunk.TSN}, data length {chunk.UserData.Length}, " +
                            $"flags {chunk.ChunkFlags:X2}, send count {chunk.SendCount}.");

                        _sendDataChunk(chunk);
                        chunksSent++;
                        
                        if (!_inRetransmitMode)
                        {
                            logger.LogTrace($"SCTP sender entering retransmit mode.");
                            _inRetransmitMode = true;

                            // When the T3-rtx timer expires on an address, SCTP should perform slow start.
                            // RFC4960 7.2.3
                            _slowStartThreshold = (uint)Math.Max(_congestionWindow / 2, 4 * _defaultMTU);
                            _congestionWindow = _defaultMTU;

                            // For the destination address for which the timer expires, set RTO <- RTO * 2("back off the timer")
                            // RFC4960 6.3.3 E2
                            if (_hasRoundTripTime)
                            {
                                _rto = Math.Min(_rtoMaximumMilliseconds, _rto * 2);
                            }
                        }
                    }
                }
                // rfc4960 6.1: At any given time, the sender MUST NOT transmit new data to a given transport address
                // if it has cwnd or more bytes of data outstanding to that transport address.

                // Send any new data chunks that have not yet been sent.
                if (chunksSent < burstSize && _sendQueue.Count > 0 && _congestionWindow > outstandingBytes)
                {
                    while (chunksSent < burstSize && _sendQueue.TryDequeue(out var dataChunk))
                    {
                        dataChunk.LastSentAt = DateTime.Now;
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
                return (int)(_hasRoundTripTime ? _rto : _rtoInitialMilliseconds);
            }
            else
            {
                return _rtoInitialMilliseconds;
            }
        }


        /// <summary>
        /// Updates the round trip time. 
        /// See https://datatracker.ietf.org/doc/html/rfc4960#section-6.3.1
        /// </summary>
        /// <param name="rttMilliseconds">The last round trip time</param>
        private void UpdateRoundTripTime(SctpDataChunk acknowledgedChunk)
        {
            // rfc 4960 6.3.1 C5: RTT measurements MUST NOT be made using packets that were retransmitted
            if (acknowledgedChunk.SendCount > 1)
            {
                return;
            }

            var rttMilliseconds = (DateTime.Now - acknowledgedChunk.LastSentAt).TotalMilliseconds;

            if (!_hasRoundTripTime)
            {
                // rfc 4960 6.3.1 C2
                _smoothedRoundTripTime = rttMilliseconds;
                _roundTripTimeVariation = rttMilliseconds / 2;
                _rto = _smoothedRoundTripTime + 4 * _roundTripTimeVariation;
                _hasRoundTripTime = true;
            }
            else
            {
                // rfc 4960 6.3.1 C3
                _roundTripTimeVariation = (1 - _rtoBeta) * _roundTripTimeVariation + _rtoBeta * Math.Abs(_smoothedRoundTripTime - rttMilliseconds);
                _smoothedRoundTripTime = (1 - _rtoAlpha) * _smoothedRoundTripTime + _rtoAlpha * rttMilliseconds;
                _rto = _smoothedRoundTripTime + 4 * _roundTripTimeVariation;
            }

            // rfc 4960 6.3.1 C6-7
            _rto = Math.Min(Math.Max(_rto, _rtoMinimumMilliseconds), _rtoMaximumMilliseconds);
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
    }
}
