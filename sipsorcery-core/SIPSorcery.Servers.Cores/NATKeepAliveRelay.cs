// ============================================================================
// FileName: NATKeepAliveRelay.cs
//
// Description:
// A socket that listens for agents that wish to send NAT keepalives to clients and relays the
// request from a specified socket.
//
// Author(s):
// Aaron Clauson
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
// ============================================================================

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using log4net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{
    public delegate void SendNATKeepAliveDelegate(NATKeepAliveMessage keepAliveMessage);

    public class NATKeepAliveMessage
    {
        public SIPEndPoint LocalSIPEndPoint;
        public IPEndPoint RemoteEndPoint;

        private NATKeepAliveMessage()
        { }

        public NATKeepAliveMessage(SIPEndPoint localSIPEndPoint, IPEndPoint remoteEndPoint)
        {
            LocalSIPEndPoint = localSIPEndPoint;
            RemoteEndPoint = remoteEndPoint;
        }

        public static NATKeepAliveMessage ParseNATKeepAliveMessage(byte[] buffer)
        {
            if (buffer != null && buffer.Length == 16)
            {
                byte[] sendToAddrBuffer = new byte[4];
                Buffer.BlockCopy(buffer, 0, sendToAddrBuffer, 0, 4);
                IPAddress sendToAddress = new IPAddress(sendToAddrBuffer);

                int sendToPort = BitConverter.ToInt32(buffer, 4);

                byte[] sendFromAddrBuffer = new byte[4];
                Buffer.BlockCopy(buffer, 8, sendFromAddrBuffer, 0, 4);
                IPAddress sendFromAddress = new IPAddress(sendFromAddrBuffer);

                int sendFromPort = BitConverter.ToInt32(buffer, 12);

                //SIPProtocolsEnum protocol = SIPProtocolsType.GetProtocolTypeFromId(BitConverter.ToInt32(buffer, 16));
                //SIPProtocolsEnum protocol = SIPProtocolsEnum.udp;

                NATKeepAliveMessage natKeepAliveMsg = new NATKeepAliveMessage(new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(sendToAddress, sendToPort)), new IPEndPoint(sendFromAddress, sendFromPort));
                return natKeepAliveMsg;
            }
            else
            {
                return null;
            }
        }

        public byte[] ToBuffer()
        {
            if (RemoteEndPoint != null && LocalSIPEndPoint != null)
            {
                //byte[] buffer = new byte[20];
                byte[] buffer = new byte[16];

                Buffer.BlockCopy(RemoteEndPoint.Address.GetAddressBytes(), 0, buffer, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(RemoteEndPoint.Port), 0, buffer, 4, 4);
                Buffer.BlockCopy(LocalSIPEndPoint.SocketEndPoint.Address.GetAddressBytes(), 0, buffer, 8, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(LocalSIPEndPoint.SocketEndPoint.Port), 0, buffer, 12, 4);
                //Buffer.BlockCopy(BitConverter.GetBytes((int)Protocol), 0, buffer, 16, 4);

                return buffer;
            }
            else
            {
                return null;
            }
        }

        #region Unit testing.

        #if UNITTEST

        [TestFixture]
        public class NATKeepAliveMessageUnitTest
        {
            [TestFixtureSetUp]
            public void Init()
            {

            }

            [TestFixtureTearDown]
            public void Dispose()
            {

            }

            [Test]
            public void SampleTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
                Assert.IsTrue(true, "True was false.");
            }

            [Test]
            public void ReverseMessageTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string sendToAddress = "192.168.1.1";
                int sendToPort = 3455;
                string sendFromAddress = "192.168.1.2";
                int sendFromPort = 3244;
                NATKeepAliveMessage keepAliveMsg = new NATKeepAliveMessage(SIPEndPoint.ParseSIPEndPoint(sendToAddress + ":" + sendToPort), new IPEndPoint(IPAddress.Parse(sendFromAddress), sendFromPort));

                byte[] buffer = keepAliveMsg.ToBuffer();
                Assert.IsTrue(buffer != null && buffer.Length == 20, "The byte buffer produced for the NATKeepAliveMessage is invalid.");

                NATKeepAliveMessage rtnMsg = NATKeepAliveMessage.ParseNATKeepAliveMessage(buffer);
                Assert.IsNotNull(rtnMsg, "The NATKeepAliveMessage could not be parsed from the buffer.");

                Assert.IsTrue(rtnMsg.RemoteEndPoint.ToString() == keepAliveMsg.RemoteEndPoint.ToString(), "The sent and returned sendto sockets were different.");
                Assert.IsTrue(rtnMsg.LocalSIPEndPoint.ToString() == keepAliveMsg.LocalSIPEndPoint.ToString(), "The sent and returned sendfrom sockets were different.");
            }
        }

        #endif

        #endregion
    }

    /// <summary>
    /// Listens for NATKeepAlive messages on a loopback or private socket and when received actions the received messages by sending a 4 byte null payload
    /// to the requested end point from the requested SIP socket. The SIP socket will be one of the sockets the application running this object owns and
    /// the idea is to multiplex the zero byte payloads onto the same signalling socket to keep user end NAT's open.
    /// </summary>
    public class NATKeepAliveRelay
    {      
        private ILog logger = log4net.LogManager.GetLogger("natkeepalive");

        private SIPTransport m_sipTransport;
        private SIPChannel m_natKeepAliveChannel;                            // Can use a SIP Channel for this since it's essentially just a TCP or UDP listener anyway.
        private SIPMonitorLogDelegate SIPMonitorLog_External;

        private byte[] m_sendBuffer = new byte[] { 0x0, 0x0, 0x0, 0x0 };    // Doesn't matter what is sent since it's just to keep the NAT connection alive.

        /// <param name="listenerSocket">Socket to listen for NAT keepalive relay requests on.</param>
        public NATKeepAliveRelay(SIPTransport sipTransport, IPEndPoint listenerSocket, SIPMonitorLogDelegate sipMonitorLogDelegate)
        {
            m_sipTransport = sipTransport;
            m_natKeepAliveChannel = new SIPUDPChannel(listenerSocket);
            m_natKeepAliveChannel.SIPMessageReceived += NatKeepAliveChannelMessageReceived;
            SIPMonitorLog_External = sipMonitorLogDelegate;

            logger.Debug("NATKeepAlive Relay instantiated on " + listenerSocket + ".");
        }

        public void Shutdown()
        {
            m_natKeepAliveChannel.Close();
        }

        private void NatKeepAliveChannelMessageReceived(SIPChannel sipChannel, SIPEndPoint remoteEndPoint, byte[] buffer)
        {
            try
            {
                NATKeepAliveMessage keepAliveMessage = NATKeepAliveMessage.ParseNATKeepAliveMessage(buffer);

                if (keepAliveMessage != null)
                {
                    FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.NATKeepAlive, SIPMonitorEventTypesEnum.NATKeepAliveRelay, "Relaying NAT keep-alive from proxy socket " + keepAliveMessage.LocalSIPEndPoint + " to " + keepAliveMessage.RemoteEndPoint + ".", null));
                    m_sipTransport.SendRaw(keepAliveMessage.LocalSIPEndPoint, new SIPEndPoint(keepAliveMessage.RemoteEndPoint), m_sendBuffer);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception NatKeepAliveChannelMessageReceived. " + excp.Message);
            }
        }

        private void FireSIPMonitorLogEvent(SIPMonitorEvent monitorEvent)
        {
            if (SIPMonitorLog_External != null)
            {
                try
                {
                    SIPMonitorLog_External(monitorEvent);
                }
                catch (Exception excp)
                {
                    logger.Error("Exception FireProxyLogEvent NATKeepAliveRelay. " + excp.Message);
                }
            }
        }
    }
}
