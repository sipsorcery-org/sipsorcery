//-----------------------------------------------------------------------------
// Filename: RTSPHeader.cs
//
// Description: RTSP header.
//
// History:
// 09 Nov 2007	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2007 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
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
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using log4net;

namespace SIPSorcery.Net
{
    public enum RTSPHeaderParserError
    {
        None = 0,
        MandatoryColonMissing = 1,
        CSeqMethodMissing = 2,
        CSeqNotValidInteger = 3,
        CSeqEmpty = 4,
    }

    public enum RTSPHeaderError
    {
        None = 0,
    }

    public class RTSPHeader
    {
        private static string m_CRLF = RTSPConstants.CRLF;

        private static ILog logger = AssemblyStreamState.logger;

        private static char[] delimiterChars = new char[] { ':' };

        public int CSeq = -1;
        public string Session;
        public string Transport;

        public List<string> UnknownHeaders = new List<string>();    // Holds any unrecognised headers.

        public string RawCSeq;

        public RTSPHeaderParserError CSeqParserError = RTSPHeaderParserError.None;

        private RTSPHeader()
        { }

        public RTSPHeader(int cseq, string session)
        {
            CSeq = cseq;
            Session = session;
        }

        public static string[] SplitHeaders(string message)
        {
            // SIP headers can be extended across lines if the first character of the next line is at least on whitespace character.
            message = Regex.Replace(message, m_CRLF + @"\s+", " ", RegexOptions.Singleline);

            // Some user agents couldn't get the \r\n bit right.
            message = Regex.Replace(message, "\r ", m_CRLF, RegexOptions.Singleline);

            return Regex.Split(message, m_CRLF);
        }

        public static RTSPHeader ParseRTSPHeaders(string[] headersCollection)
		{
			try
			{
				RTSPHeader rtspHeader = new RTSPHeader();

    			string lastHeader = null;
						
				for(int lineIndex = 0; lineIndex<headersCollection.Length; lineIndex++)
				{
					string headerLine = headersCollection[lineIndex];

					if(headerLine == null || headerLine.Trim().Length == 0)
					{
						// No point processing blank headers.
						continue;
					}
					
					string headerName = null;
					string headerValue = null;
					
					// If the first character of a line is whitespace it's a contiuation of the previous line.
                    if(headerLine.StartsWith(" "))
                    {
                        headerName = lastHeader;
                        headerValue = headerLine.Trim();
                    }
                    else
                    {
                        string[] headerParts = headerLine.Trim().Split(delimiterChars, 2);

                        if(headerParts == null || headerParts.Length <2)
                        {
                            logger.Error("Invalid RTSP header, ignoring. header=" + headerLine + ".");
							
                            try
                            {
                                string errorHeaders = String.Join(m_CRLF, headersCollection);
                                logger.Error("Full Invalid Headers: " + errorHeaders);
                            }
                            catch{}

                            continue;
                        }

                        headerName = headerParts[0].Trim();
                        headerValue = headerParts[1].Trim();
                    }

                    try
					{
                        string headerNameLower = headerName.ToLower();

                        #region CSeq
                        if (headerNameLower == RTSPHeaders.RTSP_HEADER_CSEQ.ToLower())
						{
							rtspHeader.RawCSeq = headerValue;

                            if (headerValue == null || headerValue.Trim().Length == 0)
							{
								rtspHeader.CSeqParserError = RTSPHeaderParserError.CSeqEmpty;
                                logger.Warn("Invalid SIP header, the " + RTSPHeaders.RTSP_HEADER_CSEQ + " was empty.");
							}
                            else if (!Int32.TryParse(headerValue.Trim(), out rtspHeader.CSeq))
                            {
                                rtspHeader.CSeqParserError = RTSPHeaderParserError.CSeqNotValidInteger;
                                logger.Warn("Invalid SIP header, the " + RTSPHeaders.RTSP_HEADER_CSEQ + " was not a valid 32 bit integer, " + headerValue + ".");
                            }
                        }
						#endregion
                        #region Session
                        if (headerNameLower == RTSPHeaders.RTSP_HEADER_SESSION.ToLower())
                        {
                            rtspHeader.Session = headerValue;
                        }
                        #endregion
                        #region Transport
                        if (headerNameLower == RTSPHeaders.RTSP_HEADER_TRANSPORT.ToLower())
                        {
                            rtspHeader.Transport = headerValue;
                        }
                        #endregion
                        else
                        {
                            rtspHeader.UnknownHeaders.Add(headerLine);
                        }

                        lastHeader = headerName;
                    }
                    catch (Exception parseExcp)
                    {
                        logger.Error("Error parsing RTSP header " + headerLine + ". " + parseExcp.Message);
                        throw parseExcp;
                    }
                }

                //sipHeader.Valid = sipHeader.Validate(out sipHeader.ValidationError);

                return rtspHeader;
            }
            catch (ApplicationException appHeaderExcp)
            {
                throw appHeaderExcp;
            }
            catch (Exception excp)
            {
                logger.Error("Exception ParseRTSPHeaders. " + excp.Message);
                throw excp;
            }
        }


        public new string ToString()
        {
            try
            {
                StringBuilder headersBuilder = new StringBuilder();

                headersBuilder.Append((CSeq > 0) ? RTSPHeaders.RTSP_HEADER_CSEQ + ": " + CSeq + m_CRLF : null);
                headersBuilder.Append((Session != null) ? RTSPHeaders.RTSP_HEADER_SESSION + ": " + Session + m_CRLF : null);
                headersBuilder.Append((Transport != null) ? RTSPHeaders.RTSP_HEADER_TRANSPORT + ": " + Transport + m_CRLF : null);

                return headersBuilder.ToString();
            }
            catch (Exception excp)
            {
                logger.Error("Exception RTSPHeader ToString. " + excp.Message);
                throw excp;
            }
        }
    }
}
