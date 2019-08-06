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
using System.Data.Entity;
using System.Data.Entity.Core;
using System.Data.Entity.Core.Objects;
using System.Linq;
using System.Transactions;
using SIPSorcery.Entities;
using SIPSorcery.SIP;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Entities
{
    public class CustomerAccountDataLayer
    {
        private const int MINIMUM_INITIAL_RESERVATION_SECONDS = 45;
        private const int ACCOUNT_CODE_RANDOM_STRING_PREFIX_LENGTH = 10;
        private const int ACCOUNT_CODE_RANDOM_NUMBER_SUFFIX_LENGTH = 10;
        private const decimal SECONDS_FOR_RATE = 60M;   // The number of seconds of call time a rate corresponds to.
        private const int SECONDS_LENIENCY_FOR_RECONCILIATION = 3;
        private const int DEFAULT_INCREMENT_SECONDS = 1;                // The default number of seconds to bill calls in increments.

        private static ILog logger = AppState.GetLogger("rtcc");

        /// <summary>
        /// This method acts as a call rate lookup engine. It's very basic and simply matches the call destination
        /// based on the record that has the longest matching prefix.
        /// </summary>
        /// <param name="owner">The owner the call rate lookup is for.</param>
        /// <param name="rateCode">The rate code for the call. If specified and a matching rate is found it will 
        /// take precedence over the destination.</param>
        /// <param name="destination">The call desintation the rate lookup is for.</param>
        /// <returns>If a matching rate is found a greater than 0 decimal value otherwise 0.</returns>
        public Rate GetRate(string owner, string rateCode, string destination, int ratePlan)
        {
            logger.Debug("GetRate for owner " + owner + ", ratecode " + rateCode + " and destination " + destination + ".");

            if (owner.IsNullOrBlank() || destination.IsNullOrBlank())
            {
                return null;
            }

            using (var db = new SIPSorceryEntities())
            {
                Rate callRate = null;

                if (rateCode.NotNullOrBlank())
                {
                    callRate = (from rate in db.Rates
                                where rate.Owner.ToLower() == owner.ToLower() && rate.RateCode == rateCode && rate.RatePlan == ratePlan
                                select rate).SingleOrDefault();
                }

                if (callRate == null)
                {
                    callRate = (from rate in db.Rates
                                where rate.Owner.ToLower() == owner.ToLower() && destination.StartsWith(rate.Prefix) && rate.RatePlan == ratePlan
                                orderby rate.Prefix.Length descending
                                select rate).FirstOrDefault();
                }

                // If the rate is still null check for international prefixes.
                if (callRate == null)
                {
                    //var customerAccount = db.CustomerAccounts.Where(x => x.AccountCode == accountCode).SingleOrDefault();

                    //if (customerAccount == null)
                    //{
                    //    logger.Debug("The rate lookup for " + accountCode + " failed due to the no matching accountcode.");
                    //    return null;
                    //}

                    string rtccInternationalPrefixes = (from cust in db.Customers where cust.Name.ToLower() == owner.ToLower() select cust.RTCCInternationalPrefixes).Single();

                    if (rtccInternationalPrefixes.NotNullOrBlank())
                    {
                        string trimmedDestination = null;
                        string[] prefixes = rtccInternationalPrefixes.Split(',');
                        foreach (string prefix in prefixes.OrderByDescending(x => x.Length))
                        {
                            if (destination.StartsWith(prefix))
                            {
                                trimmedDestination = destination.Substring(prefix.Length);
                                logger.Debug("The destination matched international prefix of " + prefix + ", looking up rate for " + trimmedDestination + ".");
                                break;
                            }
                        }

                        if (trimmedDestination != null)
                        {
                            callRate = (from rate in db.Rates
                                        where rate.Owner.ToLower() == owner.ToLower() && trimmedDestination.StartsWith(rate.Prefix) && rate.RatePlan == ratePlan
                                        orderby rate.Prefix.Length descending
                                        select rate).FirstOrDefault();
                        }
                    }
                }

                if (callRate != null)
                {
                    logger.Debug("Rate found for " + owner + " and " + destination + " was " + callRate.Rate1 + ".");
                    return callRate;
                }
                else
                {
                    logger.Debug("No rate found for " + owner + " and " + destination + ".");
                    return null;
                }
            }
        }

        public SIPSorcery.SIP.App.RtccRate GetRtccRate(string owner, string rateCode, string destination, int ratePlan)
        {
            var rate = GetRate(owner, rateCode, destination,  ratePlan);

            return (rate != null) ? new SIPSorcery.SIP.App.RtccRate() { ID = rate.ID, RatePerIncrement = rate.Rate1, SetupCost = rate.SetupCost } : null;
        }

        public decimal GetBalance(string accountCode)
        {
            using (var db = new SIPSorceryEntities())
            {
                var customerAccount = db.CustomerAccounts.Where(x => x.AccountCode == accountCode).SingleOrDefault();

                if (customerAccount == null)
                {
                    return -1;
                }
                else
                {
                    return customerAccount.Credit;
                }
            }
        }

        /// <summary>
        /// This method attempts to reserve a the initial amount of credit for a call.
        /// </summary>
        /// <param name="accountCode">The accountCode the credit should be reserved against.</param>
        /// <param name="amount">The amount of credit to reserve.</param>
        /// <param name="rate">The rate for the call destination and the values that will be used for subsequent credit reservations.</param>
        /// <param name="initialSeconds">IF the reservation is successful this parameter will hold the number of seconds that were reserved for the initial reservation.</param>
        /// <returns>True if there was enough credit for the reservation otherwise false.</returns>
        public decimal ReserveInitialCredit(string accountCode, string rateID, SIPCDR cdr, out int initialSeconds)
        {
            try
            {
                logger.Debug("ReserveInitialCredit for " + accountCode + " and rate ID " + rateID + ".");

                initialSeconds = 0;

                using (var db = new SIPSorceryEntities())
                {
                    using (var trans = new TransactionScope())
                    {
                        var rate = db.Rates.Where(x => x.ID == rateID).SingleOrDefault();

                        if (accountCode.IsNullOrBlank() || (rate == null || rate.Rate1 <= 0))
                        {
                            return Decimal.MinusOne;
                        }

                        logger.Debug("ReserveInitialCredit for " + accountCode + ", rate " + rate.Rate1 + ", setup cost " + rate.SetupCost + ", increment seconds " + rate.IncrementSeconds + ".");

                        var customerAccount = db.CustomerAccounts.Where(x => x.AccountCode == accountCode).SingleOrDefault();

                        if (customerAccount == null)
                        {
                            logger.Debug("The initial reservation for " + accountCode + " failed due to the no matching accountcode.");
                            return Decimal.MinusOne;
                        }

                        // Get the owning customer's RTCC billing increment.
                        //int rtccIncrement = (from cust in db.Customers where cust.Name.ToLower() == customerAccount.Owner.ToLower() select cust.RTCCBillingIncrement).Single();

                        initialSeconds = (rate.IncrementSeconds > MINIMUM_INITIAL_RESERVATION_SECONDS) ? rate.IncrementSeconds : MINIMUM_INITIAL_RESERVATION_SECONDS;

                        decimal reservationCost = ((decimal)initialSeconds / (decimal)60 * rate.Rate1) + rate.SetupCost;

                        if (customerAccount.Credit < reservationCost)
                        {
                            logger.Debug("The initial reservation for " + accountCode + ", duration " + initialSeconds + "s and " + reservationCost.ToString("0.#####") + " failed due to lack of credit.");
                            return Decimal.MinusOne;
                        }
                        else
                        {
                            var rtccRecord = new RTCC()
                            {
                                ID = Guid.NewGuid().ToString(),
                                CDRID = cdr.CDRId.ToString(),
                                AccountCode = accountCode,
                                SecondsReserved = initialSeconds,
                                Cost = reservationCost,
                                Rate = rate.Rate1,
                                SetupCost = rate.SetupCost,
                                IncrementSeconds = rate.IncrementSeconds,
                                Inserted = DateTime.UtcNow
                            };

                            db.RTCCs1.Add(rtccRecord);
                            //var callCDR = (from cdr in db.CDRs where cdr.ID == cdrID select cdr).SingleOrDefault();

                            //if (callCDR == null)
                            //{
                            //    logger.Debug("The initial reservation for " + accountCode + " and " + reservationCost.ToString("0.#####") + " failed due to the no matching CDR for " + cdrID + ".");
                            //    return false;
                            //}
                            //else if (callCDR.HungupTime != null)
                            //{
                            //    logger.Debug("The initial reservation for " + accountCode + " and " + reservationCost.ToString("0.#####") + " failed due to the CDR already being hungup.");
                            //    return false;
                            //}

                            // The credit is available deduct it from the customer account balance and place it on the CDR.
                            customerAccount.Credit = customerAccount.Credit - reservationCost;

                            // Set the fields on the CDR.
                            //callCDR.Rate = rate;
                            //callCDR.SecondsReserved = seconds;
                            //callCDR.AccountCode = accountCode;
                            //callCDR.Cost = reservationCost;

                            //db.CDRs.AddObject(cdr);

                            db.SaveChanges();

                            trans.Complete();

                            logger.Debug("The initial reservation for " + accountCode + ", duration " + initialSeconds + "s and " + reservationCost.ToString("0.#####") + " was successful.");

                            return reservationCost;
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ReserveInitialCredit. " + excp);
                throw;
            }
        }

        public void UpdateRealTimeCallControlCDRID(string oldCDRID, SIPCDR newCDR)
        {
            logger.Debug("UpdateRealTimeCallControlCDRID old CDR ID " + oldCDRID + ", new CDR ID " + newCDR.CDRId.ToString() + ".");

            using (var db = new SIPSorceryEntities())
            {
                using (var trans = new TransactionScope())
                {
                    var realTimeCallControl = (from rtcc in db.RTCCs1 where rtcc.CDRID == oldCDRID select rtcc).FirstOrDefault();

                    if (realTimeCallControl == null)
                    {
                        logger.Error("No RTCC record could be found for CDR ID " + oldCDRID + ".");
                    }
                    else
                    {
                        //db.CDRs.AddObject(newCDR);

                        realTimeCallControl.CDRID = newCDR.CDRId.ToString();

                        db.SaveChanges();

                        trans.Complete();
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
        public bool ReserveCredit(int seconds, string rtccID)
        {
            if (seconds <= 0 || rtccID.IsNullOrBlank())
            {
                return false;
            }

            using (var db = new SIPSorceryEntities())
            {
                using (var trans = new TransactionScope())
                {
                    var callRTCC = (from rtcc in db.RTCCs1 where rtcc.ID == rtccID select rtcc).SingleOrDefault();

                    if (callRTCC == null)
                    {
                        logger.Debug("The reservation for " + rtccID + " and " + seconds + "s failed due to the no matching RTCC record.");
                        return false;
                    }
                    else
                    {
                        var callCDR = (from cdr in db.CDRs where cdr.ID == callRTCC.CDRID select cdr).SingleOrDefault();

                        if (callCDR.HungupTime != null)
                        {
                            logger.Debug("The reservation for " + rtccID + " and " + seconds + "s failed due to the CDR already being hungup.");
                            callRTCC.ReservationError = "Error, call already hungup.";
                        }

                        if (callRTCC.ReservationError == null)
                        {
                            string accountCode = callRTCC.AccountCode;
                            var customerAccount = db.CustomerAccounts.Where(x => x.AccountCode == accountCode).SingleOrDefault();

                            if (customerAccount == null)
                            {
                                logger.Debug("The reservation for " + accountCode + " and " + seconds + "s failed due to the no matching accountcode.");
                                callRTCC.ReservationError = "Error, no customer for accountcode.";
                            }
                            else
                            {
                                decimal amount = (Convert.ToDecimal(seconds) / SECONDS_FOR_RATE) * callRTCC.Rate.Value;

                                if (customerAccount.Credit < amount)
                                {
                                    logger.Debug("The reservation for " + accountCode + " and " + seconds + "s failed due to lack of credit.");
                                    callRTCC.ReservationError = "Error, insufficient credit.";
                                }
                                else
                                {
                                    // The credit is available deduct it from the customer account balance and place it on the CDR.
                                    customerAccount.Credit = customerAccount.Credit - amount;

                                    // Set the fields on the CDR.
                                    callRTCC.SecondsReserved = callRTCC.SecondsReserved + seconds;
                                    callRTCC.Cost = callRTCC.Cost + amount;
                                }
                            }
                        }

                        db.SaveChanges();

                        trans.Complete();

                        return callRTCC.ReservationError == null;
                    }
                }
            }
        }

        /// <summary>
        /// This method should be called once a billable call has been completed. It will calculate the final cost of the call and return
        /// any usused credit back to the customer account.
        /// </summary>
        /// <param name="cdrID">The ID of the CDR the credit is being returned for.</param>
        /// <returns>The total cost of the completed call.</returns>
        public decimal ReturnUnusedCredit(string rtccID)
        {
            logger.Debug("ReturnUnusedCredit for RTCC ID " + rtccID + ".");

            decimal actualCallCost = Decimal.Zero;

            using (var db = new SIPSorceryEntities())
            {
                using (var trans = new TransactionScope())
                {
                    var rtcc = (from rtc in db.RTCCs1.Include("cdr") where rtc.ID == rtccID select rtc).First();

                    if (rtcc.ReconciliationResult != null)
                    {
                        logger.Error("This CDR has already been reconciled, no further action will be taken.");
                    }
                    else
                    {
                        var callCDR = (from cdr in db.CDRs where cdr.ID == rtcc.CDRID select cdr).SingleOrDefault();

                        string reconciliationError = null;

                        if (callCDR.Duration == null)
                        {
                            reconciliationError = "Error, the call duration was null.";
                            logger.Warn("The unused credit could not be returned for " + rtcc.ID + " the CDR has not been hungup.");
                        }
                        else if (rtcc.Cost == null)
                        {
                            reconciliationError = "Error, the call cost was null.";
                            logger.Warn("The unused credit could not be returned for " + rtcc.ID + " the call cost was empty.");
                        }
                        else if (rtcc.AccountCode.IsNullOrBlank())
                        {
                            reconciliationError = "Error, the accountcode was null.";
                            logger.Warn("The unused credit could not be returned for " + rtcc.ID + " due to the CDR having a blank accountcode.");
                        }
                        else if (rtcc.Rate <= 0)
                        {
                            reconciliationError = "Error, the rate was not set.";
                            logger.Warn("The unused credit could not be returned for " + rtcc.ID + " due to the CDR having no rate.");
                        }

                        if (reconciliationError != null)
                        {
                            rtcc.ReconciliationResult = reconciliationError;
                            db.SaveChanges();
                        }
                        else
                        {
                            string accountCode = rtcc.AccountCode;
                            var customerAccount = db.CustomerAccounts.Where(x => x.AccountCode == accountCode).SingleOrDefault();

                            logger.Debug("The pre-reconciliation balance for account " + accountCode + " was " + customerAccount.Credit + " when processing RTCC ID " + rtcc.ID + ".");

                            if (customerAccount == null)
                            {
                                logger.Debug("The unused credit could not be returned for RTCC ID " + rtcc.ID + " due to the no matching accountcode.");
                                rtcc.ReconciliationResult = "Error, no matching customer for " + accountCode + ".";
                                db.SaveChanges();
                            }
                            else
                            {
                                // Get the owning customer's RTCC billing increment.
                                //int rtccIncrement = (from cust in db.Customers where cust.Name.ToLower() == callCDR.Owner.ToLower() select cust.RTCCBillingIncrement).Single();
                                int rtccIncrement = (rtcc.IncrementSeconds <= 0) ? DEFAULT_INCREMENT_SECONDS : rtcc.IncrementSeconds;


                                int billableDuration = (callCDR.Duration.Value % rtccIncrement == 0) ? callCDR.Duration.Value : (callCDR.Duration.Value / rtccIncrement + 1) * rtccIncrement;

                                if (billableDuration > 0)
                                {
                                    actualCallCost = ((Convert.ToDecimal(billableDuration) / SECONDS_FOR_RATE) * rtcc.Rate.Value) + rtcc.SetupCost;
                                }

                                logger.Debug("RTCC billable duration " + billableDuration + " (increment " + rtccIncrement + "), actual call cost calculated at " + actualCallCost.ToString("0.#####") + " for call with cost of " + rtcc.Cost + ".");

                                if (Math.Round(actualCallCost, 5) < Math.Round(rtcc.Cost.Value, 5))
                                {
                                    decimal returnCredit = Math.Round(rtcc.Cost.Value, 5) - Math.Round(actualCallCost, 5);

                                    if (returnCredit > 0)
                                    {
                                        // There is some credit to return to the customer account.
                                        rtcc.Cost = rtcc.Cost.Value - returnCredit;
                                        rtcc.ReconciliationResult = "ok";
                                        customerAccount.Credit = customerAccount.Credit + returnCredit;
                                        rtcc.PostReconciliationBalance = customerAccount.Credit;

                                        logger.Debug("The billed call cost was " + actualCallCost.ToString("0.#####") + ", return credit amount " + returnCredit.ToString("0.#####") + ", post reconciliation balance " + customerAccount.Credit.ToString("0.#####") + ".");
                                    }
                                    else
                                    {
                                        // An error has occurred and the credit reserved was less than the cost of the call.
                                        rtcc.ReconciliationResult = "Error: Actual call cost calculated as " + actualCallCost.ToString("0.#####");
                                        customerAccount.Credit = customerAccount.Credit + returnCredit;
                                        rtcc.PostReconciliationBalance = customerAccount.Credit;

                                        logger.Debug("Error, the billed call cost was " + actualCallCost.ToString("0.#####") + " which resulted in a negative return credit amount of " + returnCredit.ToString("0.#####") + ", post reconciliation balance " + customerAccount.Credit.ToString("0.#####") + ".");
                                    }

                                    db.SaveChanges();
                                }
                                else if (Math.Round(actualCallCost, 5) > Math.Round(rtcc.Cost.Value, 5) && Math.Abs(callCDR.Duration.Value - rtcc.SecondsReserved.Value) <= SECONDS_LENIENCY_FOR_RECONCILIATION)
                                {
                                    rtcc.ReconciliationResult = "ok";
                                    rtcc.PostReconciliationBalance = customerAccount.Credit;

                                    logger.Debug("The billed call duration was in +/- " + SECONDS_LENIENCY_FOR_RECONCILIATION + " of actual call duration so a billable call cost of " + rtcc.Cost.Value.ToString("0.#####") + " was accepted for an actual call cost of " + actualCallCost.ToString("0.#####")
                                        + ", post reconciliation balance " + rtcc.PostReconciliationBalance.Value.ToString("0.#####") + ".");

                                    db.SaveChanges();
                                }
                                else if (Math.Round(actualCallCost, 5) > Math.Round(rtcc.Cost.Value, 5))
                                {
                                    rtcc.ReconciliationResult = "Error, calculated cost of " + actualCallCost.ToString("0.#####") + " exceeded reserved cost of " + rtcc.Cost.Value.ToString("0.#####") + ".";
                                    rtcc.PostReconciliationBalance = customerAccount.Credit;

                                    logger.Debug("Error, calculated cost of " + actualCallCost.ToString("0.#####") + " exceeded reserved cost of " + rtcc.Cost.Value.ToString("0.#####")
                                         + ", post reconciliation balance " + rtcc.PostReconciliationBalance.Value.ToString("0.#####") + ".");

                                    db.SaveChanges();
                                }
                                else
                                {
                                    // Call cost exactly matches the reserved cost.
                                    rtcc.ReconciliationResult = "ok";
                                    rtcc.PostReconciliationBalance = customerAccount.Credit;

                                    logger.Debug("The billed call duration was the exact amount reserved of " + rtcc.Cost.Value.ToString("0.#####") + ", post reconciliation balance " + rtcc.PostReconciliationBalance.Value.ToString("0.#####") + ".");

                                    db.SaveChanges();
                                }
                            }
                        }
                    }

                    trans.Complete();
                }
            }

            return actualCallCost;
        }

        /// <summary>
        /// Checks whether the account name is already in use for this owner.
        /// </summary>
        /// <param name="owner">The owner of the customer accounts to check.</param>
        /// <param name="accountName">The account name to check.</param>
        /// <param name="updatingID">An optional parameter that can be supplied when the check is being made for an account
        /// that is being updated and in which case this ID is for the record being updated.</param>
        /// <returns>True if the account name is in use otherwise false.</returns>
        public bool IsAccountNameInUse(string owner, string accountName, string updatingID)
        {
            using (var db = new SIPSorceryEntities())
            {
                return (from ca in db.CustomerAccounts
                        where
                            ca.Owner.ToLower() == owner.ToLower() &&
                            ca.AccountName.ToLower() == accountName.ToLower() &&
                            (updatingID == null || ca.ID != updatingID)
                        select ca).Any();
            }
        }

        /// <summary>
        /// Checks whether the account number is already in use for this owner.
        /// </summary>
        /// <param name="owner">The owner of the customer accounts to check.</param>
        /// <param name="accountNumber">The account number to check.</param>
        /// <param name="updatingID">An optional parameter that can be supplied when the check is being made for an account
        /// that is being updated and in which case this ID is for the record being updated.</param>
        /// <returns>True if the account number is in use otherwise false.</returns>
        public bool IsAccountNumberInUse(string owner, string accountNumber, string updatingID)
        {
            if (accountNumber.IsNullOrBlank())
            {
                return false;
            }

            using (var db = new SIPSorceryEntities())
            {
                return (from ca in db.CustomerAccounts
                        where
                            ca.Owner.ToLower() == owner.ToLower() &&
                            ca.AccountNumber.ToLower() == accountNumber.ToLower() &&
                            (updatingID == null || ca.ID != updatingID)
                        select ca).Any();
            }
        }

        /// <summary>
        /// Checks whether the account code is already in use for the system.
        /// </summary>
        /// <param name="accountCode">The account code to check.</param>
        /// <param name="updatingID">An optional parameter that can be supplied when the check is being made for an account
        /// that is being updated and in which case this ID is for the record being updated.</param>
        /// <returns>True if the account code is in use otherwise false.</returns>
        public bool IsAccountCodeInUse(string accountCode, string updatingID)
        {
            using (var db = new SIPSorceryEntities())
            {
                return (from ca in db.CustomerAccounts
                        where
                            ca.AccountCode.ToLower() == accountCode.ToLower() &&
                            (updatingID == null || ca.ID != updatingID)
                        select ca).Any();
            }
        }

        private void CheckUniqueFields(CustomerAccount customerAccount)
        {
            if (IsAccountNameInUse(customerAccount.Owner, customerAccount.AccountName, customerAccount.ID))
            {
                throw new ApplicationException("The account name is already in use.");
            }
            else if (customerAccount.AccountNumber != null && IsAccountNumberInUse(customerAccount.Owner, customerAccount.AccountNumber, customerAccount.ID))
            {
                throw new ApplicationException("The account number is already in use.");
            }
            else if (IsAccountCodeInUse(customerAccount.AccountCode, customerAccount.ID))
            {
                throw new ApplicationException("The account code is already in use.");
            }
        }

        /// <summary>
        /// Adds a new customer account.
        /// </summary>
        /// <param name="customerAccount">The customer account to add.</param>
        public void Add(CustomerAccount customerAccount)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                customerAccount.AccountCode = Crypto.GetRandomString(ACCOUNT_CODE_RANDOM_STRING_PREFIX_LENGTH) + Crypto.GetRandomInt(ACCOUNT_CODE_RANDOM_NUMBER_SUFFIX_LENGTH).ToString();

                CheckUniqueFields(customerAccount);

                customerAccount.ID = Guid.NewGuid().ToString();
                customerAccount.Inserted = DateTimeOffset.UtcNow.ToString("o");

                sipSorceryEntities.CustomerAccounts.Add(customerAccount);
                sipSorceryEntities.SaveChanges();
            }
        }

        /// <summary>
        /// Updates an existing customer account.
        /// </summary>
        /// <param name="customerAccount">The customer account to update.</param>
        public void Update(CustomerAccount customerAccount)
        {
            using (var db = new SIPSorceryEntities())
            {
                var existingAccount = (from ca in db.CustomerAccounts where ca.ID == customerAccount.ID select ca).SingleOrDefault();

                if (existingAccount == null)
                {
                    throw new ApplicationException("The customer account to update could not be found");
                }
                else if (existingAccount.Owner.ToLower() != customerAccount.Owner.ToLower())
                {
                    throw new ApplicationException("You are not authorised to update this customer account.");
                }
                else
                {
                    CheckUniqueFields(customerAccount);

                    logger.Debug("Updating customer account " + existingAccount.AccountName + " for " + existingAccount.Owner + ".");

                    existingAccount.AccountName = customerAccount.AccountName;
                    existingAccount.AccountNumber = customerAccount.AccountNumber;
                    existingAccount.Credit = customerAccount.Credit;
                    existingAccount.PIN = customerAccount.PIN;
                    existingAccount.RatePlan = customerAccount.RatePlan;

                    db.SaveChanges();
                }
            }
        }

        public CustomerAccount Get(string owner, string accountNumber)
        {
            if (accountNumber.IsNullOrBlank())
            {
                return null;
            }

            using (var db = new SIPSorceryEntities())
            {
                return (from ca in db.CustomerAccounts
                        where
                            ca.Owner.ToLower() == owner.ToLower() &&
                            (ca.AccountNumber.ToLower() == accountNumber.ToLower() ||
                            ca.AccountCode.ToLower() == accountNumber.ToLower())
                        select ca).SingleOrDefault();
            }
        }

        public SIPSorcery.SIP.App.RtccCustomerAccount GetRtccCustomer(string owner, string accountNumber)
        {
            var customer = Get(owner, accountNumber);

            return customer != null ? new SIP.App.RtccCustomerAccount() { ID = customer.ID, AccountCode = customer.AccountCode, RatePlan = customer.RatePlan } : null;
        }

        public void SetCDRIsHangingUp(string rtccID)
        {
            using (var db = new SIPSorceryEntities())
            {
                //var callCDR = (from cdr in db.CDRs where cdr.ID == cdrID select cdr).SingleOrDefault();
                var callRTCC = (from rtcc in db.RTCCs1 where rtcc.ID == rtccID select rtcc).SingleOrDefault();

                if (callRTCC == null)
                {
                    logger.Debug("RTCC record could not be found for " + rtccID + " when attemping to set the IsHangingUp flag.");
                }
                else
                {
                    callRTCC.IsHangingUp = true;
                    db.SaveChanges();
                }
            }
        }

        /// <summary>
        /// Checks the account code is valid and if not will also check the account number.
        /// </summary>
        /// <param name="owner">The owner of the customer accounts to check.</param>
        /// <param name="accountNumber">The account code to check.</param>
        /// <returns>If a matching accountcode or number is found the account code will be returned otherwise null.</returns>
        public CustomerAccount CheckAccountCode(string owner, string accountCode)
        {
            using (var db = new SIPSorceryEntities())
            {
                var account = (from ca in db.CustomerAccounts
                        where
                            ca.Owner.ToLower() == owner.ToLower() &&
                           (ca.AccountCode.ToLower() == accountCode.ToLower() || ca.AccountNumber == accountCode)
                        select ca).SingleOrDefault();

                return (account != null) ? account : null;
            }
        }
    }
}
