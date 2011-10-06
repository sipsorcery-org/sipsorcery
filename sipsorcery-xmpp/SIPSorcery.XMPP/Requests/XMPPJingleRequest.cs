//-----------------------------------------------------------------------------
// Filename: XMPPJingleRequest.cs
//
// Description: XMPP Jingle request to initiate a peer-to-peer session.
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
    public class XMPPJingleRequest
    {
        private static XNamespace m_jabberNS = XMPPConstants.JABBER_NAMESPACE;
        private static XNamespace m_jingleNS = XMPPConstants.JINGLE_NAMESPACE;
        private static XNamespace m_jingleDescriptionNS = XMPPConstants.JINGLE_DESCRIPTION_NAMESPACE;
        private static XNamespace m_jingleICETransportNS = XMPPConstants.JINGLE_ICE_TRANSPORT_NAMESPACE;
        private static XNamespace m_jingleRTPTransportNS = XMPPConstants.JINGLE_RTP_TRANSPORT_NAMESPACE;
        private static XNamespace m_gingleTransportNS = XMPPConstants.GINGLE_TRANSPORT_NAMESPACE;
        private static XNamespace m_gingleSessionNS = XMPPConstants.GINGLE_SESSION_NAMESPACE;
        private static XNamespace m_ginglePhoneNS = XMPPConstants.GINGLE_PHONE_NAMESPACE;

        private XMPPAuthenticatedStream m_xmppStream;
        private string m_to;
        private string m_sessionID;

        public XMPPJingleRequest(XMPPAuthenticatedStream xmppStream, string to)
        {
            m_xmppStream = xmppStream;
            m_to = to;
            m_sessionID = Crypto.GetRandomString(6);
        }

        public void Initiate()
        {
            string id = Crypto.GetRandomString(6);
            XElement jingleElement = new XElement(m_jabberNS + XMPPConstants.XMPPRequestTypes.iq.ToString(), 
                                            new XAttribute("id", id),
                                            new XAttribute("from", m_xmppStream.JID),
                                            new XAttribute("to", m_to),
                                            new XAttribute("type", "set"),
                                            new XElement(m_jingleNS + XMPPConstants.XMPPRequestSubTypes.jingle.ToString(),
                                                new XAttribute("action", "session-initiate"),
                                                new XAttribute("initiator", m_xmppStream.JID),
                                                new XAttribute("sid", m_sessionID),
                                                new XElement(m_jingleNS + "content",
                                                    new XAttribute("creator", "initiator"),
                                                    new XAttribute("name", "audio"),
                                                    new XElement(m_jingleDescriptionNS + "description",
                                                        //new XAttribute(XNamespace.Xmlns + "rtp", XMPPConstants.JINGLE_DESCRIPTION_NAMESPACE),
                                                        new XAttribute("media", "audio"),
                                                         new XElement(m_jingleDescriptionNS + "payload-type",
                                                            new XAttribute("id", "0"),
                                                            new XAttribute("name", "PCMU"),
                                                            new XAttribute("clockrate", "8000"),
                                                            new XElement(m_jingleDescriptionNS + "parameter", 
                                                                new XAttribute("name", "bitrate"),
                                                                new XAttribute("value", "64000"))
                                                            )),
                                                    new XElement(m_gingleTransportNS + "transport")
                                                    //new XElement(m_jingleRTPTransportNS + "transport",
                                                    //    GetCandidate("1.1.1.1", 0))
                                                )),
                                                new XElement(m_gingleSessionNS + "session",
                                                    new XAttribute("type", "initiate"),
                                                    new XAttribute("id", m_sessionID),
                                                    new XAttribute("initiator", m_xmppStream.JID),
                                                    new XElement(m_ginglePhoneNS + "description",
                                                            new XElement(m_ginglePhoneNS + "payload-type",
                                                                new XAttribute("id", "0"),
                                                                new XAttribute("name", "PCMU"),
                                                                new XAttribute("bitrate", "64000"),
                                                                new XAttribute("clockrate", "8000"))))
                                                    );
            m_xmppStream.WriteElement(jingleElement, (s, x) => { });
        }

        public XElement GetCandidate(string ipAddress, int port)
        {
            string candidateID = Crypto.GetRandomString(6);

            XElement candidateElement = new XElement(m_jingleRTPTransportNS + "candidate",
                                            new XAttribute("component", "1"),
                                            new XAttribute("foundation", "1"),
                                            new XAttribute("generation", "0"),
                                            new XAttribute("id", Crypto.GetRandomString(6)),
                                            new XAttribute("ip", ipAddress),
                                            new XAttribute("network", "1"),
                                            new XAttribute("port", port),
                                            new XAttribute("priority", "2130706431"),
                                            new XAttribute("protocol", "udp"),
                                            new XAttribute("type", "host"));

            return candidateElement;
        }
    }
}
