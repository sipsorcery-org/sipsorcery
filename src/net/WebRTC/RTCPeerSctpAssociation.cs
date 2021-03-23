//-----------------------------------------------------------------------------
// Filename: RTCPeerSctpAssociation.cs
//
// Description: Represents an SCTP association on top of the DTLS
// transport. Each peer connection only requires a single SCTP 
// association. Multiple data channels can be created on top
// of the association.
//
// Remarks:
//
// - RFC8831 "WebRTC Data Channels" https://tools.ietf.org/html/rfc8831
//   Provides overview of WebRTC data channels and describes the DTLS +
//   SCTP infrastructure required.
//
// - RFC8832 "WebRTC Data Channel Establishment Protocol"
//   https://tools.ietf.org/html/rfc8832
//   The Data Channel Establishment Protocol (DCEP) is designed to
//   provide, in the WebRTC data channel context, a simple in-
//   band method for opening symmetric data channels. DCEP messages
//   are sent within SCTP DATA chunks (this is the in-band bit) and 
//   uses a two-way handshake to open a data channel.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 20 Jul 2020	Aaron Clauson	Created.
// 22 Mar 2021  Aaron Clauson   Refactored for new SCTP implementation.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Text;
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
    public enum SctpPayloadProtocols : uint
    {
        WebRTC_DCEP = 50,           // Data Channel Establishment Protocol (DCEP).
        WebRTC_String = 51,
        WebRTC_Binary_Partial = 52, // Deprecated.
        WebRTC_Binary = 53,
        WebRTC_String_Partial = 54, // Deprecated.
        WebRTC_String_Empty = 56,
        WebRTC_Binary_Empty = 57
    }

    public enum DataChannelMessageTypes : byte
    {
        ACK = 0x02,
        OPEN = 0x03,
    }

    public enum DataChannelTypes : byte
    {
        /// <summary>
        /// The data channel provides a reliable in-order bidirectional communication.
        /// </summary>
        DATA_CHANNEL_RELIABLE = 0x00,

        /// <summary>
        /// The data channel provides a partially reliable in-order bidirectional
        /// communication. User messages will not be retransmitted more
        /// times than specified in the Reliability Parameter
        /// </summary>
        DATA_CHANNEL_PARTIAL_RELIABLE_REXMIT = 0x01,

        /// <summary>
        /// The data channel provides a partially reliable in-order bidirectional
        /// communication. User messages might not be transmitted or
        /// retransmitted after a specified lifetime given in milliseconds
        /// in the Reliability Parameter. This lifetime starts when
        /// providing the user message to the protocol stack.
        /// </summary>
        DATA_CHANNEL_PARTIAL_RELIABLE_TIMED = 0x02,

        /// <summary>
        /// The data channel provides a reliable unordered bidirectional communication.
        /// </summary>
        DATA_CHANNEL_RELIABLE_UNORDERED = 0x80,

        /// <summary>
        /// The data channel provides a partially reliable unordered bidirectional
        /// communication. User messages will not be retransmitted more
        /// times than specified in the Reliability Parameter.
        /// </summary>
        DATA_CHANNEL_PARTIAL_RELIABLE_REXMIT_UNORDERED = 0x81,

        /// <summary>
        /// The data channel provides a partially reliable unordered bidirectional
        /// communication. User messages might not be transmitted or
        /// retransmitted after a specified lifetime given in milliseconds
        /// in the Reliability Parameter. This lifetime starts when
        /// providing the user message to the protocol stack.
        /// </summary>
        DATA_CHANNEL_PARTIAL_RELIABLE_TIMED_UNORDERED = 0x82
    }

    /// <summary>
    /// This message is initially sent using the data channel on the stream
    /// used for user messages.
    /// </summary>
    /// <remarks>
    /// See https://tools.ietf.org/html/rfc8832#section-5.1
    /// </remarks>
    public struct DataChannelOpenMessage
    {
        public const int DCEP_OPEN_FIXED_PARAMETERS_LENGTH = 12;

        /// <summary>
        ///  This field holds the IANA-defined message type for the
        /// DATA_CHANNEL_OPEN message.The value of this field is 0x03.
        /// </summary>
        public byte MessageType;

        /// <summary>
        /// This field specifies the type of data channel to be opened.
        /// </summary>
        public byte ChannelType;

        /// <summary>
        /// The priority of the data channel.
        /// </summary>
        public ushort Priority;

        /// <summary>
        /// Used to set tolerance for partially reliable data channels.
        /// </summary>
        public uint Reliability;

        /// <summary>
        /// The name of the data channel. May be an empty string.
        /// </summary>
        public string Label;

        /// <summary>
        /// If it is a non-empty string, it specifies a protocol registered in the
        /// "WebSocket Subprotocol Name Registry" created in RFC6455.
        /// </summary>
        /// <remarks>
        /// The websocket subprotocol names and specification are available at
        /// https://tools.ietf.org/html/rfc7118
        /// </remarks>
        public string Protocol;

        /// <summary>
        /// Parses the an DCEP open message from a buffer.
        /// </summary>
        /// <param name="buffer">The buffer to parse the message from.</param>
        /// <param name="posn">The position in the buffer to start parsing from.</param>
        /// <returns>A new DCEP open message instance.</returns>
        public static DataChannelOpenMessage Parse(byte[] buffer, int posn)
        {
            if (buffer.Length < DCEP_OPEN_FIXED_PARAMETERS_LENGTH)
            {
                throw new ApplicationException("The buffer did not contain the minimum number of bytes for a DCEP open message.");
            }

            var dcepOpen = new DataChannelOpenMessage();

            dcepOpen.MessageType = buffer[posn];
            dcepOpen.ChannelType = buffer[posn + 1];
            dcepOpen.Priority = NetConvert.ParseUInt16(buffer, posn + 2);
            dcepOpen.Reliability = NetConvert.ParseUInt32(buffer, posn + 4);

            ushort labelLength = NetConvert.ParseUInt16(buffer, posn + 8);
            ushort protocolLength = NetConvert.ParseUInt16(buffer, posn + 10);

            if(labelLength > 0)
            {
                dcepOpen.Label = Encoding.UTF8.GetString(buffer, 12, labelLength);
            }

            if(protocolLength > 0)
            {
                dcepOpen.Protocol = Encoding.UTF8.GetString(buffer, 12 + labelLength, protocolLength);
            }

            return dcepOpen;
        }
    }

    public class RTCPeerSctpAssociation : SctpAssociation
    {
        private static readonly ILogger logger = Log.Logger;

        /// <summary>
        /// The DTLS transport to send and receive SCTP packets on.
        /// </summary>
        private RTCSctpTransport _rtcSctpTransport;

        /// <summary>
        /// Event notifications for user data on an SCTP stream.
        /// </summary>
        /// <remarks>
        /// Parameters:
        ///  - SCTP stream ID.
        ///  - SCTP Payload ID.
        ///  - Data.
        /// </remarks>
        public event Action<ushort, uint, byte[]> OnData;

        /// <summary>
        /// Event to indicate an SCTP stream has been opened. The stream open
        /// could have been initiated by a new data channel request on the local
        /// or remote peer.
        /// </summary>
        /// <remarks>
        /// Parameters:
        ///  - The newly opened stream,
        ///  - A boolean indicating whether the stream ID is from a local create stream request or not.
        /// </remarks>
        //public Action<SCTPStream, bool> OnSCTPStreamOpen;

        /// <summary>
        /// Creates a new SCTP association with the remote peer.
        /// </summary>
        /// <param name="rtcSctpTransport">The DTLS transport that will be used to encapsulate the
        /// SCTP packets.</param>
        /// <param name="isClient">True if this peer will be the client within the association. This
        /// dictates whether streams created use odd or even ID's.</param>
        /// <param name="srcPort">The source port to use when forming the association.</param>
        /// <param name="dstPort">The destination port to use when forming the association.</param>
        public RTCPeerSctpAssociation(RTCSctpTransport rtcSctpTransport, ushort srcPort, ushort dstPort)
            : base(rtcSctpTransport, null, srcPort, dstPort)
        {
            _rtcSctpTransport = rtcSctpTransport;
            logger.LogDebug($"SCTP creating association is client { _rtcSctpTransport.IsDtlsClient} {srcPort}:{dstPort}.");

            OnDataChunk += OnDataChunkReceived;
        }

        /// <summary>
        /// Creates a new SCTP association with the remote peer based on a cookie from a COOKIE ECHO 
        /// SCTP chunk. The cookie is the result on an SCTP handshake and indicates the association 
        /// is being created based on an initial request from the remote party.
        /// </summary>
        /// <param name="rtcSctpTransport">The DTLS transport that will be used to encapsulate the
        /// SCTP packets.</param>
        /// <param anme="cookie">The cookie supplied from the SCTP handshake.</param>
        public RTCPeerSctpAssociation(RTCSctpTransport rtcSctpTransport, SctpTransportCookie cookie)
            : base(rtcSctpTransport, cookie)
        {
            _rtcSctpTransport = rtcSctpTransport;
            logger.LogDebug($"SCTP creating association from handshake cookie, is client { _rtcSctpTransport.IsDtlsClient} " +
                $"{cookie.SourcePort}:{cookie.DestinationPort}.");

            OnDataChunk += OnDataChunkReceived;
        }

        private void OnDataChunkReceived(SctpDataChunk dataChunk)
        {
            switch(dataChunk)
            {
                case var dc when dc.PPID == (uint)SctpPayloadProtocols.WebRTC_DCEP:
                    switch (dc.UserData[0])
                    {
                        case (byte)DataChannelMessageTypes.ACK:

                            break;
                        case (byte)DataChannelMessageTypes.OPEN:
                            var dcepOpen = DataChannelOpenMessage.Parse(dc.UserData, 0);
                            logger.LogDebug($"DCEP OPEN channel type {dcepOpen.ChannelType:0x00}, priority {dcepOpen.Priority}, " +
                                $"reliability {dcepOpen.Reliability}, label {dcepOpen.Label}, protocol {dcepOpen.Protocol}.");
                            break;
                        default:
                            logger.LogWarning($"DCEP message type {dc.UserData[0]} not recognised, ignoring.");
                            break;
                    }
                    break;

                default:
                    OnData?.Invoke(dataChunk.StreamID, dataChunk.PPID, dataChunk.UserData);
                    break;
            }
        }

        /// <summary>
        /// Closes all the streams for this SCTP association.
        /// </summary>
        //public void Close()
        //{
        //    //if (_sctpAssociation != null)
        //    //{
        //    //    logger.LogDebug($"SCTP closing all streams for association.");

        //    //    foreach (int streamID in _sctpAssociation.allStreams())
        //    //    {
        //    //        _sctpAssociation.getStream(streamID)?.close();
        //    //        _sctpAssociation.delStream(streamID);
        //    //    }
        //    //}
        //}

        /// <summary>
        /// Creates a new SCTP stream to act as a WebRTC data channel.
        /// </summary>
        /// <param name="label">Optional. The label to attach to the stream.</param>
        //public Task<SCTPStream> CreateStream(string label)
        //{
        //    logger.LogDebug($"SCTP creating stream for label {label}.");
        //    return Task.FromResult(_sctpAssociation.mkStream(label));
        //}

        /// <summary>
        /// Event handler for the SCTP association successfully initialising.
        /// </summary>
        //public void onAssociated(Association a)
        //{
        //    IsAssociated = true;
        //    OnAssociated?.Invoke();
        //}

        /// <summary>
        /// Event handler for a new data channel being created by the remote peer.
        /// </summary>
        /// <param name="s">The SCTP stream that was created.</param>
        /// <param name="label">The label for the stream. Can be empty and can also be a duplicate.</param>
        /// <param name="payloadProtocolID">The payload protocol ID of the new stream.</param>
        //public void onDCEPStream(SCTPStream s, string label, int payloadProtocolID)
        //{
        //    logger.LogDebug($"SCTP data channel stream opened for label {label}, ppid {payloadProtocolID}, stream id {s.getNum()}.");

        //    bool isLocalStreamID = (_isClient) ? s.getNum() % 2 == 0 : s.getNum() % 2 != 0;

        //    OnSCTPStreamOpen?.Invoke(s, isLocalStreamID);
        //}

        //public void onDisAssociated(Association a)
        //{
        //    logger.LogDebug($"SCTP disassociated.");
        //}

        /// <summary>
        /// Event handler that gets fired as part of creating a new data channel.
        /// </summary>
        //public void onRawStream(SCTPStream s)
        //{
        //    // Do nothing.
        //}
    }
}
