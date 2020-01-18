//-----------------------------------------------------------------------------
// Filename: SIPUserAgentsRolesEnum.cs
//
// Description: The roles a SIP User Agent can behave as.
//
// Author(s):
// Aaron Clauson
// 
// Author(s):
// Aaron Clauson
// 
// History:
// 29 Jul 2006	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.SIP
{
    public enum SIPUserAgentRolesEnum
    {
        Client = 1,
        Server = 2,
    }

    public class SIPUserAgentRolesTypes
    {
        public static SIPUserAgentRolesEnum GetSIPUserAgentType(string userRoleType)
        {
            return (SIPUserAgentRolesEnum)Enum.Parse(typeof(SIPUserAgentRolesEnum), userRoleType, true);
        }
    }
}
