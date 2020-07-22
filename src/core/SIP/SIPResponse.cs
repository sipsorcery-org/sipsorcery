//-----------------------------------------------------------------------------
// Filename: SIPResponse.cs
//
// Description: SIP Response.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 17 Sep 2005	Aaron Clauson	Created, Dublin, Ireland.
// 26 Nov 2019  Aaron Clauson   Added SIPMessageBase inheritance.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    public enum SIPResponseParserError
    {
        None = 0,
        TooLarge = 1,
    }

    /// <summary>
    /// Represents a SIP Response.
    /// </summary>
    public class SIPResponse : SIPMessageBase
    {
        /// <summary>
        /// The version string for the SIP response.
        /// </summary>
        public string SIPVersion;

        /// <summary>
        /// The status of the SIP response, e.g OK or NotAuthorized.
        /// </summary>
        public SIPResponseStatusCodesEnum Status;

        /// <summary>
        /// The status code of the SIP response, e.g. 200 for an OK response.
        /// </summary>
        public int StatusCode;

        /// <summary>
        /// The optional reason phrase for the SIP response.
        /// </summary>
        public string ReasonPhrase;

        /// <summary>
        /// A short one line summary of the SIP response. Useful for logging or diagnostics.
        /// </summary>
        public string ShortDescription
        {
            get { return Header?.CSeqMethod + " " + StatusCode + " " + ReasonPhrase; }
        }

        private SIPResponse()
        { }

        /// <summary>
        /// SIPResponse Constructor.
        /// </summary>
        /// <param name="responseStatus">The status code for the response.</param>
        /// <param name="reasonPhrase">Optional description for the response. Should be kept short.</param>
        public SIPResponse(
            SIPResponseStatusCodesEnum responseStatus,
            string reasonPhrase)
        {
            SIPVersion = m_sipFullVersion;
            StatusCode = (int)responseStatus;
            Status = responseStatus;
            ReasonPhrase = reasonPhrase;
            ReasonPhrase = responseStatus.ToString();
        }

        /// <summary>
        /// Parses a SIP response from a SIP message object.
        /// </summary>
        /// <param name="sipMessageBuffer">The SIP message to parse a response from.</param>
        /// <returns>A new SIP response object.</returns>
        public static SIPResponse ParseSIPResponse(SIPMessageBuffer sipMessageBuffer)
        {
            try
            {
                SIPResponse sipResponse = new SIPResponse();
                sipResponse.LocalSIPEndPoint = sipMessageBuffer.LocalSIPEndPoint;
                sipResponse.RemoteSIPEndPoint = sipMessageBuffer.RemoteSIPEndPoint;
                string statusLine = sipMessageBuffer.FirstLine;

                int firstSpacePosn = statusLine.IndexOf(" ");

                sipResponse.SIPVersion = statusLine.Substring(0, firstSpacePosn).Trim();
                statusLine = statusLine.Substring(firstSpacePosn).Trim();
                sipResponse.StatusCode = Convert.ToInt32(statusLine.Substring(0, 3));
                sipResponse.Status = SIPResponseStatusCodes.GetStatusTypeForCode(sipResponse.StatusCode);
                sipResponse.ReasonPhrase = statusLine.Substring(3).Trim();

                sipResponse.Header = SIPHeader.ParseSIPHeaders(sipMessageBuffer.SIPHeaders);
                sipResponse.Body = sipMessageBuffer.Body;

                return sipResponse;
            }
            catch (SIPValidationException)
            {
                throw;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception ParseSIPResponse. " + excp.Message);
                logger.LogError(sipMessageBuffer.RawMessage);
                throw new SIPValidationException(SIPValidationFieldsEnum.Response, "Error parsing SIP Response");
            }
        }

        /// <summary>
        /// Parses a SIP response from a string.
        /// </summary>
        /// <param name="sipMessageStr">The string to parse the SIP response from.</param>
        /// <returns>A new SIP response object.</returns>
        public static SIPResponse ParseSIPResponse(string sipMessageStr)
        {
            try
            {
                SIPMessageBuffer sipMessage = SIPMessageBuffer.ParseSIPMessage(sipMessageStr, null, null);
                return SIPResponse.ParseSIPResponse(sipMessage);
            }
            catch (SIPValidationException)
            {
                throw;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception ParseSIPResponse. " + excp.Message);
                logger.LogError(sipMessageStr);
                throw new SIPValidationException(SIPValidationFieldsEnum.Response, "Error parsing SIP Response");
            }
        }

        /// <summary>
        /// Returns a string representing the full SIP response.
        /// </summary>
        /// <returns>A string representation of the SIP response.</returns>
        public override string ToString()
        {
            string reasonPhrase = (!ReasonPhrase.IsNullOrBlank()) ? " " + ReasonPhrase : null;

            string message =
                SIPVersion + " " + StatusCode + reasonPhrase + m_CRLF +
                this.Header.ToString();

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

        /// <summary>
        /// Creates an identical copy of the SIP Response for the caller.
        /// </summary>
        /// <returns>New copy of the SIPResponse.</returns>
        public SIPResponse Copy()
        {
            SIPResponse copy = new SIPResponse();
            copy.SIPVersion = SIPVersion;
            copy.Status = Status;
            copy.StatusCode = StatusCode;
            copy.ReasonPhrase = ReasonPhrase;
            copy.Header = Header?.Copy();
            copy.Body = Body;
            copy.Created = Created;
            copy.LocalSIPEndPoint = LocalSIPEndPoint?.CopyOf();
            copy.RemoteSIPEndPoint = RemoteSIPEndPoint?.CopyOf();
            copy.SendFromHintChannelID = SendFromHintChannelID;
            copy.SendFromHintConnectionID = SendFromHintConnectionID;

            return copy;
        }

        /// <summary>
        /// Sets the send from hints for this response based on a local SIP end point.
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
        /// Helper method to create a SIP response for a SIP request. This method can be thought of as creating a 
        /// vanilla (or no frills) response for a request. It's suitable for generating error responses. For requests that
        /// require an action such as creating a call or registering a contact the response will require additional 
        /// information and this method will not be suitable.
        /// </summary>
        /// <param name="sipRequest">The SIP request to create the response for.</param>
        /// <param name="responseCode">The response code.</param>
        /// <param name="reasonPhrase">Optional reason phrase to set on the response (needs to be short).</param>
        /// <returns>A SIP response object.</returns>
        public static SIPResponse GetResponse(SIPRequest sipRequest, SIPResponseStatusCodesEnum responseCode, string reasonPhrase)
        {
            try
            {
                SIPResponse response = new SIPResponse(responseCode, reasonPhrase);
                response.SetSendFromHints(sipRequest.LocalSIPEndPoint);

                if (reasonPhrase != null)
                {
                    response.ReasonPhrase = reasonPhrase;
                }

                SIPHeader requestHeader = sipRequest.Header;
                SIPFromHeader from = (requestHeader == null || requestHeader.From != null) ? requestHeader.From : new SIPFromHeader(null, new SIPURI(sipRequest.URI.Scheme, sipRequest.LocalSIPEndPoint), null);
                SIPToHeader to = (requestHeader == null || requestHeader.To != null) ? requestHeader.To : new SIPToHeader(null, new SIPURI(sipRequest.URI.Scheme, sipRequest.LocalSIPEndPoint), null);
                int cSeq = (requestHeader == null || requestHeader.CSeq != -1) ? requestHeader.CSeq : 1;
                string callId = (requestHeader == null || requestHeader.CallId != null) ? requestHeader.CallId : CallProperties.CreateNewCallId();

                response.Header = new SIPHeader(from, to, cSeq, callId);
                response.Header.CSeqMethod = (requestHeader != null) ? requestHeader.CSeqMethod : SIPMethodsEnum.NONE;

                if (requestHeader == null || requestHeader.Vias == null || requestHeader.Vias.Length == 0)
                {
                    response.Header.Vias.PushViaHeader(new SIPViaHeader(sipRequest.RemoteSIPEndPoint, CallProperties.CreateBranchId()));
                }
                else
                {
                    response.Header.Vias = requestHeader.Vias;
                }

                response.Header.MaxForwards = Int32.MinValue;
                response.Header.Allow = m_allowedSIPMethods;

                return response;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPResponse.GetResponse. " + excp.Message);
                throw excp;
            }
        }

        /// <summary>
        /// Used to create a SIP response for a request when it was not possible to parse the incoming SIP request. 
        /// The response generated by this method may or may not make it back to the requester. Because the SIP 
        /// request could not be parsed there are no Via headers available and without those the return network
        /// path is missing. Instead a new Via header is generated that may get through if the requester is only
        /// one SIP hop away.
        /// </summary>
        /// <param name="localSIPEndPoint">The local SIP end point the request was received on.</param>
        /// <param name="remoteSIPEndPoint">The remote SIP end point the request was received on.</param>
        /// <param name="responseCode">The response code to set on the response.</param>
        /// <param name="reasonPhrase">Optional reason phrase to set on the response (keep short).</param>
        public static SIPResponse GetResponse(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteSIPEndPoint, SIPResponseStatusCodesEnum responseCode, string reasonPhrase)
        {
            try
            {
                SIPResponse response = new SIPResponse(responseCode, reasonPhrase);
                response.SetSendFromHints(localSIPEndPoint);
                SIPSchemesEnum sipScheme = (localSIPEndPoint.Protocol == SIPProtocolsEnum.tls) ? SIPSchemesEnum.sips : SIPSchemesEnum.sip;
                SIPFromHeader from = new SIPFromHeader(null, new SIPURI(sipScheme, localSIPEndPoint), null);
                SIPToHeader to = new SIPToHeader(null, new SIPURI(sipScheme, localSIPEndPoint), null);
                int cSeq = 1;
                string callId = CallProperties.CreateNewCallId();
                response.Header = new SIPHeader(from, to, cSeq, callId);
                response.Header.CSeqMethod = SIPMethodsEnum.NONE;
                response.Header.Vias.PushViaHeader(new SIPViaHeader(new SIPEndPoint(localSIPEndPoint.Protocol, remoteSIPEndPoint.GetIPEndPoint()), CallProperties.CreateBranchId()));
                response.Header.MaxForwards = Int32.MinValue;
                response.Header.Allow = m_allowedSIPMethods;

                return response;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPResponse.GetResponse. " + excp.Message);
                throw;
            }
        }
    }
}
