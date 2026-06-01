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
            else
            {
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
                var valueSpan = value.AsSpan().Trim();

                return valueSpan switch
                {
                    _ when DIALOG_EVENT_VALUE.Equals(valueSpan, StringComparison.OrdinalIgnoreCase) => SIPEventPackagesEnum.Dialog,
                    _ when MESSAGE_SUMMARY_EVENT_VALUE.Equals(valueSpan, StringComparison.OrdinalIgnoreCase) => SIPEventPackagesEnum.MessageSummary,
                    _ when PRESENCE_EVENT_VALUE.Equals(valueSpan, StringComparison.OrdinalIgnoreCase) => SIPEventPackagesEnum.Presence,
                    _ when REFER_EVENT_VALUE.Equals(valueSpan, StringComparison.OrdinalIgnoreCase) => SIPEventPackagesEnum.Refer,
                    _ => SIPEventPackagesEnum.None,
                };
            }
        }

        public static string GetEventHeader(SIPEventPackagesEnum eventPackage)
        {
            switch (eventPackage)
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
            else if ("cancelled".Equals(value, StringComparison.OrdinalIgnoreCase) ||
                "error".Equals(value, StringComparison.OrdinalIgnoreCase) ||
                "local-bye".Equals(value, StringComparison.OrdinalIgnoreCase) ||
                "rejected".Equals(value, StringComparison.OrdinalIgnoreCase) ||
                "replaced".Equals(value, StringComparison.OrdinalIgnoreCase) ||
                "remote-bye".Equals(value, StringComparison.OrdinalIgnoreCase) ||
                "timeout".Equals(value, StringComparison.OrdinalIgnoreCase))
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
                var trimmedValue = value.AsSpan().Trim();
                return trimmedValue switch
                {
                    _ when "cancelled".Equals(trimmedValue, StringComparison.OrdinalIgnoreCase) => SIPEventDialogStateEvent.Cancelled,
                    _ when "error".Equals(trimmedValue, StringComparison.OrdinalIgnoreCase) => SIPEventDialogStateEvent.Error,
                    _ when "local-bye".Equals(trimmedValue, StringComparison.OrdinalIgnoreCase) => SIPEventDialogStateEvent.LocalBye,
                    _ when "rejected".Equals(trimmedValue, StringComparison.OrdinalIgnoreCase) => SIPEventDialogStateEvent.Rejected,
                    _ when "replaced".Equals(trimmedValue, StringComparison.OrdinalIgnoreCase) => SIPEventDialogStateEvent.Replaced,
                    _ when "remote-bye".Equals(trimmedValue, StringComparison.OrdinalIgnoreCase) => SIPEventDialogStateEvent.RemoteBye,
                    _ when "timeout".Equals(trimmedValue, StringComparison.OrdinalIgnoreCase) => SIPEventDialogStateEvent.Timeout,
                    _ => throw new ArgumentException("The value is not valid for a SIPEventDialogStateEvent."),
                };
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
