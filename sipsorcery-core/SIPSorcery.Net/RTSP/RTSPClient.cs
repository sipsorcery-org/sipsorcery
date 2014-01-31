//-----------------------------------------------------------------------------
// Filename: RTSPClient.cs
//
// Description: RTSP client functions.
//
// History:
// 16 Nov 2007	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2007-2014 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Pty Ltd. 
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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using log4net;

namespace SIPSorcery.Net
{
    public class RTSPClient
    {
        //public const int DNS_RESOLUTION_TIMEOUT = 2000;    // Timeout for resolving DNS hosts in milliseconds.
        public const int RTSP_PORT = 554;
        private const int MAX_FRAMES_QUEUE_LENGTH = 1000;
        private const int RTP_KEEP_ALIVE_INTERVAL = 30;     // The interval at which to send RTP keep-alive packets to keep the RTSP server from closing the connection.

        private static ILog logger = AssemblyStreamState.logger;

        private string _url;
        private int _cseq = 1;
        private TcpClient _rtspConection;
        private NetworkStream _rtspStream;
        private RTSPSession _rtspSession;
        private Queue<RTPPacket> _packets = new Queue<RTPPacket>();
        private List<RTPFrame> _frames = new List<RTPFrame>();
        private uint _lastCompleteFrameTimestamp;

        public event Action<RTSPClient> OnSetupSuccess;
        public event Action<RTSPClient, RTPFrame> OnFrameReady;

        public bool IsClosed;

