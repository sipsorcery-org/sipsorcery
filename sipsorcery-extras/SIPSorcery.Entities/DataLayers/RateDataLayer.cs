//-----------------------------------------------------------------------------
// Filename: RateDataLayer.cs
//
// Description: Data layer class for Rate entities. Rates are used with billable calls.
// 
// History:
// 28 Nov 2012	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2012 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Pty Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Transactions;
using SIPSorcery.Entities;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Entities
{
    public class RateDataLayer
    {
        private static ILog logger = AppState.logger;

        /// <summary>
        /// Adds a new rate.
        /// </summary>
        /// <param name="rate">The rate record to add.</param>
        public void Add(Rate rate)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                if ((from rt in sipSorceryEntities.Rates where rt.Prefix == rate.Prefix && rt.Owner == rate.Owner select rt).Any())
                {
                    throw new ApplicationException("The rate prefix is already in use.");
                }
                else
                {
                    rate.ID = Guid.NewGuid().ToString();
                    rate.Inserted = DateTimeOffset.UtcNow.ToString("o");

                    sipSorceryEntities.Rates.Add(rate);
                    sipSorceryEntities.SaveChanges();
                }
            }
        }

        /// <summary>
        /// Updates an existing rate.
        /// </summary>
        /// <param name="rate">The rate to update.</param>
        public void Update(Rate rate)
        {
            using (var db = new SIPSorceryEntities())
            {
                var existingRate = (from ra in db.Rates where ra.ID == rate.ID select ra).SingleOrDefault();

                if (existingRate == null)
                {
                    throw new ApplicationException("The rate to update could not be found");
                }
                else if (existingRate.Owner.ToLower() != rate.Owner.ToLower())
                {
                    throw new ApplicationException("You are not authorised to update this rate.");
                }
                else
                {
                    if (existingRate.Prefix != rate.Prefix)
                    {
                        if ((from rt in db.Rates where rt.Prefix == rate.Prefix select rt).Any())
                        {
                            throw new ApplicationException("The rate prefix is already in use.");
                        }
                    }

                    logger.Debug("Updating rate " + existingRate.Description + " for " + existingRate.Owner + ".");

                    existingRate.Description = rate.Description;
                    existingRate.Prefix = rate.Prefix;
                    existingRate.Rate1 = rate.Rate1;
                    existingRate.RateCode = rate.RateCode;
                    existingRate.SetupCost = rate.SetupCost;
                    existingRate.IncrementSeconds = rate.IncrementSeconds;
                    existingRate.RatePlan = rate.RatePlan;

                    db.SaveChanges();
                }
            }
        }

        public Rate Get(string owner, string id)
        {
            if (id.IsNullOrBlank())
            {
                return null;
            }

            using (var db = new SIPSorceryEntities())
            {
                return (from ra in db.Rates
                        where
                            ra.Owner.ToLower() == owner.ToLower() &&
                            ra.ID.ToLower() == id.ToLower()
                        select ra).SingleOrDefault();
            }
        }

        /// <summary>
        /// Deletes an existing rate.
        /// </summary>
        /// <param name="id">The ID of the rate record to delete.</param>
        public void Delete(string id)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                var rate = (from rt in sipSorceryEntities.Rates where rt.ID == id select rt).SingleOrDefault();

                if (rate != null)
                {
                    sipSorceryEntities.Rates.Remove(rate);
                    sipSorceryEntities.SaveChanges();
                }
            }
        }

        /// <summary>
        /// Deletes all the rates for a specific owner account.
        /// </summary>
        /// <param name="owner">The owner to delete all the rates for.</param>
        public void DeleteAll(string owner)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                sipSorceryEntities.Database.ExecuteSqlCommand("delete from rate where owner = @p0", owner);
            }
        }
    }
}
