// ============================================================================
// FileName: WPRTPChannel.cs
//
// Description:
// An RTP channel that can be used with Windows Phone. The main difference from the desktop version being that
// the underlying UDP socket is asynchronous.
//
// Author(s):
// Aaron Clauson
//
// History:
// 01 Apr 2013	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2013 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD 
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
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Text;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Net
{
    public class WPRTPChannel
    {
        public static int MaxRTPMessageSize = 2048;

        private static ILog logger = AppState.logger;

        private Socket m_rtpListener;
        private IPEndPoint m_localEndPoint;
        private IPEndPoint m_remoteEndPoint;
        private bool m_isClosed = false;

        private RTPHeader m_sendRTPHeader = new RTPHeader();

        public event Action<byte[], int> SampleReceived;

        public WPRTPChannel()
        {    }

        public WPRTPChannel(IPEndPoint localEndPoint)
        {
            m_localEndPoint = localEndPoint;
            //m_rtpListener = new UDPListener(localEndPoint);
            //m_rtpListener.PacketReceived += RTPPacketReceived;

            StartListener();
        }

        public WPRTPChannel(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint)
        {
            m_localEndPoint = localEndPoint;
            m_remoteEndPoint = remoteEndPoint;
            
            //m_rtpListener = new UDPListener(localEndPoint);
            //m_rtpListener.PacketReceived += RTPPacketReceived;

            StartListener();
        }

        private void StartListener()
        {
            m_rtpListener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            m_rtpListener.Bind(m_localEndPoint);

            SocketAsyncEventArgs receiveArgs = new SocketAsyncEventArgs();
            receiveArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            receiveArgs.SetBuffer(new Byte[MaxRTPMessageSize], 0, MaxRTPMessageSize);
            receiveArgs.Completed += SocketRead_Completed;
            m_rtpListener.ReceiveAsync(receiveArgs);
        }

        private void SocketRead_Completed(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                if (e.SocketError == SocketError.Success)
                {
                    if (e.Buffer == null || e.Buffer.Length == 0)
                    {
                        // No need to care about zero byte packets.
                        //string remoteEndPoint = (inEndPoint != null) ? inEndPoint.ToString() : "could not determine";
                        //logger.Error("Zero bytes received on SIPUDPChannel " + m_localSIPEndPoint.ToString() + ".");
                    }
                    else
                    {
                        Debug.WriteLine("RTP packet received from " + e.RemoteEndPoint + ".");
                        logger.Debug("RTP packet received from " + e.RemoteEndPoint + ".");

                        if (SampleReceived != null)
                        {
                            try
                            {
                                var rtpBytes = e.Buffer.Take(e.BytesTransferred).ToArray();
                                var rtpHeader = new RTPHeader(rtpBytes);
                                SampleReceived(rtpBytes, rtpHeader.Length);
                            }
                            catch (Exception rtpParseExcp)
                            {
                                logger.Error("Exception WPRTPChannel parsing RTP. " + rtpParseExcp.Message); 
                            }
                        }
                    }
                }
                else
                {
                    Debug.WriteLine("Error listening on WPRTPChannel. " + e.SocketError.ToString());
                    logger.Error("Error listening on WPRTPChannel. " + e.SocketError.ToString());
                }

                if (!m_isClosed)
                {
                    SocketAsyncEventArgs receiveArgs = new SocketAsyncEventArgs();
                    receiveArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    receiveArgs.SetBuffer(new Byte[MaxRTPMessageSize], 0, MaxRTPMessageSize);
                    receiveArgs.Completed += SocketRead_Completed;
                    m_rtpListener.ReceiveAsync(receiveArgs);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception WPRTPChannel SocketRead_Completed. " + excp.Message);
                throw;
            }
        }

        public void SetRemoteEndPoint(IPEndPoint remoteEndPoint)
        {
            m_remoteEndPoint = remoteEndPoint;
        }

        public void SetSendCodec(RTPPayloadTypesEnum codec)
        {
            m_sendRTPHeader.PayloadType = (int)codec;
        }

        public void Close()
        {
            try
            {
                m_isClosed = true;
                m_rtpListener.Close();
            }
            catch (Exception excp)
            {
                logger.Error("Exception WPRTPChannel Close. " + excp.Message);
            }
        }

        public void Send(byte[] buffer, uint samplePeriod)
        {
            if (m_remoteEndPoint == null)
            {
                logger.Warn("RTP packet could not be sent as remote end point has not yet been set.");
            }
            else
            {
                m_sendRTPHeader.SequenceNumber++;
                m_sendRTPHeader.Timestamp += samplePeriod;

                RTPPacket rtpPacket = new RTPPacket()
                {
                    Header = m_sendRTPHeader,
                    Payload = buffer
                };

                logger.Debug("Sending RTP packet to " + m_remoteEndPoint + ", seq# " + rtpPacket.Header.SequenceNumber + ", timestamp " + rtpPacket.Header.Timestamp + ".");

                byte[] rtpOut = rtpPacket.GetBytes();
                //m_rtpListener.Send(m_remoteEndPoint, rtpOut);
                SendRaw(m_remoteEndPoint, rtpOut, rtpOut.Length);
            }
        }

        /// <summary>
        /// Can be used to send non-RTP packets on the RTP socket such as STUN binding request and responses for Gingle.
        /// </summary>
        public void SendRaw(byte[] buffer, int length)
        {
            //m_rtpListener.Send(m_remoteEndPoint, buffer, length);
            SendRaw(m_remoteEndPoint, buffer, length);
        }

        public void SendRaw(IPEndPoint remoteEndPoint, byte[] buffer, int length)
        {
            if (!m_isClosed)
            {
                //m_rtpListener.Send(remoteEndPoint, buffer, length);

                SocketAsyncEventArgs sendArgs = new SocketAsyncEventArgs();
                sendArgs.SetBuffer(buffer, 0, buffer.Length);
                sendArgs.RemoteEndPoint = remoteEndPoint;
                m_rtpListener.SendToAsync(sendArgs);
            }
        }
    }
}
