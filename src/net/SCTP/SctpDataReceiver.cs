//-----------------------------------------------------------------------------
// Filename: SctpDataReceiver.cs
//
// Description: This class is used to collate incoming DATA chunks into full
// frames.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 29 Mar 2021	Aaron Clauson	Created, Dublin, Ireland.
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

namespace SIPSorcery.Net
{
    public struct SctpDataFrame
    {
        public static SctpDataFrame Empty = new SctpDataFrame();

        public bool Unordered;
        public ushort StreamID;
        public ushort StreamSeqNum;
        public uint PPID;
        public byte[] UserData;

        /// <param name="streamID">The stream ID of the chunk.</param>
        /// <param name="streamSeqNum">The stream sequence number of the chunk. Will be 0 for unordered streams.</param>
        /// <param name="ppid">The payload protocol ID for the chunk.</param>
        /// <param name="userData">The chunk data.</param>
        public SctpDataFrame(bool unordered, ushort streamID, ushort streamSeqNum, uint ppid, byte[] userData)
        {
            Unordered = unordered;
            StreamID = streamID;
            StreamSeqNum = streamSeqNum;
            PPID = ppid;
            UserData = userData;
        }

        public bool IsEmpty()
        {
            return UserData == null;
        }
    }

    public struct SctpTsnGapBlock
    {
        /// <summary>
        /// Indicates the Start offset TSN for this Gap Ack Block.  To
        /// calculate the actual TSN number the Cumulative TSN Ack is added to
        /// this offset number.This calculated TSN identifies the first TSN
        /// in this Gap Ack Block that has been received.
        /// </summary>
        public ushort Start;

        /// <summary>
        /// Indicates the End offset TSN for this Gap Ack Block.  To calculate
        /// the actual TSN number, the Cumulative TSN Ack is added to this
        /// offset number.This calculated TSN identifies the TSN of the last
        /// DATA chunk received in this Gap Ack Block.
        /// </summary>
        public ushort End;
    }

    /// <summary>
    /// Processes incoming data chunks and handles fragmentation and congestion control. This
    /// class does NOT handle in order delivery. Different streams on the same association
    /// can have different ordering requirements so it's left up to each stream handler to
    /// deal with full frames as they see fit.
    /// </summary>
    public class SctpDataReceiver
    {
        /// <summary>
        /// The window size is the maximum number of entries that can be recorded in the 
        /// receive dictionary.
        /// </summary>
        private const ushort WINDOW_SIZE_MINIMUM = 100;

        /// <summary>
        /// The maximum number of out of order frames that will be queued per stream ID.
        /// </summary>
        private const int MAXIMUM_OUTOFORDER_FRAMES = 25;

        /// <summary>
        /// The maximum size of an SCTP fragmented message.
        /// </summary>
        private const int MAX_FRAME_SIZE = 262144;

        private static ILogger logger = LogFactory.CreateLogger<SctpDataReceiver>();

        /// <summary>
        /// This dictionary holds data chunk Transaction Sequence Numbers (TSN) that have
        /// been received out of order and are in advance of the expected TSN.
        /// </summary>
        private SortedDictionary<uint, int> _forwardTSN = new SortedDictionary<uint, int>();

        /// <summary>
        /// Storage for fragmented chunks.
        /// </summary>
        private Dictionary<uint, SctpDataChunk> _fragmentedChunks = new Dictionary<uint, SctpDataChunk>();

        /// <summary>
        /// Keeps track of the latest sequence number for each stream. Used to ensure
        /// stream chunks are delivered in order.
        /// </summary>
        private Dictionary<ushort, ushort> _streamLatestSeqNums = new Dictionary<ushort, ushort>();

        /// <summary>
        /// A dictionary of dictionaries used to hold out of order stream chunks.
        /// </summary>
        private Dictionary<ushort, Dictionary<ushort, SctpDataFrame>> _streamOutOfOrderFrames =
            new Dictionary<ushort, Dictionary<ushort, SctpDataFrame>>();

        /// <summary>
        /// The maximum amount of received data that will be stored at any one time.
        /// This is part of the SCTP congestion window mechanism. It limits the number
        /// of bytes, a sender can send to a particular destination transport address 
        /// before receiving an acknowledgement.
        /// </summary>
        private uint _receiveWindow;

