// ============================================================================
// FileName: SIPEventDialog.cs
//
// Description:
// Represents a child level XML element on a SIP event dialog payload that contains the specifics 
// of an individual dialog as described in: 
// RFC4235 "An INVITE-Initiated Dialog Event Package for the Session Initiation Protocol (SIP)".
//
// Author(s):
// Aaron Clauson
//
// History:
// 28 Feb 2010	Aaron Clauson	Created.
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
using System.Linq;
using System.Text;
using System.Xml.Linq;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    public class SIPEventDialog
    {
        private static readonly string m_dialogXMLNS = SIPEventConsts.DIALOG_XML_NAMESPACE_URN;
        private static readonly string m_sipsorceryXMLNS = SIPEventConsts.SIPSORCERY_DIALOG_XML_NAMESPACE_URN;

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
        public string SwitchboardOwner;                     // SIP Sorcery custom field that can be used to specify a sub-account as the owner of the call this dialog belongs to.
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
                SwitchboardOwner = (sipDialogue.SwitchboardOwner != null) ? sipDialogue.SwitchboardOwner : null;

                if (sipDialogue.Direction == SIPCallDirection.In)
                {
                    RemoteParticipant.CRMPersonName = sipDialogue.CRMPersonName;
                    RemoteParticipant.CRMCompanyName = sipDialogue.CRMCompanyName;
                    RemoteParticipant.CRMPictureURL = sipDialogue.CRMPictureURL;
                    LocalParticipant.SwitchboardLineName = sipDialogue.SwitchboardLineName;
                }
                else if (sipDialogue.Direction == SIPCallDirection.Out)
                {
                    LocalParticipant.CRMPersonName = sipDialogue.CRMPersonName;
                    LocalParticipant.CRMCompanyName = sipDialogue.CRMCompanyName;
                    LocalParticipant.CRMPictureURL = sipDialogue.CRMPictureURL;
                    RemoteParticipant.SwitchboardLineName = sipDialogue.SwitchboardLineName;
                }
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
            XNamespace ss = m_sipsorceryXMLNS;

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
            eventDialog.BridgeID = (dialogElement.Element(ss + "bridgeid") != null) ? dialogElement.Element(ss + "bridgeid").Value : null;
            eventDialog.SwitchboardOwner = (dialogElement.Element(ss + "switchboardowner") != null) ? dialogElement.Element(ss + "switchboardowner").Value : null;

            eventDialog.LocalParticipant = (dialogElement.Element(ns + "local") != null) ? SIPEventDialogParticipant.Parse(dialogElement.Element(ns + "local")) : null;
            eventDialog.RemoteParticipant = (dialogElement.Element(ns + "remote") != null) ? SIPEventDialogParticipant.Parse(dialogElement.Element(ns + "remote")) : null;

            return eventDialog;
        }

        public XElement ToXML()
        {
            XNamespace ns = m_dialogXMLNS;
            XNamespace ss = m_sipsorceryXMLNS;

            XElement eventDialogElement = new XElement(ns + "dialog",
                new XAttribute("id", ID),
                new XElement(ns + "state", State)
                );

            // Add the optional information if available.
            if (!CallID.IsNullOrBlank()) { eventDialogElement.Add(new XAttribute("call-id", CallID)); }
            if (!LocalTag.IsNullOrBlank()) { eventDialogElement.Add(new XAttribute("local-tag", LocalTag)); }
            if (!RemoteTag.IsNullOrBlank()) { eventDialogElement.Add(new XAttribute("remote-tag", RemoteTag)); }
            if (Direction != SIPEventDialogDirectionEnum.none) { eventDialogElement.Add(new XAttribute("direction", Direction)); }
            if (StateCode != 0) { eventDialogElement.Element(ns + "state").Add(new XAttribute("code", StateCode)); }
            if (StateEvent != SIPEventDialogStateEvent.None) { eventDialogElement.Element(ns + "state").Add(new XAttribute("event", StateEvent.ToString())); }
            if (Duration != 0) { eventDialogElement.Add(new XElement(ns + "duration", Duration)); }
            if (BridgeID != null) { eventDialogElement.Add(new XElement(ss + "bridgeid", BridgeID)); }
            if (SwitchboardOwner != null) { eventDialogElement.Add(new XElement(ss + "switchboardowner", SwitchboardOwner)); }
            //if (LocalParticipant != null) { eventDialogElement.Add(LocalParticipant.ToXML("local", filter)); }
            if (LocalParticipant != null) { eventDialogElement.Add(LocalParticipant.ToXML("local")); }
            //if (RemoteParticipant != null) { eventDialogElement.Add(RemoteParticipant.ToXML("remote", filter)); }
            if (RemoteParticipant != null) { eventDialogElement.Add(RemoteParticipant.ToXML("remote")); }

            return eventDialogElement;
        }
    }
}
