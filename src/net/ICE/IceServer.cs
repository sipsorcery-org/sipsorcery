//-----------------------------------------------------------------------------
// Filename: IceServer.cs
//
// Description: Encapsulates the connection details for a TURN/STUN server.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 22 Jun 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Digests;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

/// <summary>
/// If ICE servers (STUN or TURN) are being used with the session this class is used to track
/// the connection state for each server that gets used.
/// </summary>
public class IceServer
{
    private static readonly ILogger logger = Log.Logger;

    /// <summary>
    /// A magic cookie to use as the prefix for STUN requests generated for ICE servers.
    /// Allows quick matching of responses for ICE servers compared to responses for
    /// ICE candidate connectivity checks;
    /// </summary>
    internal const string ICE_SERVER_TXID_PREFIX = "91245";

    /// <summary>
    /// The length of the magic cookie of server ID that are used as the prefix for
    /// each ICE server transaction ID.
    /// </summary>
    internal const int ICE_SERVER_TXID_PREFIX_LENGTH = 6;

    /// <summary>
    /// The minimum ICE server ID that can be set.
    /// </summary>
    internal const int MINIMUM_ICE_SERVER_ID = 0;

    /// <summary>
    /// The maximum ICE server ID that can be set. Means the number of ICE servers per
    /// session is limited to 10. Checking 10 ICE servers when attempting to establish
    /// a peer connection seems very, very high. It would generally be expected that only
    /// 1 or 2 ICE servers would ever be used.
    /// </summary>
    internal const int MAXIMUM_ICE_SERVER_ID = 9;

    /// <summary>
    /// The maximum number of requests to send to an ICE server without getting 
    /// a response.
    /// </summary>
    internal const int MAX_REQUESTS = 25;

    /// <summary>
    /// The maximum number of error responses before failing the ICE server checks.
    /// A success response will reset the count.
    /// </summary>
    internal const int MAX_ERRORS = 3;

    /// <summary>
    /// Time to wait for a DNS lookup of an ICE server to complete.
    /// </summary>
    internal const int DNS_LOOKUP_TIMEOUT_SECONDS = 3;

    /// <summary>
    /// The period at which to refresh a successful STUN binding. If the ICE
    /// server did not get used as the nominated candidate the ICE server 
    /// checks timer will be stopped.
    /// </summary>
    internal const int STUN_BINDING_REQUEST_REFRESH_SECONDS = 180;

    /// <summary>
    /// The STUN error code response indicating an authenticated request is required.
    /// </summary>
    internal const int STUN_UNAUTHORISED_ERROR_CODE = 401;

    /// <summary>
    /// The STUN error code response indicating a stale nonce
    /// </summary>
    internal const int STUN_STALE_NONCE_ERROR_CODE = 438;

    internal STUNUri Uri { get; }

    internal ReadOnlyMemory<byte> Username { get; }

    internal ReadOnlyMemory<byte> Password { get; }

    /// <summary>
    /// An incrementing number that needs to be unique for each server in the session.
    /// </summary>
    internal int Id { get; }

    /// <summary>
    /// The end point for this STUN or TURN server. Will be set asynchronously once
    /// any required DNS lookup completes.
    /// </summary>
    public IPEndPoint? ServerEndPoint { get; set; }

    /// <summary>
    /// The transaction ID to use in STUN requests. It is used to match responses
    /// with connection checks for this ICE serve entry.
    /// </summary>
    internal string? TransactionID { get; private set; }

    /// <summary>
    /// The timestamp that the DNS lookup for this ICE server was sent at.
    /// </summary>
    internal DateTime DnsLookupSentAt { get; set; } = DateTime.MinValue;

    /// <summary>
    /// The number of requests that have been sent to the server without
    /// a response.
    /// </summary>
    internal int OutstandingRequestsSent { get; set; }

    /// <summary>
    /// The timestamp the most recent binding request was sent at.
    /// </summary>
    internal DateTime LastRequestSentAt { get; set; }

    /// <summary>
    /// The timestamp of the most recent response received from the ICE server.
    /// </summary>
    internal DateTime LastResponseReceivedAt { get; set; } = DateTime.MinValue;

    /// <summary>
    /// This field records the time when allocation expires
    /// </summary>
    public DateTime TurnTimeToExpiry { get; set; } = DateTime.MinValue;

