// ============================================================================
// FileName: SIPEventPresence.cs
//
// Description:
// Represents the top level XML element on a SIP event presence payload as described in: 
// RFC3856 "A Presence Event Package for the Session Initiation Protocol (SIP)".
//
// Author(s):
// Aaron Clauson
//
// History:
// 23 Mar 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), SIPSorcery Ltd, London, UK (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
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
using System.Text;
using System.Xml;
using System.Xml.Linq;
using SIPSorcery.Sys;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    public class SIPEventPresence : SIPEvent
    {
        private static ILogger logger = Log.Logger;

        public static readonly string m_pidfXMLNS = SIPEventConsts.PIDF_XML_NAMESPACE_URN;

        public SIPURI Entity;
        public List<SIPEventPresenceTuple> Tuples = new List<SIPEventPresenceTuple>();

        public SIPEventPresence()
        { }

        public SIPEventPresence(SIPURI entity)
        {
            Entity = entity.CopyOf();
        }

        public override void Load(string presenceXMLStr)
        {
            try
            {
                XNamespace ns = m_pidfXMLNS;
                XDocument presenceDoc = XDocument.Parse(presenceXMLStr);

                Entity = SIPURI.ParseSIPURI(((XElement)presenceDoc.FirstNode).Attribute("entity").Value);

                var tupleElements = presenceDoc.Root.Elements(ns + "tuple");
                foreach (XElement tupleElement in tupleElements)
                {
                    Tuples.Add(SIPEventPresenceTuple.Parse(tupleElement));
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPEventPresence Load. " + excp.Message);
                throw;
            }
        }

        public static SIPEventPresence Parse(string presenceXMLStr)
        {
           SIPEventPresence presenceEvent = new SIPEventPresence();
           presenceEvent.Load(presenceXMLStr);
           return presenceEvent;
        }

        public override string ToXMLText()
        {
            XNamespace ns = m_pidfXMLNS;
            
            XDocument presenceDoc = new XDocument(new XElement(ns + "presence",
                new XAttribute("entity", Entity.ToString())));

            Tuples.ForEach((item) =>
            {
                XElement tupleElement = item.ToXML();
                presenceDoc.Root.Add(tupleElement);
            });

            StringBuilder sb = new StringBuilder();
            XmlWriterSettings xws = new XmlWriterSettings();
            xws.NewLineHandling = NewLineHandling.None;
            xws.Indent = true;

            using (XmlWriter xw = XmlWriter.Create(sb, xws))
            {
                presenceDoc.WriteTo(xw);
            }

            return sb.ToString();
        }
    }
}