        /// <summary>
        /// The most recent in order TSN received. This is the value that gets used
        /// in the "Cumulative TSN Ack" field to SACK chunks. 
        /// </summary>
        private uint _lastInOrderTSN;

        /// <summary>
        /// The window size is the maximum number of chunks we're prepared to hold in the 
        /// receive dictionary.
        /// </summary>
        private ushort _windowSize;

        /// <summary>
        /// Record of the duplicate Transaction Sequence Number (TSN) chunks received since
        /// the last SACK chunk was generated.
        /// </summary>
        private Dictionary<uint, int> _duplicateTSN = new Dictionary<uint, int>();

        /// <summary>
        /// Gets the Transaction Sequence Number (TSN) that can be acknowledged to the remote peer.
        /// It represents the most recent in order TSN that has been received. If no in order
        /// TSN's have been received then null will be returned.
        /// </summary>
        public uint? CumulativeAckTSN => (_inOrderReceiveCount > 0) ? _lastInOrderTSN : (uint?)null;

        /// <summary>
        /// A count of the total entries in the receive dictionary. Note that if chunks
        /// have been received out of order this count could include chunks that have
        /// already been processed. They are kept in the dictionary as empty chunks to
        /// track which TSN's have been received.
        /// </summary>
        public int ForwardTSNCount => _forwardTSN.Count;

        private uint _initialTSN;
        private uint _inOrderReceiveCount;

        /// <summary>
        /// Creates a new SCTP data receiver instance.
        /// </summary>
        /// <param name="receiveWindow">The size of the receive window. This is the window around the 
        /// expected Transaction Sequence Number (TSN). If a data chunk is received with a TSN outside
        /// the window it is ignored.</param>
        /// <param name="mtu">The Maximum Transmission Unit for the network layer that the SCTP
        /// association is being used with.</param>
        /// <param name="initialTSN">The initial TSN for the association from the INIT handshake.</param>
        public SctpDataReceiver(uint receiveWindow, uint mtu, uint initialTSN)
        {
            _receiveWindow = receiveWindow != 0 ? receiveWindow : SctpAssociation.DEFAULT_ADVERTISED_RECEIVE_WINDOW;
            _initialTSN = initialTSN;

            mtu = mtu != 0 ? mtu : SctpUdpTransport.DEFAULT_UDP_MTU;
            _windowSize = (ushort)(_receiveWindow / mtu);
            _windowSize = (_windowSize < WINDOW_SIZE_MINIMUM) ? WINDOW_SIZE_MINIMUM : _windowSize;

            logger.LogDebug("SCTP windows size for data receiver set at {WindowSize}.", _windowSize);
        }

        /// <summary>
        /// Used to set the initial TSN for the remote party when it's not known at creation time.
        /// </summary>
        /// <param name="tsn">The initial Transaction Sequence Number (TSN) for the 
        /// remote party.</param>
        public void SetInitialTSN(uint tsn)
        {
            _initialTSN = tsn;
        }

