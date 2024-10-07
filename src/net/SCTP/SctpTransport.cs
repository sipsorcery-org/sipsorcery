//-----------------------------------------------------------------------------
// Filename: SctpTransport.cs
//
// Description: Represents a common SCTP transport layer.
//
// Remarks:
// The interface defined in https://tools.ietf.org/html/rfc4960#section-10 
// was used as a basis for this class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// St Patrick's Day 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using TinyJson;

namespace SIPSorcery.Net
{
    /// <summary>
    /// The opaque cookie structure that will be sent in response to an SCTP INIT
    /// packet.
    /// </summary>
    public struct SctpTransportCookie
    {
        public static SctpTransportCookie Empty = new SctpTransportCookie() { _isEmpty = true };

        public ushort SourcePort { get; set; }
        public ushort DestinationPort { get; set; }
        public uint RemoteTag { get; set; }
        public uint RemoteTSN { get; set; }
        public uint RemoteARwnd { get; set; }
        public string RemoteEndPoint { get; set; }
        public uint Tag { get; set; }
        public uint TSN { get; set; }
        public uint ARwnd { get; set; }
        public string CreatedAt { get; set; }
        public int Lifetime { get; set; }
        public string HMAC { get; set; }

        private bool _isEmpty;

        public bool IsEmpty()
        {
            return _isEmpty;
        }
    }

    /// <summary>
    /// Contains the common methods that an SCTP transport layer needs to implement.
    /// As well as being able to be carried directly in IP packets, SCTP packets can
    /// also be wrapped in higher level protocols.
    /// </summary>
    public abstract class SctpTransport
    {
        private const int HMAC_KEY_SIZE = 64;

        /// <summary>
        /// As per https://tools.ietf.org/html/rfc4960#section-15.
        /// </summary>
        public const int DEFAULT_COOKIE_LIFETIME_SECONDS = 60;

        private static ILogger logger = SIPSorcery.LogFactory.CreateLogger<SctpTransport>();

        /// <summary>
        /// Ephemeral secret key to use for generating cookie HMAC's. The purpose of the HMAC is
        /// to prevent resource depletion attacks. This does not justify using an external key store.
        /// </summary>
        private static byte[] _hmacKey = new byte[HMAC_KEY_SIZE];

        /// <summary>
        /// This property can be used to indicate whether an SCTP transport layer is port agnostic.
        /// For example a DTLS transport is likely to only ever create a single SCTP association 
        /// and the SCTP ports are redundant for matching end points. This allows the checks done
        /// on received SCTP packets to be more accepting about the ports used in the SCTP packet
        /// header.
        /// </summary>
        /// <returns>
        /// True if the transport implementation does not rely on the SCTP source and
        /// destination port for end point matching. False if it does.
        /// </returns>
        public virtual bool IsPortAgnostic => false;

        public abstract void Send(string associationID, ReadOnlySpan<byte> buffer);

        static SctpTransport()
        {
            Crypto.GetRandomBytes(_hmacKey);
        }

        protected void GotInit(SctpPacketView initPacket, IPEndPoint remoteEndPoint)
        {
            // INIT packets have specific processing rules in order to prevent resource exhaustion.
            // See Section 5 of RFC 4960 https://tools.ietf.org/html/rfc4960#section-5 "Association Initialization".

            var initChunk = initPacket.GetChunk(SctpChunkType.INIT);

            if (initChunk.InitiateTag == 0 ||
                initChunk.NumberInboundStreams == 0 ||
                initChunk.NumberOutboundStreams == 0)
            {
                // If the value of the Initiate Tag in a received INIT chunk is found
                // to be 0, the receiver MUST treat it as an error and close the
                // association by transmitting an ABORT. (RFC4960 pg. 25)

                // Note: A receiver of an INIT with the OS value set to 0 SHOULD
                // abort the association. (RFC4960 pg. 25)

                // Note: A receiver of an INIT with the MIS value of 0 SHOULD abort
                // the association. (RFC4960 pg. 26)

                SendError(
                  true,
                  initPacket.Header.DestinationPort,
                  initPacket.Header.SourcePort,
                  initChunk.InitiateTag,
                  new SctpCauseOnlyError(SctpErrorCauseCode.InvalidMandatoryParameter));
            }
            else
            {
                var initAckPacket = GetInitAck(initPacket.AsPacket(), remoteEndPoint);
                Send(null, initAckPacket);
            }
        }

