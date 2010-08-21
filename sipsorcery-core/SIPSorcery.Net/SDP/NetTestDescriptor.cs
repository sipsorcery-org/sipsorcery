//-----------------------------------------------------------------------------
// Filename: NetTestDescriptor.cs
//
// Description: SIP request body that describes a network test request. The network
// test is typically used to measure the realtime characteristics of a network path 
// 
// History:
// 09 Jan 2007	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Text;
using System.Text.RegularExpressions;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Net
{    
    public class NetTestDescriptor
    {   
        public const int MAXIMUM_PAYLOAD_SIZE = 1460;			// Ethernet MTU = 1500. (12B RTP header, 8B UDP Header, 20B IP Header, TOTAL = 40B).
        public const int MAXIMUM_RATEPER_CHANNEL = 500000;      // This is in bits. Needs to be less than 1460B * 8bits * 66.667 Packets/s, so 500Kbps is a nice number.
        public const int RTP_HEADER_OVERHEAD = 12;              // 12B RTP header.
        public const int DEFAULT_RTP_PAYLOADSIZE = 160;         // g711 @ 20ms.

        private string m_CRLF = AppState.CRLF;

        protected static ILog logger = AppState.logger;

        public int NumberChannels = 1;
        public int FrameSize = 20;        // In milliseconds, determines how often packets are transmitted, e.g. framezie=20ms results in 50 packets per second.
        public int PayloadSize = 172;     // The size of each packet in bytes.
        public IPEndPoint RemoteSocket;   // Socket the data stream will be sent to.

        public NetTestDescriptor()
        { }

        public NetTestDescriptor(int numberChannels, int frameSize, int payloadSize, IPEndPoint remoteEndPoint)
        {
            NumberChannels = numberChannels;
            FrameSize = frameSize;
            PayloadSize = payloadSize;
            RemoteSocket = remoteEndPoint;
        }

        public static NetTestDescriptor ParseNetTestDescriptor(string description)
        {
            try
            {
                if (description == null || description.Trim().Length == 0)
                {
                    logger.Error("Cannot parse NetTestDescriptor from an empty string.");
                    return null;
                }
                else
                {
                    int numberChannels = Convert.ToInt32(Regex.Match(description, @"channels=(?<channels>\d+)", RegexOptions.IgnoreCase | RegexOptions.Singleline).Result("${channels}"));
                    int frameSize = Convert.ToInt32(Regex.Match(description, @"frame=(?<frame>\d+)", RegexOptions.IgnoreCase | RegexOptions.Singleline).Result("${frame}"));
                    int payloadSize = Convert.ToInt32(Regex.Match(description, @"payload=(?<payload>\d+)", RegexOptions.IgnoreCase | RegexOptions.Singleline).Result("${payload}"));
                    string socketStr = Regex.Match(description, @"socket=(?<socket>.+?)(\s|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline).Result("${socket}");

                    NetTestDescriptor descriptor = new NetTestDescriptor(numberChannels, frameSize, payloadSize, IPSocket.ParseSocketString(socketStr));
                    return descriptor;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ParseNetTestDescriptor. " + excp.Message);
                throw excp;
            }
        }

        public new string ToString()
        {
            string description =
                "channels=" + NumberChannels + m_CRLF +
                "frame=" + FrameSize + m_CRLF +
                "payload=" + PayloadSize + m_CRLF +
                "socket=" + RemoteSocket.ToString() + m_CRLF;

            return description;
        }
    }
}
