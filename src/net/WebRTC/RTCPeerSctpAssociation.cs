//-----------------------------------------------------------------------------
// Filename: RTCPeerSctpAssociation.cs
//
// Description: Represents an SCTP association on top of the DTLS
// transport. Each peer connection only requires a single SCTP 
// association. Multiple data channels can be created on top
// of the association.
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
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class RTCPeerSctpAssociation : SctpAssociation
    {
        private static readonly ILogger logger = Log.Logger;

        /// <summary>
        /// The DTLS transport to send and receive SCTP packets on.
        /// </summary>
        private RTCSctpTransport _rtcSctpTransport;

        /// <summary>
        /// Indicates whether the SCTP association is ready for communications.
        /// </summary>
        public bool IsAssociated { get; private set; } = false;

        /// <summary>
        /// Event to indicate the SCTP association is ready.
        /// </summary>
        public Action OnAssociated;

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
        }

        /// <summary>
        /// Initiates the association with the remote peer.
        /// </summary>
        //public void Associate()
        //{
        //    //_sctpAssociation.associate();
        //}

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