        /// <summary>
        /// Sends an SCTP packet to the remote peer.
        /// </summary>
        /// <param name="pkt">The packet to send.</param>
        internal void Send(string? ID, SctpPacket pkt)
        {
            Span<byte> span = stackalloc byte[4 * 1024];
            if (pkt.GetBytes(span) is { } size and >= 0)
            {
                Send(ID, span.Slice(0, size));
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("SCTP packet too large to send without allocation.");
                byte[] buffer = pkt.GetBytes();
                Send(ID, buffer.AsSpan());
            }
        }

        /// <summary>
        /// Gets a cookie to send in an INIT ACK chunk. This method
        /// is overloadable so that different transports can tailor how the cookie
        /// is created. For example the WebRTC SCTP transport only ever uses a
        /// single association so the local Tag and TSN properties must be
        /// the same rather than random.
        /// </summary>
        protected virtual SctpTransportCookie GetInitAckCookie(
            ushort sourcePort,
            ushort destinationPort,
            uint remoteTag,
            uint remoteTSN,
            uint remoteARwnd,
            string remoteEndPoint,
            int lifeTimeExtension = 0)
        {
            var cookie = new SctpTransportCookie
            {
                SourcePort = sourcePort,
                DestinationPort = destinationPort,
                RemoteTag = remoteTag,
                RemoteTSN = remoteTSN,
                RemoteARwnd = remoteARwnd,
                RemoteEndPoint = remoteEndPoint,
                Tag = Crypto.GetRandomUInt(),
                TSN = Crypto.GetRandomUInt(),
                ARwnd = SctpAssociation.DEFAULT_ADVERTISED_RECEIVE_WINDOW,
                CreatedAt = DateTime.Now.ToString("o"),
                Lifetime = DEFAULT_COOKIE_LIFETIME_SECONDS + lifeTimeExtension,
                HMAC = string.Empty
            };

            return cookie;
        }

        /// <summary>
        /// Creates the INIT ACK chunk and packet to send as a response to an SCTP
        /// packet containing an INIT chunk.
        /// </summary>
        /// <param name="initPacket">The received packet containing the INIT chunk.</param>
        /// <param name="remoteEP">Optional. The remote IP end point the INIT packet was
        /// received on. For transports that don't use an IP transport directly this parameter
        /// can be set to null and it will not form part of the COOKIE ECHO checks.</param>
        /// <returns>An SCTP packet with a single INIT ACK chunk.</returns>
        protected SctpPacket GetInitAck(SctpPacket initPacket, IPEndPoint remoteEP)
        {
            var initChunk = (SctpInitChunk)initPacket.GetChunk(SctpChunkType.INIT);

            SctpPacket initAckPacket = new SctpPacket(
                initPacket.Header.DestinationPort,
                initPacket.Header.SourcePort,
                initChunk.InitiateTag);

            var cookie = GetInitAckCookie(
                initPacket.Header.DestinationPort,
                initPacket.Header.SourcePort,
                initChunk.InitiateTag,
                initChunk.InitialTSN,
                initChunk.ARwnd,
                remoteEP != null ? remoteEP.ToString() : string.Empty,
                (int)(initChunk.CookiePreservative / 1000));

            var json = cookie.ToJson();
            var jsonBuffer = Encoding.UTF8.GetBytes(json);

            using (HMACSHA256 hmac = new HMACSHA256(_hmacKey))
            {
                var result = hmac.ComputeHash(jsonBuffer);
                cookie.HMAC = result.HexStr();
            }

            var jsonWithHMAC = cookie.ToJson();
            var jsonBufferWithHMAC = Encoding.UTF8.GetBytes(jsonWithHMAC);

            SctpInitChunk initAckChunk = new SctpInitChunk(
                SctpChunkType.INIT_ACK,
                cookie.Tag,
                cookie.TSN,
                cookie.ARwnd,
                SctpAssociation.DEFAULT_NUMBER_OUTBOUND_STREAMS,
                SctpAssociation.DEFAULT_NUMBER_INBOUND_STREAMS);
            initAckChunk.StateCookie = jsonBufferWithHMAC;
            initAckChunk.UnrecognizedPeerParameters = initChunk.UnrecognizedPeerParameters;

            initAckPacket.AddChunk(initAckChunk);

            return initAckPacket;
        }

