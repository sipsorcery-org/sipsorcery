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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

[assembly: InternalsVisibleToAttribute("SIPSorcery.UnitTests")]

namespace SIPSorcery.Net
{
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

        internal STUNUri _uri;
        internal string _username;
        internal string _password;
        internal int _id;           // An incrementing number that needs to be unique for each server in the session.

        /// <summary>
        /// The end point for this STUN or TURN server. Will be set asynchronously once
        /// any required DNS lookup completes.
        /// </summary>
        internal IPEndPoint ServerEndPoint { get; set; }

        /// <summary>
        /// The transaction ID to use in STUN requests. It is used to match responses
        /// with connection checks for this ICE serve entry.
        /// </summary>
        internal string TransactionID { get; private set; }

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
        /// Records the failure message if there was an error configuring or contacting
        /// the STUN or TURN server.
        /// </summary>
        internal SocketError Error { get; set; } = SocketError.Success;

        /// <summary>
        /// If the initial Binding (for STUN) or Allocate (for TURN) connection check is successful 
        /// this will hold the resultant server reflexive transport address.
        /// </summary>
        internal IPEndPoint ServerReflexiveEndPoint { get; set; }

        /// <summary>
        /// If the ICE server being checked is a TURN one and the Allocate request is successful this
        /// will hold the relay transport address.
        /// </summary>
        internal IPEndPoint RelayEndPoint { get; set; }

        /// <summary>
        /// If requests to the server need to be authenticated this is the nonce to set. 
        /// Normally the nonce will come from the server in a 401 Unauthorized response.
        /// </summary>
        internal byte[] Nonce { get; set; }

        /// <summary>
        /// If requests to the server need to be authenticated this is the realm to set. 
        /// The realm may be known in advance or can come from the server in a 401 
        /// Unauthorized response.
        /// </summary>
        internal byte[] Realm { get; set; }

        /// <summary>
        /// Count of the number of error responses received without a success response.
        /// </summary>
        internal int ErrorResponseCount = 0;

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="uri">The STUN or TURN server URI the connection is being attempted to.</param>
        /// <param name="id">Needs to be set uniquely for each ICE server used in this session. Gets added to the
        /// transaction ID to facilitate quick matching of STUN requests and responses. Needs to be between
        /// 0 and 9.</param>
        /// <param name="username">Optional. If authentication is required the username to use.</param>
        /// <param name="password">Optional. If authentication is required the password to use.</param>
        internal IceServer(STUNUri uri, int id, string username, string password)
        {
            _uri = uri;
            _id = id;
            _username = username;
            _password = password;
            GenerateNewTransactionID();
        }

        /// <summary>
        /// Gets an ICE candidate for this ICE server once the required server responses have been received.
        /// Note the related address and port are deliberately not set to avoid leaking information about
        /// internal network configuration.
        /// </summary>
        /// <param name="init">The initialisation parameters for the ICE candidate (mainly local username).</param>
        /// <param name="type">The type of ICE candidate to get, must be srflx or relay.</param>
        /// <returns>An ICE candidate that can be sent to the remote peer.</returns>
        internal RTCIceCandidate GetCandidate(RTCIceCandidateInit init, RTCIceCandidateType type)
        {
            RTCIceCandidate candidate = new RTCIceCandidate(init);

            if (type == RTCIceCandidateType.srflx && ServerReflexiveEndPoint != null)
            {
                candidate.SetAddressProperties(RTCIceProtocol.udp, ServerReflexiveEndPoint.Address, (ushort)ServerReflexiveEndPoint.Port,
                                type, null, 0);
                candidate.IceServer = this;

                return candidate;
            }
            else if (type == RTCIceCandidateType.relay && RelayEndPoint != null)
            {
                candidate.SetAddressProperties(RTCIceProtocol.udp, RelayEndPoint.Address, (ushort)RelayEndPoint.Port,
                    type, null, 0);
                candidate.IceServer = this;

                return candidate;
            }
            else
            {
                logger.LogWarning($"Could not get ICE server candidate for {_uri} and type {type}.");
                return null;
            }
        }

