//-----------------------------------------------------------------------------
// Filename: RTSPURI.cs
//
// Description: RTSP URI.
//
// History:
// 04 May 2007	Aaron Clauson	Created.
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
using System.Collections;
using System.Collections.Specialized;
using System.Data;
using System.Net;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Net
{
    /// <summary>
    /// RFC2326 3.2:
    /// rtsp_URL  =   ( "rtsp:" | "rtspu:" )
    ///             "//" host [ ":" port ] [ abs_path ]
    ///             host      =   <A legal Internet host domain name of IP address (in dotted decimal form), as defined by Section 2.1 of RFC 1123 cite{rfc1123}>
    ///             port      =   *DIGIT
    /// abs_path is defined in RFC2616 (HTTP 1.1) 3.2.1 which refers to RFC2396 (URI Generic Syntax) 
    ///            abs_path      = "/"  path_segments
    ///            path_segments = segment *( "/" segment )
    ///            segment       = *pchar *( ";" param )
    ///            param         = *pchar
    ///            pchar         = unreserved | escaped | ":" | "@" | "&" | "=" | "+" | "$" | ","
    ///
    /// </summary>
    public class RTSPURL
	{
        public const int DNS_RESOLUTION_TIMEOUT = 2000;    // Timeout for resolving DNS hosts in milliseconds.

        public const string TRANSPORT_ADDR_SEPARATOR = "://";
        public const char HOST_ADDR_DELIMITER = '/';
        public const char PARAM_TAG_DELIMITER = ';';
        public const char HEADER_START_DELIMITER = '?';
        private const char HEADER_TAG_DELIMITER = '&';
        private const char TAG_NAME_VALUE_SEPERATOR = '=';
        
        //private static int m_defaultRTSPPort = RTSPConstants.DEFAULT_RTSP_PORT;
        private static string m_rtspTransport = RTSPConstants.RTSP_RELIABLE_TRANSPORTID;
		
		private static ILog logger = AssemblyStreamState.logger;

        public string URLTransport = m_rtspTransport;
        public string Host;
        public string Path;

		private RTSPURL()
		{}

		public static RTSPURL ParseRTSPURL(string url)
		{
			RTSPHeaderParserError notConcerned;
			return ParseRTSPURL(url, out notConcerned);
		}
	
		public static RTSPURL ParseRTSPURL(string url, out RTSPHeaderParserError parserError)
		{
            try
            {
                parserError = RTSPHeaderParserError.None;

                RTSPURL rtspURL = new RTSPURL();

                if (url == null || url.Trim().Length == 0)
                {
                    throw new ApplicationException("An RTSP URI cannot be parsed from an empty string.");
                }
                else
                {
                     int transAddrPosn = url.IndexOf(TRANSPORT_ADDR_SEPARATOR);

                     if (transAddrPosn == -1)
                     {
                         parserError = RTSPHeaderParserError.MandatoryColonMissing;
                         return null;
                     }
                     else
                     {
                         rtspURL.URLTransport = url.Substring(0, transAddrPosn);
                         string urlHostPortion = url.Substring(transAddrPosn + TRANSPORT_ADDR_SEPARATOR.Length);

                         int hostDelimIndex = urlHostPortion.IndexOf(HOST_ADDR_DELIMITER);
                         if (hostDelimIndex != -1)
                         {
                             rtspURL.Host = urlHostPortion.Substring(0, hostDelimIndex);
                             rtspURL.Path = urlHostPortion.Substring(hostDelimIndex);
                         }
                         else
                         {
                             rtspURL.Host = urlHostPortion;
                         }
                     }

                    return rtspURL;
                }
            }
            catch (ApplicationException appExcp)
            {
                throw appExcp;
            }
            catch (Exception excp)
            {
                throw new ApplicationException("There was an exception parsing an RTSP URL. " + excp.Message + " url=" + url);
            }
		}

		public new string ToString()
		{
			try
			{
                string urlStr = URLTransport + TRANSPORT_ADDR_SEPARATOR + Host;
                urlStr += (Path != null) ? Path : null;
               
				return urlStr;
			}
			catch(Exception excp)
			{
				logger.Error("Exception RTSPURL ToString. " + excp.Message);
				throw excp;
			}
		}
	}
}
