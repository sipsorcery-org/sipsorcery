//-----------------------------------------------------------------------------
// Filename: XMPPAuthenticatedStream.cs
//
// Description: Represents the XMPP stream after the client has successfully authenticated
// with the XMPP server.
// 
// History:
// 13 Dec 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), Hobart, Tasmania, Australia (www.sipsorcery.com)
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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.XMPP
{
    public class XMPPAuthenticatedStream : XMPPStream
    {
        private static ILog logger = AppState.logger;

        private static XNamespace m_bindNS = BIND_NAMESPACE;
        private static XNamespace m_sessionNS = GOOGLE_SESSION_NAMESPACE;
        private static XNamespace m_phoneNS = GOOGLE_PHONE_SESSION_NAMESPACE;
        private static XNamespace m_transportNS = TRANSPORT_NAMESPACE;

        private bool m_isBound;

        public Action IsBound;
        private Dictionary<string, Action<XElement>> m_sessions = new Dictionary<string, Action<XElement>>();            // [id, Action(iq element)]
        private Dictionary<string, Action<string, XElement>> m_iqs = new Dictionary<string, Action<string, XElement>>(); // [id, Action(id, iq element)]

        public XMPPAuthenticatedStream(Stream stream) :
            base(stream)
        {
            IsTLS = true;
            IsAuthenticated = true;
            base.ElementReceived += Receive;
        }

        public void RegisterSession(string session, Action<XElement> handler)
        {
            m_sessions.Add(session, handler);
        }

        public void WriteElement(XElement element, Action<string, XElement> handler)
        {
            m_iqs.Add(element.Attribute("id").Value, handler);
            base.WriteElement(element);
        }

        public void WriteNonIQElement(XElement element)
        {
            base.WriteElement(element);
        }

        private void Receive(XElement element)
        {
            //logger.Debug("XMPPAuthenticatedStream ElementReceived " + element.Name + ".");

            switch (element.Name.LocalName)
            {
                case "features":
                    if (Features != null && (from feature in Features where feature.Name == BIND_ELEMENT_NAME select feature).Count() > 0)
                    {
                        // Bind.
                        //Console.WriteLine("Binding is required.");
                        string bindingID = Crypto.GetRandomString(6);
                        XElement bindElement = new XElement(JabberClientNS + IQ_ELEMENT_NAME,
                            new XAttribute("id", bindingID),
                            new XAttribute("type", "set"),
                            new XElement(m_bindNS + BIND_ELEMENT_NAME));
                        //Console.WriteLine(bindElement);
                        WriteElement(bindElement);
                    }
                    break;
                case "iq":
                    if(!m_isBound)
                    {
                        IQBinding(element);
                    }
                    else
                    {
                        string iqID = element.Attribute("id").Value;
                        if (m_iqs.ContainsKey(iqID))
                        {
                            m_iqs[iqID](iqID, element);
                            m_iqs.Remove(iqID);
                        }
                        else
                        {
                            string sessionID = element.Element(m_sessionNS + "session").Attribute("id").Value;
                            if (m_sessions.ContainsKey(sessionID))
                            {
                                m_sessions[sessionID](element);
                            }
                            else
                            {
                                logger.Warn("XMPPAuthenticatedStream session id with " + sessionID + " was not matched.");
                            }
                        }
                    }
                    break;
                default:
                    logger.Warn("XMPPAuthenticatedStream node " + element.Name.LocalName + " was not recognised.");
                    break;
            }
        }

        private void IQBinding(XElement element)
        {
            m_isBound = true;
            JID = element.Element(m_bindNS + "bind").Element(m_bindNS + "jid").Value;
            logger.Debug("XMPPAuthenticatedStream JID=" + JID);
            IsBound();
        }
    }
}
