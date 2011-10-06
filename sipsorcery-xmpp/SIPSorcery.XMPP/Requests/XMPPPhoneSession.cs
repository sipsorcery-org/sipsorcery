//-----------------------------------------------------------------------------
// Filename: XMPPPhoneSession.cs
//
// Description: Represents the an XMPP session that operates on top of an authenticated
// XMPP stream and that negotiates a voice call.
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
using System.Linq;
using System.Xml.Linq;
using System.Text;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.XMPP
{
    public class XMPPPhoneSession
    {
        private static ILog logger = AppState.logger;

        private static XNamespace m_sessionNS = XMPPStream.GOOGLE_SESSION_NAMESPACE;
        private static XNamespace m_phoneNS = XMPPStream.GOOGLE_PHONE_SESSION_NAMESPACE;
        private static XNamespace m_transportNS = XMPPStream.TRANSPORT_NAMESPACE;
        protected static XNamespace JabberClientNS = XMPPConstants.JABBER_NAMESPACE;

        private string m_destination;
        private SDP m_sdp;
        private string m_jid;
        private XMPPAuthenticatedStream m_xmppStream;
        private string m_sessionID;

        // IQ id's.
        private string m_descriptionID;

        // Call response.
        private string m_remoteIPAddress;
        private int m_remotePort;
        private string m_remoteUsername;
        private string m_remotePassword;
        private List<XElement> m_payloads;

        public Action<SDP> Accepted;
        public Action Rejected;
        public Action Hungup;

        public XMPPPhoneSession(string jid, XMPPAuthenticatedStream xmppStream)
        {
            m_sessionID = Crypto.GetRandomString(6);
            m_jid = jid;
            m_xmppStream = xmppStream;
            m_xmppStream.RegisterSession(m_sessionID, OnIQRequest);
        }

        public void PlaceCall(string destination, SDP sdp)
        {
            m_destination = destination;
            m_sdp = sdp;

            XElement descriptionElement = SDPToJingle.GetDescription(m_sdp);

            m_descriptionID = Crypto.GetRandomString(6);

            XElement callElement = new XElement(JabberClientNS + "iq",
                                    new XAttribute("from", m_jid),
                                    new XAttribute("id", m_descriptionID),
                                    new XAttribute("to", m_destination),
                                    new XAttribute("type", "set"),
                                    new XElement(m_sessionNS + "session",
                                        new XAttribute("type", "initiate"),
                                        new XAttribute("id", m_sessionID),
                                        new XAttribute("initiator", m_jid),
                                        descriptionElement
                                        //new XElement(m_transportNS + "transport")
                                        ));

            logger.Debug("XMPPPhoneSession sending iq with session description.");
            m_xmppStream.WriteElement(callElement, OnIQResponse);
        }

        private void SendCandidates()
        {
            XElement candidatesElement = SDPToJingle.GetCandidates(m_sdp);

            string candidateIqID = Crypto.GetRandomString(6);

            XElement candElement = new XElement(JabberClientNS + "iq",
                                    new XAttribute("from", m_jid),
                                    new XAttribute("id", candidateIqID),
                                    new XAttribute("to", m_destination),
                                    new XAttribute("type", "set"),
                                    new XElement(m_sessionNS + "session",
                                        new XAttribute("type", "candidates"),
                                        new XAttribute("id", m_sessionID),
                                        new XAttribute("initiator", m_jid),
                                        candidatesElement));

            logger.Debug("XMPPPhoneSession sending iq with session candidate.");

            m_xmppStream.WriteElement(candElement, OnIQResponse);
        }

        public void RetargetCallMedia(string ipAddress, int port)
        {
            XElement candidateElement = SDPToJingle.GetCandidate(ipAddress, port);

            string candidateIqID = Crypto.GetRandomString(6);

            XElement candElement = new XElement(JabberClientNS + "iq",
                                    new XAttribute("from", m_jid),
                                    new XAttribute("id", candidateIqID),
                                    new XAttribute("to", m_destination),
                                    new XAttribute("type", "set"),
                                    new XElement(m_sessionNS + "session",
                                        new XAttribute("type", "candidates"),
                                        new XAttribute("id", m_sessionID),
                                        new XAttribute("initiator", m_jid),
                                        candidateElement));

            m_xmppStream.WriteElement(candElement, OnIQResponse);
        }

        public void TerminateCall()
        {
            string iqID = Crypto.GetRandomString(6);

            XElement cancelElement = new XElement(JabberClientNS + "iq",
                                    new XAttribute("from", m_jid),
                                    new XAttribute("id", iqID),
                                    new XAttribute("to", m_destination),
                                    new XAttribute("type", "set"),
                                    new XElement(m_sessionNS + "session",
                                        new XAttribute("type", "terminate"),
                                        new XAttribute("id", m_sessionID),
                                        new XAttribute("initiator", m_jid)));

            logger.Debug("XMPPPhoneSession sending iq to terminate session.");

            m_xmppStream.WriteElement(cancelElement, OnIQResponse);
        }

        /// <summary>
        /// Handler method for receiving responses to iq stanzas initiated by us.
        /// </summary>
        /// <param name="id">The id of the original iq request.</param>
        /// <param name="iq">The iq response stanza.</param>
        public void OnIQResponse(string id, XElement iq)
        {
            //Console.WriteLine("IQ received result for ID " + id + " => " + iq.ToString());
            // Send ok.
            //XAttribute sessionType = iq.Element(m_sessionNS + "session").Attribute("type");
            XAttribute iqType = iq.Attribute("type");

            logger.Debug("XMPPPhoneSession iq response, type=" + iqType.Value + ".");

            if (iqType.Value == "result")
            {
                string iqID = iq.Attribute("id").Value;

                if (iqID == m_descriptionID)
                {
                    SendCandidates();
                }
            }
            else if (iqType.Value == "error")
            {
                int errorCode = 0;
                Int32.TryParse(iq.Element(JabberClientNS + "error").Attribute("code").Value, out errorCode);

                if (errorCode >= 300 && errorCode <= 399)
                {
                    // Redirect.
                    string redirect = iq.Element(JabberClientNS + "error").Element(m_sessionNS + "redirect").Value;
                    m_destination = redirect.Replace("xmpp:", String.Empty);
                    PlaceCall(m_destination, m_sdp);
                }
                else
                {
                    Rejected();
                }
            }
        }

        /// <summary>
        /// Handler method for receiving iq requests from the remote server.
        /// </summary>
        /// <param name="iq">The iq stanza request received.</param>
        public void OnIQRequest(XElement iq)
        {
            logger.Debug("XMPPPhoneSession iq request, type=" + iq.Attribute("type").Value + ".");

            if (iq.Element(m_sessionNS + "session") != null)
            {
                XAttribute sessionType = iq.Element(m_sessionNS + "session").Attribute("type");

                if (sessionType != null)
                {
                    if (sessionType.Value == "candidates")
                    {
                        logger.Debug("XMPPPhoneSession session candidates stanza was received.");

                        XElement candidate = (from cand in iq.Descendants(m_sessionNS + "session").Descendants(m_sessionNS + "candidate")
                                              where cand.Attribute("protocol").Value == "udp"
                                              select cand).First();

                        m_remoteIPAddress = candidate.Attribute("address").Value;
                        Int32.TryParse(candidate.Attribute("port").Value, out m_remotePort);
                        m_remoteUsername = candidate.Attribute("username").Value;
                        m_remotePassword = candidate.Attribute("password").Value;
                    }
                    else if (sessionType.Value == "accept")
                    {
                        logger.Debug("XMPPPhoneSession session accept stanza was received.");

                        m_payloads = (from pl in iq.Descendants(m_sessionNS + "session").Descendants(m_phoneNS + "description")
                                          .Descendants(m_phoneNS + "payload-type")
                                      select pl).ToList();

                        Accepted(SDPToJingle.GetSDP(m_remoteIPAddress, m_remotePort, m_remoteUsername, m_remotePassword, m_payloads));
                    }
                    else if (sessionType.Value == "terminate")
                    {
                        logger.Debug("XMPPPhoneSession session terminate stanza was received.");

                        Hungup();
                    }
                }
                else
                {
                    logger.Warn("XMPPPhoneSession session element was received with a missing type attribute.");
                    logger.Warn(iq.ToString());
                }
            }
            else
            {
                logger.Warn("XMPPPhoneSession an iq element was received with no session element.");
                logger.Warn(iq.ToString());
            }
        }
    }
}
