﻿// ============================================================================
// FileName: SIPEventDialog.cs
//
// Description:
// Represents a child level XML element on a SIP event dialog payload that contains the specifics 
// of a participant in a dialog as described in: 
// RFC4235 "An INVITE-Initiated Dialog Event Package for the Session Initiation Protocol (SIP)".
//
// Author(s):
// Aaron Clauson
//
// History:
// 28 Feb 2010	Aaron Clauson	Created (aaron@sipsorcery.com), SIPSorcery Ltd, London, UK (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.Xml.Linq;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    public class SIPEventDialogParticipant
    {
        private static readonly string m_dialogXMLNS = SIPEventConsts.DIALOG_XML_NAMESPACE_URN;
        private static readonly string m_sipsorceryXMLNS = SIPEventConsts.SIPSORCERY_DIALOG_XML_NAMESPACE_URN;

        public string DisplayName;
        public SIPURI URI;
        public SIPURI TargetURI;
        public int CSeq;
        public string SwitchboardLineName;
        public string CRMPersonName;
        public string CRMCompanyName;
        public string CRMPictureURL;

        private SIPEventDialogParticipant()
        { }

        public SIPEventDialogParticipant(string displayName, SIPURI uri, SIPURI targetURI, int cseq)
        {
            DisplayName = displayName;
            URI = uri;
            TargetURI = targetURI;
            CSeq = cseq;
        }

        public static SIPEventDialogParticipant Parse(string participantXMLStr)
        {
            XElement dialogElement = XElement.Parse(participantXMLStr);
            return Parse(dialogElement);
        }

        public static SIPEventDialogParticipant Parse(XElement participantElement)
        {
            XNamespace ns = m_dialogXMLNS;
            XNamespace ss = m_sipsorceryXMLNS;
            SIPEventDialogParticipant participant = new SIPEventDialogParticipant();

            XElement identityElement = participantElement.Element(ns + "identity");
            if (identityElement != null)
            {
                participant.DisplayName = (identityElement.Attribute("display-name") != null) ? identityElement.Attribute("display-name").Value : null;
                participant.URI = SIPURI.ParseSIPURI(identityElement.Value);
            }

            XElement targetElement = participantElement.Element(ns + "target");
            if (targetElement != null)
            {
                participant.TargetURI = SIPURI.ParseSIPURI(targetElement.Attribute("uri").Value);
            }

            participant.CSeq = (participantElement.Element(ns + "cseq") != null) ? Convert.ToInt32(participantElement.Element(ns + "cseq").Value) : 0;
            //participant.SwitchboardDescription = (participantElement.Element(ss + "switchboarddescription") != null) ? participantElement.Element(ss + "switchboarddescription").Value : null;
            //participant.SwitchboardCallerDescription = (participantElement.Element(ss + "switchboardcallerdescription") != null) ? participantElement.Element(ss + "switchboardcallerdescription").Value : null;
            participant.SwitchboardLineName = (participantElement.Element(ss + "switchboardlinename") != null) ? participantElement.Element(ss + "switchboardlinename").Value : null;
            participant.CRMPersonName = (participantElement.Element(ss + "crmpersonname") != null) ? participantElement.Element(ss + "crmpersonname").Value : null;
            participant.CRMCompanyName = (participantElement.Element(ss + "crmcompanyname") != null) ? participantElement.Element(ss + "crmcompanyname").Value : null;
            participant.CRMPictureURL = (participantElement.Element(ss + "crmpictureurl") != null) ? participantElement.Element(ss + "crmpictureurl").Value : null;

            return participant;
        }

        /// <summary>
        /// Puts the dialog participant information to an XML element.
        /// </summary>
        /// <param name="nodeName">A participant can represent a local or remote party, the node name needs to be set to either "local" or "remote".</param>
        /// <returns>An XML element representing the dialog participant.</returns>
        public XElement ToXML(string nodeName)
        {
            XNamespace ns = m_dialogXMLNS;
            XNamespace ss = m_sipsorceryXMLNS;
            XElement participantElement = new XElement(ns + nodeName);

            if (URI != null)
            {
                XElement identityElement = new XElement(ns + "identity", URI.ToString());
                if (!DisplayName.IsNullOrBlank())
                {
                    identityElement.Add(new XAttribute("display-name", DisplayName));
                }
                participantElement.Add(identityElement);
            }

            if (TargetURI != null)
            {
                XElement targetElement = new XElement(ns + "target", new XAttribute("uri", TargetURI.ToString()));
                participantElement.Add(targetElement);
            }

            if (CSeq > 0)
            {
                XElement cseqElement = new XElement(ns + "cseq", CSeq);
                participantElement.Add(cseqElement);
            }

            if (!SwitchboardLineName.IsNullOrBlank())
            {
                XElement switchLineNameElement = new XElement(ss + "switchboardlinename", SwitchboardLineName);
                participantElement.Add(switchLineNameElement);
            }

            if (!CRMPersonName.IsNullOrBlank())
            {
                XElement crmPersonNameElement = new XElement(ss + "crmpersonname", CRMPersonName);
                participantElement.Add(crmPersonNameElement);
            }

            if (!CRMCompanyName.IsNullOrBlank())
            {
                XElement crmCompanyNameElement = new XElement(ss + "crmcompanyname", CRMCompanyName);
                participantElement.Add(crmCompanyNameElement);
            }

            if (!CRMPictureURL.IsNullOrBlank())
            {
                XElement crmPictureURLElement = new XElement(ss + "crmpictureurl", CRMPictureURL);
                participantElement.Add(crmPictureURLElement);
            }

            return participantElement;
        }
    }
}