        /// <summary>
        /// Handler for processing new data chunks.
        /// </summary>
        /// <param name="dataChunk">The newly received data chunk.</param>
        /// <returns>If the received chunk resulted in a full chunk becoming available one 
        /// or more new frames will be returned otherwise an empty frame is returned. Multiple
        /// frames may be returned if this chunk is part of a stream and was received out
        /// or order. For unordered chunks the list will always have a single entry.</returns>
        public List<SctpDataFrame> OnDataChunk(SctpDataChunk dataChunk)
        {
            var sortedFrames = new List<SctpDataFrame>();
            var frame = SctpDataFrame.Empty;

            if (_inOrderReceiveCount == 0 &&
                GetDistance(_initialTSN, dataChunk.TSN) > _windowSize)
            {
                logger.LogWarning("SCTP data receiver received a data chunk with a TSN {TSN} when the initial TSN was {InitialTSN} and a window size of {WindowSize}, ignoring.", dataChunk.TSN, _initialTSN, _windowSize);
            }
            else if (_inOrderReceiveCount > 0 &&
                GetDistance(_lastInOrderTSN, dataChunk.TSN) > _windowSize)
            {
                logger.LogWarning("SCTP data receiver received a data chunk with a TSN {TSN} when the expected TSN was {ExpectedTSN} and a window size of {WindowSize}, ignoring.", dataChunk.TSN, _lastInOrderTSN + 1, _windowSize);
            }
            else if (_inOrderReceiveCount > 0 &&
                !IsNewer(_lastInOrderTSN, dataChunk.TSN))
            {
                logger.LogWarning("SCTP data receiver received an old data chunk with TSN {TSN} when the expected TSN was {ExpectedTSN}, ignoring.", dataChunk.TSN, _lastInOrderTSN + 1);
            }
            else if (!_forwardTSN.ContainsKey(dataChunk.TSN))
            {
                logger.LogTrace("SCTP receiver got data chunk with TSN {TSN}, last in order TSN {LastInOrderTSN}, in order receive count {InOrderReceiveCount}.", dataChunk.TSN, _lastInOrderTSN, _inOrderReceiveCount);

                bool processFrame = true;

                // Relying on unsigned integer wrapping.
                unchecked
                {
                    if ((_inOrderReceiveCount > 0 && _lastInOrderTSN + 1 == dataChunk.TSN) ||
                        (_inOrderReceiveCount == 0 && dataChunk.TSN == _initialTSN))
                    {
                        _inOrderReceiveCount++;
                        _lastInOrderTSN = dataChunk.TSN;

                        // See if the in order TSN can be bumped using any out of order chunks 
                        // already received.
                        if (_inOrderReceiveCount > 0 && _forwardTSN.Count > 0)
                        {
                            while (_forwardTSN.ContainsKey(_lastInOrderTSN + 1))
                            {
                                _lastInOrderTSN++;
                                _inOrderReceiveCount++;
                                _forwardTSN.Remove(_lastInOrderTSN);
                            }
                        }
                    }
                    else
                    {
                        if (!dataChunk.Unordered &&
                            _streamOutOfOrderFrames.TryGetValue(dataChunk.StreamID, out var outOfOrder) &&
                            outOfOrder.Count >= MAXIMUM_OUTOFORDER_FRAMES)
                        {
                            // Stream is nearing capacity, only chunks that advance _lastInOrderTSN can be accepted. 
                            logger.LogWarning("Stream {StreamID} is at buffer capacity. Rejected out-of-order data chunk TSN {TSN}.", dataChunk.StreamID, dataChunk.TSN);
                            processFrame = false;
                        }
                        else
                        {
                            _forwardTSN.Add(dataChunk.TSN, 1);
                        }
                    }
                }

                if (processFrame)
                {
                    // Now go about processing the data chunk.
                    if (dataChunk.Begining && dataChunk.Ending)
                    {
                        // Single packet chunk.
                        frame = new SctpDataFrame(
                            dataChunk.Unordered,
                            dataChunk.StreamID,
                            dataChunk.StreamSeqNum,
                            dataChunk.PPID,
                            dataChunk.UserData);
                    }
                    else
                    {
                        // This is a data chunk fragment.
                        _fragmentedChunks.Add(dataChunk.TSN, dataChunk);
                        (var begin, var end) = GetChunkBeginAndEnd(_fragmentedChunks, dataChunk.TSN);

                        if (begin != null && end != null)
                        {
                            frame = GetFragmentedChunk(_fragmentedChunks, begin.Value, end.Value);
                        }
                    }
                }
            }
            else
            {
                logger.LogTrace("SCTP duplicate TSN received for {TSN}.", dataChunk.TSN);
                if (!_duplicateTSN.ContainsKey(dataChunk.TSN))
                {
                    _duplicateTSN.Add(dataChunk.TSN, 1);
                }
                else
                {
                    _duplicateTSN[dataChunk.TSN] = _duplicateTSN[dataChunk.TSN] + 1;
                }
            }

            if (!frame.IsEmpty() && !dataChunk.Unordered)
            {
                return ProcessStreamFrame(frame);
            }
            else
            {
                if (!frame.IsEmpty())
                {
                    sortedFrames.Add(frame);
                }

                return sortedFrames;
            }
        }

