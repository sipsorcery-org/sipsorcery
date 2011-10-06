//-----------------------------------------------------------------------------
// Filename: XMPPConstants.cs
//
// Description: Constants and namespaces used for and within XMPP transmissions.
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

namespace SIPSorcery.XMPP
{
    public class XMPPConstants
    {
        public const string JABBER_NAMESPACE = "jabber:client";
        public const string JINGLE_NAMESPACE = "urn:xmpp:jingle:1";
        public const string JINGLE_DESCRIPTION_NAMESPACE = "urn:xmpp:jingle:apps:rtp:1";
        public const string JINGLE_ICE_TRANSPORT_NAMESPACE = "urn:xmpp:jingle:transports:ice-udp:1";
        public const string JINGLE_RTP_TRANSPORT_NAMESPACE = "urn:xmpp:jingle:apps:rtp:1";
        public const string GINGLE_TRANSPORT_NAMESPACE = "http://www.google.com/transport/p2p";
        public const string DISCOVERY_NAMESPACE = "http://jabber.org/protocol/disco#info";
        public const string GOOGLE_JINGLE_INFO_NAMEPSACE = "google:jingleinfo";
        public const string ROSTER_NAMEPSACE = "jabber:iq:roster";
        public const string CAPS_NAMESPACE = "http://jabber.org/protocol/caps";
        public const string GINGLE_SESSION_NAMESPACE = "http://www.google.com/session";
        public const string GINGLE_PHONE_NAMESPACE = "http://www.google.com/session/phone";

        public enum XMPPRequestTypes
        {
            iq,
            message,
            presence,
        }

        public enum XMPPRequestSubTypes
        {
            jingle,
            query,
        }
    }
}
