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
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SIPSorcery.Sys;
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

    /// <summary>
    /// Represents the Transport header used in RTSP requests and responses.
    /// </summary>
    public class RTSPTransportHeader
    {
        public const string DESTINATION_FIELD_NAME = "destination";
        public const string SOURCE_FIELD_NAME = "source";
        public const string MULTICAST_RTP_PORT_FIELD_NAME = "port";
        public const string CLIENT_RTP_PORT_FIELD_NAME = "client_port";
        public const string SERVER_RTP_PORT_FIELD_NAME = "server_port";

        private const string DEFAULT_TRANSPORT_SPECIFIER = "RTP/AVP";
        private const string DEFAULT_BROADCAST_TYPE = "unicast";

        private static ILog logger = AppState.logger;

        public string RawHeader;

        public string TransportSpecifier;   // RTP/AVP{/[TCP/UDP]}, default is UDP.
        public string BroadcastType;        // Unicast or multicast.
        public string Destination;
        public string Source;
        public string MulticastRTPPortRange;// e.g. port=3456-3457.
        public string ClientRTPPortRange;   // e.g. client_port=3456-3457.
        public string ServerRTPPortRange;   // e.g. server_port=3456-3457.

        public RTSPTransportHeader()
        {
            TransportSpecifier = DEFAULT_TRANSPORT_SPECIFIER;
            BroadcastType = DEFAULT_BROADCAST_TYPE;
        }

        public static RTSPTransportHeader Parse(string header)
        {
            var transportHeader = new RTSPTransportHeader();

            if (header.NotNullOrBlank())
            {
                transportHeader.RawHeader = header;

                string[] fields = header.Split(';');

                transportHeader.TransportSpecifier = fields[0];
                transportHeader.BroadcastType = fields[1];

                foreach(string field in fields.Where(x => x.Contains('=')))
                {
                    string fieldName = field.Split('=')[0];
                    string fieldValue = field.Split('=')[1];

                    switch(fieldName.ToLower())
                    {
                        case CLIENT_RTP_PORT_FIELD_NAME:
                            transportHeader.ClientRTPPortRange = fieldValue.Trim();
                            break;
                        case DESTINATION_FIELD_NAME:
                            transportHeader.Destination = fieldValue.Trim();
                            break;
                        case SERVER_RTP_PORT_FIELD_NAME:
                            transportHeader.ServerRTPPortRange = fieldValue.Trim();
                            break;
                        case SOURCE_FIELD_NAME:
                            transportHeader.Source = fieldValue.Trim();
                            break;
                        default:
                            logger.Warn("An RTSP Transport header parameter was not recognised. " + field);
                            break;
                    }
                }
            }

            return transportHeader;
        }

        /// <summary>
        /// Attempts to determine the remote RTP port based on the transport header attributes.
        /// </summary>
        /// <returns>The remote port that RTP packets should be sent to. If the port cannot be determined then 0.</returns>
        public int GetClientRTPPort()
        {
            if(ClientRTPPortRange.NotNullOrBlank())
            {
                int clientRTPPort = 0;

                var fields = ClientRTPPortRange.Split('-');

                if(Int32.TryParse(fields[0], out clientRTPPort))
                {
                    return clientRTPPort;
                }
            }

            return 0;
        }

        public override string ToString()
        {
            string transportHeader = TransportSpecifier + ";" + BroadcastType;

            if(Destination.NotNullOrBlank())
            {
                transportHeader += String.Format(";{0}={1}", DESTINATION_FIELD_NAME, Destination);
            }

            if (Source.NotNullOrBlank())
            {
                transportHeader += String.Format(";{0}={1}", SOURCE_FIELD_NAME, Source);
            }

            if (ClientRTPPortRange.NotNullOrBlank())
            {
                transportHeader += String.Format(";{0}={1}", CLIENT_RTP_PORT_FIELD_NAME, ClientRTPPortRange);
            }

            if (ServerRTPPortRange.NotNullOrBlank())
            {
                transportHeader += String.Format(";{0}={1}", SERVER_RTP_PORT_FIELD_NAME, ServerRTPPortRange);
            }

            return transportHeader;
        }
    }


    public class RTSPHeader
    {
        private static string m_CRLF = RTSPConstants.CRLF;

        private static ILog logger = AssemblyStreamState.logger;

        private static char[] delimiterChars = new char[] { ':' };

        public string Accept;
        public string ContentType;
        public int ContentLength;
        public int CSeq = -1;
        public string Session;
        public RTSPTransportHeader Transport;

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

                for (int lineIndex = 0; lineIndex < headersCollection.Length; lineIndex++)
                {
                    string headerLine = headersCollection[lineIndex];

                    if (headerLine == null || headerLine.Trim().Length == 0)
                    {
                        // No point processing blank headers.
                        continue;
                    }

                    string headerName = null;
                    string headerValue = null;

                    // If the first character of a line is whitespace it's a contiuation of the previous line.
                    if (headerLine.StartsWith(" "))
                    {
                        headerName = lastHeader;
                        headerValue = headerLine.Trim();
                    }
                    else
                    {
                        string[] headerParts = headerLine.Trim().Split(delimiterChars, 2);

                        if (headerParts == null || headerParts.Length < 2)
                        {
                            logger.Error("Invalid RTSP header, ignoring. header=" + headerLine + ".");

                            try
                            {
                                string errorHeaders = String.Join(m_CRLF, headersCollection);
                                logger.Error("Full Invalid Headers: " + errorHeaders);
                            }
                            catch { }

                            continue;
                        }

                        headerName = headerParts[0].Trim();
                        headerValue = headerParts[1].Trim();
                    }

                    try
                    {
                        string headerNameLower = headerName.ToLower();

                        #region Accept
                        if (headerNameLower == RTSPHeaders.RTSP_HEADER_ACCEPT.ToLower())
                        {
                            rtspHeader.Accept = headerValue;
                        }
                        #endregion
                        #region ContentType
                        if (headerNameLower == RTSPHeaders.RTSP_HEADER_CONTENTTYPE.ToLower())
                        {
                            rtspHeader.ContentType = headerValue;
                        }
                        #endregion
                        #region ContentLength
                        if (headerNameLower == RTSPHeaders.RTSP_HEADER_CONTENTLENGTH.ToLower())
                        {
                            rtspHeader.RawCSeq = headerValue;

                            if (headerValue == null || headerValue.Trim().Length == 0)
                            {
                                logger.Warn("Invalid RTSP header, the " + RTSPHeaders.RTSP_HEADER_CONTENTLENGTH + " was empty.");
                            }
                            else if (!Int32.TryParse(headerValue.Trim(), out rtspHeader.ContentLength))
                            {
                                logger.Warn("Invalid RTSP header, the " + RTSPHeaders.RTSP_HEADER_CONTENTLENGTH + " was not a valid 32 bit integer, " + headerValue + ".");
                            }
                        }
                        #endregion
                        #region CSeq
                        if (headerNameLower == RTSPHeaders.RTSP_HEADER_CSEQ.ToLower())
                        {
                            rtspHeader.RawCSeq = headerValue;

                            if (headerValue == null || headerValue.Trim().Length == 0)
                            {
                                rtspHeader.CSeqParserError = RTSPHeaderParserError.CSeqEmpty;
                                logger.Warn("Invalid RTSP header, the " + RTSPHeaders.RTSP_HEADER_CSEQ + " was empty.");
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
                            rtspHeader.Transport = RTSPTransportHeader.Parse(headerValue);
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
                headersBuilder.Append((Accept != null) ? RTSPHeaders.RTSP_HEADER_ACCEPT + ": " + Accept + m_CRLF : null);
                headersBuilder.Append((ContentType != null) ? RTSPHeaders.RTSP_HEADER_CONTENTTYPE + ": " + ContentType + m_CRLF : null);
                headersBuilder.Append((ContentLength != 0) ? RTSPHeaders.RTSP_HEADER_CONTENTLENGTH + ": " + ContentLength + m_CRLF : null);
                headersBuilder.Append((Transport != null) ? RTSPHeaders.RTSP_HEADER_TRANSPORT + ": " + Transport.ToString() + m_CRLF : null);

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
