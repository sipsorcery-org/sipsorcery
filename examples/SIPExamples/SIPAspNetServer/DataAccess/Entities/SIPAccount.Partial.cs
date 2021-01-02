// ============================================================================
// FileName: SIPAccount.Partial.cs
//
// Description:
// Represents the SIPAccount entity. This partial class is used to apply 
// additional properties or metadata to the audo generated SIPAccount class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 31 Dec 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using SIPSorcery.SIP.App;

#nullable disable

namespace demo.DataAccess
{
    public partial class SIPAccount : ISIPAccount
    {
        public string SIPDomain
        {
            get => Domain?.Domain;
        }
    }
}
