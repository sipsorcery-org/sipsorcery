// ============================================================================
// FileName: SIPAccount.cs
//
// Description:
// Represents a SIP account that holds authentication information and additional settings
// for SIP accounts.
//
// Author(s):
// Aaron Clauson
//
// History:
// 10 May 2008  Aaron Clauson   Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;

namespace SIPSorcery.SIP.App
{
    // Real-time call control delegates.
    public delegate RtccCustomerAccount RtccGetCustomerDelegate(string owner, string accountCode);
    public delegate RtccRate RtccGetRateDelegate(string owner, string rateCode, string rateDestination, int ratePlan);
    public delegate decimal RtccGetBalanceDelegate(string accountCode);
    public delegate decimal RtccReserveInitialCreditDelegate(string accountCode, string rateID, SIPCDR cdr, out int intialSeconds);
    public delegate void RtccUpdateCdrDelegate(string cdrID, SIPCDR cdr);

    public class RtccCustomerAccount
    {
        public string ID;
        public string AccountCode;
        public int RatePlan;
    }

    public class RtccRate
    {
        public string ID;
        public decimal RatePerIncrement;
        public decimal SetupCost;
    }

    /// <remarks>
    /// SIP account usernames can be treated by some SIP Sorcery server agents as domain name like structures where a username of
    /// "x.username" will match the "username" account for receiving calls. To facilitate this SIP accounts with a '.' character in them
    /// can only be created where the suffix "username" portion matches the Owner field. This allows users to create SIP accounts with '.'
    /// in them but will prevent a different user from being able to hijack an "x.username" account and cause unexpected behaviour.
    /// </remarks>
    public class SIPAccount
    {
        public int TimeZoneOffsetMinutes;

        public Guid Id { get; set; }
        public string Owner { get; set; }
        public string AdminMemberId { get; set; }    // If set it designates this asset as a belonging to a user with the matching adminid.
        public string SIPUsername { get; set; }
        public string SIPPassword { get; set; }
        public string SIPDomain { get; set; }
        public bool SendNATKeepAlives { get; set; }
        public bool IsIncomingOnly { get; set; }
        public string OutDialPlanName { get; set; }
        public string InDialPlanName { get; set; }
        public bool IsUserDisabled { get; set; }
        public bool IsAdminDisabled { get; set; }
        public string AdminDisabledReason { get; set; }
        public string NetworkId { get; set; }
        public string IPAddressACL { get; set; }
        public DateTimeOffset Inserted { get; set; }
        public bool IsSwitchboardEnabled { get; set; }
        public bool DontMangleEnabled { get; set; }
        public string AvatarURL { get; set; }
        public string AccountCode { get; set; }
        public string Description { get; set; }

        public DateTimeOffset InsertedLocal
        {
            get { return Inserted.AddMinutes(TimeZoneOffsetMinutes); }
        }

        public bool IsDisabled
        {
            get { return IsUserDisabled || IsAdminDisabled; }
        }

        public SIPAccount() { }

        public SIPAccount(string owner, string sipDomain, string sipUsername, string sipPassword, string outDialPlanName)
        {
            Id = Guid.NewGuid();
            Owner = owner;
            SIPDomain = sipDomain;
            SIPUsername = sipUsername;
            SIPPassword = sipPassword;
            OutDialPlanName = (outDialPlanName != null && outDialPlanName.Trim().Length > 0) ? outDialPlanName.Trim() : null;
            Inserted = DateTimeOffset.UtcNow;
        }
    }
}
