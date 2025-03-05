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
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public delegate void OnRTCDataChannelOpened(ushort streamID);

    public delegate void OnNewRTCDataChannel(ushort streamID, DataChannelTypes type, ushort priority, uint reliability, string label, string protocol);

    public class RTCPeerSctpAssociation : SctpAssociation
    {
        // TODO: Add MTU path discovery.
        public const ushort DEFAULT_DTLS_MTU = 1200;

        private static readonly ILogger logger = Log.Logger;

        /// <summary>
        /// The DTLS transport to send and receive SCTP packets on.
        /// </summary>
        private RTCSctpTransport _rtcSctpTransport;

        /// <summary>
        /// Event notifications for user data on an SCTP stream representing a data channel.
        /// </summary>
        public event Action<SctpDataFrame> OnDataChannelData;

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
        /// <param name="srcPort">The source port to use when forming the association.</param>
        /// <param name="dstPort">The destination port to use when forming the association.</param>
        /// <param name="dtlsPort">Optional. The local UDP port being used for the DTLS connection. This
        /// will be set on the SCTP association to aid in diagnostics.</param>
        public RTCPeerSctpAssociation(RTCSctpTransport rtcSctpTransport, ushort srcPort, ushort dstPort, int dtlsPort)
            : base(rtcSctpTransport, null, srcPort, dstPort, DEFAULT_DTLS_MTU, dtlsPort)
        {
            _rtcSctpTransport = rtcSctpTransport;
            logger.LogDebug("SCTP creating DTLS based association, is DTLS client {IsDtlsClient}, ID {ID}.", _rtcSctpTransport.IsDtlsClient, ID);

            OnData += OnDataFrameReceived;
        }

        /// <summary>
        /// Event handler for a DATA chunk being received. The chunk can be either a DCEP message or data channel data
        /// payload.
        /// </summary>
        /// <param name="dataFrame">The received data frame which could represent one or more chunks depending
        /// on fragmentation..</param>
        private void OnDataFrameReceived(SctpDataFrame dataFrame)
        {
            switch (dataFrame)
            {
                case var frame when frame.PPID == (uint)DataChannelPayloadProtocols.WebRTC_DCEP:
                    switch (frame.UserData[0])
                    {
                        case (byte)DataChannelMessageTypes.ACK:
                            OnDataChannelOpened?.Invoke(frame.StreamID);
                            break;
                        case (byte)DataChannelMessageTypes.OPEN:
                            var dcepOpen = DataChannelOpenMessage.Parse(frame.UserData, 0);

                            logger.LogDebug("DCEP OPEN channel type {ChannelType}, priority {Priority}, reliability {Reliability}, label {Label}, protocol {Protocol}.",
                                dcepOpen.ChannelType, dcepOpen.Priority, dcepOpen.Reliability, dcepOpen.Label, dcepOpen.Protocol);

                            DataChannelTypes channelType = DataChannelTypes.DATA_CHANNEL_RELIABLE;
                            if(Enum.IsDefined(typeof(DataChannelTypes), dcepOpen.ChannelType))
                            {
                                channelType = (DataChannelTypes)dcepOpen.ChannelType;
                            }
                            else
                            {
                                logger.LogWarning("DECP OPEN channel type of {ChannelType} not recognised, defaulting to {DefaultChannelType}.", dcepOpen.ChannelType, channelType);
                            }

                            OnNewDataChannel?.Invoke(
                                frame.StreamID,
                                channelType,
                                dcepOpen.Priority,
                                dcepOpen.Reliability,
                                dcepOpen.Label,
                                dcepOpen.Protocol);

                            break;
                        default:
                            logger.LogWarning("DCEP message type {MessageType} not recognised, ignoring.", frame.UserData[0]);
                            break;
                    }
                    break;

                default:
                    OnDataChannelData?.Invoke(dataFrame);
                    break;
            }
        }
    }
}
