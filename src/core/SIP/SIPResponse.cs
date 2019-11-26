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
    public class SIPResponse
    {
        private static ILogger logger = Log.Logger;

        private static string m_CRLF = SIPConstants.CRLF;
        private static string m_sipVersion = SIPConstants.SIP_FULLVERSION_STRING;

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
        /// The optional body or payload for the SIP response.
        /// </summary>
        public string Body;

        /// <summary>
        /// The SIP response's headers collection.
        /// </summary>
        public SIPHeader Header = new SIPHeader();

        /// <summary>
        /// Timestamp for the SIP response's creation.
        /// </summary>
        public DateTime Created = DateTime.Now;

        /// <summary>
        /// The remote SIP socket the response was received from.
        /// </summary>
        public SIPEndPoint RemoteSIPEndPoint { get; private set; }

        /// <summary>
        /// The local SIP socket the response was received on.
        /// </summary>
        public SIPEndPoint LocalSIPEndPoint { get; private set; }

        /// <summary>
        /// When the SIP transport layer has mutliple channels it will use this ID hint to choose amongst them.
        /// </summary>
        public string SendFromHintChannelID;

        /// <summary>
        /// For connection oriented SIP transport channels this ID provides a hint about the specific connection to use.
        /// </summary>
        public string SendFromHintConnectionID;

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
            SIPVersion = m_sipVersion;
            StatusCode = (int)responseStatus;
            Status = responseStatus;
            ReasonPhrase = reasonPhrase;
            ReasonPhrase = responseStatus.ToString();
        }

        /// <summary>
        /// SIPResponse Constructor.
        /// </summary>
        /// <param name="responseStatus">The status code for the response.</param>
        /// <param name="reasonPhrase">Optional description for the response. Should be kept short.</param>
        /// <param name="localSIPEndPoint">The local SIP end point the response was received on or should be sent from.</param>
        /// <param name="remoteSIPEndPoint">Optional remote SIP end point the response came from or should be sent to.
        /// If set as null the SIP transport layer will attempt to resolve the remote end point using the response's headers.</param>
        [Obsolete("The local and remote SIP end points are now only for recording receiving sockets. Use the new send from hint proprties.", true)]
        public SIPResponse(
            SIPResponseStatusCodesEnum responseStatus,
            string reasonPhrase,
            SIPEndPoint localSIPEndPoint,
            SIPEndPoint remoteSIPEndPoint)
        {
            SIPVersion = m_sipVersion;
            StatusCode = (int)responseStatus;
            Status = responseStatus;
            ReasonPhrase = reasonPhrase;
            ReasonPhrase = responseStatus.ToString();
            LocalSIPEndPoint = localSIPEndPoint;
            RemoteSIPEndPoint = remoteSIPEndPoint;
        }

        /// <summary>
        /// Parses a SIP response from a SIP message object.
        /// </summary>
        /// <param name="sipMessage">The SIP message to parse a response from.</param>
        /// <returns>A new SIP response object.</returns>
        public static SIPResponse ParseSIPResponse(SIPMessage sipMessage)
        {
            try
            {
                SIPResponse sipResponse = new SIPResponse();
                sipResponse.LocalSIPEndPoint = sipMessage.LocalSIPEndPoint;
                sipResponse.RemoteSIPEndPoint = sipMessage.RemoteSIPEndPoint;
                string statusLine = sipMessage.FirstLine;

                int firstSpacePosn = statusLine.IndexOf(" ");

                sipResponse.SIPVersion = statusLine.Substring(0, firstSpacePosn).Trim();
                statusLine = statusLine.Substring(firstSpacePosn).Trim();
                sipResponse.StatusCode = Convert.ToInt32(statusLine.Substring(0, 3));
                sipResponse.Status = SIPResponseStatusCodes.GetStatusTypeForCode(sipResponse.StatusCode);
                sipResponse.ReasonPhrase = statusLine.Substring(3).Trim();

                sipResponse.Header = SIPHeader.ParseSIPHeaders(sipMessage.SIPHeaders);
                sipResponse.Body = sipMessage.Body;

                return sipResponse;
            }
            catch (SIPValidationException)
            {
                throw;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception ParseSIPResponse. " + excp.Message);
                logger.LogError(sipMessage.RawMessage);
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
                SIPMessage sipMessage = SIPMessage.ParseSIPMessage(sipMessageStr, null, null);
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
    }
}
