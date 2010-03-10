//-----------------------------------------------------------------------------
// Filename: SilverlightTCPSIPChannel.cs
//
// Description: TCP SIP channel for us with Silverlight client. The Silverlight TCP socket
// has a number of restrictions functionality and security wise hence the need for a custom
// channel implementation different from a standard TCP SIP channel.
// 
// History:
// 12 Oct 2008	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2010 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Ltd, London, UK (www.sipsorcery.com)
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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.Sys;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP
{
    public class SilverlightTCPSIPChannel : SIPChannel
    {
        private const int CONNECTION_TIMEOUT = 10000;

        public static int MaxSIPTCPMessageSize = SIPConstants.SIP_MAXIMUM_LENGTH;

        private Socket m_socket;
        private byte[] m_socketBuffer = new byte[2 * MaxSIPTCPMessageSize];
        private IPEndPoint m_remoteEndPoint;
        public DateTime LastTransmission;           // Records when a SIP packet was last sent or received.

        private bool m_isConnected = false;
        public bool IsConnected
        {
            get { return m_isConnected; }
        }
        private ManualResetEvent m_connectedMRE = new ManualResetEvent(false);

        public SilverlightTCPSIPChannel(IPEndPoint localEndPoint)
        {
            m_localSIPEndPoint = new SIPEndPoint(SIPProtocolsEnum.tcp, localEndPoint);
            m_isReliable = true;
            m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        public void Connect(IPEndPoint remoteEndPoint)
        {
            m_connectedMRE.Reset();
            m_remoteEndPoint = remoteEndPoint;
            SocketAsyncEventArgs socketConnectionArgs = new SocketAsyncEventArgs();
            socketConnectionArgs.RemoteEndPoint = m_remoteEndPoint;
            socketConnectionArgs.Completed += SocketConnect_Completed;

            m_socket.ConnectAsync(socketConnectionArgs);
        }

        public void SetLocalSIPEndPoint(SIPEndPoint localSIPEndPoint)
        {
            m_localSIPEndPoint = localSIPEndPoint;
        }

        private void SocketConnect_Completed(object sender, SocketAsyncEventArgs e)
        {
            m_isConnected = (e.SocketError == SocketError.Success);

            if (m_isConnected)
            {
                SocketAsyncEventArgs receiveArgs = new SocketAsyncEventArgs();
                receiveArgs.SetBuffer(m_socketBuffer, 0, MaxSIPTCPMessageSize);
                receiveArgs.Completed += SocketRead_Completed;
                m_socket.ReceiveAsync(receiveArgs);
                m_connectedMRE.Set();
            }
            else
            {
                throw new ApplicationException("Connection to " + m_remoteEndPoint + " failed.");
            }
        }

        private void SocketRead_Completed(object sender, SocketAsyncEventArgs e)
        {
            int bytesRead = e.BytesTransferred;

            if (bytesRead > 0)
            {
                int receivePosition = 0;
                byte[] sipMsgBuffer = SIPConnection.ProcessReceive(m_socketBuffer, 0, bytesRead);

                while (sipMsgBuffer != null)
                {
                    receivePosition += sipMsgBuffer.Length;

                    if (SIPMessageReceived != null)
                    {
                        LastTransmission = DateTime.Now;
                        SIPMessageReceived(this, new SIPEndPoint(SIPProtocolsEnum.tcp, m_remoteEndPoint), sipMsgBuffer);
                    }

                    if (sipMsgBuffer.Length == bytesRead)
                    {
                        break;
                    }
                    else
                    {
                        sipMsgBuffer = SIPConnection.ProcessReceive(m_socketBuffer, receivePosition, bytesRead);
                    }
                }

                int bytesLeftOver = 0;
                if (receivePosition != bytesRead)
                {
                    bytesLeftOver = bytesRead - receivePosition;
                }

                byte[] nextReceiveBuffer = new byte[MaxSIPTCPMessageSize + bytesLeftOver];
                if (bytesLeftOver > 0)
                {
                    // Copy the unprocessed portion of the current receive buffer into the start if the next receive buffer.
                    Array.Copy(m_socketBuffer, receivePosition, nextReceiveBuffer, 0, bytesLeftOver);
                    m_socketBuffer = nextReceiveBuffer;
                }

                SocketAsyncEventArgs receiveArgs = new SocketAsyncEventArgs();
                receiveArgs.SetBuffer(m_socketBuffer, 0, MaxSIPTCPMessageSize);
                receiveArgs.Completed += SocketRead_Completed;
                m_socket.ReceiveAsync(receiveArgs);
            }
        }

        private void Send(string message)
        {
            byte[] sendBuffer = Encoding.UTF8.GetBytes(message);
            SocketAsyncEventArgs sendArgs = new SocketAsyncEventArgs();
            sendArgs.SetBuffer(sendBuffer, 0, sendBuffer.Length);
            m_socket.SendAsync(sendArgs);
        }

        public override void Send(IPEndPoint destinationEndPoint, string message)
        {
            Send(destinationEndPoint, message);
        }

        public override void Send(IPEndPoint destinationEndPoint, byte[] buffer)
        {
            if (!m_isConnected)
            {
                Connect(destinationEndPoint);

                if (!m_connectedMRE.WaitOne(CONNECTION_TIMEOUT))
                {
                    throw new ApplicationException("Connection to " + destinationEndPoint.ToString() + " timed out.");
                }
            }

            SocketAsyncEventArgs sendArgs = new SocketAsyncEventArgs();
            sendArgs.SetBuffer(buffer, 0, buffer.Length);
            m_socket.SendAsync(sendArgs);
        }

        public override void Send(IPEndPoint dstEndPoint, byte[] buffer, string serverCN)
        {
            throw new ApplicationException("This Send method is not available in the Silverlight SIP TCP channel, please use an alternative overload.");
        }

        public override void Close()
        {
            m_socket.Close();
        }

        public override bool IsConnectionEstablished(IPEndPoint remoteEndPoint)
        {
            throw new NotImplementedException();
        }

        protected override Dictionary<string, SIPConnection> GetConnectionsList()
        {
            throw new NotImplementedException();
        }
    }
}
