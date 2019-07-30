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

namespace SIPSorcery.SIP.App
{
    /// <remarks>
    /// SIP account usernames can be treated by some SIP Sorcery server agents as domain name like structures where a username of
    /// "x.username" will match the "username" account for receiving calls. To facilitate this SIP accounts with a '.' character in them
    /// can only be created where the suffix "username" portion matches the Owner field. This allows users to create SIP accounts with '.'
    /// in them but will prevent a different user from being able to hijack an "x.username" account and caue unexpected behaviour.
    /// </remarks>
    public interface ISIPAccount
    {
        Guid Id { get; set; }

        string Owner { get; set; }

        string AdminMemberId { get; set; }

        string SIPUsername { get; set; }

        string SIPPassword { get; set; }

        string SIPDomain { get; set; }

        bool SendNATKeepAlives { get; set; }

        bool IsIncomingOnly { get; set; }

        string OutDialPlanName { get; set; }

        string InDialPlanName { get; set; }

        bool IsUserDisabled { get; set; }

        bool IsAdminDisabled { get; set; }

        string AdminDisabledReason { get; set; }

        string NetworkId { get; set; }

        string IPAddressACL { get; set; }

        DateTimeOffset Inserted { get; set; }

        bool IsSwitchboardEnabled { get; set; }

        bool DontMangleEnabled { get; set; }

        string AvatarURL { get; set; }

        string AccountCode { get; set; }

        string Description { get; set; }

        bool IsDisabled { get; }
    }
}
