//-----------------------------------------------------------------------------
// Filename: SctpDataChunk.cs
//
// Description: Represents the SCTP DATA chunk.
//
// Remarks:
// Defined in section 3 of RFC4960:
// https://tools.ietf.org/html/rfc4960#section-3.3.1.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 18 Mar 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class SctpDataChunk : SctpChunk
    {
        /// <summary>
        /// An empty data chunk. The main use is to indicate a DATA chunk has
        /// already been delivered to the Upper Layer Protocol (ULP) in 
        /// <see cref="SctpDataReceiver"/>.
        /// </summary>
        public static SctpDataChunk EmptyDataChunk = new SctpDataChunk();

        /// <summary>
        /// The length in bytes of the fixed parameters used by the DATA chunk.
        /// </summary>
        public const int FIXED_PARAMETERS_LENGTH = 12;

        /// <summary>
        /// The (U)nordered bit, if set to true, indicates that this is an
        /// unordered DATA chunk.
        /// </summary>
        public bool Unordered { get; set; } = false;

        /// <summary>
        /// The (B)eginning fragment bit, if set, indicates the first fragment
        /// of a user message.
        /// </summary>
        public bool Begining { get; set; } = true;

        /// <summary>
        /// The (E)nding fragment bit, if set, indicates the last fragment of
        /// a user message.
        /// </summary>
        public bool Ending { get; set; } = true;

        /// <summary>
        /// This value represents the Transmission Sequence Number (TSN) for
        /// this DATA chunk.
        /// </summary>
        public uint TSN;

        /// <summary>
        /// Identifies the stream to which the following user data belongs.
        /// </summary>
        public ushort StreamID;

        /// <summary>
        /// This value represents the Stream Sequence Number of the following
        /// user data within the stream using the <seealso cref="StreamID"/>.
        /// </summary>
        public ushort StreamSeqNum;

        /// <summary>
        /// Payload Protocol Identifier (PPID). This value represents an application 
        /// (or upper layer) specified protocol identifier.This value is passed to SCTP 
        /// by its upper layer and sent to its peer.
        /// </summary>
        public uint PPID;

        /// <summary>
        /// This is the payload user data.
        /// </summary>
        public byte[] UserData;

        // These properties are used by the data sender.
        internal DateTime LastSentAt;
        internal int SendCount;

        private SctpDataChunk()
            : base(SctpChunkType.DATA)
        { }

        /// <summary>
        /// Creates a new DATA chunk.
        /// </summary>
        /// <param name="isUnordered">Must be set to true if the application wants to send this data chunk
        /// without requiring it to be delivered to the remote part in order.</param>
        /// <param name="isBegining">Must be set to true for the first chunk in a user data payload.</param>
        /// <param name="isEnd">Must be set to true for the last chunk in a user data payload. Note that
        /// <see cref="isBegining"/> and <see cref="isEnd"/> must both be set to true when the full payload
        /// is being sent in a single data chunk.</param>
        /// <param name="tsn">The Transmission Sequence Number for this chunk.</param>
        /// <param name="streamID">Optional. The stream ID for this data chunk.</param>
        /// <param name="seqnum">Optional. The stream sequence number for this send. Set to 0 for unordered streams.</param>
        /// <param name="ppid">Optional. The payload protocol ID for this data chunk.</param>
        /// <param name="data">The data to send.</param>
        public SctpDataChunk(
            bool isUnordered,
            bool isBegining,
            bool isEnd,
            uint tsn, 
            ushort streamID, 
            ushort seqnum, 
            uint ppid, 
            byte[] data) : base(SctpChunkType.DATA)
        {
            if (data == null || data.Length == 0)
            {
                throw new ArgumentNullException("data", "The SctpDataChunk data parameter cannot be empty.");
            }

            Unordered = isUnordered;
            Begining = isBegining;
            Ending = isEnd;
            TSN = tsn;
            StreamID = streamID;
            StreamSeqNum = seqnum; 
            PPID = ppid;
            UserData = data;

            ChunkFlags = (byte)(
                (Unordered ? 0x04 : 0x0) +
                (Begining ? 0x02 : 0x0) +
                (Ending ? 0x01 : 0x0));
        }

        /// <summary>
        /// Calculates the length for DATA chunk.
        /// </summary>
        /// <param name="padded">If true the length field will be padded to a 4 byte boundary.</param>
        /// <returns>The length of the chunk.</returns>
        public override ushort GetChunkLength(bool padded)
        {
            ushort len = SCTP_CHUNK_HEADER_LENGTH + FIXED_PARAMETERS_LENGTH;
            len += (ushort)(UserData != null ? UserData.Length : 0);
            return (padded) ? SctpPadding.PadTo4ByteBoundary(len) : len;
        }

        /// <summary>
        /// Serialises a DATA chunk to a pre-allocated buffer.
        /// </summary>
        /// <param name="buffer">The buffer to write the serialised chunk bytes to. It
        /// must have the required space already allocated.</param>
        /// <param name="posn">The position in the buffer to write to.</param>
        /// <returns>The number of bytes, including padding, written to the buffer.</returns>
        public override ushort WriteTo(byte[] buffer, int posn)
        {
            WriteChunkHeader(buffer, posn);

            // Write fixed parameters.
            int startPosn = posn + SCTP_CHUNK_HEADER_LENGTH;

            NetConvert.ToBuffer(TSN, buffer, startPosn);
            NetConvert.ToBuffer(StreamID, buffer, startPosn + 4);
            NetConvert.ToBuffer(StreamSeqNum, buffer, startPosn + 6);
            NetConvert.ToBuffer(PPID, buffer, startPosn + 8);

            int userDataPosn = startPosn + FIXED_PARAMETERS_LENGTH;

            if (UserData != null)
            {
                Buffer.BlockCopy(UserData, 0, buffer, userDataPosn, UserData.Length);
            }

            return GetChunkLength(true);
        }

        public bool IsEmpty()
        {
            return UserData == null;
        }

        /// <summary>
        /// Parses the DATA chunk fields
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position to start parsing at.</param>
        public static SctpDataChunk ParseChunk(byte[] buffer, int posn)
        {
            var dataChunk = new SctpDataChunk();
            ushort chunkLen = dataChunk.ParseFirstWord(buffer, posn);

            if (chunkLen < FIXED_PARAMETERS_LENGTH)
            {
                throw new ApplicationException($"SCTP data chunk cannot be parsed as buffer too short for fixed parameter fields.");
            }

            dataChunk.Unordered = (dataChunk.ChunkFlags & 0x04) > 0;
            dataChunk.Begining = (dataChunk.ChunkFlags & 0x02) > 0;
            dataChunk.Ending = (dataChunk.ChunkFlags & 0x01) > 0;

            int startPosn = posn + SCTP_CHUNK_HEADER_LENGTH;

            dataChunk.TSN = NetConvert.ParseUInt32(buffer, startPosn);
            dataChunk.StreamID = NetConvert.ParseUInt16(buffer, startPosn + 4);
            dataChunk.StreamSeqNum = NetConvert.ParseUInt16(buffer, startPosn + 6);
            dataChunk.PPID = NetConvert.ParseUInt32(buffer, startPosn + 8);

            int userDataPosn = startPosn + FIXED_PARAMETERS_LENGTH;
            int userDataLen = chunkLen - SCTP_CHUNK_HEADER_LENGTH - FIXED_PARAMETERS_LENGTH;

            if (userDataLen > 0)
            {
                dataChunk.UserData = new byte[userDataLen];
                Buffer.BlockCopy(buffer, userDataPosn, dataChunk.UserData, 0, dataChunk.UserData.Length);
            }

            return dataChunk;
        }
    }
}
