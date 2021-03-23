//-----------------------------------------------------------------------------
// Filename: SctpInitChunk.cs
//
// Description: Represents the SCTP INIT chunk.
//
// Remarks:
// Defined in section 3 of RFC4960:
// https://tools.ietf.org/html/rfc4960#section-3.3.2
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

using System.Linq;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// This class is used to represent both an INIT and INIT ACK chunk.
    /// The only structural difference between them is the INIT ACK requires
    /// the Cookie variable parameter to be set.
    /// The INIT chunk is used to initiate an SCTP association between two
    /// endpoints. The INIT ACK chunk is used to respond to an incoming
    /// INIT chunk from a remote party.
    /// </summary>
    public class SctpInitChunk : SctpChunk
    {
        public const int FIXED_PARAMETERS_LENGTH = 16;
        public const int DEFAULT_NUMBER_OUTBOUND_STREAMS = 65535;
        public const int DEFAULT_NUMBER_INBOUND_STREAMS = 65535;

        /// <summary>
        /// The receiver of the INIT (the responding end) records the value of
        /// the Initiate Tag parameter.This value MUST be placed into the
        /// Verification Tag field of every SCTP packet that the receiver of
        /// the INIT transmits within this association.
        /// </summary>
        public uint InitiateTag;

        /// <summary>
        /// Advertised Receiver Window Credit. This value represents the dedicated 
        /// buffer space, in number of bytes, the sender of the INIT has reserved in 
        /// association with this window.
        /// </summary>
        public uint ARwnd;

        /// <summary>
        /// Defines the number of outbound streams the sender of this INIT
        /// chunk wishes to create in this association.
        /// </summary>
        public ushort NumberOutboundStreams;

        /// <summary>
        /// Defines the maximum number of streams the sender of this INIT
        /// chunk allows the peer end to create in this association.
        /// </summary>
        public ushort NumberInboundStreams;

        /// <summary>
        /// The initial Transmission Sequence Number (TSN) that the sender will use.
        /// </summary>
        public uint InitialTSN;

        private SctpInitChunk()
        { }

        /// <summary>
        /// Initialises the chunk as either INIT or INIT ACK.
        /// </summary>
        /// <param name="initChunkType">Either INIT or INIT ACK.</param>
        public SctpInitChunk(SctpChunkType initChunkType,
            uint initiateTag,
            uint initialTSN,
            uint arwnd,
            ushort numberOutboundStreams = DEFAULT_NUMBER_OUTBOUND_STREAMS,
            ushort numberInboundStreams = DEFAULT_NUMBER_INBOUND_STREAMS) : base(initChunkType)
        {
            InitiateTag = initiateTag;
            NumberOutboundStreams = numberOutboundStreams;
            NumberInboundStreams = numberInboundStreams;
            InitialTSN = initialTSN;
            ARwnd = arwnd;
        }

        /// <summary>
        /// Calculates the length for INIT and INIT ACK chunks.
        /// </summary>
        /// <returns>The length of the chunk.</returns>
        public override ushort GetChunkLength()
        {
            ushort len = SCTP_CHUNK_HEADER_LENGTH + FIXED_PARAMETERS_LENGTH;
            if (VariableParameters != null)
            {
                len += (ushort)(VariableParameters.Sum(x => x.GetParameterPaddedLength()));
            }
            return len;
        }

        /// <summary>
        /// Serialises an INIT or INIT ACK chunk to a pre-allocated buffer.
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

            NetConvert.ToBuffer(InitiateTag, buffer, startPosn);
            NetConvert.ToBuffer(ARwnd, buffer, startPosn + 4);
            NetConvert.ToBuffer(NumberOutboundStreams, buffer, startPosn + 8);
            NetConvert.ToBuffer(NumberInboundStreams, buffer, startPosn + 10);
            NetConvert.ToBuffer(InitialTSN, buffer, startPosn + 12);

            // Write optional parameters.
            if (VariableParameters?.Count > 0)
            {
                int paramPosn = startPosn + FIXED_PARAMETERS_LENGTH;
                foreach(var optParam in VariableParameters)
                {
                    paramPosn += optParam.WriteTo(buffer, paramPosn);
                }
            }

            return GetChunkPaddedLength();
        }

        /// <summary>
        /// Parses the INIT or INIT ACK chunk fields
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position to start parsing at.</param>
        public static SctpInitChunk ParseChunk(byte[] buffer, int posn)
        {
            var initChunk = new SctpInitChunk();
            ushort chunkLen = initChunk.ParseFirstWord(buffer, posn);

            int startPosn = posn + SCTP_CHUNK_HEADER_LENGTH;

            initChunk.InitiateTag = NetConvert.ParseUInt32(buffer, startPosn);
            initChunk.ARwnd = NetConvert.ParseUInt32(buffer, startPosn + 4);
            initChunk.NumberOutboundStreams = NetConvert.ParseUInt16(buffer, startPosn + 8);
            initChunk.NumberInboundStreams = NetConvert.ParseUInt16(buffer, startPosn + 10);
            initChunk.InitialTSN = NetConvert.ParseUInt32(buffer, startPosn + 12);

            int paramPosn = startPosn + FIXED_PARAMETERS_LENGTH;
            int paramsBufferLength = chunkLen - SCTP_CHUNK_HEADER_LENGTH - FIXED_PARAMETERS_LENGTH;

            if (paramPosn < paramsBufferLength)
            {
                initChunk.VariableParameters = ParseVariableParameters(buffer, paramPosn, paramsBufferLength);
            }

            return initChunk;
        }
    }
}
