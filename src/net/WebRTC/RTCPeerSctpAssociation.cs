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
    public delegate void OnRTCDataChannelData(ushort streamID, ushort streamSeqnum, uint ppid, byte[] data);

    public delegate void OnRTCDataChannelOpened(ushort streamID);

    public delegate void OnNewRTCDataChannel(ushort streamID, DataChannelTypes type, ushort priority, uint reliability, string label, string protocol);

    public class RTCPeerSctpAssociation : SctpAssociation
    {
        private static readonly ILogger logger = Log.Logger;

        /// <summary>
        /// The DTLS transport to send and receive SCTP packets on.
        /// </summary>
        private RTCSctpTransport _rtcSctpTransport;

        /// <summary>
        /// Event notifications for user data on an SCTP stream representing a data channel.
        /// </summary>
        public event OnRTCDataChannelData OnDataChannelData;

        /// <summary>
        /// Event notifications for the request to open a data channel being confirmed. This
        /// event corresponds to the DCEP ACK message for a DCEP OPEN message by this peer.
        /// </summary>
        public event OnRTCDataChannelOpened OnDataChannelOpened;

        /// <summary>
        /// Event notification for a new data channel open request from the remote peer.
        /// </summary>
        public event OnNewRTCDataChannel OnNewDataChannel;

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
            logger.LogDebug($"SCTP creating association is client {_rtcSctpTransport.IsDtlsClient} {srcPort}:{dstPort}.");

            OnDataChunk += OnDataChunkReceived;
        }

        /// <summary>
        /// Event handler for a DATA chunk being received. The chunk can be either a DCEP message or data channel data
        /// payload.
        /// </summary>
        /// <param name="dataChunk">The received data chunk.</param>
        private void OnDataChunkReceived(SctpDataChunk dataChunk)
        {
            switch (dataChunk)
            {
                case var dc when dc.PPID == (uint)DataChannelPayloadProtocols.WebRTC_DCEP:
                    switch (dc.UserData[0])
                    {
                        case (byte)DataChannelMessageTypes.ACK:
                            OnDataChannelOpened?.Invoke(dataChunk.StreamID);
                            break;
                        case (byte)DataChannelMessageTypes.OPEN:
                            var dcepOpen = DataChannelOpenMessage.Parse(dc.UserData, 0);

                            logger.LogDebug($"DCEP OPEN channel type {dcepOpen.ChannelType}, priority {dcepOpen.Priority}, " +
                                $"reliability {dcepOpen.Reliability}, label {dcepOpen.Label}, protocol {dcepOpen.Protocol}.");

                            DataChannelTypes channelType = DataChannelTypes.DATA_CHANNEL_RELIABLE;
                            if(Enum.IsDefined(typeof(DataChannelTypes), dcepOpen.ChannelType))
                            {
                                channelType = (DataChannelTypes)dcepOpen.ChannelType;
                            }
                            else
                            {
                                logger.LogWarning($"DECP OPEN channel type of {dcepOpen.ChannelType} not recognised, defaulting to {channelType}.");
                            }

                            OnNewDataChannel?.Invoke(
                                dataChunk.StreamID,
                                channelType,
                                dcepOpen.Priority,
                                dcepOpen.Reliability,
                                dcepOpen.Label,
                                dcepOpen.Protocol);

                            break;
                        default:
                            logger.LogWarning($"DCEP message type {dc.UserData[0]} not recognised, ignoring.");
                            break;
                    }
                    break;

                default:
                    OnDataChannelData?.Invoke(dataChunk.StreamID, dataChunk.StreamSeqNum, dataChunk.PPID, dataChunk.UserData);
                    break;
            }
        }
    }
}
