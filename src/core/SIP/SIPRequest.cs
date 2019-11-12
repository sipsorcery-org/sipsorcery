//-----------------------------------------------------------------------------
// Filename: SIPRequest.cs
//
// Description: SIP Request.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 20 Oct 2005	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using SIPSorcery.Sys;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    /// <bnf>
	///  Method SP Request-URI SP SIP-Version CRLF
	///  *message-header
	///	 CRLF
	///	 [ message-body ]
	///	 
	///	 Methods: REGISTER, INVITE, ACK, CANCEL, BYE, OPTIONS
	///	 SIP-Version: SIP/2.0
	///	 
	///	 SIP-Version    =  "SIP" "/" 1*DIGIT "." 1*DIGIT
	/// </bnf>
	public class SIPRequest
	{
        private static ILogger logger = Log.Logger;

        private delegate bool IsLocalSIPSocketDelegate(string socket, SIPProtocolsEnum protocol);

		private static string m_CRLF = SIPConstants.CRLF;
		private static string m_sipFullVersion = SIPConstants.SIP_FULLVERSION_STRING;
		private static string m_sipVersion = SIPConstants.SIP_VERSION_STRING;
		private static int m_sipMajorVersion = SIPConstants.SIP_MAJOR_VERSION;
		private static int m_sipMinorVersion = SIPConstants.SIP_MINOR_VERSION;

		public string SIPVersion = m_sipVersion;
		public int SIPMajorVersion = m_sipMajorVersion;
		public int SIPMinorVersion = m_sipMinorVersion;
		public SIPMethodsEnum Method;
		public string UnknownMethod = null;

		public SIPURI URI;
		public SIPHeader Header;
		public string Body;
        public SIPRoute ReceivedRoute;

		public DateTime Created = DateTime.Now;
		public SIPEndPoint RemoteSIPEndPoint;               // The remote IP socket the request was received from or sent to.
        public SIPEndPoint LocalSIPEndPoint;                // The local SIP socket the request was received on or sent from.

        public string StatusLine
        {
            get 
            {
                string methodStr = (Method != SIPMethodsEnum.UNKNOWN) ? Method.ToString() : UnknownMethod;
                return methodStr + " " + URI.ToString() + " " + SIPVersion;  
            }
        }

		private SIPRequest()
		{
            //Created++;
        }
			
		public SIPRequest(SIPMethodsEnum method, string uri)
		{
            try
            {
                Method = method;
                URI = SIPURI.ParseSIPURI(uri);
                SIPVersion = m_sipFullVersion;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPRequest ctor. " + excp.Message);
                throw;
            }
		}

        public SIPRequest(SIPMethodsEnum method, SIPURI uri)
        {
             //Created++;
             Method = method;
             URI = uri;
             SIPVersion = m_sipFullVersion;
        }

		public static SIPRequest ParseSIPRequest(SIPMessage sipMessage)
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
                    sipRequest.Body = sipMessage.Body;

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
                SIPMessage sipMessage = SIPMessage.ParseSIPMessage(sipMessageStr, null, null);
                return SIPRequest.ParseSIPRequest(sipMessage);
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

		public new string ToString()
		{
			try
			{
				string message = StatusLine + m_CRLF + this.Header.ToString();

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
			catch(Exception excp)
			{
				logger.LogError("Exception SIPRequest ToString. " + excp.Message);
				throw excp;
			}
		}

        /// <summary>
        /// Creates an identical copy of the SIP Request for the caller.
        /// </summary>
        /// <returns>New copy of the SIPRequest.</returns>
        public SIPRequest Copy()
        {
            return ParseSIPRequest(this.ToString());
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
			foreach(SIPViaHeader viaHeader in Header.Vias.Via)
			{
				if(viaHeader.Host == ipAddress && viaHeader.Port == port)
				{
					if(viaHeader.Branch == currentBranchId)
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
	}
}
