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
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class SctpDataSender
    {
        public const ushort DEFAULT_SCTP_MTU = 1300;

        private static ILogger logger = LogFactory.CreateLogger<SctpDataSender>();

        /// <summary>
        /// Callback method that sends data chunks.
        /// </summary>
        private Action<SctpDataChunk> _sendDataChunk;

        private ushort _defaultMTU;
        private uint _initialTSN;
        private bool _gotFirstSACK;

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
        private Queue<SctpDataFrame> _pendingSends = new Queue<SctpDataFrame>();

        /// <summary>
        /// Chunks that have been sent to the remote peer but have yet to be acknowledged.
        /// </summary>
        private ConcurrentDictionary<uint, SctpDataChunk> _unconfirmedChunks = new ConcurrentDictionary<uint, SctpDataChunk>();

        /// <summary>
        /// Chunks that have been flagged by a gap report from the remote peer as requiring
        /// a retransmit.
        /// </summary>
        private ConcurrentDictionary<uint, int> _retransmitChunks = new ConcurrentDictionary<uint, int>();

        /// <summary>
        /// The number of bytes that have been sent to the remote peer and that
        /// have not yet been acknowledged with a SACK chunk.
        /// </summary>
        //private uint _outstandingByteCount;

        public uint TSN { get; internal set; }

        /// <summary>
        /// Advertised Receiver Window Credit. This value represents the dedicated 
        /// buffer space on the remote peer, in number of bytes, that will be used 
        /// for the receive buffer for DATA chunks sent by this association.
        /// </summary>
        public uint RemoteARwnd { get; internal set; }

        internal SctpDataChunk NextRetransmitChunk =>
            _retransmitChunks.Count > 0 ? _unconfirmedChunks[_retransmitChunks.First().Key] : null;

        public SctpDataSender(
            Action<SctpDataChunk> sendDataChunk,
            ushort defaultMTU,
            uint initialTSN,
            uint remoteARwnd)
        {
            _sendDataChunk = sendDataChunk;
            _defaultMTU = defaultMTU > 0 ? defaultMTU : DEFAULT_SCTP_MTU;
            _initialTSN = initialTSN;
            TSN = initialTSN;
            RemoteARwnd = remoteARwnd;
        }

        /// <summary>
        /// Handler for SACK chunks received from the remote peer.
        /// </summary>
        /// <param name="sack">The SACK chunk from the remote peer.</param>
        internal void GotSack(SctpSackChunk sack)
        {
            unchecked
            {
                uint maxTSNDistance = SctpDataReceiver.GetDistance(_cumulativeAckTSN, TSN);
                bool processGapReports = true;

                if (!_gotFirstSACK)
                {
                    if (SctpDataReceiver.GetDistance(_initialTSN, sack.CumulativeTsnAck) < maxTSNDistance
                        && SctpDataReceiver.IsNewer(_initialTSN, sack.CumulativeTsnAck))
                    {
                        _gotFirstSACK = true;
                        _cumulativeAckTSN = sack.CumulativeTsnAck;

                        logger.LogTrace($"SCTP remote peer TSN ACK {_cumulativeAckTSN} current {TSN - 1} (gap reports {sack.GapAckBlocks.Count}).");
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
                            while (_cumulativeAckTSN != sack.CumulativeTsnAck)
                            {
                                if (!_unconfirmedChunks.TryRemove(_cumulativeAckTSN, out _))
                                {
                                    logger.LogWarning($"SCTP data sender could not remove unconfirmed chunk for {_cumulativeAckTSN}.");
                                    break;
                                }
                                _cumulativeAckTSN++;
                            }

                            logger.LogTrace($"SCTP remote peer TSN ACK {_cumulativeAckTSN} current {TSN - 1} (gap reports {sack.GapAckBlocks.Count}).");
                        }
                    }
                }

                // Check gap reports. Only process them if the cumulative ACK TSN was acceptable.
                if (processGapReports && sack.GapAckBlocks.Count > 0)
                {
                    uint lastAckTSN = _cumulativeAckTSN;

                    foreach (var gapBlock in sack.GapAckBlocks)
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
                                    }
                                    else
                                    {
                                        logger.LogTrace($"SCTP SACK gap adding retransmit entry for TSN {missingTSN}.");
                                        //_retransmitChunks.TryAdd(missingTSN, 0);
                                        _sendDataChunk(_unconfirmedChunks[missingTSN]);
                                    }
                                }

                                missingTSN++;
                            }
                        }

                        lastAckTSN = _cumulativeAckTSN + gapBlock.End;
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

            // If the remote peer's receive window has space available do an immediate send otherwise queue.

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

                _unconfirmedChunks.TryAdd(TSN, dataChunk);

                _sendDataChunk(dataChunk);

                TSN = (TSN == UInt32.MaxValue) ? 0 : TSN + 1;
            }
        }
    }
}
