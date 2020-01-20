// ============================================================================
// FileName: SIPEventPackages.cs
//
// Description:
// Data structures and types related to RFC3265 "Session Initiation Protocol (SIP)-Specific Event Notification".
//
// Author(s):
// Aaron Clauson
//
// History:
// 23 Feb 2010	Aaron Clauson	Created (aaron@sipsorcery.com), SIPSorcery Ltd, London, UK (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
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
                    case "dialog":
                        return SIPEventPackage.Dialog;
                    case "message-summary":
                        return SIPEventPackage.MessageSummary;
                    case "presence":
                        return SIPEventPackage.Presence;
                    case "refer":
                        return SIPEventPackage.Refer;
                    default:
                        throw new ArgumentException("The value is not valid for a SIPEventPackage.");
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
                    case "cancelled":
                        return SIPEventDialogStateEvent.Cancelled;
                    case "error":
                        return SIPEventDialogStateEvent.Error;
                    case "local-bye":
                        return SIPEventDialogStateEvent.LocalBye;
                    case "rejected":
                        return SIPEventDialogStateEvent.Rejected;
                    case "replaced":
                        return SIPEventDialogStateEvent.Replaced;
                    case "remote-bye":
                        return SIPEventDialogStateEvent.RemoteBye;
                    case "timeout":
                        return SIPEventDialogStateEvent.Timeout;
                    default:
                        throw new ArgumentException("The value is not valid for a SIPEventDialogStateEvent.");
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