        /// <summary>
        /// Attempts to retrieve the cookie that should have been set by this peer from a COOKIE ECHO
        /// chunk. This is the step in the handshake that a new SCTP association will be created
        /// for a remote party. Providing the state cookie is valid create a new association.
        /// </summary>
        /// <param name="sctpPacket">The packet containing the COOKIE ECHO chunk received from the remote party.</param>
        /// <returns>If the state cookie in the chunk is valid a new SCTP association will be returned. IF
        /// it's not valid an empty cookie will be returned and an error response gets sent to the peer.</returns>
        protected SctpTransportCookie GetCookie(SctpPacketView sctpPacket)
        {
            var cookieEcho = sctpPacket.GetChunk(SctpChunkType.COOKIE_ECHO);
            var cookieBuffer = cookieEcho.Value;
            var cookie = JSONParser.FromJson<SctpTransportCookie>(cookieBuffer.ToString(Encoding.UTF8));

            logger.LogDebug($"Cookie: {cookie.ToJson()}");

            string calculatedHMAC = GetCookieHMAC(cookieBuffer);
            if (calculatedHMAC != cookie.HMAC)
            {
                logger.LogWarning($"SCTP COOKIE ECHO chunk had an invalid HMAC, calculated {calculatedHMAC}, cookie {cookie.HMAC}.");
                SendError(
                  true,
                  sctpPacket.Header.DestinationPort,
                  sctpPacket.Header.SourcePort,
                  0,
                  new SctpCauseOnlyError(SctpErrorCauseCode.InvalidMandatoryParameter));
                return SctpTransportCookie.Empty;
            }
            else if (DateTime.Now.Subtract(DateTime.Parse(cookie.CreatedAt)).TotalSeconds > cookie.Lifetime)
            {
                logger.LogWarning($"SCTP COOKIE ECHO chunk was stale, created at {cookie.CreatedAt}, now {DateTime.Now.ToString("o")}, lifetime {cookie.Lifetime}s.");
                var diff = DateTime.Now.Subtract(DateTime.Parse(cookie.CreatedAt).AddSeconds(cookie.Lifetime));
                SendError(
                  true,
                  sctpPacket.Header.DestinationPort,
                  sctpPacket.Header.SourcePort,
                  0,
                  new SctpErrorStaleCookieError { MeasureOfStaleness = (uint)(diff.TotalMilliseconds * 1000) });
                return SctpTransportCookie.Empty;
            }
            else
            {
                return cookie;
            }
        }

        /// <summary>
        /// Checks whether the state cookie that is supplied in a COOKIE ECHO chunk is valid for
        /// this SCTP transport.
        /// </summary>
        /// <param name="buffer">The buffer holding the state cookie.</param>
        /// <returns>True if the cookie is determined as valid, false if not.</returns>
        protected string GetCookieHMAC(ReadOnlySpan<byte> buffer)
        {
            var cookie = JSONParser.FromJson<SctpTransportCookie>(buffer.ToString(Encoding.UTF8));
            string hmacCalculated = null;
            cookie.HMAC = string.Empty;

            byte[] cookiePreImage = Encoding.UTF8.GetBytes(cookie.ToJson());

            using (HMACSHA256 hmac = new HMACSHA256(_hmacKey))
            {
                var result = hmac.ComputeHash(cookiePreImage);
                hmacCalculated = result.HexStr();
            }

            return hmacCalculated;
        }

