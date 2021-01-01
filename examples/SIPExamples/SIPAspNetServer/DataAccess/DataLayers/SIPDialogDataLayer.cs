//-----------------------------------------------------------------------------
// Filename: SIPDialogDataLayer.cs
//
// Description: Data access methods for the SIPDialogs entity. 
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

namespace SIPAspNetServer.DataAccess
{
    public class SIPDialogDataLayer
    {
        public SIPDialog Get(Guid id)
        {
            using (var db = new SIPAssetsDbContext())
            {
                return db.SIPDialogs.Where(x => x.ID == id).FirstOrDefault();
            }
        }

        public SIPDialog Get(Expression<Func<SIPDialog, bool>> where)
        {
            using (var db = new SIPAssetsDbContext())
            {
                return db.SIPDialogs.Where(where).FirstOrDefault();
            }
        }

        public SIPDialog Add(SIPDialog dialog)
        {
            using (var db = new SIPAssetsDbContext())
            {
                dialog.ID = Guid.NewGuid();
                dialog.Inserted = DateTimeOffset.UtcNow;

                db.SIPDialogs.Add(dialog);
                db.SaveChanges();
            }

            return dialog;
        }

        public void Delete(Guid id)
        {
            using (var db = new SIPAssetsDbContext())
            {
                var dialog = db.SIPDialogs.Where(x => x.ID == id).SingleOrDefault();
                if (dialog != null)
                {
                    db.SIPDialogs.Remove(dialog);
                    db.SaveChanges();
                }
            }
        }
    }
}
