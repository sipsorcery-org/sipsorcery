// ============================================================================
// FileName: SIPEventPackages.cs
//
// Description:
// Data structures and types realted to RFC3265 "Session Initiation Protocol (SIP)-Specific Event Notification".
//
// Author(s):
// Aaron Clauson
//
// History:
// 23 Feb 2010	Aaron Clauson	Created.
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
using System.ComponentModel;
using System.Linq;
using System.Text;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    public class SIPEventConsts
    {
        public const string DIALOG_XML_NAMESPACE_URN = "urn:ietf:params:xml:ns:dialog-info";
        public const string PIDF_XML_NAMESPACE_URN = "urn:ietf:params:xml:ns:pidf";             // Presence Information Data Format XML namespace.
        public const string SIPSORCERY_DIALOG_XML_NAMESPACE_PREFIX = "ss";                      // Used for custom sipsorcery elements in dialog event notification payloads.
        public const string SIPSORCERY_DIALOG_XML_NAMESPACE_URN = "sipsorcery:dialog-info";     // Used for custom sipsorcery elements in dialog event notification payloads.
    }

    public struct SIPEventPackage
    {
        public static SIPEventPackage None = new SIPEventPackage(null);
        public static SIPEventPackage Dialog = new SIPEventPackage("dialog");                   // RFC4235 "An INVITE-Initiated Dialog Event Package for the Session Initiation Protocol (SIP)".
        public static SIPEventPackage MessageSummary = new SIPEventPackage("message-summary");  // RFC3842 "A Message Summary and Message Waiting Indication Event Package for the Session Initiation Protocol (SIP)"
        public static SIPEventPackage Presence = new SIPEventPackage("presence");               // RFC3856.
        public static SIPEventPackage Refer = new SIPEventPackage("refer");                     // RFC3515 "The Session Initiation Protocol (SIP) Refer Method".

        private string m_value;

        private SIPEventPackage(string value)
        {
            m_value = value;
        }

        public static bool IsValid(string value)
        {
            if (value.IsNullOrBlank())
            {
                return false;
            }
            else if (value.ToLower() == "dialog" || value.ToLower() == "message-summary" || 
                value.ToLower() == "presence" || value.ToLower() == "refer")
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static SIPEventPackage Parse(string value)
        {
            if (!IsValid(value))
            {
                throw new ArgumentException("The value is not valid for a SIPEventPackage.");
            }
            else
            {
                string trimmedValue = value.Trim().ToLower();
                switch (trimmedValue)
                {
                    case "dialog": return SIPEventPackage.Dialog;
                    case "message-summary": return SIPEventPackage.MessageSummary;
                    case "presence": return SIPEventPackage.Presence;
                    case "refer": return SIPEventPackage.Refer;
                    default: throw new ArgumentException("The value is not valid for a SIPEventPackage.");
                }
            }
        }

        public override string ToString()
        {
            return m_value;
        }

        public override bool Equals(object obj)
        {
            return AreEqual(this, (SIPEventPackage)obj);
        }

        public static bool AreEqual(SIPEventPackage x, SIPEventPackage y)
        {
            return x == y;
        }

        public static bool operator ==(SIPEventPackage x, SIPEventPackage y)
        {
            return x.m_value == y.m_value;
        }

        public static bool operator !=(SIPEventPackage x, SIPEventPackage y)
        {
            return !(x == y);
        }

        public override int GetHashCode()
        {
            return m_value.GetHashCode();
        }
    }

    public enum SIPEventDialogInfoStateEnum
    {
        none,
        full,
        partial,
    }

    public enum SIPEventDialogDirectionEnum
    {
        none,
        initiator,
        recipient,
    }

    public class SIPEventFilters
    {
        public const string SIP_DIALOG_INCLUDE_SDP = "includesdp=true";
    }

    public struct SIPEventDialogStateEvent 
    {
        public static SIPEventDialogStateEvent None = new SIPEventDialogStateEvent(null);
        public static SIPEventDialogStateEvent Cancelled = new SIPEventDialogStateEvent("cancelled");
        public static SIPEventDialogStateEvent Error = new SIPEventDialogStateEvent("error");
        public static SIPEventDialogStateEvent LocalBye = new SIPEventDialogStateEvent("local-bye");
        public static SIPEventDialogStateEvent Rejected = new SIPEventDialogStateEvent("rejected");
        public static SIPEventDialogStateEvent Replaced = new SIPEventDialogStateEvent("replaced");
        public static SIPEventDialogStateEvent RemoteBye = new SIPEventDialogStateEvent("remote-bye");
        public static SIPEventDialogStateEvent Timeout = new SIPEventDialogStateEvent("timeout");

        private string m_value;

        private SIPEventDialogStateEvent(string value)
        {
            m_value = value;
        }

        public static bool IsValid(string value)
        {
            if (value.IsNullOrBlank())
            {
                return false;
            }
            else if (value.ToLower() == "cancelled" || value.ToLower() == "error" || value.ToLower() == "local-bye" ||
                value.ToLower() == "rejected" || value.ToLower() == "replaced" || value.ToLower() == "remote-bye" ||
                value.ToLower() == "timeout")
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static SIPEventDialogStateEvent Parse(string value)
        {
            if (!IsValid(value))
            {
                throw new ArgumentException("The value is not valid for a SIPEventDialogStateEvent.");
            }
            else
            {
                string trimmedValue = value.Trim().ToLower();
                switch (trimmedValue)
                {
                    case "cancelled": return SIPEventDialogStateEvent.Cancelled;
                    case "error": return SIPEventDialogStateEvent.Error;
                    case "local-bye": return SIPEventDialogStateEvent.LocalBye;
                    case "rejected": return SIPEventDialogStateEvent.Rejected;
                    case "replaced": return SIPEventDialogStateEvent.Replaced;
                    case "remote-bye": return SIPEventDialogStateEvent.RemoteBye;
                    case "timeout": return SIPEventDialogStateEvent.Timeout;
                    default: throw new ArgumentException("The value is not valid for a SIPEventDialogStateEvent.");
                }
            }
        }

        public override string ToString()
        {
            return m_value;
        }

        public override bool Equals(object obj)
        {
            return AreEqual(this, (SIPEventDialogStateEvent)obj);
        }

        public static bool AreEqual(SIPEventDialogStateEvent x, SIPEventDialogStateEvent y)
        {
            return x == y;
        }

        public static bool operator ==(SIPEventDialogStateEvent x, SIPEventDialogStateEvent y)
        {
            return x.m_value == y.m_value;
        }

        public static bool operator !=(SIPEventDialogStateEvent x, SIPEventDialogStateEvent y)
        {
            return !(x == y);
        }

        public override int GetHashCode()
        {
            return m_value.GetHashCode();
        }
    }

    public enum SIPEventPresenceStateEnum
    {
        none,
        closed,
        open,
    }
}
