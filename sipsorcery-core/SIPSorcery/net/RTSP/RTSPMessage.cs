//-----------------------------------------------------------------------------
// Filename: RTSPMessage.cs
//
// Description: RTSP message.
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
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Net
{
    public class RTSPMessage
    {
        private const string RTSP_RESPONSE_PREFIX = "RTSP";
        private const string RTSP_MESSAGE_IDENTIFIER = "RTSP";	// String that must be in a message buffer to be recognised as an RTSP message and processed.

        private static ILog logger = AssemblyStreamState.logger;

        private static string m_CRLF = RTSPConstants.CRLF;
        private static int m_minFirstLineLength = 7;
        
        public string RawMessage;
		public RTSPMessageTypesEnum RTSPMessageType = RTSPMessageTypesEnum.Unknown;
		public string FirstLine;
		public string[] RTSPHeaders;
		public string Body;
		public byte[] RawBuffer;

        public DateTime ReceivedAt = DateTime.MinValue;
        public IPEndPoint ReceivedFrom;
        public IPEndPoint ReceivedOn;

        public static RTSPMessage ParseRTSPMessage(byte[] buffer, IPEndPoint receivedFrom, IPEndPoint receivedOn)
        {
            string message = null;

            try
            {
                if (buffer == null || buffer.Length < m_minFirstLineLength)
                {
                    // Ignore.
                    return null;
                }
                else if (buffer.Length > RTSPConstants.RTSP_MAXIMUM_LENGTH)
                {
                    logger.Error("RTSP message received that exceeded the maximum allowed message length, ignoring.");
                    return null;
                }
                else if (!ByteBufferInfo.HasString(buffer, 0, buffer.Length, RTSP_MESSAGE_IDENTIFIER, m_CRLF))
                {
                    // Message does not contain "RTSP" anywhrere on the first line, ignore.
                    return null;
                }
                else
                {
                    message = Encoding.UTF8.GetString(buffer);
                    RTSPMessage rtspMessage = ParseRTSPMessage(message, receivedFrom, receivedOn);
                    rtspMessage.RawBuffer = buffer;

                    return rtspMessage;
                }
            }
            catch (Exception excp)
            {
                message = message.Replace("\n", "LF");
                message = message.Replace("\r", "CR");
                logger.Error("Exception ParseRTSPMessage. " + excp.Message + "\nRTSP Message=" + message + ".");
                return null;
            }
        }

        public static RTSPMessage ParseRTSPMessage(string message, IPEndPoint receivedFrom, IPEndPoint receivedOn)
        {
            try
            {
                RTSPMessage rtspMessage = new RTSPMessage();
                rtspMessage.ReceivedAt = DateTime.Now;
                rtspMessage.ReceivedFrom = receivedFrom;
                rtspMessage.ReceivedOn = receivedOn;

                rtspMessage.RawMessage = message;
                int endFistLinePosn = message.IndexOf(m_CRLF);

                if (endFistLinePosn != -1)
                {
                    rtspMessage.FirstLine = message.Substring(0, endFistLinePosn);

                    if (rtspMessage.FirstLine.Substring(0, RTSP_RESPONSE_PREFIX.Length) == RTSP_RESPONSE_PREFIX)
                    {
                        rtspMessage.RTSPMessageType = RTSPMessageTypesEnum.Response;
                    }
                    else
                    {
                        rtspMessage.RTSPMessageType = RTSPMessageTypesEnum.Request;
                    }

                    int endHeaderPosn = message.IndexOf(m_CRLF + m_CRLF);
                    if (endHeaderPosn == -1)
                    {
                        // Assume flakey implementation if message does not contain the required CRLFCRLF sequence and treat the message as having no body.
                        string headerString = message.Substring(endFistLinePosn + 2, message.Length - endFistLinePosn - 2);
                        rtspMessage.RTSPHeaders = RTSPHeader.SplitHeaders(headerString); 
                    }
                    else if (endHeaderPosn > endFistLinePosn + 2)
                    {
                        string headerString = message.Substring(endFistLinePosn + 2, endHeaderPosn - endFistLinePosn - 2);
                        rtspMessage.RTSPHeaders = RTSPHeader.SplitHeaders(headerString); 

                        if (message.Length > endHeaderPosn + 4)
                        {
                            rtspMessage.Body = message.Substring(endHeaderPosn + 4);
                        }
                    }

                    return rtspMessage;
                }
                else
                {
                    logger.Error("Error ParseRTSPMessage, there were no end of line characters in the string being parsed.");
                    return null;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ParseRTSPMessage. " + excp.Message + "\nRTSP Message=" + message + ".");
                return null;
            }
        }
    }
}