    /// <summary>
    /// Records the failure message if there was an error configuring or contacting
    /// the STUN or TURN server.
    /// </summary>
    internal SocketError Error { get; set; } = SocketError.Success;

    /// <summary>
    /// If the initial Binding (for STUN) or Allocate (for TURN) connection check is successful 
    /// this will hold the resultant server reflexive transport address.
    /// </summary>
    public IPEndPoint? ServerReflexiveEndPoint { get; set; }

    /// <summary>
    /// If the ICE server being checked is a TURN one and the Allocate request is successful this
    /// will hold the relay transport address.
    /// </summary>
    internal IPEndPoint? RelayEndPoint { get; set; }

    /// <summary>
    /// If requests to the server need to be authenticated this is the nonce to set. 
    /// Normally the nonce will come from the server in a 401 Unauthorized response.
    /// </summary>
    internal ReadOnlyMemory<byte> Nonce { get; private set; }

    /// <summary>
    /// If requests to the server need to be authenticated this is the realm to set. 
    /// The realm may be known in advance or can come from the server in a 401 
    /// Unauthorized response.
    /// </summary>
    internal ReadOnlyMemory<byte> Realm { get; private set; }

    /// <summary>
    /// Count of the number of error responses received without a success response.
    /// </summary>
    internal int ErrorResponseCount;

    public ProtocolType Protocol => Uri.Protocol;

    /// <summary>
    /// Task that completes when this server is done (resolved or timed out).
    /// </summary>
    internal Task? DnsResolutionTask { get; set; }

    internal SslClientAuthenticationOptions? SslClientAuthenticationOptions { get; set; }

    internal ReadOnlyMemory<byte> MessageIntegrityKey { get; private set; }

    /// <summary>
    /// Default constructor.
    /// </summary>
    /// <param name="uri">The STUN or TURN server URI the connection is being attempted to.</param>
    /// <param name="id">Needs to be set uniquely for each ICE server used in this session. Gets added to the
    /// transaction ID to facilitate quick matching of STUN requests and responses. Needs to be between
    /// 0 and 9.</param>
    /// <param name="username">Optional. If authentication is required the username to use.</param>
    /// <param name="password">Optional. If authentication is required the password to use.</param>
    internal IceServer(STUNUri uri, int id, string? username, string? password)
    {
        Uri = uri;
        Id = id;
        Username = string.IsNullOrEmpty(username) ? default : Encoding.UTF8.GetBytes(username);
        Password = string.IsNullOrEmpty(password) ? default : Encoding.UTF8.GetBytes(password);

        GenerateNewTransactionID();
    }

