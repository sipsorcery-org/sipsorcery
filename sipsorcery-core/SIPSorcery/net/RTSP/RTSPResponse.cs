//-----------------------------------------------------------------------------
// Filename: RTSPResponse.cs
//
// Description: RTSP response.
//
// History:
// 09 Nov 2007	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2007 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Collections.Generic;
using System.Text;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Net
{
    public enum RTSPResponseParserError
    {
        None = 0,
    }
    
    /// <summary>
    /// RFC2326 7.1:
    /// Status-Line =   RTSP-Version SP Status-Code SP Reason-Phrase CRLF

    /// </summary>
    public class RTSPResponse
    {
        private static ILog logger = AssemblyStreamState.logger;
		
		private static string m_CRLF = RTSPConstants.CRLF;
		//private static string m_rtspFullVersion = RTSPConstants.RTSP_FULLVERSION_STRING;
		private static string m_rtspVersion = RTSPConstants.RTSP_VERSION_STRING;
		private static int m_rtspMajorVersion = RTSPConstants.RTSP_MAJOR_VERSION;
		private static int m_rtspMinorVersion = RTSPConstants.RTSP_MINOR_VERSION;

		public bool Valid = true;
		public RTSPHeaderError ValidationError = RTSPHeaderError.None;

		public string RTSPVersion = m_rtspVersion;
		public int RTSPMajorVersion = m_rtspMajorVersion;
		public int RTSPMinorVersion = m_rtspMinorVersion;
        public RTSPResponseStatusCodesEnum Status;
        public int StatusCode;
        public string ReasonPhrase;
        public string Body;
		public RTSPHeader Header;

		public DateTime ReceivedAt = DateTime.MinValue;
		public IPEndPoint ReceivedFrom;

        private RTSPResponse()
        { }

        public RTSPResponse(RTSPResponseStatusCodesEnum responseType, string reasonPhrase)
		{
            StatusCode = (int)responseType;
            Status = responseType;
            ReasonPhrase = reasonPhrase;
            ReasonPhrase = responseType.ToString();
		}

        public static RTSPResponse ParseRTSPResponse(RTSPMessage rtspMessage)
        {
            RTSPResponseParserError dontCare = RTSPResponseParserError.None;
            return ParseRTSPResponse(rtspMessage, out dontCare);
        }

		public static RTSPResponse ParseRTSPResponse(RTSPMessage rtspMessage, out RTSPResponseParserError responseParserError)
		{
            responseParserError = RTSPResponseParserError.None;
			
			try
			{
                RTSPResponse rtspResponse = new RTSPResponse();

                string statusLine = rtspMessage.FirstLine;

                int firstSpacePosn = statusLine.IndexOf(" ");

                rtspResponse.RTSPVersion = statusLine.Substring(0, firstSpacePosn).Trim();
                statusLine = statusLine.Substring(firstSpacePosn).Trim();
                rtspResponse.StatusCode = Convert.ToInt32(statusLine.Substring(0, 3));
                rtspResponse.Status = RTSPResponseStatusCodes.GetStatusTypeForCode(rtspResponse.StatusCode);
                rtspResponse.ReasonPhrase = statusLine.Substring(3).Trim();

                rtspResponse.Header = RTSPHeader.ParseRTSPHeaders(rtspMessage.RTSPHeaders);
                rtspResponse.Body = rtspMessage.Body;

                //rtspResponse.Valid = rtspResponse.Validate(out sipResponse.ValidationError);

                return rtspResponse;
    		}
			catch(Exception excp)
			{
				logger.Error("Exception parsing RTSP reqsponse. "  + excp.Message);
				throw new ApplicationException("There was an exception parsing an RTSP response. " + excp.Message);
			}
		}

		public new string ToString()
		{
			try
			{
                string reasonPhrase = (!ReasonPhrase.IsNullOrBlank()) ? " " + ReasonPhrase : null;

                string message =
                    RTSPVersion + "/" + RTSPMajorVersion + "." + RTSPMinorVersion + " " + StatusCode + reasonPhrase + m_CRLF +
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
			catch(Exception excp)
			{
				logger.Error("Exception RTSPResponse ToString. " + excp.Message);
				throw excp;
			}
		}
    }
}
