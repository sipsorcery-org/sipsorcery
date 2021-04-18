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

        public RTCDataChannelState readyState { get; internal set; } = RTCDataChannelState.connecting;

        public ulong bufferedAmount => _transport?.RTCSctpAssociation?.SendBufferedAmount ?? 0;

        public ulong bufferedAmountLowThreshold { get; set; }
        public string binaryType { get; set; }

        //public long MaxMessageSize { get; set; }

        public string Error { get; private set; }

        public bool IsOpened { get; internal set; } = false;

        private RTCSctpTransport _transport;

        public event Action onopen;
        //public event Action onbufferedamountlow;
        public event Action<string> onerror;
        //public event Action onclosing;
        public event Action onclose;
        public event OnDataChannelMessageDelegate onmessage;

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
            readyState = RTCDataChannelState.closed;
            // TODO. What actions are required?
        }

        /// <summary>
        /// Sends a string data payload on the data channel.
        /// </summary>
        /// <param name="message">The string message to send.</param>
        public void send(string message)
        {
            if (message != null & Encoding.UTF8.GetByteCount(message) > _transport.maxMessageSize)
            {
                throw new ApplicationException($"Data channel {label} was requested to send data of length {Encoding.UTF8.GetByteCount(message)} " +
                    $" that exceeded the maximum allowed message size of {_transport.maxMessageSize}.");
            }
            else if (_transport.state != RTCSctpTransportState.Connected)
            {
                logger.LogWarning($"WebRTC data channel send failed due to SCTP transport in state {_transport.state}.");
            }
            else
            {
                lock (this)
                {
                    if (string.IsNullOrEmpty(message))
                    {
                        _transport.RTCSctpAssociation.SendData(id.GetValueOrDefault(),
                            (uint)DataChannelPayloadProtocols.WebRTC_String_Empty,
                            new byte[] { 0x00 });
                    }
                    else
                    {
                        _transport.RTCSctpAssociation.SendData(id.GetValueOrDefault(),
                            (uint)DataChannelPayloadProtocols.WebRTC_String,
                            Encoding.UTF8.GetBytes(message));
                    }
                }
            }
        }

        /// <summary>
        /// Sends a binary data payload on the data channel.
        /// </summary>
        /// <param name="data">The data to send.</param>
        public void send(byte[] data)
        {
            if (data.Length > _transport.maxMessageSize)
            {
                throw new ApplicationException($"Data channel {label} was requested to send data of length {data.Length} " +
                    $" that exceeded the maximum allowed message size of {_transport.maxMessageSize}.");
            }
            else if (_transport.state != RTCSctpTransportState.Connected)
            {
                logger.LogWarning($"WebRTC data channel send failed due to SCTP transport in state {_transport.state}.");
            }
            else
            {
                lock (this)
                {
                    if (data?.Length == 0)
                    {
                        _transport.RTCSctpAssociation.SendData(id.GetValueOrDefault(),
                            (uint)DataChannelPayloadProtocols.WebRTC_Binary_Empty,
                            new byte[] { 0x00 });
                    }
                    else
                    {
                        _transport.RTCSctpAssociation.SendData(id.GetValueOrDefault(),
                            (uint)DataChannelPayloadProtocols.WebRTC_Binary,
                           data);
                    }
                }
            }
        }

        /// <summary>
        /// Sends an OPEN Data Channel Establishment Protocol (DCEP) message
        /// to open a data channel on the remote peer for send/receive.
        /// </summary>
        internal void SendDcepOpen()
        {
            var dcepOpen = new DataChannelOpenMessage()
            {
                MessageType = (byte)DataChannelMessageTypes.OPEN,
                ChannelType = (byte)DataChannelTypes.DATA_CHANNEL_RELIABLE_UNORDERED,
                Label = label
            };

            lock (this)
            {
                _transport.RTCSctpAssociation.SendData(id.GetValueOrDefault(),
                       (uint)DataChannelPayloadProtocols.WebRTC_DCEP,
                       dcepOpen.GetBytes());
            }
        }

        /// <summary>
        /// Sends an ACK response for a Data Channel Establishment Protocol (DCEP)
        /// control message.
        /// </summary>
        internal void SendDcepAck()
        {
            lock (this)
            {
                _transport.RTCSctpAssociation.SendData(id.GetValueOrDefault(),
                       (uint)DataChannelPayloadProtocols.WebRTC_DCEP,
                       new byte[] { (byte)DataChannelMessageTypes.ACK });
            }
        }

        public void close(uint streamID)
        {
            IsOpened = false;
            logger.LogDebug($"Data channel stream closed id {streamID}.");
            onclose?.Invoke();
        }

        /// <summary>
        /// Event handler for an SCTP data chunk being received for this data channel.
        /// </summary>
        internal void GotData(ushort streamID, ushort streamSeqNum, uint ppID, byte[] data)
        {
            //logger.LogTrace($"WebRTC data channel GotData stream ID {streamID}, stream seqnum {streamSeqNum}, ppid {ppID}, label {label}.");

            // If the ppID is not recognised default to binary.
            DataChannelPayloadProtocols payloadType = DataChannelPayloadProtocols.WebRTC_Binary;

            if (Enum.IsDefined(typeof(DataChannelPayloadProtocols), ppID))
            {
                payloadType = (DataChannelPayloadProtocols)ppID;
            }

            onmessage?.Invoke(this, (DataChannelPayloadProtocols)ppID, data);
        }
    }
}
