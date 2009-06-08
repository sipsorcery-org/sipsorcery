//-----------------------------------------------------------------------------
// Filename: SIPConnection.cs
//
// Description: Represents an established socket connection on a connection oriented SIP 
// TCL or TLS.
//
// History:
// 31 Mar 2009	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2009 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP
{
    public delegate void SIPConnectionDisconnectedDelegate(IPEndPoint remoteEndPoint);

    public enum SIPConnectionsEnum
    {
        Listener = 1,   // Indicates the connection was initiated by the remote client to a local server socket.
        Caller = 2,     // Indicated the connection was initiated locally to a remote server socket.
    }

    public class SIPConnection {
        private ILog logger = AssemblyState.logger;

        public static int MaxSIPTCPMessageSize = SIPConstants.SIP_MAXIMUM_LENGTH;
        private static string m_sipMessageDelimiter = SIPConstants.CRLF + SIPConstants.CRLF;
        private static char[] delimiterChars = new char[] { ':' };

        //public TcpClient TCPClient;
        public Stream SIPStream;
        public IPEndPoint RemoteEndPoint;
        public SIPProtocolsEnum ConnectionProtocol;
        public SIPConnectionsEnum ConnectionType;
        public byte[] SocketBuffer;

        private SIPChannel m_owningChannel;
        private int m_bufferEndIndex = 0;

        public event SIPMessageReceivedDelegate SIPMessageReceived;
        public event SIPConnectionDisconnectedDelegate SIPSocketDisconnected = (ep) => { };

        public SIPConnection(SIPChannel channel, Stream sipStream, IPEndPoint remoteEndPoint, SIPProtocolsEnum connectionProtocol, SIPConnectionsEnum connectionType) {
            m_owningChannel = channel;
            SIPStream = sipStream;
            RemoteEndPoint = remoteEndPoint;
            ConnectionProtocol = connectionProtocol;
            ConnectionType = connectionType;
            SocketBuffer = new byte[MaxSIPTCPMessageSize];
        }

        public void ReceiveCallback(IAsyncResult ar) {
            try {
                int bytesRead = SIPStream.EndRead(ar);

                //logger.Debug(bytesRead + " " + ConnectionProtocol + " bytes read from " + RemoteEndPoint + ".");

                if (bytesRead > 0) {
                    m_bufferEndIndex += bytesRead;
                    int endMessageIndex = ByteBufferInfo.GetStringPosition(SocketBuffer, m_sipMessageDelimiter, null);
                    if (endMessageIndex != -1) {
                        //logger.Debug("SIP message delimiter found in buffer. SIP Message:\n" + Encoding.UTF8.GetString(SocketBuffer, 0, endMessageIndex));

                        int contentLength = 0;

                        string[] headers = SIPHeader.SplitHeaders(Encoding.UTF8.GetString(SocketBuffer, 0, endMessageIndex));
                        foreach (string header in headers) {
                            // If the first character of a line is whitespace it's a contiuation of the previous header and not the Content-Length header which won't be spread over multiple lines.
                            if (header.StartsWith(" ")) {
                                continue;
                            }
                            else {
                                string[] headerParts = header.Trim().Split(delimiterChars, 2);

                                if (headerParts == null || headerParts.Length < 2) {
                                    // Invalid SIP header, ignoring.
                                    continue;
                                }

                                string headerName = headerParts[0].Trim().ToLower();
                                if (headerName == SIPHeaders.SIP_COMPACTHEADER_CONTENTLENGTH.ToLower() ||
                                    headerName == SIPHeaders.SIP_HEADER_CONTENTLENGTH.ToLower()) {
                                    string headerValue = headerParts[1].Trim();
                                    if (!Int32.TryParse(headerValue, out contentLength)) {
                                        logger.Warn("The content length could not be parsed from " + headerValue + " in the SIPConnection. buffer now invalid and being purged.");
                                    }
                                }
                            }
                        }

                        if (SocketBuffer.Length >= endMessageIndex + m_sipMessageDelimiter.Length + contentLength) {
                            byte[] sipMsgBuffer = new byte[endMessageIndex + m_sipMessageDelimiter.Length + contentLength];
                            Buffer.BlockCopy(SocketBuffer, 0, sipMsgBuffer, 0, endMessageIndex + m_sipMessageDelimiter.Length + contentLength);
                            endMessageIndex = endMessageIndex - sipMsgBuffer.Length + m_sipMessageDelimiter.Length + contentLength;

                            //logger.Debug("SIP TCP message detected length=" + sipMsgBuffer.Length + ", buffer end index=" + endMessageIndex + ".");

                            if (SIPMessageReceived != null) {
                                SIPMessageReceived(m_owningChannel, new SIPEndPoint(SIPProtocolsEnum.tcp, RemoteEndPoint), sipMsgBuffer);
                            }
                        }
                        else {
                            logger.Warn("The Body of a SIP " + ConnectionProtocol + " message has not yet been received.");
                        }
                    }

                    SocketBuffer = new byte[MaxSIPTCPMessageSize];
                    SIPStream.BeginRead(SocketBuffer, 0, MaxSIPTCPMessageSize, new AsyncCallback(ReceiveCallback), null);
                }
                else {
                    //logger.Debug("SIP " + ConnectionProtocol + " socket to " + RemoteEndPoint + " was disconnected, closing.");
                    SIPStream.Close();
                    SIPSocketDisconnected(RemoteEndPoint);
                }
            }
            catch (Exception excp) {
                logger.Error("Exception ReceiveCallback. " + excp);
                SIPSocketDisconnected(RemoteEndPoint);
            }
        }
    }
}
