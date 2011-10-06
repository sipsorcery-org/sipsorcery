//-----------------------------------------------------------------------------
// Filename: XMPPServiceDiscoveryRequest.cs
//
// Description: XMPP request to discover services offered.
// 
// History:
// 24 Jul 2011	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2011 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty. Ltd. Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery. 
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
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using SIPSorcery.Sys;

namespace SIPSorcery.XMPP
{
    public class XMPPServiceDiscoveryRequest
    {
        private static XNamespace m_jabberNS = XMPPConstants.JABBER_NAMESPACE;
        private static XNamespace m_discoveryNS = XMPPConstants.DISCOVERY_NAMESPACE;
        private static XNamespace m_jingleDiscoveryNS = XMPPConstants.GOOGLE_JINGLE_INFO_NAMEPSACE;

        private XMPPAuthenticatedStream m_xmppStream;
        private string m_to;

        public XMPPServiceDiscoveryRequest(XMPPAuthenticatedStream xmppStream, string to)
        {
            m_xmppStream = xmppStream;
            m_to = to;
        }

        public void SendServerDiscoveryQuery()
        {
            string id = Crypto.GetRandomString(6);
            XElement discoveryElement = new XElement(m_jabberNS + XMPPConstants.XMPPRequestTypes.iq.ToString(),
                                            new XAttribute("id", id),
                                            new XAttribute("to", "gmail.com"),
                                            new XAttribute("type", "get"),
                                            new XElement(m_discoveryNS + XMPPConstants.XMPPRequestSubTypes.query.ToString()));
            m_xmppStream.WriteElement(discoveryElement, null);
        }

        public void SendJingleInfoQuery()
        {
            string id = Crypto.GetRandomString(6);
            XElement discoveryElement = new XElement(m_jabberNS + XMPPConstants.XMPPRequestTypes.iq.ToString(),
                                            new XAttribute("id", id),
                                            new XAttribute("from", m_xmppStream.JID),
                                            new XAttribute("to", m_to),
                                            new XAttribute("type", "get"),
                                            new XElement(m_jingleDiscoveryNS + XMPPConstants.XMPPRequestSubTypes.query.ToString()));
            m_xmppStream.WriteElement(discoveryElement, null);
        }

        public void Send()
        {
            string id = Crypto.GetRandomString(6);
            XElement discoveryElement = new XElement(m_jabberNS + XMPPConstants.XMPPRequestTypes.iq.ToString(),
                                            new XAttribute("id",  id),
                                            new XAttribute("from", m_xmppStream.JID),
                                            new XAttribute("to", m_to),
                                            new XAttribute("type", "get"),
                                            new XElement(m_discoveryNS + XMPPConstants.XMPPRequestSubTypes.query.ToString()));
            m_xmppStream.WriteElement(discoveryElement, null);
        }
    }
}
