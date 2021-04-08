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

        /// <summary>
        /// Used to limit the number of packets that are sent at any one time, i.e. when 
        /// the transmit timer fires do not send more than this many packets.
        /// </summary>
        public const int MAX_BURST = 4;

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

        public const decimal RTO_ALPHA = 0.125M;

        public const decimal RTO_BETA = 0.25M;

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
        private ManualResetEventSlim _senderMre = new ManualResetEventSlim();

        /// <summary>
        /// Congestion control window (cwnd, in bytes), which is adjusted by
        /// the sender based on observed network conditions.
        /// </summary>
        private uint _congestionWindow;

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
        /// Chunks that have been flagged by a gap report from the remote peer as requiring
        /// a retransmit.
        /// </summary>
        internal ConcurrentDictionary<uint, int> _retransmitChunks = new ConcurrentDictionary<uint, int>();

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

            // RFC4960 7.2.1 (point 1)
            _congestionWindow = (uint)(Math.Min(4 * _defaultMTU, Math.Max(2 * _defaultMTU, 4380)));

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
                unchecked
                {
                    uint maxTSNDistance = SctpDataReceiver.GetDistance(_cumulativeAckTSN, TSN);
                    bool processGapReports = true;

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
                        }
                    }

                    // Check gap reports. Only process them if the cumulative ACK TSN was acceptable.
                    if (processGapReports && sack.GapAckBlocks.Count > 0)
                    {
                        ProcessGapReports(sack.GapAckBlocks, maxTSNDistance);
                    }
                }
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
                        if (!_retransmitChunks.ContainsKey(missingTSN))
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
                                _retransmitChunks.TryAdd(missingTSN, 0);
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
            }
            else
            {
                int safety = _unconfirmedChunks.Count();

                do
                {
                    _cumulativeAckTSN++;
                    safety--;

                    if (!_unconfirmedChunks.TryRemove(_cumulativeAckTSN, out _))
                    {
                        logger.LogWarning($"SCTP data sender could not remove unconfirmed chunk for {_cumulativeAckTSN}.");
                    }
                } while (_cumulativeAckTSN != sackTSN && safety > 0);
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

                int chunksSent = 0;

                // Retransmits take priority.
                if (_retransmitChunks.Count > 0)
                {
                    var retransmits = _retransmitChunks.GetEnumerator();
                    bool haveRetransmit = retransmits.MoveNext();

                    while (chunksSent < MAX_BURST && haveRetransmit)
                    {
                        if (_unconfirmedChunks.ContainsKey(retransmits.Current.Key))
                        {
                            var retransmitChunk = _unconfirmedChunks[retransmits.Current.Key];

                            retransmitChunk.LastSentAt = now;
                            retransmitChunk.SendCount += 1;

                            logger.LogTrace($"SCTP retransmitting data chunk for TSN {retransmitChunk.TSN}, data length {retransmitChunk.UserData.Length}, " +
                                $"flags {retransmitChunk.ChunkFlags:X2}, send count {retransmitChunk.SendCount}.");

                            _sendDataChunk(retransmitChunk);
                            chunksSent++;
                        }

                        haveRetransmit = retransmits.MoveNext();
                    }
                }

                // Check if there are any unconfirmed transactions that are due for a retransmit.
                if (chunksSent < MAX_BURST && _unconfirmedChunks.Count > 0)
                {
                    foreach (var chunk in _unconfirmedChunks.Values
                        .Where(x => now.Subtract(x.LastSentAt).TotalSeconds > RTO_MIN_SECONDS)
                        .Take(MAX_BURST - chunksSent))
                    {
                        chunk.LastSentAt = DateTime.Now;
                        chunk.SendCount += 1;

                        logger.LogTrace($"SCTP retransmitting data chunk for TSN {chunk.TSN}, data length {chunk.UserData.Length}, " +
                            $"flags {chunk.ChunkFlags:X2}, send count {chunk.SendCount}.");

                        _sendDataChunk(chunk);
                        chunksSent++;
                    }
                }

                // Finally send any new data chunks that have not yet been sent.
                if (chunksSent < MAX_BURST && _sendQueue.Count > 0)
                {
                    while (chunksSent < MAX_BURST && _sendQueue.TryDequeue(out var dataChunk))
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
                // TODO. Fix this timeout.
                _senderMre.Wait(50);
            }

            logger.LogDebug($"SCTP association data send thread stopped for association {_associationID}.");
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
            uint outstandingBytes = (uint)(_unconfirmedChunks.Sum(x => x.Value.UserData.Length));
            return (outstandingBytes > advertisedReceiveWindow) ? outstandingBytes - advertisedReceiveWindow : 0;
        }

        /// <summary>
        /// Calculates an updated value for the congestion window.
        /// </summary>
        /// <param name="lastAckDataChunkSize">The size of last ACK'ed DATA chunk.</param>
        /// <returns>A congestion window value.</returns>
        private uint CalculateCongestionWindow(uint lastAckDataChunkSize)
        {
            uint outstandingBytes = (uint)(_unconfirmedChunks.Sum(x => x.Value.UserData.Length));

            if (_congestionWindow < _slowStartThreshold)
            {
                // In Slow-Start mode, see RFC4960 7.2.1.

                if (_congestionWindow < outstandingBytes)
                {
                    // When cwnd is less than or equal to ssthresh, an SCTP endpoint MUST
                    // use the slow - start algorithm to increase cwnd only if the current
                    // congestion window is being fully utilized.
                    uint increasedCwnd = _congestionWindow + Math.Min(lastAckDataChunkSize, _defaultMTU);

                    logger.LogDebug($"SCTP sender congestion window can be increased from {_congestionWindow} to {increasedCwnd}.");

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

                if (outstandingBytes >= _congestionWindow)
                {
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