        /// <summary>
        /// Send an SCTP packet with one of the error type chunks (ABORT or ERROR) to the remote peer.
        /// </summary>
        /// <param name="isAbort">Set to true to use an ABORT chunk otherwise an ERROR chunk will be used.</param>
        /// <param name="destinationPort">The SCTP destination port.</param>
        /// <param name="sourcePort">The SCTP source port.</param>
        /// <param name="initiateTag">If available the initial tag for the remote peer.</param>
        /// <param name="error">The error to send.</param>
        private void SendError(
            bool isAbort,
            ushort destinationPort,
            ushort sourcePort,
            uint initiateTag,
            ISctpErrorCause error)
        {
            SctpPacket errorPacket = new SctpPacket(
                destinationPort,
                sourcePort,
                initiateTag);

            SctpErrorChunk errorChunk = isAbort ? new SctpAbortChunk(true) : new SctpErrorChunk();
            errorChunk.AddErrorCause(error);
            errorPacket.AddChunk(errorChunk);

            Send(null, errorPacket);
        }

        /// <summary>
        /// This method allows SCTP to initialise its internal data structures
        /// and allocate necessary resources for setting up its operation
        /// environment.
        /// </summary>
        /// <param name="localPort">SCTP port number, if the application wants it to be specified.</param>
        /// <returns>The local SCTP instance name.</returns>
        public string Initialize(ushort localPort)
        {
            return "local SCTP instance name";
        }

        /// <summary>
        /// Initiates an association to a specific peer end point
        /// </summary>
        /// <param name="destination"></param>
        /// <param name="streamCount"></param>
        /// <returns>An association ID, which is a local handle to the SCTP association.</returns>
        public string Associate(IPAddress destination, int streamCount)
        {
            return "association ID";
        }

        /// <summary>
        /// Gracefully closes an association. Any locally queued user data will
        /// be delivered to the peer.The association will be terminated only
        /// after the peer acknowledges all the SCTP packets sent.
        /// </summary>
        /// <param name="associationID">Local handle to the SCTP association.</param>
        public void Shutdown(string associationID)
        {

        }

        /// <summary>
        /// Ungracefully closes an association. Any locally queued user data
        /// will be discarded, and an ABORT chunk is sent to the peer.
        /// </summary>
        /// <param name="associationID">Local handle to the SCTP association.</param>
        public void Abort(string associationID)
        {

        }

        /// <summary>
        /// This is the main method to send user data via SCTP.
        /// </summary>
        /// <param name="associationID">Local handle to the SCTP association.</param>
        /// <param name="buffer">The buffer holding the data to send.</param>
        /// <param name="length">The number of bytes from the buffer to send.</param>
        /// <param name="contextID">Optional. A 32-bit integer that will be carried in the
        /// sending failure notification to the application if the transportation of
        /// this user message fails.</param>
        /// <param name="streamID">Optional. To indicate which stream to send the data on. If not
        /// specified, stream 0 will be used.</param>
        /// <param name="lifeTime">Optional. specifies the life time of the user data. The user
        /// data will not be sent by SCTP after the life time expires.This
        /// parameter can be used to avoid efforts to transmit stale user
        /// messages.</param>
        /// <returns></returns>
        public string Send(string associationID, byte[] buffer, int length, int contextID, int streamID, int lifeTime)
        {
            return "ok";
        }

        /// <summary>
        /// Instructs the local SCTP to use the specified destination transport
        /// address as the primary path for sending packets.
        /// </summary>
        /// <param name="associationID"></param>
        /// <returns></returns>
        public string SetPrimary(string associationID)
        {
            // Note: Seems like this will be a noop for SCTP encapsulated in UDP.
            return "ok";
        }

