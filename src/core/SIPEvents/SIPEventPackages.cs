// ============================================================================
// FileName: SIPEventPackages.cs
//
// Description:
// Data structures and types related to RFC3265 "Session Initiation Protocol
// (SIP)-Specific Event Notification".
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 23 Feb 2010	Aaron Clauson	Created, Hobart, Australia.
// 01 Feb 2021  Aaron Clauson   Simplified parsing of the event package type.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    public static class SIPEventConsts
    {
        public const string DIALOG_XML_NAMESPACE_URN = "urn:ietf:params:xml:ns:dialog-info";
        public const string PIDF_XML_NAMESPACE_URN = "urn:ietf:params:xml:ns:pidf";             // Presence Information Data Format XML namespace.
    }

    public enum SIPEventPackagesEnum
    {
        None,

        /// <summary>
        /// RFC4235 "An INVITE-Initiated Dialog Event Package for the Session Initiation Protocol (SIP)".
        /// </summary>
        Dialog,

        /// <summary>
        /// RFC3842 "A Message Summary and Message Waiting Indication Event Package for the Session
        /// Initiation Protocol (SIP)"
        /// </summary>
        MessageSummary,

        /// <summary>
        /// RFC3856.
        /// </summary>
        Presence,

        /// <summary>
        /// RFC3515 "The Session Initiation Protocol (SIP) Refer Method".
        /// </summary>
        Refer
    }

    public static class SIPEventPackageType
    {
        public const string DIALOG_EVENT_VALUE = "dialog";
        public const string MESSAGE_SUMMARY_EVENT_VALUE = "message-summary";
        public const string PRESENCE_EVENT_VALUE = "presence";
        public const string REFER_EVENT_VALUE = "refer";

        public static bool IsValid(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            else {
                value = value.Trim();

                return 
                    string.Equals(value, DIALOG_EVENT_VALUE, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, MESSAGE_SUMMARY_EVENT_VALUE, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, PRESENCE_EVENT_VALUE, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, REFER_EVENT_VALUE, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static SIPEventPackagesEnum Parse(string value)
        {
            if (!IsValid(value))
            {
                return SIPEventPackagesEnum.None;
            }
            else
            {
                value = value.Trim().ToLower();
                switch (value)
                {
                    case DIALOG_EVENT_VALUE:
                        return SIPEventPackagesEnum.Dialog;
                    case MESSAGE_SUMMARY_EVENT_VALUE:
                        return SIPEventPackagesEnum.MessageSummary;
                    case PRESENCE_EVENT_VALUE:
                        return SIPEventPackagesEnum.Presence;
                    case REFER_EVENT_VALUE:
                        return SIPEventPackagesEnum.Refer;
                    default:
                        return SIPEventPackagesEnum.None;
                }
            }
        }

        public static string GetEventHeader(SIPEventPackagesEnum eventPackage)
        {
            switch(eventPackage)
            {
                case SIPEventPackagesEnum.Dialog:
                    return DIALOG_EVENT_VALUE;
                case SIPEventPackagesEnum.MessageSummary:
                    return MESSAGE_SUMMARY_EVENT_VALUE;
                case SIPEventPackagesEnum.Presence:
                    return PRESENCE_EVENT_VALUE;
                case SIPEventPackagesEnum.Refer:
                    return REFER_EVENT_VALUE;
                default:
                    return null;
            }
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

    public static class SIPEventFilters
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
