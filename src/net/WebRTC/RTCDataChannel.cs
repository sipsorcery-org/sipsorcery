//-----------------------------------------------------------------------------
// Filename: RTCDataChannel.cs
//
// Description: Contains an implementation for a WebRTC data channel.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Jul 2020	Aaron Clauson	Created.
// 22 Mar 2021  Aaron Clauson   Refactored for new SCTP implementation.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// The assignments for SCTP payload protocol IDs used with
    /// WebRTC data channels.
    /// </summary>
    /// <remarks>
    /// See https://tools.ietf.org/html/rfc8831#section-8
    /// </remarks>
    public enum DataChannelPayloadProtocols : uint
    {
        WebRTC_DCEP = 50,           // Data Channel Establishment Protocol (DCEP).
        WebRTC_String = 51,
        WebRTC_Binary_Partial = 52, // Deprecated.
        WebRTC_Binary = 53,
        WebRTC_String_Partial = 54, // Deprecated.
        WebRTC_String_Empty = 56,
        WebRTC_Binary_Empty = 57
    }

    /// <summary>
    /// A WebRTC data channel is generic transport service
    /// that allows peers to exchange generic data in a peer
    /// to peer manner.
    /// </summary>
    public class RTCDataChannel : IRTCDataChannel
    {
        private static ILogger logger = Log.Logger;

        public string label { get; set; }

        public bool ordered { get; set; }

        public ushort? maxPacketLifeTime { get; set; }

        public ushort? maxRetransmits { get; set; }

        public string protocol { get; set; }

        public bool negotiated { get; set; }

        public ushort? id { get; set; }

        public RTCDataChannelState readyState { get; private set; } = RTCDataChannelState.connecting;

        public ulong bufferedAmount { get; set; }

        public ulong bufferedAmountLowThreshold { get; set; }
        public string binaryType { get; set; }

        //public long MaxMessageSize { get; set; }

        public string Error { get; private set; }

        public bool IsOpened { get; private set; } = false;

        private RTCSctpTransport _transport;
        
        /// <summary>
        /// For ordered data channel streams this is the sequence number that
        /// will be set on the SCTP DATA chunk.
        /// The DCEP ACK uses the 0 sequence number.
        /// </summary>
        private ushort _seqnum = 1;

        public event Action onopen;
        //public event Action onbufferedamountlow;
        public event Action<string> onerror;
        //public event Action onclosing;
        public event Action onclose;
        public event Action<DataChannelPayloadProtocols, byte[]> onmessage;

        public RTCDataChannel(RTCSctpTransport transport, RTCDataChannelInit init = null)
        {
            _transport = transport;

            // TODO: Apply init settings.
        }

        internal void GotAck()
        {
            logger.LogDebug($"Data channel for label {label} now open.");
            IsOpened = true;
            readyState = RTCDataChannelState.open;
            onopen?.Invoke();
        }

        /// <summary>
        /// Sets the error message is there was a problem creating the data channel.
        /// </summary>
        internal void SetError(string error)
        {
            Error = error;
            onerror?.Invoke(error);
        }

        public void close()
        {
            IsOpened = false;
            readyState = RTCDataChannelState.closing;
           //_sctpStream?.close();
            readyState = RTCDataChannelState.closed;
        }

        public void send(string message)
        {
            if (_transport.state != RTCSctpTransportState.Connected)
            {
                logger.LogWarning($"WebRTC data channel send failed due to SCTP transport in state {_transport.state}.");
            }
            else
            {
                if (string.IsNullOrEmpty(message))
                {
                    _transport.RTCSctpAssociation.SendData(id.GetValueOrDefault(),
                        _seqnum,
                        (uint)DataChannelPayloadProtocols.WebRTC_String_Empty, 
                        new byte[] { 0x00 });
                }
                else
                {
                    _transport.RTCSctpAssociation.SendData(id.GetValueOrDefault(),
                        _seqnum,
                        (uint)DataChannelPayloadProtocols.WebRTC_String,
                        Encoding.UTF8.GetBytes(message));
                }

                _seqnum = (ushort)((_seqnum == UInt16.MaxValue) ? 0 : _seqnum + 1);
            }
        }

        public void send(byte[] data)
        {
            if (_transport.state != RTCSctpTransportState.Connected)
            {
                logger.LogWarning($"WebRTC data channel send failed due to SCTP transport in state {_transport.state}.");
            }
            else
            {
                if (data?.Length == 0)
                {
                    _transport.RTCSctpAssociation.SendData(id.GetValueOrDefault(),
                        _seqnum,
                        (uint)DataChannelPayloadProtocols.WebRTC_Binary_Empty, 
                        new byte[] { 0x00 });
                }
                else
                {
                    _transport.RTCSctpAssociation.SendData(id.GetValueOrDefault(),
                        _seqnum,
                        (uint)DataChannelPayloadProtocols.WebRTC_Binary,
                       data);
                }

                _seqnum = (ushort)((_seqnum == UInt16.MaxValue) ? 0 : _seqnum + 1);
            }
        }

        public void close(uint streamID)
        {
            IsOpened = false;
            logger.LogDebug($"Data channel stream closed id {streamID}.");
            onclose?.Invoke();
        }

        internal void GotData(ushort streamID, ushort streamSeqNum, uint ppID, byte[] data)
        {
            logger.LogTrace($"WebRTC data channel GotData stream ID {streamID}, stream seqnum {streamSeqNum}, ppid {ppID}, label {label}.");

            if(Enum.IsDefined(typeof(DataChannelPayloadProtocols), ppID))
            {
                onmessage?.Invoke((DataChannelPayloadProtocols)ppID, data);
            }
            else
            {
                // Payload ID not recognised. Pass to application as raw data.
                onmessage?.Invoke(DataChannelPayloadProtocols.WebRTC_Binary, data);
            }
        }
    }
}
