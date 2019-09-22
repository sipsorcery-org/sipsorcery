//-----------------------------------------------------------------------------
// Filename: SIPMessage.cs
//
// Desciption: Functionality to determine whether a SIP message is a request or
// a response and break a message up into its constituent parts.
//
// History:
// 04 May 2006	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
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
using System.Text;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP
{
    /// <bnf>
    /// generic-message  =  start-line
    ///                     *message-header
    ///                     CRLF
    ///                     [ message-body ]
    /// start-line       =  Request-Line / Status-Line
    /// </bnf>
    public class SIPMessage
    {
        private const string SIP_RESPONSE_PREFIX = "SIP";
        private const string SIP_MESSAGE_IDENTIFIER = "SIP";    // String that must be in a message buffer to be recognised as a SIP message and processed.

        private static int m_sipFullVersionStrLen = SIPConstants.SIP_FULLVERSION_STRING.Length;
        private static int m_minFirstLineLength = 7;
        private static string m_CRLF = SIPConstants.CRLF;

        private static ILog logger = Log.logger;

        public string RawMessage;
        public SIPMessageTypesEnum SIPMessageType = SIPMessageTypesEnum.Unknown;
        public string FirstLine;
        public string[] SIPHeaders;
        public string Body;
        public byte[] RawBuffer;

        public DateTime Created = DateTime.Now;
        public SIPEndPoint RemoteSIPEndPoint;               // The remote IP socket the message was received from or sent to.
        public SIPEndPoint LocalSIPEndPoint;                // The local SIP socket the message was received on or sent from.

        public static SIPMessage ParseSIPMessage(byte[] buffer, SIPEndPoint localSIPEndPoint, SIPEndPoint remoteSIPEndPoint)
        {
            string message = null;

            try
            {
                if (buffer == null || buffer.Length < m_minFirstLineLength)
                {
                    // Ignore.
                    return null;
                }
                else if (buffer.Length > SIPConstants.SIP_MAXIMUM_RECEIVE_LENGTH)
                {
                    throw new ApplicationException("SIP message received that exceeded the maximum allowed message length, ignoring.");
                }
                else if (!ByteBufferInfo.HasString(buffer, 0, buffer.Length, SIP_MESSAGE_IDENTIFIER, m_CRLF))
                {
                    // Message does not contain "SIP" anywhere on the first line, ignore.
                    return null;
                }
                else
                {
                    message = Encoding.UTF8.GetString(buffer, 0, buffer.Length);
                    SIPMessage sipMessage = ParseSIPMessage(message, localSIPEndPoint, remoteSIPEndPoint);

                    if (sipMessage != null)
                    {
                        sipMessage.RawBuffer = buffer;
                        return sipMessage;
                    }
                    else
                    {
                        return null;
                    }
                }
            }
            catch (Exception excp)
            {
                message = message.Replace("\n", "LF");
                message = message.Replace("\r", "CR");
                logger.Error("Exception ParseSIPMessage. " + excp.Message + "\nSIP Message=" + message + ".");
                return null;
            }
        }

        public static SIPMessage ParseSIPMessage(string message, SIPEndPoint localSIPEndPoint, SIPEndPoint remoteSIPEndPoint)
        {
            try
            {
                SIPMessage sipMessage = new SIPMessage();
                sipMessage.LocalSIPEndPoint = localSIPEndPoint;
                sipMessage.RemoteSIPEndPoint = remoteSIPEndPoint;

                sipMessage.RawMessage = message;
                int endFistLinePosn = message.IndexOf(m_CRLF);

                if (endFistLinePosn != -1)
                {
                    sipMessage.FirstLine = message.Substring(0, endFistLinePosn);

                    if (sipMessage.FirstLine.Substring(0, 3) == SIP_RESPONSE_PREFIX)
                    {
                        sipMessage.SIPMessageType = SIPMessageTypesEnum.Response;
                    }
                    else
                    {
                        sipMessage.SIPMessageType = SIPMessageTypesEnum.Request;
                    }

                    int endHeaderPosn = message.IndexOf(m_CRLF + m_CRLF);
                    if (endHeaderPosn == -1)
                    {
                        // Assume flakey implementation if message does not contain the required CRLFCRLF sequence and treat the message as having no body.
                        string headerString = message.Substring(endFistLinePosn + 2, message.Length - endFistLinePosn - 2);
                        sipMessage.SIPHeaders = SIPHeader.SplitHeaders(headerString); //Regex.Split(headerString, m_CRLF);
                    }
                    else
                    {
                        string headerString = message.Substring(endFistLinePosn + 2, endHeaderPosn - endFistLinePosn - 2);
                        sipMessage.SIPHeaders = SIPHeader.SplitHeaders(headerString); //Regex.Split(headerString, m_CRLF);

                        if (message.Length > endHeaderPosn + 4)
                        {
                            sipMessage.Body = message.Substring(endHeaderPosn + 4);
                        }
                    }

                    return sipMessage;
                }
                else
                {
                    logger.Warn("Error ParseSIPMessage, there were no end of line characters in the string being parsed.");
                    return null;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ParseSIPMessage. " + excp.Message + "\nSIP Message=" + message + ".");
                return null;
            }
        }
    }
}
