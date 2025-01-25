//-----------------------------------------------------------------------------
// Filename: SIPRequest.cs
//
// Description: SIP Request.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 20 Oct 2005	Aaron Clauson   Created, Dublin, Ireland.
// 26 Nov 2019  Aaron Clauson   Added SIPMessageBase inheritance.
// 14 Jul 2021  Aaron Clauson   Added duplicate and authenticate convenience method.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    /// <summary>
    /// Represents a SIP Request.
    /// </summary>
    public class SIPRequest : SIPMessageBase
    {
        public string SIPVersion = m_sipFullVersion;
        public SIPMethodsEnum Method;
        public string UnknownMethod = null;

        /// <summary>
        /// The SIP request's URI.
        /// </summary>
        public SIPURI URI;

        public SIPRoute ReceivedRoute;

        /// <summary>
        /// The first line of the SIP request.
        /// </summary>
        public string StatusLine
        {
            get
            {
                string methodStr = (Method != SIPMethodsEnum.UNKNOWN) ? Method.ToString() : UnknownMethod;
                return methodStr + " " + URI.ToString() + " " + SIPVersion;
            }
        }

        private SIPRequest(Encoding sipEncoding, Encoding sipBodyEncoding) : this(SIPMethodsEnum.NONE, SIPURI.None,sipEncoding,sipBodyEncoding)
        {
        }

        private SIPRequest()
        {
        }

        public SIPRequest(SIPMethodsEnum method, string uri):this(method, SIPURI.ParseSIPURI(uri))
        {
        }

        public SIPRequest(SIPMethodsEnum method, SIPURI uri) 
        {
            Method = method;
            URI = uri;
            SIPVersion = m_sipFullVersion;
        }

        public SIPRequest(SIPMethodsEnum method, SIPURI uri,Encoding sipEncoding,Encoding sipBodyEncoding):base(sipEncoding,sipBodyEncoding)
        {
            Method = method;
            URI = uri;
            SIPVersion = m_sipFullVersion;
        }

        public static SIPRequest ParseSIPRequest(SIPMessageBuffer sipMessage) =>
            ParseSIPRequest(sipMessage, SIPConstants.DEFAULT_ENCODING, SIPConstants.DEFAULT_ENCODING);

        public static SIPRequest ParseSIPRequest(SIPMessageBuffer sipMessage,Encoding sipEncoding,Encoding sipBodyEncoding)
        {
            try
            {
                SIPRequest sipRequest = new SIPRequest(sipEncoding,sipBodyEncoding);
                sipRequest.LocalSIPEndPoint = sipMessage.LocalSIPEndPoint;
                sipRequest.RemoteSIPEndPoint = sipMessage.RemoteSIPEndPoint;

                string statusLine = sipMessage.FirstLine;

                int firstSpacePosn = statusLine.IndexOf(" ");

                string method = statusLine.Substring(0, firstSpacePosn).Trim();
                sipRequest.Method = SIPMethods.GetMethod(method);
                if (sipRequest.Method == SIPMethodsEnum.UNKNOWN)
                {
                    sipRequest.UnknownMethod = method;
                    logger.LogWarning("Unknown SIP method received {UnknownMethod}.", sipRequest.UnknownMethod);
                }

                statusLine = statusLine.Substring(firstSpacePosn).Trim();
                int secondSpacePosn = statusLine.IndexOf(" ");

                if (secondSpacePosn != -1)
                {
                    string uriStr = statusLine.Substring(0, secondSpacePosn);

                    sipRequest.URI = SIPURI.ParseSIPURI(uriStr);
                    sipRequest.SIPVersion = statusLine.Substring(secondSpacePosn, statusLine.Length - secondSpacePosn).Trim();
                    sipRequest.Header = SIPHeader.ParseSIPHeaders(sipMessage.SIPHeaders);
                    sipRequest.BodyBuffer = sipMessage.Body;

                    return sipRequest;
                }
                else
                {
                    throw new SIPValidationException(SIPValidationFieldsEnum.Request, "URI was missing on Request.");
                }
            }
            catch (SIPValidationException)
            {
                throw;
            }
            catch (Exception excp)
            {
                logger.LogError(excp, "Exception parsing SIP Request: {SipMessage}. {ErrorMessage}", sipMessage.RawMessage, excp.Message);
                throw new SIPValidationException(SIPValidationFieldsEnum.Request, "Unknown error parsing SIP Request");
            }
        }

        public static SIPRequest ParseSIPRequest(string sipMessageStr) =>
            ParseSIPRequest(sipMessageStr, SIPConstants.DEFAULT_ENCODING, SIPConstants.DEFAULT_ENCODING);

        public static SIPRequest ParseSIPRequest(string sipMessageStr,Encoding sipEncoding,Encoding sipBodyEncoding)
        {
            try
            {
                SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(sipMessageStr, sipEncoding,sipBodyEncoding, null, null);
                return SIPRequest.ParseSIPRequest(sipMessageBuffer,sipEncoding,sipBodyEncoding);
            }
            catch (SIPValidationException)
            {
                throw;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception ParseSIPRequest: {SipMessage}. {ErrorMessage}", sipMessageStr, excp.Message);
                throw new SIPValidationException(SIPValidationFieldsEnum.Request, "Unknown error parsing SIP Request");
            }
        }

        public override string ToString()
        {
            try
            {
                string message = StatusLine + m_CRLF + this.Header.ToString();

                if (Body != null)
                {
                    message += m_CRLF + Body;
                }
                else
                {
                    message += m_CRLF;
                }

                return message;
            }
            catch (Exception excp)
            {
                logger.LogError(excp, "Exception SIPRequest ToString. {ErrorMessage}", excp.Message);
                throw;
            }
        }

        /// <summary>
        /// Creates an identical copy of the SIP Request for the caller.
        /// </summary>
        /// <returns>New copy of the SIPRequest.</returns>
        public SIPRequest Copy()
        {
            SIPRequest copy = new SIPRequest();
            copy.SIPVersion = SIPVersion;
            copy.Method = Method;
            copy.UnknownMethod = UnknownMethod;
            copy.URI = URI?.CopyOf();
            copy.Header = Header?.Copy();
            copy.SIPEncoding = SIPEncoding;
            copy.SIPBodyEncoding = SIPBodyEncoding;

            if (_body != null && _body.Length > 0)
            {
                copy._body = new byte[_body.Length];
                Buffer.BlockCopy(_body, 0, copy._body, 0, _body.Length);
            }

            if (ReceivedRoute != null)
            {
                copy.ReceivedRoute = new SIPRoute(ReceivedRoute.URI, !ReceivedRoute.IsStrictRouter);
            }

            copy.Created = Created;
            copy.LocalSIPEndPoint = LocalSIPEndPoint?.CopyOf();
            copy.RemoteSIPEndPoint = RemoteSIPEndPoint?.CopyOf();
            copy.SendFromHintChannelID = SendFromHintChannelID;
            copy.SendFromHintConnectionID = SendFromHintConnectionID;

            return copy;
        }

        public string CreateBranchId()
        {
            string routeStr = (Header.Routes != null) ? Header.Routes.ToString() : null;
            string toTagStr = (Header.To != null) ? Header.To.ToTag : null;
            string fromTagStr = (Header.From != null) ? Header.From.FromTag : null;
            string topViaStr = (Header.Vias != null && Header.Vias.TopViaHeader != null) ? Header.Vias.TopViaHeader.ToString() : null;

            return CallProperties.CreateBranchId(
                SIPConstants.SIP_BRANCH_MAGICCOOKIE,
                toTagStr,
                fromTagStr,
                Header.CallId,
                URI.ToString(),
                topViaStr,
                Header.CSeq,
                routeStr,
                Header.ProxyRequire,
                null);
        }

        /// <summary>
        /// Determines if this SIP header is a looped header. The basis for the decision is the branchid in the Via header. If the branchid for a new
        /// header computes to the same branchid as a Via header already in the SIP header then it is considered a loop.
        /// </summary>
        /// <returns>True if this header is a loop otherwise false.</returns>
        public bool IsLoop(string ipAddress, int port, string currentBranchId)
        {
            foreach (SIPViaHeader viaHeader in Header.Vias.Via)
            {
                if (viaHeader.Host == ipAddress && viaHeader.Port == port)
                {
                    if (viaHeader.Branch == currentBranchId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public bool IsValid(out SIPValidationFieldsEnum errorField, out string errorMessage)
        {
            errorField = SIPValidationFieldsEnum.Unknown;
            errorMessage = null;

            if (Header.Vias.Length == 0)
            {
                errorField = SIPValidationFieldsEnum.ViaHeader;
                errorMessage = "No Via headers";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Sets the send from hints for this request based on a local SIP end point.
        /// The local SIP end point should generally be the one a related request or response was
        /// received on.
        /// </summary>
        /// <param name="localEndPoint">The SIP end point to base the send from hints on.</param>
        public void SetSendFromHints(SIPEndPoint localEndPoint)
        {
            SendFromHintChannelID = localEndPoint?.ChannelID;
            SendFromHintConnectionID = localEndPoint?.ConnectionID;
        }

        /// <summary>
        /// Builds a very basic SIP request. In most cases additional headers will need to be added in order for it to be useful.
        /// When this method is called the channel used for sending the request has not been decided. The headers below depend on 
        /// the sending channel. By setting them to "0.0.0.0:0" the send request methods will substitute in the appropriate value
        /// at send time:
        /// - Top Via header.
        /// - From header.
        /// - Contact header.
        /// </summary>
        /// <param name="method">The method for the SIP request.</param>
        /// <param name="uri">The destination URI for the request.</param>
        /// <returns>A SIP request object.</returns>
        public static SIPRequest GetRequest(SIPMethodsEnum method, SIPURI uri)
        {
            return GetRequest(
                method,
                uri,
                new SIPToHeader(null, new SIPURI(uri.User, uri.Host, null, uri.Scheme, SIPProtocolsEnum.udp), null),
                SIPFromHeader.GetDefaultSIPFromHeader(uri.Scheme));
        }

        /// <summary>
        /// Builds a very basic SIP request. In most cases additional headers will need to be added in order for it to be useful.
        /// When this method is called the channel used for sending the request has not been decided. The headers below depend on 
        /// the sending channel. By setting them to "0.0.0.0:0" the send request methods will substitute in the appropriate value
        /// at send time:
        /// - Top Via header.
        /// - From header.
        /// - Contact header.
        /// </summary>
        /// <param name="method">The method for the SIP request.</param>
        /// <param name="uri">The destination URI for the request.</param>
        /// <param name="to">The To header for the request.</param>
        /// <param name="from">The From header for the request.</param>
        /// <returns>A SIP request object.</returns>
        public static SIPRequest GetRequest(SIPMethodsEnum method, SIPURI uri, SIPToHeader to, SIPFromHeader from)
        {
            SIPRequest request = new SIPRequest(method, uri);

            SIPHeader header = new SIPHeader(from, to, 1, CallProperties.CreateNewCallId());
            request.Header = header;
            header.CSeqMethod = method;
            header.Allow = m_allowedSIPMethods;
            header.Vias.PushViaHeader(SIPViaHeader.GetDefaultSIPViaHeader(uri.Protocol));

            return request;
        }

        public byte[] GetBytes()
        {
            return base.GetBytes(StatusLine + m_CRLF);
        }

        /// <summary>
        /// Duplicates an existing SIP request, typically one that received an unauthorised response, to an
        /// authenticated version. The CSeq and Via branch ID are also incremented so
        /// that the request will not be flagged as a retransmit.
        /// </summary>
        /// <param name="authenticationChallenges">The challenges to authenticate the request against. Typically 
        /// the challenges come from a SIP response.</param>
        /// <param name="username">The username to authenticate with.</param>
        /// <param name="password">The password to authenticate with.</param>
        /// <returns>A SIP request that is a duplicate of the original but with an authentication header added and
        /// the state header values updated so as not to be flagged as a retransmit.</returns>
        public SIPRequest DuplicateAndAuthenticate(List<SIPAuthenticationHeader> authenticationChallenges,
            string username,
            string password)
        {
            var dupRequest = this.Copy();
            dupRequest.Header.Vias.TopViaHeader.Branch = CallProperties.CreateBranchId();
            dupRequest.Header.CSeq = dupRequest.Header.CSeq + 1;

            dupRequest.Header.AuthenticationHeaders.Clear();

            // RFC8760 (which introduces SHA256/512 for SIP) states that multiple authentication headers with different digest algorithms
            // can be included in a SIP request. When testing this with the latest versions (Jul 2021) of Asterisk v18.5.0 and FreeSWITCH v1.10.6
            // request authentication failed if the MD5 digest was not first and it's almost certain the subsequent SHA256 digest was ignored.
            // As a consequence the logic below will only use a SHA256 digest IFF the UAS put an authentication challenge with the digest 
            // algorithm explicitly set to SHA-256.
            // See https://github.com/sipsorcery-org/sipsorcery/issues/525.

            bool useSHA256 = authenticationChallenges.Any(x => x.SIPDigest.DigestAlgorithm == DigestAlgorithmsEnum.SHA256);
            if (useSHA256)
            {
                var sha256AuthHeader = SIPAuthChallenge.GetAuthenticationHeader(authenticationChallenges, this.URI, this.Method, username, password, DigestAlgorithmsEnum.SHA256);
                dupRequest.Header.AuthenticationHeaders.Add(sha256AuthHeader);
            }
            else
            {
                var md5AuthHeader = SIPAuthChallenge.GetAuthenticationHeader(authenticationChallenges, this.URI, this.Method, username, password);
                dupRequest.Header.AuthenticationHeaders.Add(md5AuthHeader);
            }
            
            return dupRequest;
        }
    }
}