        /// <summary>
        /// This method shall read the first user message in the SCTP in-queue
        /// into the buffer specified by the application, if there is one available.The
        /// size of the message read, in bytes, will be returned.
        /// </summary>
        /// <param name="associationID">Local handle to the SCTP association.</param>
        /// <param name="buffer">The buffer to place the received data into.</param>
        /// <param name="length">The maximum size of the data to receive.</param>
        /// <param name="streamID">Optional. If specified indicates which stream to 
        /// receive the data on.</param>
        /// <returns></returns>
        public int Receive(string associationID, byte[] buffer, int length, int streamID)
        {
            return 0;
        }

        /// <summary>
        /// Returns the current status of the association.
        /// </summary>
        /// <param name="associationID">Local handle to the SCTP association.</param>
        /// <returns></returns>
        public SctpStatus Status(string associationID)
        {
            return new SctpStatus();
        }

        /// <summary>
        /// Instructs the local endpoint to enable or disable heartbeat on the
        /// specified destination transport address.
        /// </summary>
        /// <param name="associationID">Local handle to the SCTP association.</param>
        /// <param name="interval">Indicates the frequency of the heartbeat if
        /// this is to enable heartbeat on a destination transport address.
        /// This value is added to the RTO of the destination transport
        /// address.This value, if present, affects all destinations.</param>
        /// <returns></returns>
        public string ChangeHeartbeat(string associationID, int interval)
        {
            return "ok";
        }

        /// <summary>
        /// Instructs the local endpoint to perform a HeartBeat on the specified
        /// destination transport address of the given association.
        /// </summary>
        /// <param name="associationID">Local handle to the SCTP association.</param>
        /// <returns>Indicates whether the transmission of the HEARTBEAT
        /// chunk to the destination address is successful.</returns>
        public string RequestHeartbeat(string associationID)
        {
            return "ok";
        }

        /// <summary>
        /// Instructs the local SCTP to report the current Smoothed Round Trip Time (SRTT)
        /// measurement on the specified destination transport address of the given 
        /// association.
        /// </summary>
        /// <param name="associationID">Local handle to the SCTP association.</param>
        /// <returns>An integer containing the most recent SRTT in milliseconds.</returns>
        public int GetSrttReport(string associationID)
        {
            return 0;
        }

        /// <summary>
        /// This method allows the local SCTP to customise the protocol
        /// parameters.
        /// </summary>
        /// <param name="associationID">Local handle to the SCTP association.</param>
        /// <param name="protocolParameters">The specific names and values of the
        /// protocol parameters that the SCTP user wishes to customise.</param>
        public void SetProtocolParameters(string associationID, object protocolParameters)
        {

        }

        /// <summary>
        /// ??
        /// </summary>
        /// <param name="dataRetrievalID">The identification passed to the application in the
        /// failure notification.</param>
        /// <param name="buffer">The buffer to store the received message.</param>
        /// <param name="length">The maximum size of the data to receive.</param>
        /// <param name="streamID">This is a return value that is set to indicate which
        /// stream the data was sent to.</param>
        public void ReceiveUnsent(string dataRetrievalID, byte[] buffer, int length, int streamID)
        {

        }

        /// <summary>
        /// ??
        /// </summary>
        /// <param name="dataRetrievalID">The identification passed to the application in the
        /// failure notification.</param>
        /// <param name="buffer">The buffer to store the received message.</param>
        /// <param name="length">The maximum size of the data to receive.</param>
        /// <param name="streamID">This is a return value that is set to indicate which
        /// stream the data was sent to.</param>
        public void ReceiveUnacknowledged(string dataRetrievalID, byte[] buffer, int length, int streamID)
        {

        }

        /// <summary>
        /// Release the resources for the specified SCTP instance.
        /// </summary>
        /// <param name="instanceName"></param>
        public void Destroy(string instanceName)
        {

        }
    }
}
