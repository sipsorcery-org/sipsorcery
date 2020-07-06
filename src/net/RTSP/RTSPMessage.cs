//-----------------------------------------------------------------------------
// Filename: RTSPMessage.cs
//
// Description: RTSP message.
//
// Author(s):
// Aaron Clauson
//
// History:
// 09 Nov 2007	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class RTSPMessage
    {
        private const string RTSP_RESPONSE_PREFIX = "RTSP";
        private const string RTSP_MESSAGE_IDENTIFIER = "RTSP";  // String that must be in a message buffer to be recognised as an RTSP message and processed.

        private static ILogger logger = Log.Logger;

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
                    logger.LogError("RTSP message received that exceeded the maximum allowed message length, ignoring.");
                    return null;
                }
                else if (!BufferUtils.HasString(buffer, 0, buffer.Length, RTSP_MESSAGE_IDENTIFIER, m_CRLF))
                {
                    // Message does not contain "RTSP" anywhere on the first line, ignore.
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
                logger.LogError("Exception ParseRTSPMessage. " + excp.Message + "\nRTSP Message=" + message + ".");
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
                    logger.LogError("Error ParseRTSPMessage, there were no end of line characters in the string being parsed.");
                    return null;
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception ParseRTSPMessage. " + excp.Message + "\nRTSP Message=" + message + ".");
                return null;
            }
        }
    }
}
