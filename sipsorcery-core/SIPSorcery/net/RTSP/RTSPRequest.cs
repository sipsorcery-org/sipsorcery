//-----------------------------------------------------------------------------
// Filename: RTSPRequest.cs
//
// Description: RTSP request.
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
using log4net;

namespace SIPSorcery.Net
{
    public enum RTSPRequestParserError
    {
        None = 0,
    }
    
    /// <summary>
    /// RFC2326 6.1:
    /// Request-Line = Method SP Request-URI SP RTSP-Version CRLF
    /// </summary>
    public class RTSPRequest
    {
        private static ILog logger = AssemblyStreamState.logger;
		
		private static string m_CRLF = RTSPConstants.CRLF;
		private static string m_rtspFullVersion = RTSPConstants.RTSP_FULLVERSION_STRING;
		private static string m_rtspVersion = RTSPConstants.RTSP_VERSION_STRING;
		private static int m_rtspMajorVersion = RTSPConstants.RTSP_MAJOR_VERSION;
		private static int m_rtspMinorVersion = RTSPConstants.RTSP_MINOR_VERSION;

		public bool Valid = true;
		public RTSPHeaderError ValidationError = RTSPHeaderError.None;

		public string RTSPVersion = m_rtspVersion;
		public int RTSPMajorVersion = m_rtspMajorVersion;
		public int RTSPMinorVersion = m_rtspMinorVersion;
		public RTSPMethodsEnum Method;
		public string UnknownMethod = null;

		public RTSPURL URL;
		public RTSPHeader Header;
		public string Body;

		public DateTime ReceivedAt = DateTime.MinValue;
		public IPEndPoint ReceivedFrom;

		private RTSPRequest()
		{}
			
		public RTSPRequest(RTSPMethodsEnum method, string url)
		{
			RTSPHeaderParserError urlParserError = RTSPHeaderParserError.None;
			
			try
			{
				Method = method;
				
				URL = RTSPURL.ParseRTSPURL(url, out urlParserError);
                if (urlParserError != RTSPHeaderParserError.None)
                {
                    throw new ApplicationException("Error parsing RTSP URL, " + urlParserError.ToString() + ".");
                }

				RTSPVersion = m_rtspFullVersion;
			}
			catch(Exception excp)
			{
				logger.Error("Exception RTSPRequest Ctor. " + excp.Message + ".");
			}
		}

        public RTSPRequest(RTSPMethodsEnum method, RTSPURL url)
        {
             Method = method;
             URL = url;
             RTSPVersion = m_rtspFullVersion;
        }

        public static RTSPRequest ParseRTSPRequest(RTSPMessage rtspMessage)
        {
            RTSPRequestParserError dontCare = RTSPRequestParserError.None;
            return ParseRTSPRequest(rtspMessage, out dontCare);
        }

		public static RTSPRequest ParseRTSPRequest(RTSPMessage rtspMessage, out RTSPRequestParserError requestParserError)
		{
            requestParserError = RTSPRequestParserError.None;
            string urlStr = null;
			
			try
			{
				RTSPRequest rtspRequest = new RTSPRequest();

                string statusLine = rtspMessage.FirstLine;

                int firstSpacePosn = statusLine.IndexOf(" ");

                string method = statusLine.Substring(0, firstSpacePosn).Trim();
                rtspRequest.Method = RTSPMethods.GetMethod(method);
                if (rtspRequest.Method == RTSPMethodsEnum.UNKNOWN)
                {
                    rtspRequest.UnknownMethod = method;
                    logger.Warn("Unknown RTSP method received " + rtspRequest.Method + ".");
                }

                statusLine = statusLine.Substring(firstSpacePosn).Trim();
                int secondSpacePosn = statusLine.IndexOf(" ");

                if (secondSpacePosn != -1)
                {
                    urlStr = statusLine.Substring(0, secondSpacePosn);

                    rtspRequest.URL = RTSPURL.ParseRTSPURL(urlStr);
                    rtspRequest.RTSPVersion = statusLine.Substring(secondSpacePosn, statusLine.Length - secondSpacePosn).Trim();
                    rtspRequest.Header = (rtspMessage.RTSPHeaders != null) ? RTSPHeader.ParseRTSPHeaders(rtspMessage.RTSPHeaders) : new RTSPHeader(0, null);
                    rtspRequest.Body = rtspMessage.Body;

                    return rtspRequest;
                }
                else
                {
                    throw new ApplicationException("URI was missing on RTSP request.");
                }
    		}
			catch(Exception excp)
			{
				logger.Error("Exception parsing RTSP request. URI, " + urlStr + ".");
				throw new ApplicationException("There was an exception parsing an RTSP request. " + excp.Message);
			}
		}

		public new string ToString()
		{
			try
			{
				string methodStr = (Method != RTSPMethodsEnum.UNKNOWN) ? Method.ToString() : UnknownMethod;

                string message = methodStr + " " + URL.ToString() + " " + RTSPVersion + m_CRLF;
                message += (Header != null) ? Header.ToString() : null;

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
				logger.Error("Exception RTSPRequest ToString. " + excp.Message);
				throw excp;
			}
		}
    }
}
