//-----------------------------------------------------------------------------
// Filename: SIPResponse.cs
//
// Description: SIP Response.
//
// Author(s):
// Aaron Clauson
// 
// History:
// 17 Sep 2005	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using SIPSorcery.Sys;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    public enum SIPResponseParserError
    {
        None = 0,
        TooLarge = 1,
    }
    
    /// <bnf>
	/// SIP-Version SP Status-Code SP Reason-Phrase CRLF
	/// *message-header
	///	CRLF
	///	[ message-body ]
	///	</bnf>
    /// <summary>
	///  Status Codes:	
	///  1xx: Provisional -- request received, continuing to process the request;
	///  2xx: Success -- the action was successfully received, understood, and accepted;
    ///  3xx: Redirection -- further action needs to be taken in order to complete the request;
    ///  4xx: Client Error -- the request contains bad syntax or cannot be fulfilled at this server;
    ///  5xx: Server Error -- the server failed to fulfill an apparently valid request;
    ///  6xx: Global Failure -- the request cannot be fulfilled at any server.
	/// </summary>
	public class SIPResponse
	{
        private static ILogger logger = Log.Logger;
		
		private static string m_CRLF = SIPConstants.CRLF;
		private static string m_sipVersion = SIPConstants.SIP_FULLVERSION_STRING;
	
		public string SIPVersion;
		public SIPResponseStatusCodesEnum Status;
		public int StatusCode;
		public string ReasonPhrase;
		public string Body;
        public SIPHeader Header = new SIPHeader();

        public DateTime Created = DateTime.Now;
        public SIPEndPoint RemoteSIPEndPoint;               // The remote IP socket the response was received from or sent to.
        public SIPEndPoint LocalSIPEndPoint;                // The local SIP socket the response was received on or sent from.
        
        public string ShortDescription
        {
            get { return Header?.CSeqMethod + " " + StatusCode + " " + ReasonPhrase; }
        }

		private SIPResponse()
		{}

        public SIPResponse(SIPResponseStatusCodesEnum responseType, string reasonPhrase, SIPEndPoint localSIPEndPoint)
		{
			SIPVersion = m_sipVersion;
			StatusCode = (int)responseType;
            Status = responseType;
            ReasonPhrase = reasonPhrase;
			ReasonPhrase = responseType.ToString();
            LocalSIPEndPoint = localSIPEndPoint;
		}

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

		public new string ToString()
		{
			string reasonPhrase = (!ReasonPhrase.IsNullOrBlank()) ? " " + ReasonPhrase : null;

			string message = 
				SIPVersion + " " + StatusCode + reasonPhrase + m_CRLF +
				this.Header.ToString();

			if(Body != null)
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
            return ParseSIPResponse(this.ToString());
        }
	}
}
