// ============================================================================
// FileName: SIPEventPresence.cs
//
// Description:
// Represents the top level XML element on a SIP event presence payload as described in: 
// RFC3856 "A Presence Event Package for the Session Initiation Protocol (SIP)".
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 23 Mar 2010	Aaron Clauson	Created, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    public class SIPEventPresence
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

        public void Load(string presenceXMLStr)
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

        public string ToXMLText()
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
