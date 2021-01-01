//-----------------------------------------------------------------------------
// Filename: CDRDataLayer.cs
//
// Description: Data access methods for the Call Detail Records (CDR) entity. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 01 Jan 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;

namespace SIPAspNetServer.DataAccess
{
    public class CDRDataLayer
    {
        public CDR Get(Guid id)
        {
            using (var db = new SIPAssetsDbContext())
            {
                return db.CDRs.Where(x => x.ID == id).FirstOrDefault();
            }
        }

        public CDR Hangup(Guid id, string reason)
        {
            using (var db = new SIPAssetsDbContext())
            {
                var existing = db.CDRs.Where(x => x.ID == id).SingleOrDefault();

                if (existing == null)
                {
                    throw new ApplicationException("The CDR to update could not be found.");
                }

                existing.HungupAt = DateTimeOffset.UtcNow;
                existing.HungupReason = reason;

                db.SaveChanges();

                return existing;
            }
        }
    }
}
