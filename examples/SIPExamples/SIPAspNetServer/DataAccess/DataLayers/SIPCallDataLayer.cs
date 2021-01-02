//-----------------------------------------------------------------------------
// Filename: SIPCallDataLayer.cs
//
// Description: Data access methods for the SIPCalls entity. 
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
using System.Linq.Expressions;

namespace demo.DataAccess
{
    public class SIPCallDataLayer
    {
        public SIPCall Get(Guid id)
        {
            using (var db = new SIPAssetsDbContext())
            {
                return db.SIPCalls.Where(x => x.ID == id).FirstOrDefault();
            }
        }

        public SIPCall Get(Expression<Func<SIPCall, bool>> where)
        {
            using (var db = new SIPAssetsDbContext())
            {
                return db.SIPCalls.Where(where).FirstOrDefault();
            }
        }

        public SIPCall Add(SIPCall call)
        {
            using (var db = new SIPAssetsDbContext())
            {
                call.ID = Guid.NewGuid();
                call.Inserted = DateTimeOffset.UtcNow;

                db.SIPCalls.Add(call);
                db.SaveChanges();
            }

            return call;
        }

        public void Delete(Guid id)
        {
            using (var db = new SIPAssetsDbContext())
            {
                var call = db.SIPCalls.Where(x => x.ID == id).SingleOrDefault();
                if (call != null)
                {
                    db.SIPCalls.Remove(call);
                    db.SaveChanges();
                }
            }
        }
    }
}
