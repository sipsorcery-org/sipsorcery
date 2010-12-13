//-----------------------------------------------------------------------------
// Filename: XMPPInitialStream.cs
//
// Description: Represents the XMPP stream resulting from the initial TCP connection
// to the XMPP server.
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
    public class XMPPInitialStream : XMPPStream
    {
        private const string STARTTLS_ELEMENT_NAME = "starttls";
        public const string TLS_NAMESPACE = "urn:ietf:params:xml:ns:xmpp-tls";

        private static ILog logger = AppState.logger;

        private static XNamespace m_tlsNS = TLS_NAMESPACE;

        public XMPPInitialStream(Stream stream) :
            base(stream)
        {
            base.ElementReceived += Receive;
        }

        private void Receive(XElement element)
        {
            //Console.WriteLine("XMPPInitialStream ElementReceived " + element.Name + ".");

            switch (element.Name.LocalName)
            {
                case "features":
                    if (Features != null && (from feature in Features where feature.Name == STARTTLS_ELEMENT_NAME select feature).Count() > 0)
                    {
                        // Request TLS.
                        logger.Debug("XMPPInitialStream TLS is required.");
                        WriteElement(new XElement(m_tlsNS + STARTTLS_ELEMENT_NAME));
                    }
                    break;
                case "proceed":
                    // TLS approved. Close this stream and negotiate TLS.
                    Exit = true;
                    break;
                default:
                    logger.Warn("XMPPInitialStream Node " + element.Name.LocalName + " was not recognised.");
                    break;
            }
        }
    }
}
