//-----------------------------------------------------------------------------
// Filename: CustomerAccountDataLayer.cs
//
// Description: Data layer class for customer account entities. Customer accounts are used to hold credit for use
// with billable calls.
// 
// History:
// 02 Jul 2012	Aaron Clauson	Created.
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
    public class CustomerAccountDataLayer
    {
        private const decimal SECONDS_FOR_RATE = 60M;   // The number of seconds of call time a rate corresponds to.

        private static ILog logger = AppState.logger;

        /// <summary>
        /// This method acts as a call rate lookup engine. It's very basic and simply matches the call destination
        /// based on the record that has the longest matching prefix.
        /// </summary>
        /// <param name="accountCode">The accountCode the call rate lookup is for.</param>
        /// <param name="destination">The call desintation the rate lookup is for.</param>
        /// <returns>If a matching rate is found a greater than 0 decimal value otherwise 0.</returns>
        public decimal GetRate(string accountCode, string destination)
        {
            if (accountCode.IsNullOrBlank() || destination.IsNullOrBlank())
            {
                return 0;
            }

            using (var db = new SIPSorceryEntities())
            {
                var callRate = (from rate in db.Rates1
                                join custAcc in db.CustomerAccounts1 on rate.Owner equals custAcc.Owner
                                where custAcc.AccountCode == accountCode && destination.StartsWith(rate.Prefix)
                                orderby rate.Prefix.Length descending
                                select rate).FirstOrDefault();

                if (callRate != null)
                {
                    logger.Debug("Rate found for " + accountCode + " and " + destination + " was " + callRate.Rate1 + ".");
                    return callRate.Rate1;
                }
                else
                {
                    logger.Debug("No rate found for " + accountCode + " and " + destination + ".");
                    return 0;
                }
            }
        }

        /// <summary>
        /// This method attempts to reserve a the initial amount of credit for a call.
        /// </summary>
        /// <param name="accountCode">The accountCode the credit should be reserved against.</param>
        /// <param name="amount">The amount of credit to reserve.</param>
        /// <param name="rate">The rate for the call destination and the values that will be used for subsequent credit reservations.</param>
        /// <param name="seconds">The duration of call time in seconds that the reservation corresponds to.</param>
        /// <param name="cdrID">The CDR that the credit reservation will be applied to.</param>
        /// <returns>True if there was enough credit for the reservation otherwise false.</returns>
        public bool ReserveInitialCredit(string accountCode, decimal amount, decimal rate, int seconds, string cdrID)
        {
            logger.Debug("ReserveInitialCredit for " + accountCode + ", ammount " + amount + ", rate " + rate + ", seconds " + seconds + ", CDR ID " + cdrID + ".");

            if (accountCode.IsNullOrBlank() || amount <= 0 || rate <=0 || seconds <= 0 || cdrID.IsNullOrBlank())
            {
                return false;
            }

            using (var db = new SIPSorceryEntities())
            {
                using (var trans = new TransactionScope())
                {
                    var customerAccount = db.CustomerAccounts1.Where(x => x.AccountCode == accountCode).SingleOrDefault();

                    if (customerAccount == null)
                    {
                        logger.Debug("The initial reservation for " + accountCode + " and " + amount + " failed due to the no matching accountcode.");
                        return false;
                    }
                    else if (customerAccount.Credit < amount)
                    {
                        logger.Debug("The initial reservation for " + accountCode + " and " + amount + " failed due to lack of credit.");
                        return false;
                    }
                    else
                    {
                        var callCDR = (from cdr in db.CDRs where cdr.ID == cdrID select cdr).SingleOrDefault();

                        if (callCDR == null)
                        {
                            logger.Debug("The initial reservation for " + accountCode + " and " + amount + " failed due to the no matching CDR for " + cdrID + ".");
                            return false;
                        }
                        else if (callCDR.HungupTime != null)
                        {
                            logger.Debug("The initial reservation for " + accountCode + " and " + amount + " failed due to the CDR already being hungup.");
                            return false;
                        }

                        // The credit is available deduct it from the customer account balance and place it on the CDR.
                        customerAccount.Credit = customerAccount.Credit - amount;
                        
                        // Set the fields on the CDR.
                        callCDR.Rate = rate;
                        callCDR.SecondsReserved = seconds;
                        callCDR.AccountCode = accountCode;
                        callCDR.Cost = amount;

                        db.SaveChanges();

                        trans.Complete();

                        return true;
                    }
                }
            }
        }

        /// <summary>
        /// This method attempts to reserve a chunk of credit for to allow a call to continue.
        /// </summary>
        /// <param name="seconds">The number of seconds the reservation is being requested for.</param>
        /// <param name="cdrID">The CDR that the credit reservation will be applied to.</param>
        /// <returns>True if there was enough credit for the reservation otherwise false.</returns>
        public bool ReserveCredit(int seconds, string cdrID)
        {
            logger.Debug("ReserveCredit for  seconds " + seconds + ", CDR ID " + cdrID + ".");

            if (seconds <= 0 || cdrID.IsNullOrBlank())
            {
                return false;
            }

            using (var db = new SIPSorceryEntities())
            {
                using (var trans = new TransactionScope())
                {
                    var callCDR = (from cdr in db.CDRs where cdr.ID == cdrID select cdr).SingleOrDefault();

                    if (callCDR == null)
                    {
                        logger.Debug("The reservation for " + cdrID + " and " + seconds + "s failed due to the no matching CDR.");
                        return false;
                    }
                    else if (callCDR.HungupTime != null)
                    {
                        logger.Debug("The reservation for " + cdrID + " and " + seconds + "s failed due to the CDR already being hungup.");
                        return false;
                    }
                    else if (callCDR.AccountCode.IsNullOrBlank())
                    {
                        logger.Debug("The reservation for " + cdrID + " and " + seconds + "s failed due to the CDR having a blank accountcode.");
                        return false;
                    }
                    else if (callCDR.Rate <= 0)
                    {
                        logger.Debug("The reservation for " + cdrID + " and " + seconds + "s failed due to the CDR having no rate.");
                        return false;
                    }

                    string accountCode = callCDR.AccountCode;
                    var customerAccount = db.CustomerAccounts1.Where(x => x.AccountCode == accountCode).SingleOrDefault();

                    if (customerAccount == null)
                    {
                        logger.Debug("The reservation for " + accountCode + " and " + seconds + "s failed due to the no matching accountcode.");
                        return false;
                    }

                    decimal amount = (Convert.ToDecimal(seconds) / SECONDS_FOR_RATE) * callCDR.Rate.Value;

                    if (customerAccount.Credit < amount)
                    {
                        logger.Debug("The reservation for " + accountCode + " and " + seconds + "s failed due to lack of credit.");
                        return false;
                    }
                    else
                    {
                        // The credit is available deduct it from the customer account balance and place it on the CDR.
                        customerAccount.Credit = customerAccount.Credit - amount;

                        // Set the fields on the CDR.
                        callCDR.SecondsReserved = callCDR.SecondsReserved + seconds;
                        callCDR.Cost = callCDR.Cost + amount;

                        db.SaveChanges();

                        trans.Complete();

                        return true;
                    }
                }
            }
        }

        /// <summary>
        /// This method should be called once a billable call has been completed. It will calcualte the final cost of the call and return
        /// any usused credit back to the customer account.
        /// </summary>
        /// <param name="cdrID">The ID of the CDR the credit is being returned for.</param>
        public void ReturnUnusedCredit(string cdrID)
        {
            logger.Debug("ReturnUnusedCredit for CDR ID " + cdrID + ".");

            if (cdrID.NotNullOrBlank())
            {
                using (var db = new SIPSorceryEntities())
                {
                    using (var trans = new TransactionScope())
                    {
                        var callCDR = (from cdr in db.CDRs where cdr.ID == cdrID select cdr).SingleOrDefault();

                        if (callCDR == null)
                        {
                            logger.Debug("The unused credit could not be returned for " + cdrID + "  due to the no matching CDR.");
                        }
                        else if (callCDR.Duration == null)
                        {
                            logger.Debug("The unused credit could not be returned for " + cdrID + " the CDR has not been hungup.");
                        }
                        else if (callCDR.Cost == null)
                        {
                            logger.Debug("The unused credit could not be returned for " + cdrID + " the call cost was empty.");
                        }
                        else if (callCDR.AccountCode.IsNullOrBlank())
                        {
                            logger.Debug("The unused credit could not be returned for " + cdrID + " due to the CDR having a blank accountcode.");
                        }
                        else if (callCDR.Rate <= 0)
                        {
                            logger.Debug("The unused credit could not be returned for " + cdrID + " due to the CDR having no rate.");
                        }
                        else
                        {
                            decimal actualCallCost = 0;

                            if (callCDR.Duration == 0)
                            {
                                actualCallCost = callCDR.Cost.Value;
                                callCDR.Cost = 0;
                            }
                            else
                            {
                                actualCallCost = (Convert.ToDecimal(callCDR.Duration) / SECONDS_FOR_RATE) * callCDR.Rate.Value;

                                logger.Debug("Actual call cost calculated at " + actualCallCost + " for call with cost of " + callCDR.Cost + ".");

                                if (Math.Round(actualCallCost, 2) < Math.Round(callCDR.Cost.Value, 2))
                                {
                                    string accountCode = callCDR.AccountCode;
                                    var customerAccount = db.CustomerAccounts1.Where(x => x.AccountCode == accountCode).SingleOrDefault();

                                    if (customerAccount == null)
                                    {
                                        logger.Debug("The unused credit could not be returned for " + cdrID + " due to the no matching accountcode.");
                                    }
                                    else
                                    {
                                        decimal returnCredit = Math.Round(callCDR.Cost.Value, 2) - Math.Round(actualCallCost, 2);
                                        callCDR.Cost = callCDR.Cost.Value - returnCredit;
                                        customerAccount.Credit = customerAccount.Credit + returnCredit;

                                        db.SaveChanges();

                                        trans.Complete();
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
