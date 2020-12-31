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
using SIPSorcery.SIP;

namespace SIPAspNetServer.DataAccess
{
    public class SIPRegistrarBindingDataLayer
    {
        public List<SIPRegistrarBinding> GetForSIPAccount(Guid sipAccountID)
        {
            using (var db = new SIPAssetsDbContext())
            {
                return db.SIPRegistrarBindings.Where(x => x.SIPAccountID == sipAccountID).ToList();
            }
        }

        public SIPRegistrarBinding GetNextExpired(DateTimeOffset expiryTime)
        {
            using (var db = new SIPAssetsDbContext())
            {
                return db.SIPRegistrarBindings.Where(x => x.ExpiryTime <= expiryTime)
                    .OrderBy(x => x.ExpiryTime)
                    .FirstOrDefault();
            }
        }

        public SIPRegistrarBinding Add(SIPRegistrarBinding binding)
        {
            using (var db = new SIPAssetsDbContext())
            {
                binding.ID = Guid.NewGuid();
                binding.LastUpdate = DateTimeOffset.UtcNow;

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
            using (var db = new SIPAssetsDbContext())
            {
                var existing = db.SIPRegistrarBindings.Where(x => x.ID == id).SingleOrDefault();

                if (existing == null)
                {
                    throw new ApplicationException("The SIP Registrar Binding to update could not be found.");
                }

                existing.LastUpdate = DateTimeOffset.UtcNow;
                existing.Expiry = expiry;
                existing.RemoteSIPSocket = remoteSIPEndPoint?.ToString();
                existing.ProxySIPSocket = proxySIPEndPoint?.ToString();
                existing.RegistrarSIPSocket = registrarSIPEndPoint?.ToString();

                db.SaveChanges();

                return existing;
            }
        }

        public SIPRegistrarBinding SetExpiry(Guid id,int expiry)
        {
            using (var db = new SIPAssetsDbContext())
            {
                var existing = db.SIPRegistrarBindings.Where(x => x.ID == id).SingleOrDefault();

                if (existing == null)
                {
                    throw new ApplicationException("The SIP Registrar Binding to update could not be found.");
                }

                existing.LastUpdate = DateTimeOffset.UtcNow;
                existing.Expiry = expiry;

                db.SaveChanges();

                return existing;
            }
        }

        public void Delete(Guid id)
        {
            using (var db = new SIPAssetsDbContext())
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
