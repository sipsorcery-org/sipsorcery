//-----------------------------------------------------------------------------
// Filename: SIPDomainDataLayer.cs
//
// Description: Data access methods for the SIPDomain entity. 
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

namespace demo.DataAccess
{
    public class SIPDomainDataLayer
    {
        public string GetCanonicalDomain(string host, bool wildcardOk)
        {
            if (string.IsNullOrEmpty(host))
            {
                throw new ArgumentNullException(nameof(host), "The host parameter must be specified for GetCanonicalDomain.");
            }

            using (var db = new SIPAssetsDbContext())
            {
                SIPDomain sipDomain = db.SIPDomains.Where(x => x.Domain.ToLower() == host.ToLower()).SingleOrDefault();
                if (sipDomain == null)
                {
                    sipDomain = db.SIPDomains.Where(x => x.AliasList.ToLower().Contains(host.ToLower())).FirstOrDefault();
                }

                return sipDomain?.Domain;
            }
        }
    }
}
