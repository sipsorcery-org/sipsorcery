//-----------------------------------------------------------------------------
// Filename: SIPChannel.cs
//
// Description: Generic items for SIP channels.
// 
// History:
// 19 Apr 2008	Aaron Clauson	Created (split from original SIPUDPChannel).
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
using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP
{
	public class IncomingMessage
	{
    	public SIPChannel LocalSIPChannel;
        public SIPEndPoint RemoteEndPoint;
		public byte[] Buffer;
        public DateTime ReceivedAt;

		public IncomingMessage(SIPChannel sipChannel, SIPEndPoint remoteEndPoint, byte[] buffer)
		{
            LocalSIPChannel = sipChannel;
            RemoteEndPoint = remoteEndPoint;
			Buffer = buffer;
            ReceivedAt = DateTime.Now;
		}
	}

    public abstract class SIPChannel
    {
        protected SIPEndPoint m_localSIPEndPoint = null;
        public SIPEndPoint SIPChannelEndPoint
        {
            get { return m_localSIPEndPoint; }
        }

        /// <summary>
        /// This is the URI to be used for contacting this SIP channel.
        /// </summary>
        public string SIPChannelContactURI
        {
            get { return m_localSIPEndPoint.ToString(); }
        }

        protected bool m_isReliable;    //If the underlying transport channel is reliable, such as TCP, this will be set to true;
        public bool IsReliable
        {
            get { return m_isReliable; }
        }

        protected bool m_isTLS;
        public bool IsTLS {
            get { return m_isTLS; }
        }
       
        public SIPMessageReceivedDelegate SIPMessageReceived;

        public abstract void Send(IPEndPoint destinationEndPoint, string message);
        public abstract void Send(IPEndPoint destinationEndPoint, byte[] buffer);
        public abstract void Send(IPEndPoint destinationEndPoint, byte[] buffer, string serverCertificateName);
        public abstract void Close();
    }
}
