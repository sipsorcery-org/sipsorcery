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
// 08 Sep 2026              Aaron Clauson   Switched from JSON to binary serialisation
//                                          for the transport cookie.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// The opaque cookie structure that will be sent in response to an SCTP INIT
    /// packet.
    /// </summary>
    /// <remarks>
    /// The cookie is serialised to a fixed binary layout rather than to JSON. As per
    /// https://tools.ietf.org/html/rfc4960#section-5.1.3 the state cookie is opaque:
    /// the peer that creates it is the only peer that ever reads it, the remote party
    /// echoes it back verbatim in the COOKIE ECHO chunk. There is therefore no
    /// interoperability requirement on the format and no versioning concern.
    ///
    /// A binary layout is used in preference to a serialiser because:
    ///  - It is deterministic. The HMAC is computed over these exact bytes so the
    ///    field order and encoding are load bearing rather than cosmetic. Reflection
    ///    member ordering is not guaranteed by the runtime, a fixed layout is.
    ///  - It survives trimming. A reflection based serialiser loses the members of
    ///    this type under Native AOT, which produces an empty cookie, an HMAC that
    ///    can never match and an association that stalls in CookieEchoed until it
    ///    times out, so data channels never open.
    ///  - The HMAC covers the buffer prefix, so validating a received cookie does not
    ///    require deserialising and re-serialising it first.
    ///  - It is roughly a quarter of the size, which matters because the cookie is
    ///    carried in the INIT ACK.
    /// </remarks>
    public struct SctpTransportCookie
    {
        /// <summary>
        /// The length of the HMAC-SHA256 that occupies the tail of the serialised cookie.
        /// </summary>
        internal const int HMAC_LENGTH = 32;

        /// <summary>
        /// The length of the fixed portion of the serialised cookie, i.e. everything
        /// preceding the variable length remote end point and the trailing HMAC.
        /// </summary>
        private const int FIXED_LENGTH = 42;

        /// <summary>
        /// Field offsets within the serialised cookie.
        /// </summary>
        private const int SOURCE_PORT_OFFSET = 0;
        private const int DESTINATION_PORT_OFFSET = 2;
        private const int REMOTE_TAG_OFFSET = 4;
        private const int REMOTE_TSN_OFFSET = 8;
        private const int REMOTE_ARWND_OFFSET = 12;
        private const int TAG_OFFSET = 16;
        private const int TSN_OFFSET = 20;
        private const int ARWND_OFFSET = 24;
        private const int CREATED_AT_OFFSET = 28;
        private const int LIFETIME_OFFSET = 36;
        private const int REMOTE_END_POINT_LENGTH_OFFSET = 40;
        private const int REMOTE_END_POINT_OFFSET = 42;

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

        /// <summary>
        /// The UTC time the cookie was created. Used together with <see cref="Lifetime"/> to
        /// determine whether an echoed cookie is stale.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// The number of seconds after <see cref="CreatedAt"/> that the cookie remains valid for.
        /// </summary>
        public int Lifetime { get; set; }

        /// <summary>
        /// The HMAC-SHA256 over the remainder of the serialised cookie. Set by the transport
        /// once the cookie has been serialised, see <see cref="SctpTransport.GetInitAck"/>.
        /// </summary>
        public byte[] HMAC { get; set; }

        private bool _isEmpty;

        public bool IsEmpty()
        {
            return _isEmpty;
        }

        /// <summary>
        /// Serialises the cookie to the opaque buffer that gets carried in the state cookie
        /// parameter of an INIT ACK chunk.
        /// </summary>
        /// <remarks>
        /// The trailing HMAC bytes are left zeroed. The caller is expected to compute the HMAC
        /// over the returned buffer, excluding those trailing bytes, and then write it into
        /// them, see <see cref="SctpTransport.GetInitAck"/>. Doing it that way means the
        /// pre-image is simply the buffer prefix and never needs to be reconstructed.
        /// </remarks>
        /// <returns>The serialised cookie.</returns>
        public byte[] GetBytes()
        {
            byte[] endPoint = string.IsNullOrEmpty(RemoteEndPoint)
                ? Array.Empty<byte>()
                : Encoding.UTF8.GetBytes(RemoteEndPoint);

            if (endPoint.Length > ushort.MaxValue)
            {
                throw new ApplicationException("The SCTP state cookie remote end point was too long to serialise.");
            }

            var buffer = new byte[FIXED_LENGTH + endPoint.Length + HMAC_LENGTH];
            var span = buffer.AsSpan();

            BinaryPrimitives.WriteUInt16BigEndian(span.Slice(SOURCE_PORT_OFFSET), SourcePort);
            BinaryPrimitives.WriteUInt16BigEndian(span.Slice(DESTINATION_PORT_OFFSET), DestinationPort);
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(REMOTE_TAG_OFFSET), RemoteTag);
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(REMOTE_TSN_OFFSET), RemoteTSN);
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(REMOTE_ARWND_OFFSET), RemoteARwnd);
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(TAG_OFFSET), Tag);
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(TSN_OFFSET), TSN);
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(ARWND_OFFSET), ARwnd);
            BinaryPrimitives.WriteInt64BigEndian(span.Slice(CREATED_AT_OFFSET), CreatedAt.ToUniversalTime().Ticks);
            BinaryPrimitives.WriteInt32BigEndian(span.Slice(LIFETIME_OFFSET), Lifetime);
            BinaryPrimitives.WriteUInt16BigEndian(span.Slice(REMOTE_END_POINT_LENGTH_OFFSET), (ushort)endPoint.Length);

            Buffer.BlockCopy(endPoint, 0, buffer, REMOTE_END_POINT_OFFSET, endPoint.Length);

            if (HMAC != null)
            {
                if (HMAC.Length != HMAC_LENGTH)
                {
                    throw new ApplicationException("The SCTP state cookie HMAC was not the expected length.");
                }

                Buffer.BlockCopy(HMAC, 0, buffer, buffer.Length - HMAC_LENGTH, HMAC_LENGTH);
            }

            return buffer;
        }

        /// <summary>
        /// Attempts to deserialise a cookie from the buffer carried in a COOKIE ECHO chunk.
        /// </summary>
        /// <remarks>
        /// A remote peer controls these bytes so anything that does not match the layout is
        /// rejected rather than throwing. The caller treats a failed parse as a packet to
        /// drop, whereas an exception here would land in the receive loop.
        ///
        /// Note that a successful parse says nothing about authenticity, that is what the
        /// HMAC check in <see cref="SctpTransport.GetCookie"/> is for.
        /// </remarks>
        /// <param name="buffer">The buffer holding the serialised cookie.</param>
        /// <param name="cookie">If the parse succeeded this holds the deserialised cookie.</param>
        /// <returns>True if the buffer held a well formed cookie, false if not.</returns>
        public static bool TryParse(byte[] buffer, out SctpTransportCookie cookie)
        {
            cookie = Empty;

            if (buffer == null || buffer.Length < FIXED_LENGTH + HMAC_LENGTH)
            {
                return false;
            }

            var span = buffer.AsSpan();
            int endPointLength = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(REMOTE_END_POINT_LENGTH_OFFSET));

            if (buffer.Length != FIXED_LENGTH + endPointLength + HMAC_LENGTH)
            {
                return false;
            }

            long createdAtTicks = BinaryPrimitives.ReadInt64BigEndian(span.Slice(CREATED_AT_OFFSET));

            if (createdAtTicks < DateTime.MinValue.Ticks || createdAtTicks > DateTime.MaxValue.Ticks)
            {
                return false;
            }

            var hmac = new byte[HMAC_LENGTH];
            Buffer.BlockCopy(buffer, buffer.Length - HMAC_LENGTH, hmac, 0, HMAC_LENGTH);

            cookie = new SctpTransportCookie
            {
                SourcePort = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(SOURCE_PORT_OFFSET)),
                DestinationPort = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(DESTINATION_PORT_OFFSET)),
                RemoteTag = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(REMOTE_TAG_OFFSET)),
                RemoteTSN = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(REMOTE_TSN_OFFSET)),
                RemoteARwnd = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(REMOTE_ARWND_OFFSET)),
                Tag = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(TAG_OFFSET)),
                TSN = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(TSN_OFFSET)),
                ARwnd = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(ARWND_OFFSET)),
                CreatedAt = new DateTime(createdAtTicks, DateTimeKind.Utc),
                Lifetime = BinaryPrimitives.ReadInt32BigEndian(span.Slice(LIFETIME_OFFSET)),
                RemoteEndPoint = endPointLength > 0
                    ? Encoding.UTF8.GetString(buffer, REMOTE_END_POINT_OFFSET, endPointLength)
                    : string.Empty,
                HMAC = hmac
            };

            return true;
        }

        public override string ToString()
        {
            return $"SourcePort={SourcePort}, DestinationPort={DestinationPort}, RemoteTag={RemoteTag}, " +
                $"RemoteTSN={RemoteTSN}, RemoteARwnd={RemoteARwnd}, RemoteEndPoint={RemoteEndPoint}, " +
                $"Tag={Tag}, TSN={TSN}, ARwnd={ARwnd}, CreatedAt={CreatedAt:o}, Lifetime={Lifetime}, " +
                $"HMAC={(HMAC != null ? HMAC.HexStr() : null)}.";
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

        public abstract void Send(string associationID, byte[] buffer, int offset, int length);

        static SctpTransport()
        {
            Crypto.GetRandomBytes(_hmacKey);
        }

        protected void GotInit(SctpPacket initPacket, IPEndPoint remoteEndPoint)
        {
            // INIT packets have specific processing rules in order to prevent resource exhaustion.
            // See Section 5 of RFC 4960 https://tools.ietf.org/html/rfc4960#section-5 "Association Initialization".

            SctpInitChunk initChunk = initPacket.Chunks.Single(x => x.KnownType == SctpChunkType.INIT) as SctpInitChunk;

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
                var initAckPacket = GetInitAck(initPacket, remoteEndPoint);
                var buffer = initAckPacket.GetBytes();
                Send(null, buffer, 0, buffer.Length);
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
                CreatedAt = DateTime.UtcNow,
                Lifetime = DEFAULT_COOKIE_LIFETIME_SECONDS + lifeTimeExtension,
                HMAC = null
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
            SctpInitChunk initChunk = initPacket.Chunks.Single(x => x.KnownType == SctpChunkType.INIT) as SctpInitChunk;

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

            // The HMAC covers the serialised cookie up to but not including the HMAC itself, so
            // the buffer is serialised once with the HMAC bytes zeroed and the HMAC is then
            // written into place. That keeps the pre-image identical to the buffer prefix the
            // COOKIE ECHO gets validated against and avoids serialising twice.
            var cookieBuffer = cookie.GetBytes();
            var cookieHMAC = GetCookieHMAC(cookieBuffer);
            Buffer.BlockCopy(cookieHMAC, 0, cookieBuffer, cookieBuffer.Length - SctpTransportCookie.HMAC_LENGTH,
                SctpTransportCookie.HMAC_LENGTH);

            SctpInitChunk initAckChunk = new SctpInitChunk(
                SctpChunkType.INIT_ACK,
                cookie.Tag,
                cookie.TSN,
                cookie.ARwnd,
                SctpAssociation.DEFAULT_NUMBER_OUTBOUND_STREAMS,
                SctpAssociation.DEFAULT_NUMBER_INBOUND_STREAMS);
            initAckChunk.StateCookie = cookieBuffer;
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
        protected SctpTransportCookie GetCookie(SctpPacket sctpPacket)
        {
            var cookieEcho = sctpPacket.Chunks.Single(x => x.KnownType == SctpChunkType.COOKIE_ECHO);
            var cookieBuffer = cookieEcho.ChunkValue;

            // A remote peer controls this buffer. A COOKIE ECHO chunk with no chunk value, or one
            // that does not match the cookie layout, is a malformed packet to drop rather than an
            // exception to let loose in the receive loop.
            if (!SctpTransportCookie.TryParse(cookieBuffer, out var cookie))
            {
                logger.LogWarning("SCTP COOKIE ECHO chunk could not be parsed, ignoring.");
                return SctpTransportCookie.Empty;
            }

            logger.LogDebug("Cookie: {Cookie}", cookie);

            byte[] calculatedHMAC = GetCookieHMAC(cookieBuffer);
            if (!FixedTimeEquals(calculatedHMAC, cookie.HMAC))
            {
                logger.LogWarning("SCTP COOKIE ECHO chunk had an invalid HMAC, calculated {calculatedHMAC}, cookie {cookieHMAC}.", calculatedHMAC.HexStr(), cookie.HMAC.HexStr());
                SendError(
                  true,
                  sctpPacket.Header.DestinationPort,
                  sctpPacket.Header.SourcePort,
                  0,
                  new SctpCauseOnlyError(SctpErrorCauseCode.InvalidMandatoryParameter));
                return SctpTransportCookie.Empty;
            }
            else if (DateTime.UtcNow.Subtract(cookie.CreatedAt).TotalSeconds > cookie.Lifetime)
            {
                logger.LogWarning("SCTP COOKIE ECHO chunk was stale, created at {CreatedAt}, now {Now}, lifetime {Lifetime}s.", cookie.CreatedAt.ToString("o"), DateTime.UtcNow.ToString("o"), cookie.Lifetime);
                var diff = DateTime.UtcNow.Subtract(cookie.CreatedAt.AddSeconds(cookie.Lifetime));
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
        /// Calculates the HMAC for a serialised state cookie.
        /// </summary>
        /// <remarks>
        /// The pre-image is the whole of the supplied buffer apart from the trailing HMAC. No
        /// deserialise and re-serialise round trip is needed, which removes any possibility of
        /// the pre-image differing from the bytes that were originally hashed.
        /// </remarks>
        /// <param name="buffer">The buffer holding the state cookie.</param>
        /// <returns>The HMAC calculated over the supplied cookie.</returns>
        protected byte[] GetCookieHMAC(byte[] buffer)
        {
            if (buffer == null || buffer.Length < SctpTransportCookie.HMAC_LENGTH)
            {
                throw new ArgumentException("The buffer was too short to hold an SCTP state cookie.", nameof(buffer));
            }

            using (HMACSHA256 hmac = new HMACSHA256(_hmacKey))
            {
                return hmac.ComputeHash(buffer, 0, buffer.Length - SctpTransportCookie.HMAC_LENGTH);
            }
        }

        /// <summary>
        /// Compares two HMAC's in an amount of time that does not depend on how many leading
        /// bytes they have in common.
        /// </summary>
        /// <remarks>
        /// System.Security.Cryptography.CryptographicOperations.FixedTimeEquals is not available
        /// on all the frameworks this library targets.
        /// </remarks>
        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            int difference = 0;

            for (int i = 0; i < left.Length; i++)
            {
                difference |= left[i] ^ right[i];
            }

            return difference == 0;
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

            var buffer = errorPacket.GetBytes();
            Send(null, buffer, 0, buffer.Length);
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
