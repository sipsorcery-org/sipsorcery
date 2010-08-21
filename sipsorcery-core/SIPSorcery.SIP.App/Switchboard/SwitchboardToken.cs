// ============================================================================
// FileName: SwitchboardToken.cs
//
// Description:
// Represents a security token that is utilised by the switchboard client application to
// authorise operations on different sipsorcery servers.
//
// Author(s):
// Aaron Clauson
//
// History:
// 09 Jun 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Ltd, Hobart, Australia (www.sipsorcery.com)
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
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
    public class SwitchboardToken
    {
        private const int MAX_SWITCHBOARD_TOKEN_EXPIRY = 3600;
        private const int TOKEN_ID_STRING_LENGTH = 96;  // 384 bits of entropy.

        private static string m_newLine = AppState.NewLine;

        public DateTimeOffset Created;
        public int Expiry;                  // Time in seconds the token is valid for.
        public string SessionID;
        public string Identity;
        public string IPAddress;
        public string SignedHash;

        private SwitchboardToken()
        { }

        public SwitchboardToken(int requestedExpiry, string identity, string ipAddress)
        {
            Created = DateTimeOffset.UtcNow;
            Identity = identity;
            IPAddress = ipAddress;
            Expiry = (requestedExpiry < MAX_SWITCHBOARD_TOKEN_EXPIRY) ? requestedExpiry : MAX_SWITCHBOARD_TOKEN_EXPIRY;
            SessionID = Crypto.GetRandomByteString(TOKEN_ID_STRING_LENGTH / 2);
        }

        public static SwitchboardToken ParseSwitchboardToken(string tokenStr)
        {
            SwitchboardToken token = new SwitchboardToken();

            XElement tokenElement = XElement.Parse(tokenStr);
            token.Created = DateTimeOffset.Parse(tokenElement.Element("created").Value);
            token.Expiry = Int32.Parse(tokenElement.Element("expiry").Value);
            token.Identity = tokenElement.Element("identity").Value;
            token.IPAddress = tokenElement.Element("ipaddress").Value;
            token.SessionID = tokenElement.Element("sessionid").Value;
            token.SignedHash = tokenElement.Element("signedhash").Value;

            return token;
        }

        public string ToXML(bool includeSignedHash)
        {
            string tokenXML =
            "<switchboardtoken>" + m_newLine +
            "  <created>" + Created.ToString("o") + "</created>" + m_newLine +
            "  <expiry>" + Expiry + "</expiry>" + m_newLine +
            "  <identity>" + Identity + "</identity>" + m_newLine +
            "  <ipaddress>" + IPAddress + "</ipaddress>" + m_newLine +
            "  <sessionid>" + SessionID + "</sessionid>" + m_newLine;

            if (includeSignedHash)
            {
                tokenXML += "  <signedhash>" + SignedHash + "</signedhash>" + m_newLine;
            }

            tokenXML += "</switchboardtoken>";

            return tokenXML;
        }

        public string GetHashString()
        {
            return Created.ToString("o") + ":" + Expiry + ":" + Identity + ":" + IPAddress;
        }
    }
}
