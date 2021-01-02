//-----------------------------------------------------------------------------
// Filename: SIPAccountDataLayer.cs
//
// Description: Data access methods for the SIPAccount entity. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 31 Dec 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace demo.DataAccess
{
    public class SIPAccountDataLayer
    {
        public SIPAccount GetSIPAccount(string username, string domain)
        {
            if (string.IsNullOrEmpty(username))
            {
                throw new ArgumentNullException(nameof(username), "The username parameter must be specified for GetSIPAccount.");
            }
            else if (string.IsNullOrEmpty(domain))
            {
                throw new ArgumentNullException(nameof(domain), "The domain parameter must be specified for GetSIPAccount.");
            }

            using (var db = new SIPAssetsDbContext())
            {
                SIPAccount sipAccount = db.SIPAccounts.Include(x => x.Domain).Where(x => x.SIPUsername.ToLower() == username.ToLower() &&
                                                               x.Domain.Domain.ToLower() == domain.ToLower()).SingleOrDefault();
                if (sipAccount == null)
                {
                    // A full lookup failed. Now try a partial lookup if the incoming username is in a dotted domain name format.
                    if (username.Contains("."))
                    {
                        string usernameSuffix = username.Substring(username.LastIndexOf(".") + 1);
                        sipAccount = db.SIPAccounts.Include(x => x.Domain).Where(x => x.SIPUsername.ToLower() == usernameSuffix.ToLower() &&
                                                               x.Domain.Domain.ToLower() == domain.ToLower()).SingleOrDefault();
                    }
                }

                return sipAccount;
            }
        }
    }
}
