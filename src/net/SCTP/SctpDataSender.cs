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
using System.Collections.Generic;
using System.Text;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class SctpDataSender
    {
        public const ushort DEFAULT_SCTP_MTU = 1300;

        SctpAssociation _sctpAssociation;
        private ushort _defaultMTU;
        private Dictionary<ushort, ushort> _streamSeqnums = new Dictionary<ushort, ushort>();

        public uint TSN { get; private set; }

        public SctpDataSender(
            SctpAssociation sctpAssociation, 
            ushort defaultMTU,
            uint initialTSN)
        {
            _sctpAssociation = sctpAssociation;
            _defaultMTU = defaultMTU > 0 ? defaultMTU : DEFAULT_SCTP_MTU;
            TSN = initialTSN;
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

            if(_streamSeqnums.ContainsKey(streamID))
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

                _sctpAssociation.SendChunk(dataChunk);

                TSN = (TSN == UInt32.MaxValue) ? 0 : TSN + 1;
            }
        }
    }
}
