// ============================================================================
// FileName: ISIPAccount.cs
//
// Description:
// Represents a SIP account that holds authentication information and 
// additional settings for SIP accounts.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 10 May 2008  Aaron Clauson   Created, Hobart, Australia.
// 30 Dec 2020  Aaron Clauson   Rename from SIPAccount to ISIPAccount and removed
//                              sipsorcery.com specific fields.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;

namespace SIPSorcery.SIP.App
{
    /// <remarks>
    /// SIP account usernames can be treated by some SIP Sorcery server agents as domain name like structures where a username of
    /// "x.username" will match the "username" account for receiving calls. To facilitate this SIP accounts with a '.' character in them
    /// can only be created where the suffix "username" portion matches the Owner field. This allows users to create SIP accounts with '.'
    /// in them but will prevent a different user from being able to hijack an "x.username" account and cause unexpected behaviour.
    /// </remarks>
    public interface ISIPAccount
    {
        Guid ID { get; }
        string SIPUsername { get; }
        string SIPPassword { get; }
        string HA1Digest { get; }   // Digest of the username + domain + password. Can be used for authentication instead of the password field.
        string SIPDomain { get; }
        bool IsDisabled { get; }
    }
}