    /// <summary>
    /// Parses a semicolon-delimited ICE server span into an IceServer instance.
    /// Expected format:
    /// urls[;username[;credential]]
    /// Examples:
    /// "stun:stun.example.com:3478"
    /// "turn:turn.example.com?transport=tcp;user1;pass1"
    /// "stun:stun1.example.com,stun:stun2.example.com"
    /// Notes:
    /// - Whitespace is trimmed.
    /// - Surrounding quotes are removed from fields.
    /// - If multiple URLs are provided in the first field (comma or whitespace separated),
    /// the first non-empty URL is used.
    /// - If the URL lacks a scheme (e.g. "example.com:3478"), a stun: scheme will be assumed.
    /// </summary>
    /// <param name="iceServer">The ICE server span to parse. Format: "urls[;username[;credential]]".</param>
    /// <returns>An IceServer configured with the parsed values.</returns>
    /// <exception cref="ArgumentException">Thrown if iceServer is empty or the URL is invalid.</exception>
    public static IceServer ParseIceServer(ReadOnlySpan<char> iceServer)
    {
        ArgumentException.ThrowIfEmptyWhiteSpace(iceServer);

        // Trim the input
        iceServer = iceServer.Trim();

        // Extract fields on demand without creating a list
        var urlsFieldRaw = UnquoteSpan(ExtractField(iceServer, 0));
        if (urlsFieldRaw.IsEmpty)
        {
            throw new ArgumentException("ICE server value must include a STUN/TURN URL in the first field.", nameof(iceServer));
        }

        // If multiple URLs are provided, take the first non-empty candidate.
        // Split on comma or whitespace and return early on first match.
        ReadOnlySpan<char> selectedUrl = default;
        var start = 0;
        for (var i = 0; i <= urlsFieldRaw.Length; i++)
        {
            if (i == urlsFieldRaw.Length || urlsFieldRaw[i] == ',' || urlsFieldRaw[i] == ' ')
            {
                if (i > start)
                {
                    var candidate = urlsFieldRaw.Slice(start, i - start).Trim();
                    if (!candidate.IsEmpty)
                    {
                        selectedUrl = candidate;
                        break;
                    }
                }
                start = i + 1;
            }
        }

        if (selectedUrl.IsEmpty)
        {
            selectedUrl = urlsFieldRaw.Trim();
        }

        // Try validate; if it fails, try auto-prefixing stun:
        if (!STUNUri.TryParse(selectedUrl, out var stunUri))
        {
            selectedUrl = $"stun:{selectedUrl.ToString()}";
            if (!STUNUri.TryParse(selectedUrl, out stunUri))
            {
                throw new ArgumentException(
                    $"Invalid ICE server URL: '{selectedUrl}'. Expected a STUN/TURN URI such as 'stun:example.org:3478' or 'turn:example.org?transport=tcp'.",
                    nameof(iceServer));
            }
        }

        // username (optional)
        var username = UnquoteSpan(ExtractField(iceServer, 1));

        // credential (optional)
        var credential = UnquoteSpan(ExtractField(iceServer, 2));

        return new IceServer(
            stunUri,
            0,
            username.IsEmpty ? null : username.ToString(),
            credential.IsEmpty ? null : credential.ToString());

        static ReadOnlySpan<char> ExtractField(ReadOnlySpan<char> input, int fieldIndex)
        {
            var fieldCount = 0;
            var start = 0;

            for (var i = 0; i <= input.Length; i++)
            {
                if (i == input.Length || input[i] == ';')
                {
                    if (fieldCount == fieldIndex)
                    {
                        return input.Slice(start, i - start).Trim();
                    }
                    fieldCount++;
                    start = i + 1;
                }
            }

            return ReadOnlySpan<char>.Empty;
        }

        static ReadOnlySpan<char> UnquoteSpan(ReadOnlySpan<char> s)
        {
            if (s.IsEmpty)
            {
                return ReadOnlySpan<char>.Empty;
            }

            s = s.Trim();
            if (s.Length >= 2)
            {
                if ((s[0] == '"' && s[s.Length - 1] == '"') ||
                (s[0] == '\'' && s[s.Length - 1] == '\''))
                {
                    return s.Slice(1, s.Length - 2).Trim();
                }
            }
            return s;
        }
    }

    /// <summary>
    /// Gets an ICE candidate for this ICE server once the required server responses have been received.
    /// Note the related address and port are deliberately not set to avoid leaking information about
    /// internal network configuration.
    /// </summary>
    /// <param name="init">The initialisation parameters for the ICE candidate (mainly local username).</param>
    /// <param name="type">The type of ICE candidate to get, must be srflx or relay.</param>
    /// <returns>An ICE candidate that can be sent to the remote peer.</returns>
    internal RTCIceCandidate? GetCandidate(RTCIceCandidateInit init, RTCIceCandidateType type)
    {
        if (type == RTCIceCandidateType.srflx && ServerReflexiveEndPoint is { })
        {
            // TODO: Currently implementation always use UDP candidates as we will only support TURN TCP Transport.
            //var srflxProtocol = _uri.Protocol == ProtocolType.Tcp ? RTCIceProtocol.tcp : RTCIceProtocol.udp;
            var srflxProtocol = RTCIceProtocol.udp;

            var candidate = new RTCIceCandidate(init);

            candidate.SetAddressProperties(srflxProtocol, ServerReflexiveEndPoint.Address, (ushort)ServerReflexiveEndPoint.Port,
                            type, null, 0);
            candidate.IceServer = this;

            return candidate;
        }
        else if (type == RTCIceCandidateType.relay && RelayEndPoint is { })
        {
            // TODO: Currently implementation always use UDP candidates as we will only support TURN TCP Transport.
            //var relayProtocol = _uri.Protocol == ProtocolType.Tcp ? RTCIceProtocol.tcp : RTCIceProtocol.udp;
            var relayProtocol = RTCIceProtocol.udp;

            var candidate = new RTCIceCandidate(init);

            candidate.SetAddressProperties(relayProtocol, RelayEndPoint.Address, (ushort)RelayEndPoint.Port,
                type, null, 0);
            candidate.IceServer = this;

            return candidate;
        }
        else
        {
            logger.LogIceServerCandidateUnavailable(Uri, type);
            return null;
        }
    }

