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
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    /// <summary>
    /// Represents a SIP Request.
    /// </summary>
    public class SIPRequest : SIPMessageBase
    {
        private delegate bool IsLocalSIPSocketDelegate(string socket, SIPProtocolsEnum protocol);

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

        private SIPRequest()
        { }

        public SIPRequest(SIPMethodsEnum method, string uri)
        {
            Method = method;
            URI = SIPURI.ParseSIPURI(uri);
            SIPVersion = m_sipFullVersion;
        }

        public SIPRequest(SIPMethodsEnum method, SIPURI uri)
        {
            Method = method;
            URI = uri;
            SIPVersion = m_sipFullVersion;
        }

        public static SIPRequest ParseSIPRequest(SIPMessageBuffer sipMessage)
        {
            try
            {
                SIPRequest sipRequest = new SIPRequest();
                sipRequest.LocalSIPEndPoint = sipMessage.LocalSIPEndPoint;
                sipRequest.RemoteSIPEndPoint = sipMessage.RemoteSIPEndPoint;

                string statusLine = sipMessage.FirstLine;

                int firstSpacePosn = statusLine.IndexOf(" ");

                string method = statusLine.Substring(0, firstSpacePosn).Trim();
                sipRequest.Method = SIPMethods.GetMethod(method);
                if (sipRequest.Method == SIPMethodsEnum.UNKNOWN)
                {
                    sipRequest.UnknownMethod = method;
                    logger.LogWarning("Unknown SIP method received " + sipRequest.UnknownMethod + ".");
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
                logger.LogError("Exception parsing SIP Request. " + excp.Message);
                logger.LogError(sipMessage.RawMessage);
                throw new SIPValidationException(SIPValidationFieldsEnum.Request, "Unknown error parsing SIP Request");
            }
        }

        public static SIPRequest ParseSIPRequest(string sipMessageStr)
        {
            try
            {
                SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(sipMessageStr, null, null);
                return SIPRequest.ParseSIPRequest(sipMessageBuffer);
            }
            catch (SIPValidationException)
            {
                throw;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception ParseSIPRequest. " + excp.Message);
                logger.LogError(sipMessageStr);
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
                logger.LogError("Exception SIPRequest ToString. " + excp.Message);
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
            //copy.SIPMajorVersion = m_sipMajorVersion;
            //copy.SIPMinorVersion = m_sipMinorVersion;
            copy.Method = Method;
            copy.UnknownMethod = UnknownMethod;
            copy.URI = URI?.CopyOf();
            copy.Header = Header?.Copy();
            
            if(_body != null && _body.Length > 0)
            {
                copy._body = new byte[_body.Length];
                Buffer.BlockCopy(copy._body, 0, copy._body, 0, copy._body.Length);
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
            header.Vias.PushViaHeader(SIPViaHeader.GetDefaultSIPViaHeader());

            return request;
        }

        public byte[] GetBytes()
        {
            return base.GetBytes(StatusLine + m_CRLF);
        }
    }
}
