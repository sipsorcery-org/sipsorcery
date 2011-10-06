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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP
{
    public class SilverlightTCPSIPChannel : SIPChannel
    {
        private const int CONNECTION_TIMEOUT = 10000;

        public static int MaxSIPTCPMessageSize = SIPConstants.SIP_MAXIMUM_RECEIVE_LENGTH;

        private Socket m_socket;
        private SocketAsyncEventArgs m_socketConnectionArgs;
        private IPEndPoint m_remoteEndPoint;
        private SIPConnection m_sipConnection;
        public DateTime LastTransmission;           // Records when a SIP packet was last sent or received.

        private bool m_isConnecting = false;
        private bool m_isConnected = false;
        public bool IsConnected
        {
            get { return m_isConnected; }
        }

        public event Action Connected;
        public event Action<string> Disconnected;

        public SilverlightTCPSIPChannel()
        {
            m_localSIPEndPoint = new SIPEndPoint(SIPProtocolsEnum.tcp, new IPEndPoint(IPAddress.Any, 0));
            m_isReliable = true;
            m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        public void Connect(IPEndPoint remoteEndPoint)
        {
            m_isConnecting = true;
            m_remoteEndPoint = remoteEndPoint;

            m_sipConnection = new SIPConnection(this, m_socket, m_remoteEndPoint, SIPProtocolsEnum.tcp, SIPConnectionsEnum.Caller);
            m_sipConnection.SIPSocketDisconnected += SIPSocketDisconnected;
            m_sipConnection.SIPMessageReceived += SIPTCPMessageReceived;

            m_socketConnectionArgs = new SocketAsyncEventArgs();
            m_socketConnectionArgs.RemoteEndPoint = m_remoteEndPoint;
            m_socketConnectionArgs.Completed += SocketConnect_Completed;
            m_socket.ConnectAsync(m_socketConnectionArgs);
            //if (!m_socket.ConnectAsync(m_socketConnectionArgs))
            //{
            //    throw new ApplicationException("Asynchronous socket connect operation unexpectedly returned synchronously.");
            //}
        }

        public void CancelConnect()
        {
            try
            {
                if (m_isConnecting && m_socketConnectionArgs != null)
                {
                    Socket.CancelConnectAsync(m_socketConnectionArgs);
                    m_socket.Close();
                }
                //else
                //{
                //    logger.Warn("SilverlightTCPSIPChannel CancelConnect was called on a socket that's not in the connecting state.");
                //}
            }
            catch (Exception excp)
            {
                logger.Error("Exception SilverlightTCPSIPChannel CancelConnect. " + excp.Message);
            }
        }

        private void SIPSocketDisconnected(IPEndPoint remoteEndPoint)
        {
            if (m_isConnected && Disconnected != null)
            {
                Disconnected("Connection to " + m_remoteEndPoint + " was disconnected.");
            }

            m_isConnected = false;
        }

        public void SetLocalSIPEndPoint(SIPEndPoint localSIPEndPoint)
        {
            m_localSIPEndPoint = localSIPEndPoint;
        }

        private void SocketConnect_Completed(object sender, SocketAsyncEventArgs e)
        {
            m_isConnecting = false;
            m_isConnected = (e.SocketError == SocketError.Success);

            if (m_isConnected)
            {
                SocketAsyncEventArgs receiveArgs = new SocketAsyncEventArgs();
                receiveArgs.SetBuffer(m_sipConnection.SocketBuffer, 0, MaxSIPTCPMessageSize);
                receiveArgs.Completed += SocketRead_Completed;
                m_socket.ReceiveAsync(receiveArgs);

                if (Connected != null)
                {
                    Connected();
                }
             }
            else
            {
                if (Disconnected != null)
                {
                    Disconnected("Connection to " + m_remoteEndPoint + " failed.");
                }
            }
        }

        private void SocketRead_Completed(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                if (m_sipConnection.SocketReadCompleted(e.BytesTransferred))
                {
                    SocketAsyncEventArgs receiveArgs = new SocketAsyncEventArgs();
                    receiveArgs.SetBuffer(m_sipConnection.SocketBuffer, m_sipConnection.SocketBufferEndPosition, MaxSIPTCPMessageSize - m_sipConnection.SocketBufferEndPosition);
                    receiveArgs.Completed += SocketRead_Completed;

                    if (receiveArgs != null)
                    {
                        m_socket.ReceiveAsync(receiveArgs);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SilverlightTCPSIPChannel SocketRead_Completed. " + excp.Message);
                throw;
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
            Send(destinationEndPoint, Encoding.UTF8.GetBytes(message));
        }

        public override void Send(IPEndPoint destinationEndPoint, byte[] buffer)
        {
            if (destinationEndPoint.ToString() != m_remoteEndPoint.ToString())
            {
                throw new ApplicationException("The SilverlightTCPSIPChannel can only send to a single server socket. The current connection is to " + m_remoteEndPoint + ", the request to send was to " + destinationEndPoint + ".");
            }
            else if (!m_isConnected)
            {
                throw new ApplicationException("The SilverlightTCPSIPChannel cannot send as it is in a disconnected state.");
            }

            try
            {
                SocketAsyncEventArgs sendArgs = new SocketAsyncEventArgs();
                sendArgs.SetBuffer(buffer, 0, buffer.Length);
                m_socket.SendAsync(sendArgs);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SilverlightTCPSIPChannel Send. " + excp.Message);
                SIPSocketDisconnected(m_remoteEndPoint);
            }
        }

        private void SIPTCPMessageReceived(SIPChannel channel, SIPEndPoint remoteEndPoint, byte[] buffer)
        {
            if (SIPMessageReceived != null)
            {
                SIPMessageReceived(channel, remoteEndPoint, buffer);
            }
        }

        public override void Send(IPEndPoint dstEndPoint, byte[] buffer, string serverCN)
        {
            throw new ApplicationException("This Send method is not available in the Silverlight SIP TCP channel, please use an alternative overload.");
        }

        public override void Close()
        {
            try
            {
                if (m_isConnected && Disconnected != null)
                {
                    Disconnected("Connection to " + m_remoteEndPoint + " was closed by application request.");
                }

                m_isConnected = false;
                m_socket.Close();
            }
            catch (Exception excp)
            {
                logger.Error("Exception SilverlightTCPSIPChannel Close. " + excp.Message);
            }
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
