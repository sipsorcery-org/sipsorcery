using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP
{
    public class SIPEventPresenceTuple
    {
        private static readonly string m_pidfXMLNS = SIPEventConsts.PIDF_XML_NAMESPACE_URN;

        public string ID;
        public SIPEventPresenceStateEnum Status;
        public SIPURI ContactURI;
        public decimal ContactPriority = Decimal.Zero;
        public string AvatarURL;

        private SIPEventPresenceTuple()
        {}

        public SIPEventPresenceTuple(string id, SIPEventPresenceStateEnum status)
        {
            ID = id;
            Status = status;
        }

        public SIPEventPresenceTuple(string id, SIPEventPresenceStateEnum status, SIPURI contactURI, decimal contactPriority, string avatarURL)
        {
            ID = id;
            Status = status;
            ContactURI = contactURI;
            ContactPriority = contactPriority;
            AvatarURL = avatarURL;
        }

        public static SIPEventPresenceTuple Parse(string tupleXMLStr)
        {
            XElement tupleElement = XElement.Parse(tupleXMLStr);
            return Parse(tupleElement);
        }

        public static SIPEventPresenceTuple Parse(XElement tupleElement)
        {
            XNamespace ns = m_pidfXMLNS;

            SIPEventPresenceTuple tuple = new SIPEventPresenceTuple();
            tuple.ID = tupleElement.Attribute("id").Value;
            tuple.Status = (SIPEventPresenceStateEnum)Enum.Parse(typeof(SIPEventPresenceStateEnum), tupleElement.Element(ns + "status").Element(ns + "basic").Value, true);
            tuple.ContactURI = (tupleElement.Element(ns + "contact") != null) ? SIPURI.ParseSIPURI(tupleElement.Element(ns + "contact").Value) : null;
            tuple.ContactPriority = (tuple.ContactURI != null && tupleElement.Element(ns + "contact").Attribute("priority") != null) ? Decimal.Parse(tupleElement.Element(ns + "contact").Attribute("priority").Value) : Decimal.Zero;
            tuple.AvatarURL = (tuple.ContactURI != null && tupleElement.Element(ns + "contact").Attribute("avatarurl") != null) ? tupleElement.Element(ns + "contact").Attribute("avatarurl").Value : null;

            return tuple;
        }

        public XElement ToXML()
        {
            XNamespace ns = m_pidfXMLNS;

            XElement tupleElement = new XElement(ns + "tuple",
                new XAttribute("id", ID),
                new XElement(ns + "status", 
                    new XElement(ns + "basic", Status.ToString()))
                );

            if (ContactURI != null)
            {
                XElement contactElement = new XElement(ns + "contact", ContactURI.ToString());
                if (ContactPriority != Decimal.Zero)
                {
                    contactElement.Add(new XAttribute("priority", ContactPriority.ToString("0.###")));
                }
                if (AvatarURL != null)
                {
                    contactElement.Add(new XAttribute("avatarurl", AvatarURL));
                }
                tupleElement.Add(contactElement);
            }

            return tupleElement;
        }
    }
}
