//-----------------------------------------------------------------------------
// Filename: SIPRegistrarBindingDataLayer.cs
//
// Description: Data access methods for the SIPRegistrarBinding entity. 
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
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SIPSorcery.SIP;

namespace demo.DataAccess
{
    public class SIPRegistrarBindingDataLayer
    {
        private readonly IDbContextFactory<SIPAssetsDbContext> _dbContextFactory;

        public SIPRegistrarBindingDataLayer(IDbContextFactory<SIPAssetsDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public List<SIPRegistrarBinding> GetForSIPAccount(Guid sipAccountID)
        {
            using (var db = _dbContextFactory.CreateDbContext())
            {
                return db.SIPRegistrarBindings.Where(x => x.SIPAccountID == sipAccountID).ToList();
            }
        }

        public SIPRegistrarBinding GetNextExpired(DateTime expiryTime)
        {
            using (var db = _dbContextFactory.CreateDbContext())
            {
                return db.SIPRegistrarBindings
                    .Include(x => x.SIPAccount)
                    .Where(x => x.ExpiryTime <= expiryTime)
                    .OrderBy(x => x.ExpiryTime)
                    .FirstOrDefault();
            }
        }

        public SIPRegistrarBinding Add(SIPRegistrarBinding binding)
        {
            using (var db = _dbContextFactory.CreateDbContext())
            {
                binding.ID = Guid.NewGuid();
                binding.LastUpdate = DateTime.UtcNow;

                db.SIPRegistrarBindings.Add(binding);
                db.SaveChanges();
            }

            return binding;
        }

        public SIPRegistrarBinding RefreshBinding(
            Guid id, 
            int expiry, 
            SIPEndPoint remoteSIPEndPoint, 
            SIPEndPoint proxySIPEndPoint, 
            SIPEndPoint registrarSIPEndPoint, 
            bool dontMangle)
        {
            using (var db = _dbContextFactory.CreateDbContext())
            {
                var existing = db.SIPRegistrarBindings.Where(x => x.ID == id).SingleOrDefault();

                if (existing == null)
                {
                    throw new ApplicationException("The SIP Registrar Binding to update could not be found.");
                }

                existing.LastUpdate = DateTime.UtcNow;
                existing.Expiry = expiry;
                existing.ExpiryTime = DateTime.UtcNow.AddSeconds(expiry);
                existing.RemoteSIPSocket = remoteSIPEndPoint?.ToString();
                existing.ProxySIPSocket = proxySIPEndPoint?.ToString();
                existing.RegistrarSIPSocket = registrarSIPEndPoint?.ToString();

                db.SaveChanges();

                return existing;
            }
        }

        public SIPRegistrarBinding SetExpiry(Guid id,int expiry)
        {
            using (var db = _dbContextFactory.CreateDbContext())
            {
                var existing = db.SIPRegistrarBindings.Where(x => x.ID == id).SingleOrDefault();

                if (existing == null)
                {
                    throw new ApplicationException("The SIP Registrar Binding to update could not be found.");
                }

                existing.LastUpdate = DateTime.UtcNow;
                existing.Expiry = expiry;

                db.SaveChanges();

                return existing;
            }
        }

        public void Delete(Guid id)
        {
            using (var db = _dbContextFactory.CreateDbContext())
            {
                var binding = db.SIPRegistrarBindings.Where(x => x.ID == id).SingleOrDefault();
                if (binding != null)
                {
                    db.SIPRegistrarBindings.Remove(binding);
                    db.SaveChanges();
                }
            }
        }
    }
}
