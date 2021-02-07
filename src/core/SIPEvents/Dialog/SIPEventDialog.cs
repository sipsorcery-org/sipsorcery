// ============================================================================
// FileName: SIPEventDialog.cs
//
// Description:
// Represents a child level XML element on a SIP event dialog payload that contains the specifics 
// of an individual dialog as described in: 
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
using System.Xml.Linq;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    public class SIPEventDialog
    {
        private static readonly string m_dialogXMLNS = SIPEventConsts.DIALOG_XML_NAMESPACE_URN;

        public string ID;                               // The ID is a only mandatory attribute for a dialog element.
        public string CallID;
        public string LocalTag;
        public string RemoteTag;
        public SIPEventDialogDirectionEnum Direction;   // Optional setting indicating whether this dialog was initiated by a sipsorcery user or not.
        public string State;                            // The state a mandatory value for a dialog element.
        public int StateCode;
        public SIPEventDialogStateEvent StateEvent;
        public int Duration;
        public SIPEventDialogParticipant LocalParticipant;
        public SIPEventDialogParticipant RemoteParticipant;
        public string BridgeID;                             // SIPSorcery custom field that is used to show when two dialogues are bridged together by the B2BUA.
        public bool HasBeenSent;                            // Can be used by a subscription manager to indicate the event has been included in a notify request.

        private SIPEventDialog()
        { }

        public SIPEventDialog(string id, string state, SIPDialogue sipDialogue)
        {
            ID = id;
            State = state;

            if (sipDialogue != null)
            {
                CallID = sipDialogue.CallId;
                LocalTag = sipDialogue.LocalTag;
                RemoteTag = sipDialogue.RemoteTag;
                Direction = (sipDialogue.Direction == SIPCallDirection.In) ? SIPEventDialogDirectionEnum.recipient : SIPEventDialogDirectionEnum.initiator;
                Duration = Convert.ToInt32((DateTimeOffset.UtcNow - sipDialogue.Inserted).TotalSeconds % Int32.MaxValue);
                //LocalParticipant = new SIPEventDialogParticipant(sipDialogue.LocalUserField.Name, sipDialogue.LocalUserField.URI, null, sipDialogue.CSeq, sipDialogue.SDP);
                LocalParticipant = new SIPEventDialogParticipant(sipDialogue.LocalUserField.Name, sipDialogue.LocalUserField.URI, null, sipDialogue.CSeq);
                //RemoteParticipant = new SIPEventDialogParticipant(sipDialogue.RemoteUserField.Name, sipDialogue.RemoteUserField.URI, sipDialogue.RemoteTarget, sipDialogue.CSeq, sipDialogue.RemoteSDP);
                RemoteParticipant = new SIPEventDialogParticipant(sipDialogue.RemoteUserField.Name, sipDialogue.RemoteUserField.URI, sipDialogue.RemoteTarget, sipDialogue.CSeq);
                BridgeID = (sipDialogue.BridgeId != Guid.Empty) ? sipDialogue.BridgeId.ToString() : null;
            }
        }

        public SIPEventDialog(string id, string state, int stateCode, SIPEventDialogStateEvent stateEvent, int duration)
        {
            ID = id;
            State = state;
            StateCode = stateCode;
            StateEvent = stateEvent;
            Duration = duration;
        }

        public static SIPEventDialog Parse(string dialogXMLStr)
        {
            XElement dialogElement = XElement.Parse(dialogXMLStr);
            return Parse(dialogElement);
        }

        public static SIPEventDialog Parse(XElement dialogElement)
        {
            XNamespace ns = m_dialogXMLNS;

            SIPEventDialog eventDialog = new SIPEventDialog();
            eventDialog.ID = dialogElement.Attribute("id").Value;
            eventDialog.CallID = (dialogElement.Attribute("call-id") != null) ? dialogElement.Attribute("call-id").Value : null;
            eventDialog.LocalTag = (dialogElement.Attribute("local-tag") != null) ? dialogElement.Attribute("local-tag").Value : null;
            eventDialog.RemoteTag = (dialogElement.Attribute("remote-tag") != null) ? dialogElement.Attribute("remote-tag").Value : null;
            eventDialog.Direction = (dialogElement.Attribute("direction") != null) ? (SIPEventDialogDirectionEnum)Enum.Parse(typeof(SIPEventDialogDirectionEnum), dialogElement.Attribute("direction").Value, true) : SIPEventDialogDirectionEnum.none;

            XElement stateElement = dialogElement.Element(ns + "state");
            eventDialog.State = stateElement.Value;
            eventDialog.StateCode = (stateElement.Attribute("code") != null) ? Convert.ToInt32(stateElement.Attribute("code").Value) : 0;
            eventDialog.StateEvent = (stateElement.Attribute("event") != null) ? SIPEventDialogStateEvent.Parse(stateElement.Attribute("event").Value) : SIPEventDialogStateEvent.None;

            eventDialog.Duration = (dialogElement.Element(ns + "duration") != null) ? Convert.ToInt32(dialogElement.Element(ns + "duration").Value) : 0;

            eventDialog.LocalParticipant = (dialogElement.Element(ns + "local") != null) ? SIPEventDialogParticipant.Parse(dialogElement.Element(ns + "local")) : null;
            eventDialog.RemoteParticipant = (dialogElement.Element(ns + "remote") != null) ? SIPEventDialogParticipant.Parse(dialogElement.Element(ns + "remote")) : null;

            return eventDialog;
        }

        public XElement ToXML()
        {
            XNamespace ns = m_dialogXMLNS;

            XElement eventDialogElement = new XElement(ns + "dialog",
                new XAttribute("id", ID),
                new XElement(ns + "state", State)
                );

            // Add the optional information if available.
            if (!CallID.IsNullOrBlank())
            { eventDialogElement.Add(new XAttribute("call-id", CallID)); }
            if (!LocalTag.IsNullOrBlank())
            { eventDialogElement.Add(new XAttribute("local-tag", LocalTag)); }
            if (!RemoteTag.IsNullOrBlank())
            { eventDialogElement.Add(new XAttribute("remote-tag", RemoteTag)); }
            if (Direction != SIPEventDialogDirectionEnum.none)
            { eventDialogElement.Add(new XAttribute("direction", Direction)); }
            if (StateCode != 0)
            { eventDialogElement.Element(ns + "state").Add(new XAttribute("code", StateCode)); }
            if (StateEvent != SIPEventDialogStateEvent.None)
            { eventDialogElement.Element(ns + "state").Add(new XAttribute("event", StateEvent.ToString())); }
            if (Duration != 0)
            { eventDialogElement.Add(new XElement(ns + "duration", Duration)); }
            if (LocalParticipant != null)
            { eventDialogElement.Add(LocalParticipant.ToXML("local")); }
            if (RemoteParticipant != null)
            { eventDialogElement.Add(RemoteParticipant.ToXML("remote")); }

            return eventDialogElement;
        }
    }
}