    /// <summary>
    /// A new transaction ID is needed for each request.
    /// </summary>
    internal void GenerateNewTransactionID()
    {
        TransactionID = ICE_SERVER_TXID_PREFIX + Id.ToString()
            + Crypto.GetRandomString(STUNHeader.TRANSACTION_ID_LENGTH - ICE_SERVER_TXID_PREFIX_LENGTH);
    }

    /// <summary>
    /// Checks whether a STUN response transaction ID belongs to a request that was sent for
    /// this ICE server entry.
    /// </summary>
    /// <param name="responseTxID">The transaction ID from the STUN response.</param>
    /// <returns>True if it dos match. False if not.</returns>
    internal bool IsTransactionIDMatch(string responseTxID)
    {
        if (responseTxID.Length < ICE_SERVER_TXID_PREFIX.Length
            || !responseTxID.StartsWith(ICE_SERVER_TXID_PREFIX, StringComparison.Ordinal))
        {
            return false;
        }

        var idPart = responseTxID.AsSpan(ICE_SERVER_TXID_PREFIX.Length);
        return idPart.StartsWith(Id.ToString().AsSpan(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Handler for a STUN response received in response to an ICE server connectivity check.
    /// Note that no STUN requests are expected to be received from an ICE server during the initial
    /// connection to an ICE server. Requests will only arrive if a TURN relay is used and data
    /// indications arrive but this will be at a later stage.
    /// </summary>
    /// <param name="stunResponse">The STUN response received.</param>
    /// <param name="remoteEndPoint">The remote end point the STUN response was received from.</param>
    /// <returns>True if the STUN response resulted in new ICE candidates being available (which
    /// will be either a "server reflexive" or "relay" candidate.</returns>
    internal bool GotStunResponse(STUNMessage stunResponse, IPEndPoint remoteEndPoint)
    {
        var candidatesAvailable = false;

        // Ignore responses to old requests on the assumption they are retransmits.
        if (!Encoding.ASCII.Equals(TransactionID, stunResponse.Header.TransactionId))
        {
            return candidatesAvailable;
        }

        // The STUN response is for a check sent to an ICE server.
        LastResponseReceivedAt = DateTime.Now;
        OutstandingRequestsSent = 0;

        switch (stunResponse.Header.MessageType)
        {
            case STUNMessageTypesEnum.AllocateSuccessResponse:
                ErrorResponseCount = 0;

                // If the relay end point is set then this connection check has already been completed.
                if (RelayEndPoint is null)
                {
                    logger.LogIceAllocationSucceeded(Uri);

                    STUNXORAddressAttribute? mappedAddressAttribute = null;
                    STUNXORAddressAttribute? relayedAddressAttribute = null;
                    STUNAttribute? lifetimeAttribute = null;

                    foreach (var attr in stunResponse.Attributes)
                    {
                        if (mappedAddressAttribute is null && attr.AttributeType == STUNAttributeTypesEnum.XORMappedAddress)
                        {
                            mappedAddressAttribute = attr as STUNXORAddressAttribute;
                        }
                        else if (relayedAddressAttribute is null && attr.AttributeType == STUNAttributeTypesEnum.XORRelayedAddress)
                        {
                            relayedAddressAttribute = attr as STUNXORAddressAttribute;
                        }
                        else if (lifetimeAttribute is null && attr.AttributeType == STUNAttributeTypesEnum.Lifetime)
                        {
                            lifetimeAttribute = attr;
                        }

                        if (mappedAddressAttribute is { } && relayedAddressAttribute is { } && lifetimeAttribute is { })
                        {
                            break;
                        }
                    }

                    if (mappedAddressAttribute is { })
                    {
                        ServerReflexiveEndPoint = mappedAddressAttribute.GetIPEndPoint();
                    }

                    if (relayedAddressAttribute is { })
                    {
                        RelayEndPoint = relayedAddressAttribute.GetIPEndPoint();
                    }

                    if (lifetimeAttribute is { })
                    {
                        TurnTimeToExpiry =
                            DateTime.Now + TimeSpan.FromSeconds(BinaryPrimitives.ReadUInt32BigEndian(lifetimeAttribute.Value.Span));
                    }
                    else
                    {
                        TurnTimeToExpiry = DateTime.Now +
                                           TimeSpan.FromSeconds(3600);
                    }

                    candidatesAvailable = true;
                }
                break;
            case STUNMessageTypesEnum.AllocateErrorResponse:
                ErrorResponseCount++;

                STUNErrorCodeAttribute? allocateErrorCodeAttribute = null;
                STUNAddressAttribute? allocateAlternateServerAttribute = null;
                foreach (var attr in stunResponse.Attributes)
                {
                    if (allocateErrorCodeAttribute is null && attr.AttributeType == STUNAttributeTypesEnum.ErrorCode)
                    {
                        allocateErrorCodeAttribute = attr as STUNErrorCodeAttribute;
                        break; // Stop as soon as the first ErrorCode is found
                    }
                    else if (allocateAlternateServerAttribute is null && attr.AttributeType == STUNAttributeTypesEnum.AlternateServer)
                    {
                        allocateAlternateServerAttribute = attr as STUNAddressAttribute;
                    }

                    if (allocateErrorCodeAttribute is { } && allocateAlternateServerAttribute is { })
                    {
                        break; // Stop as soon as both attributes are found
                    }
                }

                if (allocateErrorCodeAttribute is { })
                {
                    if (allocateErrorCodeAttribute.ErrorCode is STUN_UNAUTHORISED_ERROR_CODE or STUN_STALE_NONCE_ERROR_CODE)
                    {
                        if (allocateErrorCodeAttribute.ErrorCode is STUN_UNAUTHORISED_ERROR_CODE)
                        {
                            logger.LogStunUnauthorisedError(remoteEndPoint);
                        }
                        else
                        {
                            logger.LogStunStaleNonceError(remoteEndPoint);
                        }

                        // Set the authentication properties authenticate.
                        SetAuthenticationFields(stunResponse);

                        // Set a new transaction ID.
                        GenerateNewTransactionID();

                        ErrorResponseCount = 1;
                    }
                    else if (allocateAlternateServerAttribute is { })
                    {
                        Debug.Assert(allocateAlternateServerAttribute.Address is { });
                        ServerEndPoint = new IPEndPoint(allocateAlternateServerAttribute.Address, allocateAlternateServerAttribute.Port);

                        logger.LogIceStunAlternateServer(Uri, ServerEndPoint);

                        // Set a new transaction ID.
                        GenerateNewTransactionID();

                        ErrorResponseCount = 1;
                    }
                    else
                    {
                        logger.LogIceAllocateRequestErrorResponseWithCode(Uri, allocateErrorCodeAttribute.ErrorCode, allocateErrorCodeAttribute.ReasonPhrase);
                    }
                }
                else
                {
                    logger.LogIceStunAllocateError(Uri);
                }
                break;
            case STUNMessageTypesEnum.BindingSuccessResponse:
                ErrorResponseCount = 0;

                // If the server reflexive end point is set then this connection check has already been completed.
                if (ServerReflexiveEndPoint is null)
                {
                    logger.LogIceStunBindingSuccess(Uri);
                    foreach (var attr in stunResponse.Attributes)
                    {
                        if (attr.AttributeType == STUNAttributeTypesEnum.XORMappedAddress)
                        {
                            ServerReflexiveEndPoint = ((STUNXORAddressAttribute)attr).GetIPEndPoint();
                            candidatesAvailable = true;
                            break;
                        }
                    }
                }
                break;
            case STUNMessageTypesEnum.BindingErrorResponse:
                ErrorResponseCount++;

                STUNErrorCodeAttribute? bindErrorCodeAttribute = null;
                foreach (var attr in stunResponse.Attributes)
                {
                    if (attr.AttributeType == STUNAttributeTypesEnum.ErrorCode)
                    {
                        bindErrorCodeAttribute = attr as STUNErrorCodeAttribute;
                        break;
                    }
                }

                if (bindErrorCodeAttribute is { })
                {
                    if (bindErrorCodeAttribute.ErrorCode is STUN_UNAUTHORISED_ERROR_CODE or STUN_STALE_NONCE_ERROR_CODE)
                    {
                        SetAuthenticationFields(stunResponse);

                        // Set a new transaction ID.
                        GenerateNewTransactionID();
                    }
                    else
                    {
                        logger.LogIceBindingRequestErrorResponseWithCode(Uri, bindErrorCodeAttribute.ErrorCode, bindErrorCodeAttribute.ReasonPhrase);
                    }
                }
                else
                {
                    logger.LogIceStunBindingError(Uri);
                    // The STUN response is for a check sent to an ICE server.
                    Error = SocketError.ConnectionRefused;
                }
                break;
            case STUNMessageTypesEnum.RefreshSuccessResponse:
                ErrorResponseCount = 0;

                logger.LogIceStunBindingSuccess(Uri);

                STUNAttribute? refreshLifetimeAttr = null;
                foreach (var attr in stunResponse.Attributes)
                {
                    if (attr.AttributeType == STUNAttributeTypesEnum.Lifetime)
                    {
                        refreshLifetimeAttr = attr;
                        break;
                    }

                }
                if (refreshLifetimeAttr is { })
                {
                    TurnTimeToExpiry = DateTime.Now +
                                       TimeSpan.FromSeconds(BinaryPrimitives.ReadUInt32BigEndian(refreshLifetimeAttr.Value.Span));
                }

                break;
            case STUNMessageTypesEnum.RefreshErrorResponse:
                ErrorResponseCount++;

                STUNErrorCodeAttribute? refreshErrorCodeAttribute = null;
                foreach (var attr in stunResponse.Attributes)
                {
                    if (attr.AttributeType == STUNAttributeTypesEnum.ErrorCode)
                    {
                        refreshErrorCodeAttribute = attr as STUNErrorCodeAttribute;
                        break;
                    }
                }

                if (refreshErrorCodeAttribute is { })
                {
                    if (refreshErrorCodeAttribute.ErrorCode is STUN_UNAUTHORISED_ERROR_CODE or STUN_STALE_NONCE_ERROR_CODE)
                    {
                        SetAuthenticationFields(stunResponse);

                        // Set a new transaction ID.
                        GenerateNewTransactionID();
                    }
                    else
                    {
                        logger.LogIceRefreshRequestErrorResponseWithCode(Uri, refreshErrorCodeAttribute.ErrorCode, refreshErrorCodeAttribute.ReasonPhrase);
                    }
                }
                else
                {
                    logger.LogIceStunBindingError(Uri);
                    // The STUN response is for a check sent to an ICE server.
                    Error = SocketError.ConnectionRefused;
                }
                break;
            default:
                logger.LogIceUnrecognisedStunResponse(stunResponse.Header.MessageType, remoteEndPoint);
                ErrorResponseCount++;
                break;
        }

        return candidatesAvailable;
    }

    /// <summary>
    /// Extracts the fields required for authentication from a STUN error response.
    /// </summary>
    /// <param name="stunResponse">The STUN authentication required error response.</param>
    internal void SetAuthenticationFields(STUNMessage stunResponse)
    {
        // Set the authentication properties authenticate.

        var computeMessageIntegrityKey = false;

        foreach (var attr in stunResponse.Attributes)
        {
            if (attr.AttributeType == STUNAttributeTypesEnum.Nonce)
            {
                Nonce = attr.Value.ToArray();
                computeMessageIntegrityKey = true;
            }
            else if (attr.AttributeType == STUNAttributeTypesEnum.Realm)
            {
                Realm = attr.Value.ToArray();
                computeMessageIntegrityKey = true;
            }

            if (!Nonce.IsEmpty && !Realm.IsEmpty)
            {
                break;
            }
        }

        if (computeMessageIntegrityKey && !Realm.IsEmpty && !Nonce.IsEmpty && !Username.IsEmpty && !Password.IsEmpty)
        {
            var messageIntegrityKeySource = new byte[Username.Length + Realm.Length + Password.Length + 2];
            var messageIntegrityKeySourceSpan = messageIntegrityKeySource.AsSpan();

            var offset = Username.Length;
            Username.Span.CopyTo(messageIntegrityKeySourceSpan);
            messageIntegrityKeySourceSpan[offset++] = (byte)':';

            Realm.Span.CopyTo(messageIntegrityKeySourceSpan.Slice(offset));
            offset += Realm.Length;
            messageIntegrityKeySourceSpan[offset++] = (byte)':';

            Password.Span.CopyTo(messageIntegrityKeySourceSpan.Slice(offset));


            var md5Digest = new MD5Digest();
            var md5DigestLength = md5Digest.GetDigestSize();

            var messageIntegrityKey = new byte[md5DigestLength];

            md5Digest.BlockUpdate(messageIntegrityKeySource);
            md5Digest.DoFinal(messageIntegrityKey, 0);

            MessageIntegrityKey = messageIntegrityKey;
        }
    }
}