        public string GetStreamDescription(string url)
        {
            try
            {
                string hostname = Regex.Match(url, @"rtsp://(?<hostname>\S+?)/").Result("${hostname}");
                //IPEndPoint rtspEndPoint = DNSResolver.R(hostname, DNS_RESOLUTION_TIMEOUT);

                logger.Debug("RTSP Client Connecting to " + hostname + ".");
                TcpClient rtspSocket = new TcpClient(hostname, RTSP_PORT);
                NetworkStream rtspStream = rtspSocket.GetStream();

                string rtspSDP = null;
                RTSPRequest rtspRequest = new RTSPRequest(RTSPMethodsEnum.DESCRIBE, url);
                RTSPHeader rtspHeader = new RTSPHeader(1, null);
                rtspRequest.Header = rtspHeader;
                string rtspReqStr = rtspRequest.ToString();

                RTSPMessage rtspMessage = null;
                RTSPResponse rtspResponse = null;

                byte[] rtspRequestBuffer = Encoding.UTF8.GetBytes(rtspReqStr);
                rtspStream.Write(rtspRequestBuffer, 0, rtspRequestBuffer.Length);

                byte[] buffer = new byte[2048];
                int bytesRead = rtspStream.Read(buffer, 0, 2048);

                if (bytesRead > 0)
                {
                    logger.Debug(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                    byte[] msgBuffer = new byte[bytesRead];
                    Buffer.BlockCopy(buffer, 0, msgBuffer, 0, bytesRead);
                    rtspMessage = RTSPMessage.ParseRTSPMessage(msgBuffer, null, null);

                    if (rtspMessage.RTSPMessageType == RTSPMessageTypesEnum.Response)
                    {
                        rtspResponse = RTSPResponse.ParseRTSPResponse(rtspMessage);
                        logger.Debug("RTSP Response received: " + rtspResponse.StatusCode + " " + rtspResponse.Status + " " + rtspResponse.ReasonPhrase + ".");
                    }

                    rtspSDP = rtspResponse.Body;
                }
                else
                {
                    logger.Warn("Socket closed prematurely in GetStreamDescription for " + url + ".");
                }

                rtspSocket.Close();

                return rtspSDP;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetStreamDescription. " + excp.Message);
                throw excp;
            }
        }

        public void Start(string url)
        {
            _url = url;

            string hostname = Regex.Match(url, @"rtsp://(?<hostname>\S+?)/").Result("${hostname}");

            logger.Debug("RTSP Client Connecting to " + hostname + ".");
            _rtspConection = new TcpClient(hostname, RTSP_PORT);
            _rtspStream = _rtspConection.GetStream();

            _rtspSession = new RTSPSession();
            _rtspSession.ReservePorts();
            _rtspSession.OnRTPDataReceived += RTPReceive;

            RTSPRequest rtspRequest = new RTSPRequest(RTSPMethodsEnum.SETUP, url);
            RTSPHeader rtspHeader = new RTSPHeader(_cseq++, null);
            rtspHeader.Transport = new RTSPTransportHeader() { ClientRTPPortRange = _rtspSession.RTPPort + "-" + _rtspSession.ControlPort };
            rtspRequest.Header = rtspHeader;
            string rtspReqStr = rtspRequest.ToString();

            RTSPMessage rtspMessage = null;
            RTSPResponse rtspResponse = null;

            Console.WriteLine(rtspReqStr);
            byte[] rtspRequestBuffer = Encoding.UTF8.GetBytes(rtspReqStr);
            _rtspStream.Write(rtspRequestBuffer, 0, rtspRequestBuffer.Length);

            byte[] buffer = new byte[2048];
            int bytesRead = _rtspStream.Read(buffer, 0, 2048);

            if (bytesRead > 0)
            {
                Console.Write(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                rtspMessage = RTSPMessage.ParseRTSPMessage(buffer, null, null);

                if (rtspMessage.RTSPMessageType == RTSPMessageTypesEnum.Response)
                {
                    rtspResponse = RTSPResponse.ParseRTSPResponse(rtspMessage);
                    Console.WriteLine("RTSP Response received to SETUP: " + rtspResponse.StatusCode + " " + rtspResponse.Status + " " + rtspResponse.ReasonPhrase + ".");

                    _rtspSession.SessionID = rtspResponse.Header.Session;
                    _rtspSession.Start();

                    ThreadPool.QueueUserWorkItem(delegate { ProcessRTPPackets(); });
                    ThreadPool.QueueUserWorkItem(delegate { SendKeepAlives(); });

                    if (OnSetupSuccess != null)
                    {
                        OnSetupSuccess(this);
                    }
                }
            }
            else
            {
                Console.WriteLine("socket closed.");
            }

            if (rtspResponse != null && rtspResponse.StatusCode >= 200 && rtspResponse.StatusCode <= 299)
            {
                RTSPRequest playRequest = new RTSPRequest(RTSPMethodsEnum.PLAY, url);
                RTSPHeader playHeader = new RTSPHeader(_cseq++, rtspResponse.Header.Session);
                playRequest.Header = playHeader;

                Console.WriteLine(playRequest.ToString());
                rtspRequestBuffer = Encoding.UTF8.GetBytes(playRequest.ToString());
                _rtspStream.Write(rtspRequestBuffer, 0, rtspRequestBuffer.Length);
            }

            buffer = new byte[2048];
            bytesRead = _rtspStream.Read(buffer, 0, 2048);

            if (bytesRead > 0)
            {
                Console.Write(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                rtspMessage = RTSPMessage.ParseRTSPMessage(buffer, null, null);

                if (rtspMessage.RTSPMessageType == RTSPMessageTypesEnum.Response)
                {
                    rtspResponse = RTSPResponse.ParseRTSPResponse(rtspMessage);
                    Console.WriteLine("RTSP Response received to PLAY: " + rtspResponse.StatusCode + " " + rtspResponse.Status + " " + rtspResponse.ReasonPhrase + ".");
                }
            }
            else
            {
                Console.WriteLine("socket closed.");
            }
        }

        /// <summary>
        /// Sends the RTSP teardown request for an existing RTSP session.
        /// </summary>
        private void Teardown()
        {
            if (_rtspStream != null && _rtspConection.Connected)
            {
                logger.Debug("RTSP client sending teardown request for " + _url + ".");

                RTSPRequest teardownRequest = new RTSPRequest(RTSPMethodsEnum.TEARDOWN, _url);
                RTSPHeader teardownHeader = new RTSPHeader(_cseq++, _rtspSession.SessionID);
                teardownRequest.Header = teardownHeader;

                System.Diagnostics.Debug.WriteLine(teardownRequest.ToString());

                var buffer = Encoding.UTF8.GetBytes(teardownRequest.ToString());
                _rtspStream.Write(buffer, 0, buffer.Length);
            }
            else
            {
                logger.Debug("RTSP client did not send teardown request for " + _url + ", the socket was closed.");
            }
        }

        /// <summary>
        /// Closes the session and the RTSP connection to the server.
        /// </summary>
        public void Disconnect()
        {
            try
            {
                Teardown();

                if (_rtspSession != null && !_rtspSession.IsClosed)
                {
                    _rtspSession.Close();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception RTSPClient.Disconnect. " + excp);
            }
        }

        private void RTPReceive(string sessionID, byte[] rtpPayload)
        {
            try
            {
                RTPPacket rtpPacket = new RTPPacket(rtpPayload);

                System.Diagnostics.Debug.WriteLine("RTPReceive ssrc " + rtpPacket.Header.SyncSource + ", seq num " + rtpPacket.Header.SequenceNumber + ", timestamp " + rtpPacket.Header.Timestamp + ", marker " + rtpPacket.Header.MarkerBit + ".");

                if (rtpPacket.Header.Timestamp < _lastCompleteFrameTimestamp)
                {
                    System.Diagnostics.Debug.WriteLine("Ignoring RTP packet with timestamp " + rtpPacket.Header.Timestamp + " as it's earlier than the last complete frame.");
                }
                else
                {
                    //lock(_packets)
                    //{
                        _packets.Enqueue(rtpPacket);
                    //}
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception RTSPClient.RTPReceive. " + excp);
            }
        }

        private void ProcessRTPPackets()
        {
            try
            {
                while (!IsClosed)
                {
                    while (_packets.Count > 0)
                    {
                        RTPPacket rtpPacket = null;

                        //lock (_packets)
                        //{
                            rtpPacket = _packets.Dequeue();
                        //}

                        if (rtpPacket != null)
                        {
                            while (_frames.Count > MAX_FRAMES_QUEUE_LENGTH)
                            {
                                var oldestFrame = _frames.OrderBy(x => x.Timestamp).First();
                                _frames.Remove(oldestFrame);
                                System.Diagnostics.Debug.WriteLine("Receive queue full, dropping oldest frame with timestamp " + oldestFrame.Timestamp + ".");
                            }

                            var frame = _frames.Where(x => x.Timestamp == rtpPacket.Header.Timestamp).SingleOrDefault();

                            if (frame == null)
                            {
                                frame = new RTPFrame() { Timestamp = rtpPacket.Header.Timestamp, HasMarker = rtpPacket.Header.MarkerBit == 1 };
                                frame.AddRTPPacket(rtpPacket);
                                _frames.Add(frame);
                            }
                            else
                            {
                                frame.HasMarker = rtpPacket.Header.MarkerBit == 1;
                                frame.AddRTPPacket(rtpPacket);
                            }

                            if (frame.FramePayload != null)
                            {
                                // The frame is ready for handing over to the UI.
                                byte[] imageBytes = frame.FramePayload;

                                _lastCompleteFrameTimestamp = rtpPacket.Header.Timestamp;
                                System.Diagnostics.Debug.WriteLine("Frame ready " + frame.Timestamp + ", sequence numbers " + frame.StartSequenceNumber + " to " + frame.EndSequenceNumber + ",  payload length " + imageBytes.Length + ".");
                                _frames.Remove(frame);

                                // Also remove any earlier frames as we don't care about anything that's earlier than the current complete frame.
                                foreach (var oldFrame in _frames.Where(x => x.Timestamp <= rtpPacket.Header.Timestamp).ToList())
                                {
                                    System.Diagnostics.Debug.WriteLine("Discarding old frame for timestamp " + oldFrame.Timestamp + ".");
                                    _frames.Remove(oldFrame);
                                }

                                if (OnFrameReady != null)
                                {
                                    OnFrameReady(this, frame);
                                }
                            }
                        }
                    }

                    Thread.Sleep(20);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception RTSPClient.ProcessRTPPackets. " + excp);
            }
        }

        /// <summary>
        /// Sends a keep-alive packet to keep the RTSP RTP connection from being shut.
        /// </summary>
        private void SendKeepAlives()
        {
            try
            {
                while(!IsClosed)
                {
                    _rtspSession.SendRTPRaw(new byte[] { 0x00, 0x00, 0x00, 0x00 });

                    Thread.Sleep(RTP_KEEP_ALIVE_INTERVAL * 1000);
                }
            }
            catch(Exception excp)
            {
                logger.Error("Exception RTSPClient.SendKeepAlives. " + excp);
            }
        }
    }
}