        /// <summary>
        /// Gets a SACK chunk that represents the current state of the receiver.
        /// </summary>
        /// <returns>A SACK chunk that can be sent to the remote peer to update the ACK TSN and
        /// request a retransmit of any missing DATA chunks.</returns>
        public SctpSackChunk GetSackChunk()
        {
            // Can't create a SACK until the initial DATA chunk has been received.
            if (_inOrderReceiveCount > 0)
            {
                SctpSackChunk sack = new SctpSackChunk(_lastInOrderTSN, _receiveWindow);
                sack.GapAckBlocks = GetForwardTSNGaps();
                sack.DuplicateTSN = _duplicateTSN.Keys.ToList();
                return sack;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Gets a list of the gaps in the forward TSN records. Typically the TSN gap
        /// reports are used in SACK chunks to inform the remote peer which DATA chunk
        /// TSNs have not yet been received.
        /// </summary>
        /// <returns>A list of TSN gap blocks. An empty list means there are no gaps.</returns>
        internal List<SctpTsnGapBlock> GetForwardTSNGaps()
        {
            List<SctpTsnGapBlock> gaps = new List<SctpTsnGapBlock>();

            // Can't create gap reports until the initial DATA chunk has been received.
            if (_inOrderReceiveCount > 0)
            {
                uint tsnAck = _lastInOrderTSN;

                if (_forwardTSN.Count > 0)
                {
                    ushort? start = null;
                    uint prev = 0;

                    foreach (var tsn in _forwardTSN.Keys)
                    {
                        if (start == null)
                        {
                            start = (ushort)(tsn - tsnAck);
                            prev = tsn;
                        }
                        else if (tsn != prev + 1)
                        {
                            ushort end = (ushort)(prev - tsnAck);
                            gaps.Add(new SctpTsnGapBlock { Start = start.Value, End = end });
                            start = (ushort)(tsn - tsnAck);
                            prev = tsn;
                        }
                        else
                        {
                            prev++;
                        }
                    }

                    gaps.Add(new SctpTsnGapBlock { Start = start.Value, End = (ushort)(prev - tsnAck) });
                }
            }

            return gaps;
        }

        /// <summary>
        /// Processes a data frame that is now ready and that is part of an SCTP stream.
        /// Stream frames must be delivered in order.
        /// </summary>
        /// <param name="frame">The data frame that became ready from the latest DATA chunk receive.</param>
        /// <returns>A sorted list of frames for the matching stream ID. Will be empty
        /// if the supplied frame is out of order for its stream.</returns>
        private List<SctpDataFrame> ProcessStreamFrame(SctpDataFrame frame)
        {
            // Relying on unsigned short wrapping.
            unchecked
            {
                // This is a stream chunk. Need to ensure in order delivery.
                var sortedFrames = new List<SctpDataFrame>();

                if (!_streamLatestSeqNums.ContainsKey(frame.StreamID))
                {
                    // First frame for this stream.
                    _streamLatestSeqNums.Add(frame.StreamID, frame.StreamSeqNum);
                    sortedFrames.Add(frame);
                }
                else if ((ushort)(_streamLatestSeqNums[frame.StreamID] + 1) == frame.StreamSeqNum)
                {
                    // Expected seqnum for stream.
                    _streamLatestSeqNums[frame.StreamID] = frame.StreamSeqNum;
                    sortedFrames.Add(frame);

                    // There could also be out of order frames that can now be delivered.
                    if (_streamOutOfOrderFrames.ContainsKey(frame.StreamID) &&
                        _streamOutOfOrderFrames[frame.StreamID].Count > 0)
                    {
                        var outOfOrder = _streamOutOfOrderFrames[frame.StreamID];

                        ushort nextSeqnum = (ushort)(_streamLatestSeqNums[frame.StreamID] + 1);
                        while (outOfOrder.ContainsKey(nextSeqnum) &&
                            outOfOrder.TryGetValue(nextSeqnum, out var nextFrame))
                        {
                            sortedFrames.Add(nextFrame);
                            _streamLatestSeqNums[frame.StreamID] = nextSeqnum;
                            outOfOrder.Remove(nextSeqnum);
                            nextSeqnum++;
                        }
                    }
                }
                else
                {
                    // Stream seqnum is out of order.
                    if (!_streamOutOfOrderFrames.ContainsKey(frame.StreamID))
                    {
                        _streamOutOfOrderFrames[frame.StreamID] = new Dictionary<ushort, SctpDataFrame>();
                    }

                    _streamOutOfOrderFrames[frame.StreamID].Add(frame.StreamSeqNum, frame);
                }

                return sortedFrames;
            }
        }

        /// <summary>
        /// Checks whether the fragmented chunk for the supplied TSN is complete and if so
        /// returns its begin and end TSNs.
        /// </summary>
        /// <param name="tsn">The TSN of the fragmented chunk to check for completeness.</param>
        /// <param name="fragments">The dictionary containing the chunk fragments.</param>
        /// <returns>If the chunk is complete the begin and end TSNs will be returned. If
        /// the fragmented chunk is incomplete one or both of the begin and/or end TSNs will be null.</returns>
        private (uint?, uint?) GetChunkBeginAndEnd(Dictionary<uint, SctpDataChunk> fragments, uint tsn)
        {
            unchecked
            {
                uint? beginTSN = fragments[tsn].Begining ? (uint?)tsn : null;
                uint? endTSN = fragments[tsn].Ending ? (uint?)tsn : null;

                uint revTSN = tsn - 1;
                while (beginTSN == null && fragments.ContainsKey(revTSN))
                {
                    if (fragments[revTSN].Begining)
                    {
                        beginTSN = revTSN;
                    }
                    else
                    {
                        revTSN--;
                    }
                }

                if (beginTSN != null)
                {
                    uint fwdTSN = tsn + 1;
                    while (endTSN == null && fragments.ContainsKey(fwdTSN))
                    {
                        if (fragments[fwdTSN].Ending)
                        {
                            endTSN = fwdTSN;
                        }
                        else
                        {
                            fwdTSN++;
                        }
                    }
                }

                return (beginTSN, endTSN);
            }
        }

        /// <summary>
        /// Extracts a fragmented chunk from the receive dictionary and passes it to the ULP.
        /// </summary>
        /// <param name="fragments">The dictionary containing the chunk fragments.</param>
        /// <param name="beginTSN">The beginning TSN for the fragment.</param>
        /// <param name="endTSN">The end TSN for the fragment.</param>
        private SctpDataFrame GetFragmentedChunk(Dictionary<uint, SctpDataChunk> fragments, uint beginTSN, uint endTSN)
        {
            unchecked
            {
                byte[] full = new byte[MAX_FRAME_SIZE];
                int posn = 0;
                var beginChunk = fragments[beginTSN];
                var frame = new SctpDataFrame(beginChunk.Unordered, beginChunk.StreamID, beginChunk.StreamSeqNum, beginChunk.PPID, full);

                uint afterEndTSN = endTSN + 1;
                uint tsn = beginTSN;

                while (tsn != afterEndTSN)
                {
                    var fragment = fragments[tsn].UserData;
                    Buffer.BlockCopy(fragment, 0, full, posn, fragment.Length);
                    posn += fragment.Length;
                    fragments.Remove(tsn);
                    tsn++;
                }

                frame.UserData = frame.UserData.Take(posn).ToArray();

                return frame;
            }
        }

        /// <summary>
        /// Determines if a received TSN is newer than the expected TSN taking
        /// into account if TSN wrap around has occurred.
        /// </summary>
        /// <param name="tsn">The TSN to compare against.</param>
        /// <param name="receivedTSN">The received TSN.</param>
        /// <returns>True if the received TSN is newer than the reference TSN
        /// or false if not.</returns>
        public static bool IsNewer(uint tsn, uint receivedTSN)
        {
            if (tsn < uint.MaxValue / 2 && receivedTSN > uint.MaxValue / 2)
            {
                // TSN wrap has occurred and the received TSN is old.
                return false;
            }
            else if (tsn > uint.MaxValue / 2 && receivedTSN < uint.MaxValue / 2)
            {
                // TSN wrap has occurred and the received TSN is new.
                return true;
            }
            else
            {
                return receivedTSN > tsn;
            }
        }

        public static bool IsNewerOrEqual(uint tsn, uint receivedTSN)
        {
            return tsn == receivedTSN || IsNewer(tsn, receivedTSN);
        }

        /// <summary>
        /// Gets the distance between two unsigned integers. The "distance" means how many 
        /// points are there between the two unsigned integers and allows wrapping from
        /// the unsigned integer maximum to zero.
        /// </summary>
        /// <returns>The shortest distance between the two unsigned integers.</returns>
        public static uint GetDistance(uint start, uint end)
        {
            uint fwdDistance = end - start;
            uint backDistance = start - end;

            return (fwdDistance < backDistance) ? fwdDistance : backDistance;
        }
    }
}
