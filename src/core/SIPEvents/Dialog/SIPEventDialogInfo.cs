// ============================================================================
// FileName: SIPEventDialogInfo.cs
//
// Description:
// Represents the top level XML element on a SIP event dialog payload as described in: 
// RFC4235 "An INVITE-Initiated Dialog Event Package for the Session Initiation Protocol (SIP)".
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 28 Feb 2010	Aaron Clauson	Created, Hobart, Australia.
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
    /// <summary>
    /// Represents the top level XML element on a SIP event dialog payload as described in: 
    /// RFC4235 "An INVITE-Initiated Dialog Event Package for the Session Initiation Protocol (SIP)".
    /// </summary>
    /// <remarks>
    /// RFC4235 on Dialog Event Packages:
    ///  - To establish a subscription to a specific dialog the call-id, to-tag and from-tag must be specified,
    ///  - To establish a subscription to a set of dialogs the call-id and to-tag must be specified.
    ///  Treatment of the Event header:
    ///   - If the Event header contains dialog identifiers a notification is sent for any dialogs that match them AND the user in the SUBSCRIBE URI.
    ///   - If the Event header does not contain any dialog identifiers then a notification is sent for every dialog that matches the user in the SUBSCRIBE URI.
    /// - Notifications contain the identities of the dialog participants, the target URIs and the dialog identifiers.
    /// - The format of the NOTIFY bodies must be in a format specified in a SUBSCRIBE Accept header or if omitted a default format of "application/dialog-info+xml".
    /// 
    /// Example of an empty dialog notification body:
    /// 
    /// <code>
    /// <![CDATA[
    ///  <?xml version="1.0"?>
    ///  <dialog-info xmlns="urn:ietf:params:xml:ns:dialog-info" version="0" notify-state="full" entity="sip:alice@example.com" />
    ///  ]]>
    /// </code>
    /// 
    /// Example of a single entry dialog notification body:
    /// 
    /// <code>
    /// <![CDATA[
    /// <?xml version="1.0"?>
    /// <dialog-info xmlns="urn:ietf:params:xml:ns:dialog-info" version="0" state="partial" entity="sip:alice@example.com">
    ///   <dialog id="as7d900as8" call-id="a84b4c76e66710" local-tag="1928301774" direction="initiator">
    ///    <state event="rejected" code="486">terminated</state> <!-- The state element is the only mandatory child element for a dialog element. -->
    ///    <duration>145</duration>
    ///   </dialog>
    ///  </dialog-info>
    ///  ]]>
    /// </code>
    /// </remarks>
    public class SIPEventDialogInfo
    {
        private static ILogger logger = Log.Logger;

        public static readonly string m_dialogXMLNS = SIPEventConsts.DIALOG_XML_NAMESPACE_URN;

        public int Version;
        public SIPEventDialogInfoStateEnum State;
        public SIPURI Entity;
        public List<SIPEventDialog> DialogItems = new List<SIPEventDialog>();

        public SIPEventDialogInfo()
        { }

        public SIPEventDialogInfo(int version, SIPEventDialogInfoStateEnum state, SIPURI entity)
        {
            Version = version;
            State = state;
            Entity = entity.CopyOf();
        }

        public void Load(string dialogInfoXMLStr)
        {
            try
            {
                XNamespace ns = m_dialogXMLNS;
                XDocument eventDialogDoc = XDocument.Parse(dialogInfoXMLStr);

                Version = Convert.ToInt32(((XElement)eventDialogDoc.FirstNode).Attribute("version").Value);
                State = (SIPEventDialogInfoStateEnum)Enum.Parse(typeof(SIPEventDialogInfoStateEnum), ((XElement)eventDialogDoc.FirstNode).Attribute("state").Value, true);
                Entity = SIPURI.ParseSIPURI(((XElement)eventDialogDoc.FirstNode).Attribute("entity").Value);

                var dialogElements = eventDialogDoc.Root.Elements(ns + "dialog");
                foreach (XElement dialogElement in dialogElements)
                {
                    DialogItems.Add(SIPEventDialog.Parse(dialogElement));
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPEventDialogInfo constructor. " + excp.Message);
                throw;
            }
        }

        public static SIPEventDialogInfo Parse(string dialogInfoXMLStr)
        {
            SIPEventDialogInfo eventDialogInfo = new SIPEventDialogInfo();
            eventDialogInfo.Load(dialogInfoXMLStr);
            return eventDialogInfo;
        }

        public string ToXMLText()
        {
            XNamespace ns = m_dialogXMLNS;
            XDocument dialogEventDoc = new XDocument(new XElement(ns + "dialog-info",
                new XAttribute("version", Version),
                new XAttribute("state", State),
                new XAttribute("entity", Entity.ToString())
                ));

            DialogItems.ForEach((item) =>
            {
                XElement dialogItemElement = item.ToXML();
                dialogEventDoc.Root.Add(dialogItemElement);
                item.HasBeenSent = true;
            });

            StringBuilder sb = new StringBuilder();
            XmlWriterSettings xws = new XmlWriterSettings();
            xws.NewLineHandling = NewLineHandling.None;
            xws.Indent = true;

            using (XmlWriter xw = XmlWriter.Create(sb, xws))
            {
                dialogEventDoc.WriteTo(xw);
            }

            return sb.ToString();
        }
    }
}
