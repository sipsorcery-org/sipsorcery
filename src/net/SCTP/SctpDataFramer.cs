//-----------------------------------------------------------------------------
// Filename: SctpDataFramer.cs
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

        public ushort StreamID;
        public ushort StreamSeqNum;
        public uint PPID;
        public byte[] UserData;

        public SctpDataFrame(ushort streamID, ushort streamSeqNum, uint ppid, byte[] userData)
        {
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

    /// <summary>
    /// Processes incoming data chunks and handles fragmentation and congestion control. This
    /// class does NOT handle in order delivery. Different streams on the same association
    /// can have different ordering requirements so it's left up to each stream handler to
    /// deal with full frames as they see fit.
    /// </summary>
    public class SctpDataFramer
    {
        /// <summary>
        /// The windows size is the maximum number of entries that can be recorded in the 
        /// <see cref="_receivedChunks"/> dictionary. 
        /// </summary>
        private const ushort WINDOW_SIZE_MINIMUM = 100;

        private static ILogger logger = LogFactory.CreateLogger<SctpDataFramer>();

        /// <summary>
        /// This dictionary holds data chunks that have been received within.
        /// </summary>
        private SortedDictionary<uint, SctpDataChunk> _receivedChunks = new SortedDictionary<uint, SctpDataChunk>();

        /// <summary>
        /// The maximum amount of received data that will be stored at any one time.
        /// This is part of the SCTP congestion window mechanism. It limits the number
        /// of bytes, a sender can send to a particular destination transport address 
        /// before receiving an acknowledgement.
        /// </summary>
        private uint _receiveWindow;

        /// <summary>
        /// The earliest TSN received.
        /// </summary>
        private uint _earliestTSN;

        /// <summary>
        /// The latest TSN received.
        /// </summary>
        private uint _latestTSN;

        /// <summary>
        /// The window size is the maximum number of chunks we're prepared to hold in the 
        /// receive dictionary.
        /// </summary>
        private ushort _windowSize;

        /// <summary>
        /// Count of the duplicate chunks received.
        /// </summary>
        private uint _duplicateTSNCount;

        internal int receivedChunksCount => _receivedChunks.Count;
        internal uint earliestTSN => _earliestTSN;
        internal uint latestTSN => _latestTSN;

        /// <summary>
        /// Creates a new SCTP framer instance.
        /// </summary>
        /// <param name="receiveWindow">The size of the receive window. This is the window around the 
        /// expected Transaction Sequence Number (TSN). If a data chunk is received with a TSN outside
        /// the window it is ignored.</param>
        /// <param name="mtu">The Maximum Transmission Unit for the network layer that the SCTP
        /// association is being used with.</param>
        /// <param name="initialTSN">The initial TSN for the association from the INIT handshake.</param>
        public SctpDataFramer(uint receiveWindow, uint mtu, uint initialTSN)
        {
            _receiveWindow = receiveWindow != 0 ? receiveWindow : SctpAssociation.DEFAULT_ADVERTISED_RECEIVE_WINDOW;
            _earliestTSN = initialTSN;
            _latestTSN = initialTSN;

            mtu = mtu != 0 ? mtu : SctpUdpTransport.DEFAULT_UDP_MTU;
            _windowSize = (ushort)(_receiveWindow / mtu);
            _windowSize = (_windowSize < WINDOW_SIZE_MINIMUM) ? WINDOW_SIZE_MINIMUM : _windowSize;

            logger.LogDebug($"SCTP windows size for framer set at {_windowSize}.");
        }

        /// <summary>
        /// Used to set the initial TSN for the remote party when it's not known at creation time.
        /// </summary>
        /// <param name="tsn">The initial Transaction Sequence Number (TSN) for the 
        /// remote party.</param>
        public void SetInitialTSN(uint tsn)
        {
            _earliestTSN = tsn;
            _latestTSN = tsn;
        }

        /// <summary>
        /// Handler for processing new data chunks.
        /// </summary>
        /// <param name="dataChunk">The newly received data chunk.</param>
        /// <returns>If the received chunk resulted in a full chunk becoming available a
        /// new frame will be returned otherwise an empty frame is returned.</returns>
        public SctpDataFrame OnDataChunk(SctpDataChunk dataChunk)
        {
            var frame = SctpDataFrame.Empty;

            if (GetDistance(_earliestTSN, dataChunk.TSN) > _windowSize &&
                GetDistance(_latestTSN, dataChunk.TSN) > _windowSize)
            {
                logger.LogWarning($"SCTP framer received a data chunk with a {dataChunk.TSN} " +
                    $"TSN when the current TSN range was {_earliestTSN} to {_latestTSN} and a " +
                    $"window size of {_windowSize}, ignoring.");
            }
            else if (!IsNewer(_earliestTSN, dataChunk.TSN))
            {
                logger.LogWarning($"SCTP framer received an old data chunk with {dataChunk.TSN} " +
                    $"TSN when the expected TSN was {_earliestTSN}, ignoring.");
            }
            else if (!_receivedChunks.ContainsKey(dataChunk.TSN))
            {
                if (dataChunk.Begining && dataChunk.Ending)
                {
                    // This chunk can be provided to the ULP immediately.
                    frame = new SctpDataFrame(
                        dataChunk.StreamID,
                        dataChunk.StreamSeqNum,
                        dataChunk.PPID,
                        dataChunk.UserData);

                    // Record the fact that the TSN was received.
                    _receivedChunks.Add(dataChunk.TSN, SctpDataChunk.EmptyDataChunk);
                }
                else
                {
                    // This is a data chunk fragment.
                    _receivedChunks.Add(dataChunk.TSN, dataChunk);
                    (var begin, var end) = GetChunkBeginAndEnd(dataChunk.TSN);

                    if (begin != null && end != null)
                    {
                        frame = FragmentedChunkReady(begin.Value, end.Value);
                    }
                }

                if (IsNewer(_latestTSN, dataChunk.TSN))
                {
                    _latestTSN = dataChunk.TSN;
                }

                _earliestTSN = RemoveEmpty(_earliestTSN);
            }
            else
            {
                _duplicateTSNCount++;
            }

            return frame;
        }

        /// <summary>
        /// Checks whether the fragmented chunk for the supplied TSN is complete and if so
        /// returns its begin and end TSNs.
        /// </summary>
        /// <param name="tsn">The TSN of the fragmented chunk to check for completeness.</param>
        /// <returns>If the chunk is complete the begin and end TSNs will be returned. If
        /// the fragmented chunk is incomplete one of both of the begin and/or end TSNs will be null.</returns>
        private (uint?, uint?) GetChunkBeginAndEnd(uint tsn)
        {
            uint? beginTSN = _receivedChunks[tsn].Begining ? (uint?)tsn : null;
            uint? endTSN = _receivedChunks[tsn].Ending ? (uint?)tsn : null;

            uint revTSN = tsn - 1;
            while (beginTSN == null && _receivedChunks.ContainsKey(revTSN))
            {
                if (_receivedChunks[revTSN].Begining)
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
                while (endTSN == null && _receivedChunks.ContainsKey(fwdTSN))
                {
                    if (_receivedChunks[fwdTSN].Ending)
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

        /// <summary>
        /// Extracts a fragmented chunk from the receive dictionary and passes it to the
        /// ULP.
        /// </summary>
        /// <param name="beginTSN">The beginning TSN for the fragment.</param>
        /// <param name="endTSN">The end TSN for the fragment.</param>
        private SctpDataFrame FragmentedChunkReady(uint beginTSN, uint endTSN)
        {
            byte[] full = new byte[_receiveWindow];
            int posn = 0;
            var beginChunk = _receivedChunks[beginTSN];
            var frame = new SctpDataFrame(beginChunk.StreamID, beginChunk.StreamSeqNum, beginChunk.PPID, full);

            uint afterEndTSN;
            unchecked { afterEndTSN = endTSN + 1; }
            uint tsn = beginTSN;

            while (tsn != afterEndTSN)
            {
                var fragment = _receivedChunks[tsn].UserData;
                Buffer.BlockCopy(fragment, 0, full, posn, fragment.Length);
                posn += fragment.Length;
                _receivedChunks[tsn] = SctpDataChunk.EmptyDataChunk;
                unchecked { tsn++; }
            }

            frame.UserData = frame.UserData.Take(posn).ToArray();

            return frame;
        }

        /// <summary>
        /// Removes empty data chunks, which are chunks that have already been passed
        /// to the ULP as part of a full frame, from the receive dictionary.
        /// </summary>
        private uint RemoveEmpty(uint from)
        {
            while (_receivedChunks.ContainsKey(from) && _receivedChunks[from].IsEmpty())
            {
                _receivedChunks.Remove(from);
                from++;
            }

            return from;
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
                return receivedTSN >= tsn;
            }
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
