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
// 10 May 2008  Aaron Clauson   Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2008 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
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
using System.Linq.Expressions;
using SIPSorcery.Sys;
using log4net;

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
    /// in them but will prevent a different user from being able to hijack an "x.username" account and caue unexpected behaviour.
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