        /// <summary>
        /// A new transaction ID is needed for each request.
        /// </summary>
        internal void GenerateNewTransactionID()
        {
            TransactionID = ICE_SERVER_TXID_PREFIX + _id.ToString()
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
            return responseTxID.StartsWith(ICE_SERVER_TXID_PREFIX + _id.ToString());
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
            bool candidatesAvailable = false;

            string txID = Encoding.ASCII.GetString(stunResponse.Header.TransactionId);

            // Ignore responses to old requests on the assumption they are retransmits.
            if (TransactionID == txID)
            {
                // The STUN response is for a check sent to an ICE server.
                LastResponseReceivedAt = DateTime.Now;
                OutstandingRequestsSent = 0;

                if (stunResponse.Header.MessageType == STUNMessageTypesEnum.AllocateSuccessResponse)
                {
                    ErrorResponseCount = 0;

                    // If the relay end point is set then this connection check has already been completed.
                    if (RelayEndPoint == null)
                    {
                        logger.LogDebug($"TURN allocate success response received for ICE server check to {_uri}.");

                        var mappedAddrAttr = stunResponse.Attributes.Where(x => x.AttributeType == STUNAttributeTypesEnum.XORMappedAddress).FirstOrDefault();

                        if (mappedAddrAttr != null)
                        {
                            ServerReflexiveEndPoint = (mappedAddrAttr as STUNXORAddressAttribute).GetIPEndPoint();
                        }

                        var mappedRelayAddrAttr = stunResponse.Attributes.Where(x => x.AttributeType == STUNAttributeTypesEnum.XORRelayedAddress).FirstOrDefault();

                        if (mappedRelayAddrAttr != null)
                        {
                            RelayEndPoint = (mappedRelayAddrAttr as STUNXORAddressAttribute).GetIPEndPoint();
                        }

                        candidatesAvailable = true;
                    }
                }
                else if (stunResponse.Header.MessageType == STUNMessageTypesEnum.AllocateErrorResponse)
                {
                    ErrorResponseCount++;

                    if (stunResponse.Attributes.Any(x => x.AttributeType == STUNAttributeTypesEnum.ErrorCode))
                    {
                        var errCodeAttribute = stunResponse.Attributes.First(x => x.AttributeType == STUNAttributeTypesEnum.ErrorCode) as STUNErrorCodeAttribute;

                        if (errCodeAttribute.ErrorCode == STUN_UNAUTHORISED_ERROR_CODE)
                        {
                            // Set the authentication properties authenticate.
                            SetAuthenticationFields(stunResponse);

                            // Set a new transaction ID.
                            GenerateNewTransactionID();
                        }
                        else
                        {
                            logger.LogWarning($"ICE session received an error response for an Allocate request to {_uri}, error {errCodeAttribute.ErrorCode} {errCodeAttribute.ReasonPhrase}.");
                        }
                    }
                }
                else if (stunResponse.Header.MessageType == STUNMessageTypesEnum.BindingSuccessResponse)
                {
                    ErrorResponseCount = 0;

                    // If the server reflexive end point is set then this connection check has already been completed.
                    if (ServerReflexiveEndPoint == null)
                    {
                        logger.LogDebug($"STUN binding success response received for ICE server check to {_uri}.");

                        var mappedAddrAttr = stunResponse.Attributes.Where(x => x.AttributeType == STUNAttributeTypesEnum.XORMappedAddress).FirstOrDefault();

                        if (mappedAddrAttr != null)
                        {
                            ServerReflexiveEndPoint = (mappedAddrAttr as STUNXORAddressAttribute).GetIPEndPoint();
                            candidatesAvailable = true;
                        }
                    }
                }
                else if (stunResponse.Header.MessageType == STUNMessageTypesEnum.BindingErrorResponse)
                {
                    ErrorResponseCount++;

                    if (stunResponse.Attributes.Any(x => x.AttributeType == STUNAttributeTypesEnum.ErrorCode))
                    {
                        var errCodeAttribute = stunResponse.Attributes.First(x => x.AttributeType == STUNAttributeTypesEnum.ErrorCode) as STUNErrorCodeAttribute;

                        if (errCodeAttribute.ErrorCode == STUN_UNAUTHORISED_ERROR_CODE)
                        {
                            SetAuthenticationFields(stunResponse);

                            // Set a new transaction ID.
                            GenerateNewTransactionID();
                        }
                        else
                        {
                            logger.LogWarning($"ICE session received an error response for a Binding request to {_uri}, error {errCodeAttribute.ErrorCode} {errCodeAttribute.ReasonPhrase}.");
                        }
                    }
                    else
                    {
                        logger.LogWarning($"STUN binding error response received for ICE server check to {_uri}.");
                        // The STUN response is for a check sent to an ICE server.
                        Error = SocketError.ConnectionRefused;
                    }
                }
                else
                {
                    logger.LogWarning($"An unrecognised STUN {stunResponse.Header.MessageType} response for an ICE server check was received from {remoteEndPoint}.");
                    ErrorResponseCount++;
                }
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
            var nonceAttribute = stunResponse.Attributes.FirstOrDefault(x => x.AttributeType == STUNAttributeTypesEnum.Nonce);
            Nonce = nonceAttribute?.Value;

            var realmAttribute = stunResponse.Attributes.FirstOrDefault(x => x.AttributeType == STUNAttributeTypesEnum.Realm);
            Realm = realmAttribute?.Value;
        }
    }
}
