//-----------------------------------------------------------------------------
// Filename: SDPToJingle.cs
//
// Description: A class that translates back and forth between SDP and Jingle payloads.
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
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SIPSorcery.Sys;
using SIPSorcery.Net;

namespace SIPSorcery.XMPP
{
    public class SDPToJingle
    {
        private static XNamespace m_phoneNS = XMPPStream.GOOGLE_PHONE_SESSION_NAMESPACE;
        private static XNamespace m_sessionNS = XMPPStream.GOOGLE_SESSION_NAMESPACE;
        private static XNamespace m_transportNS = XMPPStream.TRANSPORT_NAMESPACE;

        public static XElement GetDescription(SDP sdp)
        {
            XElement descriptionElement = new XElement(m_phoneNS + "description");

            foreach (SDPMediaFormat mediaFormat in sdp.Media[0].MediaFormats)
            {
                 XElement payloadElement = new XElement(m_phoneNS + "payload-type",
                    new XAttribute("id", mediaFormat.FormatID),
                    new XAttribute("name", mediaFormat.Name));

                 if (mediaFormat.ClockRate != 0)
                 {
                     payloadElement.Add(new XAttribute("clockrate", mediaFormat.ClockRate));
                 }

                descriptionElement.Add(payloadElement);
            }

            return descriptionElement;
        }

        public static XElement GetCandidates(SDP sdp)
        {
            string iceUfrag = (sdp.IceUfrag != null) ? sdp.IceUfrag : String.Empty;
            string icePwd = (sdp.IcePwd != null) ? sdp.IcePwd : String.Empty;

            string sdpIPAddress = sdp.Connection.ConnectionAddress;
            int sdpPort = sdp.Media[0].Port;
            string candidateID = Crypto.GetRandomString(6);

            XElement candidateElement = new XElement(m_sessionNS + "candidate",
                                            new XAttribute("name", "rtp"),
                                            new XAttribute("address", sdpIPAddress),
                                            new XAttribute("port", sdpPort),
                                            new XAttribute("username", iceUfrag),
                                            new XAttribute("password", icePwd),
                                            new XAttribute("preference", "1.0"),
                                            new XAttribute("protocol", "udp"),
                                            new XAttribute("type", "local"),
                                            new XAttribute("network", "0"),
                                            new XAttribute("generation", "0"));

            return candidateElement;
        }

        public static XElement GetCandidate(string ipAddress, int port)
        {
            string candidateID = Crypto.GetRandomString(6);

            XElement candidateElement = new XElement(m_sessionNS + "candidate",
                                            new XAttribute("name", "rtp"),
                                            new XAttribute("address", ipAddress),
                                            new XAttribute("port", port),
                                            new XAttribute("preference", "1.0"),
                                            new XAttribute("protocol", "udp"),
                                            new XAttribute("type", "local"),
                                            new XAttribute("network", "0"),
                                            new XAttribute("generation", "0"));

            return candidateElement;
        }

        public static SDP GetSDP(string ipAddress, int port, string username, string password, List<XElement> payloads)
        {
            string iceUsername = (username.IsNullOrBlank()) ? Crypto.GetRandomString(6) : username;
            string icePwd = (password.IsNullOrBlank()) ? Crypto.GetRandomString(6) : password;

            SDP sdp = new SDP()
            {
                Address = ipAddress,
                Username = "-",
                SessionId = Crypto.GetRandomString(5),
                AnnouncementVersion = Crypto.GetRandomInt(5),
                Connection = new SDPConnectionInformation(ipAddress),
                Timing = "0 0",
                IceUfrag = iceUsername,
                IcePwd = icePwd,
                Media = new List<SDPMediaAnnouncement>()
                {
                    new SDPMediaAnnouncement(port)
                    {
                        //BandwidthAttributes = new List<string>(){"RS:0", "RR:0"} // Indicate that RTCP is not being used.
                    }
                }
            };

            sdp.ExtraAttributes.Add("a=candidate:1 1 UDP " + Crypto.GetRandomString(10) + " " + ipAddress + " " + port + " typ host");
            sdp.ExtraAttributes.Add("a=candidate:1 2 UDP " + Crypto.GetRandomString(10) + " " + ipAddress + " " + (port + 1) + " typ host");

            foreach (XElement payload in payloads)
            {
                int formatID;
                Int32.TryParse(payload.Attribute("id").Value, out formatID);
                string name = payload.Attribute("name").Value;
                int clockRate = 0;
                if (payload.Attribute("clockrate") != null)
                {
                    Int32.TryParse(payload.Attribute("clockrate").Value, out clockRate);
                }

                if(clockRate == 0)
                {
                    sdp.Media[0].MediaFormats.Add(new SDPMediaFormat(formatID, name));
                }
                else
                {
                    sdp.Media[0].MediaFormats.Add(new SDPMediaFormat(formatID, name, clockRate));
                }
            }

            //Console.WriteLine("SDPToJingle SDP=> " + sdp.ToString());

            return sdp;
        }
    }
}
