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
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using log4net;

namespace SIPSorcery.Net
{
    public class RTSPClient
    {
        //public const int DNS_RESOLUTION_TIMEOUT = 2000;    // Timeout for resolving DNS hosts in milliseconds.
        public const int RTSP_PORT = 554;
        
        private static ILog logger = AssemblyStreamState.logger;
        
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

        public void Start(string url, IPAddress remoteAddress, int rtpPort)
        {
            string hostname = Regex.Match(url, @"rtsp://(?<hostname>\S+?)/").Result("${hostname}");

            logger.Debug("RTSP Client Connecting to " + hostname + ".");
            TcpClient rtspSocket = new TcpClient(hostname, RTSP_PORT);
            NetworkStream rtspStream = rtspSocket.GetStream();

            RTSPRequest rtspRequest = new RTSPRequest(RTSPMethodsEnum.SETUP, url);
            RTSPHeader rtspHeader = new RTSPHeader(2, null);
            int controlPort = rtpPort + 1;
            rtspHeader.Transport = "RTP/AVP;unicast;dest_addr=" + remoteAddress + ";client_port=" + rtpPort + "-" + controlPort;
            rtspRequest.Header = rtspHeader;
            string rtspReqStr = rtspRequest.ToString();

            RTSPMessage rtspMessage = null;
            RTSPResponse rtspResponse = null;

            Console.WriteLine(rtspReqStr);
            byte[] rtspRequestBuffer = Encoding.UTF8.GetBytes(rtspReqStr);
            rtspStream.Write(rtspRequestBuffer, 0, rtspRequestBuffer.Length);

            byte[] buffer = new byte[2048];
            int bytesRead = rtspStream.Read(buffer, 0, 2048);

            if (bytesRead > 0)
            {
                Console.Write(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                rtspMessage = RTSPMessage.ParseRTSPMessage(buffer, null, null);

                if (rtspMessage.RTSPMessageType == RTSPMessageTypesEnum.Response)
                {
                    rtspResponse = RTSPResponse.ParseRTSPResponse(rtspMessage);
                    Console.WriteLine("RTSP Response received to SETUP: " + rtspResponse.StatusCode + " " + rtspResponse.Status + " " + rtspResponse.ReasonPhrase + ".");
                }
            }
            else
            {
                Console.WriteLine("socket closed.");
            }

            if (rtspResponse != null && rtspResponse.StatusCode >= 200 && rtspResponse.StatusCode <= 299)
            {
                RTSPRequest playRequest = new RTSPRequest(RTSPMethodsEnum.PLAY, url);
                RTSPHeader playHeader = new RTSPHeader(3, rtspResponse.Header.Session);
                playRequest.Header = playHeader;

                Console.WriteLine(playRequest.ToString());
                rtspRequestBuffer = Encoding.UTF8.GetBytes(playRequest.ToString());
                rtspStream.Write(rtspRequestBuffer, 0, rtspRequestBuffer.Length);
            }

            buffer = new byte[2048];
            bytesRead = rtspStream.Read(buffer, 0, 2048);

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
    }
}
