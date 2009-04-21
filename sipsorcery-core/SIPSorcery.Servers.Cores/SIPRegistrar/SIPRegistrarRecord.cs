// ============================================================================
// FileName: SIPRegistrarRecord.cs
//
// Description:
// Represents a registration entry for a single user. A user can have multiple registered uri's and each registrar
// record will maintain a list of registered contacts.
//
// Author(s):
// Aaron Clauson
//
// History:
// 23 Aug 2008	Aaron Clauson	Created, re-factored from RegistrarCore.cs.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
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
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{
    /// <summary>
    /// The SIPRegistrarRecord represents a registration entry for a single user. A user can have multiple registered uri's and each registrar
    /// record will maintain a list of registered contacts.
    /// (see RFC 3261 Chapter "10.3 Processing REGISTER Requests")
    /// 
    /// The list of bindings maintained is indexed by a SIPURI which is the UNMANGLED URI sent in the Contact header of the REGISTER request. With NAT
    /// a common siuation is that a User Agent will be sending the same Contact URI but the public socket changes on each request. The Registrar will cope
    /// with that correctly as the old public socket will be overwritten with the new one but for a database only storing the public socket an issue arises.
    /// </summary>
    public class SIPRegistrarRecord
    {
        private const int MAX_USERAGENT_LENGTH = 128;
        public const int NATKEEPALIVE_DEFAULTSEND_INTERVAL = 15;
        public const int MINIMUM_EXPIRY_SECONDS = 60;
       
        private static string m_sipRegisterRemoveAll = SIPConstants.SIP_REGISTER_REMOVEALL;

        private ILog logger = AppState.GetLogger("registrar");

        public Guid SIPAccountId;                   // This is the account id of the SIP account this registrar record corresponds to. Is used when information needs to be persisted.

        public string Owner;

        public SIPParameterlessURI AddressOfRecord;  // e.g.'s sip:user@host or sips:abcd@1.2.3.4:5060, there must be no parameters or headers on this URI see RFC 3261 "10.3 Processing REGISTER Requests".

        public Dictionary<SIPURI, SIPRegistrarBinding> Bindings = new Dictionary<SIPURI, SIPRegistrarBinding>();
        public int BindingsCount
        {
            get { return Bindings.Count; }
        }

        public string AuthUser;                 // Username of the user authorised to update this record.

        public string Password;                 // Password of the user authorised to update this record.

        public bool NATSendKeepAlives = false;  // Indicates whether this account would like the proxy to send periodic NAT keep alive packets.

        public DateTime LastAuthenticationTime = DateTime.MinValue;

        public DateTime LastBindingRefresh = DateTime.Now;

        public string LastUserAgent = null;

        public SIPEndPoint LastRefreshEndPoint = null;

        private SIPMonitorLogDelegate SIPMonitorLogEvent_External;

        //public static Int64 Created;
        //public static Int64 Destroyed;

        /// <summary>
        /// Should not be used. Provided to allow service serialisation only.
        /// </summary>
        public SIPRegistrarRecord()
        { 
            //Created++;
        }

        public SIPRegistrarRecord(SIPMonitorLogDelegate sipMonitorLogDelegate, Guid sipAccountId, string owner, SIPParameterlessURI addressOfRecord, string authUser, string password, bool natSendKeepAlives)
        {
            //Created++;

            SIPMonitorLogEvent_External = sipMonitorLogDelegate;

            if (addressOfRecord == null)
            {
                throw new ApplicationException("A SIPRegistrarRecord cannot be created for an empty address-of-record value.");
            }

            SIPAccountId = sipAccountId;
            Owner = owner;
            AddressOfRecord = addressOfRecord;
            AuthUser = authUser;
            Password = password;
            NATSendKeepAlives = natSendKeepAlives;
        }

        public SIPRegistrarRecord(SIPMonitorLogDelegate sipMonitorLogDelegate, SIPAccount sipAccount)
        {
            //Created++;
            SIPMonitorLogEvent_External = sipMonitorLogDelegate;

            SIPAccountId = sipAccount.SIPAccountId;
            Owner = sipAccount.Owner;
            AddressOfRecord = new SIPParameterlessURI(SIPSchemesEnum.sip, sipAccount.SIPDomain, sipAccount.SIPUsername);
            AuthUser = sipAccount.SIPUsername;
            Password = sipAccount.SIPPassword;
            NATSendKeepAlives = sipAccount.SendNATKeepAlives;
        }

        /// <summary>
        /// Updates the bindings list for a registrar's address-of-records.
        /// </summary>
        /// <param name="proxyEndPoint">If the request arrived at this registrar via a proxy then this will contain the end point of the proxy.</param>
        /// <param name="uacRecvdEndPoint">The public end point the UAC REGISTER request was deemded to have originated from.</param>
        /// <param name="registrarEndPoint">The registrar end point the registration request was received on.</param>
        /// <param name="maxAllowedExpiry">The maximum allowed expiry that can be granted to this binding request.</param>
        /// <returns>True if a change was made to the bindings, false otherwise</returns>
        public bool UpdateBinding(
            SIPEndPoint proxySIPEndPoint,
            SIPEndPoint remoteSIPEndPoint,
            SIPEndPoint registrarSIPEndPoint,
            string authUsername,
            List<SIPContactHeader> contactHeaders,
            string callId,
            int cseq,
            int expiresHeaderValue,
            string userAgent,
            int perAddressBindings,
            int maxAllowedExpiry,
            out SIPResponseStatusCodesEnum responseStatus,
            out string responseMessage)
        {
            bool bindingsUpdated = false;
            responseMessage = null;

            try
            {
                LastBindingRefresh = DateTime.Now;
                LastUserAgent = userAgent;
                LastRefreshEndPoint = remoteSIPEndPoint;

                userAgent = (userAgent != null && userAgent.Length > MAX_USERAGENT_LENGTH) ? userAgent.Substring(0, MAX_USERAGENT_LENGTH) : userAgent;

                bool removeAll = false;

                if (contactHeaders != null && contactHeaders.Count > 0)
                {
                    // Check whether there is a remove all header present.
                    foreach (SIPContactHeader contact in contactHeaders)
                    {
                        if (contact.ContactURI.Host == m_sipRegisterRemoveAll)
                        {
                            removeAll = true;
                            break;
                        }
                    }
                }
                else
                {
                    // No bindings to update so return ok.
                    responseStatus = SIPResponseStatusCodesEnum.Ok;
                }

                if (removeAll)
                {
                    #region Process remove all bindings.

                    if (expiresHeaderValue == 0 && contactHeaders.Count == 1)
                    {
                        // Removing all bindings for user.
                        FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.BindingRemoval, "Remove all bindings requested for " + AddressOfRecord.ToString() + ".", AddressOfRecord.User));

                        lock (Bindings)
                        {
                            // Mark all the current bindings as expired.
                            foreach (SIPRegistrarBinding binding in Bindings.Values)
                            {
                                binding.RemovalReason = SIPBindingRemovalReason.ClientExpiredAll;
                            }
                        }

                        bindingsUpdated = true;
                        responseStatus = SIPResponseStatusCodesEnum.Ok;
                    }
                    else
                    {
                        // Remove all header cannot be present with other headers and must have an Expiry equal to 0.
                        responseStatus = SIPResponseStatusCodesEnum.BadRequest;
                    }

                    #endregion
                }
                else
                {
                    // Updates are an atomic operation so if any binding update fails any successful ones must be rolled back.
                    foreach (SIPContactHeader contactHeader in contactHeaders)
                    {
                        SIPURI currContactURI = contactHeader.ContactURI;
                        int requestedExpiry = (contactHeader.Expires == -1) ? expiresHeaderValue : contactHeader.Expires;
                        requestedExpiry = (requestedExpiry == -1) ? maxAllowedExpiry : requestedExpiry;   // This will happen if the Expires header and the Expiry on the Contact are both missing.
                        int grantedExpiry = (requestedExpiry > maxAllowedExpiry) ? maxAllowedExpiry : requestedExpiry;
                        grantedExpiry = (grantedExpiry < MINIMUM_EXPIRY_SECONDS) ? MINIMUM_EXPIRY_SECONDS : grantedExpiry;
                        bool existingBinding = false;

                        //logger.Debug("GetMatchingBinding: " + currContactURI.ToString());
                        //logger.Debug("Current bindings: " + BindingsToString());
                        if (Bindings.ContainsKey(contactHeader.ContactURI))
                        {
                            SIPRegistrarBinding matchingBinding = Bindings[contactHeader.ContactURI]; 
 
                            if (matchingBinding != null)
                            {
                                existingBinding = true;
                               
                                if (requestedExpiry <= 0)
                                {
                                    //logger.Debug("UAC Expired binding " + matchingBinding.ContactURI.ToString())
                                    FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.BindingExpired, "Binding expired by client for " + AddressOfRecord.ToString() + ", binding " + contactHeader.ToString() + ".", AddressOfRecord.User));
                                    matchingBinding.RemovalReason = SIPBindingRemovalReason.ClientExpiredSpecific;
                                }
                                else if (uacRecvdEndPoint != matchingBinding.RemoteSIPEndPoint || registrarEndPoint.ToString() != matchingBinding.RegistrarEndPoint.ToString() ||
                                    (matchingBinding.ProxyEndPoint != null && proxyURI == null) ||
                                    (matchingBinding.ProxyEndPoint == null && proxyURI != null) ||
                                    (matchingBinding.ProxyEndPoint != null && proxyURI != null && matchingBinding.ProxyEndPoint.ToString() != proxyURI.Host.ToString()))
                                {
                                    // The socket the binding has arrived from has changed in some way. As well as refreshing the binding the new connection 
                                    // information needs to be recorded.
                                    FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Registrar, "Binding update request for " + AddressOfRecord.ToString() + ", expiry requested " + requestedExpiry + "s granted " + grantedExpiry + "s, binding " + contactHeader.ToString() + ".", AddressOfRecord.User));
                                    matchingBinding.RefreshBindingAndRemote(grantedExpiry, uacRecvdEndPoint, proxyURI, registrarEndPoint);
                                }
                                else
                                {
                                    FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Registrar, "Binding update request for " + AddressOfRecord.ToString() + ", expiry requested " + requestedExpiry + "s granted " + grantedExpiry + "s, binding " + contactHeader.ToString() + ".", AddressOfRecord.User));
                                    matchingBinding.RefreshBinding(grantedExpiry);
                                }

                                bindingsUpdated = true;
                            }
                        }

                        if (!existingBinding)
                        {
                            if (requestedExpiry > 0)
                            {
                                FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.BindingInProgress, "New binding request for " + AddressOfRecord.ToString() + ", expiry requested " + requestedExpiry + "s granted " + grantedExpiry + "s, binding " + contactHeader.ToString() + ".", AddressOfRecord.User));

                                if (Bindings.Count >= perAddressBindings)
                                {
                                    // Mark the oldest binding as expired.
                                     SIPRegistrarBinding oldestBinding = GetOldestBinding();

                                    if (oldestBinding == null || !Bindings.ContainsKey(oldestBinding.ContactURI))
                                    {
                                        logger.Warn("There was an error removing the oldest binding.");
                                        break;
                                    }

                                    // Remove binding to free up a slot for the new one.
                                    //logger.Debug("Binding limit exceeded for " + AddressOfRecord.ToString() + " removing " + oldestBinding.ContactURI.ToString() + " to stay within binding limit of " + PerAddressBindings + ".");
                                    FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.BindingInProgress, "Binding limit exceeded for " + AddressOfRecord.ToString() + " removing " + oldestBinding.ContactURI.ToString() + " to stay within limit of " + perAddressBindings + ".", AddressOfRecord.User));
                                    oldestBinding.RemovalReason = SIPBindingRemovalReason.ExceededPerUserLimit;
                                }

                                contactHeader.Expires = grantedExpiry;
                                SIPRegistrarBinding binding = new SIPRegistrarBinding(Owner, AddressOfRecord, contactHeader, callId, cseq, userAgent, uacRecvdEndPoint, proxyURI, registrarEndPoint, grantedExpiry);
                                Bindings.Add(binding.ContactURI, binding);
                                bindingsUpdated = true;
                            }
                            else
                            {
                                //logger.Warn("New binding received for " + AddressOfRecord.ToString() + " with expired contact," + contactHeader.ToString() + ",  binding not being added.");
                                FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.BindingFailed, "New binding received for " + AddressOfRecord.ToString() + " with expired contact," + contactHeader.ToString() + " no update.", AddressOfRecord.User));
                            }
                        }
                    }

                    responseStatus = SIPResponseStatusCodesEnum.Ok;
                }

                return bindingsUpdated;
            }
            catch (Exception excp)
            {
                logger.Error("Exception UpdateBinding. " + excp.Message);
                FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Error, "Registrar error updating binding: " + excp.Message + " Binding not updated.", AddressOfRecord.User));
                responseStatus = SIPResponseStatusCodesEnum.InternalServerError;
                return false;
            }
        }

        /// <summary>
        /// Gets a SIP contact header for this address-of-record based on the bindings list.
        /// </summary>
        /// <param name="firstContact">If specified means this contact should be placed at the start of the list to allow UA's that only parse the first
        /// contact to get the expiry time regarding their registered binding.</param>
        /// <returns></returns>
        public List<SIPContactHeader> GetContactHeader(bool useMangeledURI, SIPURI firstContactURI)
        {
            List<SIPRegistrarBinding> bindings = GetBindings();

            if (bindings != null && bindings.Count > 0)
            {
                List<SIPContactHeader> contactHeaderList = new List<SIPContactHeader>();

                foreach (SIPRegistrarBinding binding in bindings)
                {
                    SIPContactHeader bindingContact = null;

                    if (!useMangeledURI)
                    {
                        bindingContact = new SIPContactHeader(null, binding.ContactURI);
                    }
                    else
                    {
                        bindingContact = new SIPContactHeader(null, binding.MangledContactURI);
                    }

                    bindingContact.Expires = Convert.ToInt32(binding.ExpiryTime.Subtract(DateTime.Now).TotalSeconds % Int32.MaxValue);

                    if (firstContactURI != null && bindingContact.ContactURI == firstContactURI)
                    {
                        contactHeaderList.Insert(0, bindingContact);
                    }
                    else
                    {
                        contactHeaderList.Add(bindingContact);
                    }
                }

                return contactHeaderList;
            }
            else
            {
                return null;
            }
        }

        public void UpdateContactSDPOwner(SIPURI contactURI, string sdpOwner)
        {
            //bool contactUpdated = false;
            foreach (SIPRegistrarBinding binding in Bindings.Values)
            {
                if (binding.MangledContactURI == contactURI)
                {
                    //logger.Debug("Updating SDP owner for " + binding.ToMangledContactString() + " to " + sdpOwner + ".");
                    binding.SDPOwner = sdpOwner;
                    //contactUpdated = true;
                    break;
                }
            }

            /*if (!contactUpdated)
            {
                logger.Debug("UpdateContactSDPOwner failed as no binding could be located for " + contactURI.ToString() + ".");
            }*/
        }

        /// <summary>
        /// Gets a list of the current bindings for this registrar record.
        /// </summary>
        /// <returns>A list of current bindings. If there are none an empty list is returned.</returns>
        public List<SIPRegistrarBinding> GetBindings()
        {
            try
            {
                List<SIPRegistrarBinding> currentBindings = new List<SIPRegistrarBinding>();
                
                lock (Bindings)
                {
                    if (Bindings.Count > 0)
                    {
                        foreach (SIPRegistrarBinding binding in Bindings.Values)
                        {
                            if (binding.ExpiryTime > DateTime.Now && binding.RemovalReason == SIPBindingRemovalReason.Unknown)
                            {
                                currentBindings.Add(binding);
                            }
                        }
                    }
                }

                return currentBindings;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetBindings. " + excp);
                throw excp;
            }
        }

        /// <summary>
        /// Gets a list of the bindings that are expired or that have been updated.
        /// </summary>
        /// <returns>A list of dirty bindings. If there are none an empty list is returned.</returns>
        public List<SIPRegistrarBinding> GetDirtyBindings()
        {
            try
            {
                List<SIPRegistrarBinding> dirtyBindings = new List<SIPRegistrarBinding>();

                lock (Bindings)
                {
                    if (Bindings.Count > 0)
                    {
                        foreach (SIPRegistrarBinding binding in Bindings.Values)
                        {
                            if (binding.ExpiryTime < DateTime.Now || binding.RemovalReason != SIPBindingRemovalReason.Unknown || binding.Dirty)
                            {
                                dirtyBindings.Add(binding);
                            }
                        }
                    }
                }

                return dirtyBindings;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetDirtyBindings. " + excp);
                throw excp;
            }
        }

        /// <summary>
        /// Gets a list of the bindings that are expired.
        /// </summary>
        /// <returns>A list of expired bindings. If there are none an empty list is returned.</returns>
        public List<SIPRegistrarBinding> GetExpiredBindings()
        {
            try
            {
                List<SIPRegistrarBinding> expiredBindings = new List<SIPRegistrarBinding>();

                lock (Bindings)
                {
                    if (Bindings.Count > 0)
                    {
                        foreach (SIPRegistrarBinding binding in Bindings.Values)
                        {
                            if(binding.RemovalReason != SIPBindingRemovalReason.Unknown)
                            {
                                expiredBindings.Add(binding);
                            }
                            else if (binding.ExpiryTime <= DateTime.Now)
                            {
                                binding.RemovalReason = SIPBindingRemovalReason.MaxLifetimeReached;
                                expiredBindings.Add(binding);
                            }
                        }
                    }
                }

                return expiredBindings;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetExpiredBindings. " + excp);
                throw excp;
            }
        }

        /// <summary>
        /// Checks the bindings for this registrar record and identifies whether there are any that have expired or that have been
        /// updated and require persistence.
        /// </summary>
        /// <returns>True if there is a dirty binding false otherwise.</returns>
        public bool HasDirtyBinding()
        {
            try
            {
                lock (Bindings)
                {
                    if (Bindings.Count > 0)
                    {
                        foreach (SIPRegistrarBinding binding in Bindings.Values)
                        {
                            if (binding.ExpiryTime <= DateTime.Now || binding.RemovalReason != SIPBindingRemovalReason.Unknown)
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
            catch (Exception excp)
            {
                logger.Error("Exception HasDirtyBinding. " + excp);
                throw excp;
            }
        }

        /// <summary>
        /// Checks the bindings for this registrar record and identifies whether there is at least one current binding.
        /// </summary>
        /// <returns>True if there is at least one current binding for the registrar record false otherwise.</returns>
        public bool HasCurrentBinding()
        {
            try
            {
                lock (Bindings)
                {
                    if (Bindings.Count > 0)
                    {
                        foreach (SIPRegistrarBinding binding in Bindings.Values)
                        {
                            if (binding.ExpiryTime > DateTime.Now && binding.RemovalReason == SIPBindingRemovalReason.Unknown)
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
            catch (Exception excp)
            {
                logger.Error("Exception HasCurrentBinding. " + excp);
                throw excp;
            }
        }

        public string BindingsToString()
        {
            List<SIPRegistrarBinding> bindings = GetBindings();

            string bindingsStr = " none";

            if (bindings.Count > 0)
            {
                bindingsStr = null;

                foreach (SIPRegistrarBinding binding in bindings)
                {
                    string userAgentStr = (binding.UserAgent != null && binding.UserAgent.Length > 50) ? binding.UserAgent.Substring(0, 50) : binding.UserAgent;
                    bindingsStr += "\r\n " + binding.ToContactString() + " - " + binding.MangledContactURI.Host + " " + userAgentStr;
                }
            }

            return bindingsStr;
        }

        private SIPRegistrarBinding GetOldestBinding()
        {
            SIPRegistrarBinding oldestBinding = null;

            foreach (SIPRegistrarBinding binding in Bindings.Values)
            {
                if (oldestBinding == null || binding.LastUpdate < oldestBinding.LastUpdate)
                {
                    oldestBinding = binding;
                }
            }

            return oldestBinding;
        }

        private void FireSIPMonitorLogEvent(SIPMonitorEvent monitorEvent)
        {
            if (SIPMonitorLogEvent_External != null)
            {
                SIPMonitorLogEvent_External(monitorEvent);
            }
        }

        //~SIPRegistrarRecord()
        //{
         //   Destroyed++;
        //}

        #region Unit testing.

		#if UNITTEST

        [TestFixture]
		public class SIPRegistrarRecordUnitTest
		{
						
			[TestFixtureSetUp]
			public void Init()
			{
				
			}

			[TestFixtureTearDown]
			public void Dispose()
			{			
				
			}

			[Test]
			public void SampleTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				Assert.IsTrue(true, "True was false.");

				Console.WriteLine("---------------------------------"); 
			}

            [Test]
            public void NewRegistrationUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testUsername = "user";
                string testDomain = "domain";
                string testHeader = "<sip:user@domain>";
                string testUA = "my useragent v1.0";

                SIPRegistrarRecord registrarRecord = new SIPRegistrarRecord(Guid.NewGuid(), SIPParameterlessURI.ParseSIPParamterlessURI("sip:user@domain"), testUsername, testDomain, false);
                SIPContactHeader testContactHeader = SIPContactHeader.ParseContactHeader("<sip:user@domain>;expires=10")[0];

                SIPRequest regRequest = new SIPRequest(SIPMethodsEnum.REGISTER, SIPURI.ParseSIPURI("sip:user@domain"));
                SIPHeader regHeader = new SIPHeader(testContactHeader, SIPFromHeader.ParseFromHeader("testHeader"), SIPToHeader.ParseToHeader(testHeader), 1, "test");
                regRequest.Header = regHeader;
                regRequest.Header.UserAgent = testUA;

                SIPResponseStatusCodesEnum updBindResult;
                string updBindingRespMessage;
                registrarRecord.UpdateBinding(null, null, null, null, regRequest.Header.Contact, regRequest.Header.CallId, regRequest.Header.CSeq, regRequest.Header.Expires, regRequest.Header.UserAgent, out updBindResult, out updBindingRespMessage);

                Console.WriteLine(registrarRecord.BindingsToString());

                Assert.IsTrue(registrarRecord.Bindings.Count == 1, "The SIP registration was not correctly added to the list.");
            }

            [Test]
            public void AddExpiredRegistrationUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                /*SIPRegistrations.m_registrations = new Dictionary<string, List<SIPRegistration>>();

                string testUsername = "user";
                string testDomain = "domain";
                string testUA = "my useragent v1.0";
                SIPContactHeader testContactHeader = SIPContactHeader.ParseContactHeader("<sip:user@sip.domain.com>;expires=0")[0];

                SIPRegistrations.UpdateContact(new SIPRegistration(testUsername, testDomain, testContactHeader, testUA));

                Assert.IsTrue(m_registrations.Count == 0, "The SIP registration was incorrectly correctly added to the list.");
                Assert.IsTrue(SIPRegistrations.Lookup(testUsername + "@" + testDomain) == null, "The SIP registration was not correctly able to be looked up.");*/
            }

            [Test]
            public void UpdateExistingRegistrationUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testUsername = "user";
                string testDomain = "domain";
                string testHeader = "<sip:user@domain>";
                string testUA = "my useragent v1.0";

                SIPRegistrarRecord registrarRecord = new SIPRegistrarRecord(Guid.NewGuid(), SIPParameterlessURI.ParseSIPParamterlessURI("sip:user@domain"), testUsername, testDomain, false);
                SIPContactHeader testContactHeader = SIPContactHeader.ParseContactHeader("<sip:user@domain>;expires=10")[0];

                SIPRequest regRequest = new SIPRequest(SIPMethodsEnum.REGISTER, SIPURI.ParseSIPURI("sip:user@domain"));
                SIPHeader regHeader = new SIPHeader(testContactHeader, SIPFromHeader.ParseFromHeader("testHeader"), SIPToHeader.ParseToHeader(testHeader), 1, "test");
                regRequest.Header = regHeader;
                regRequest.Header.UserAgent = testUA;

                SIPResponseStatusCodesEnum updBindResult;
                string updBindingRespMessage;
                registrarRecord.UpdateBinding(null, null, null, null, regRequest.Header.Contact, regRequest.Header.CallId, regRequest.Header.CSeq, regRequest.Header.Expires, regRequest.Header.UserAgent, out updBindResult, out updBindingRespMessage);
                registrarRecord.UpdateBinding(null, null, null, null, regRequest.Header.Contact, regRequest.Header.CallId, regRequest.Header.CSeq, regRequest.Header.Expires, regRequest.Header.UserAgent, out updBindResult, out updBindingRespMessage);

                Console.WriteLine(registrarRecord.BindingsToString());

                Assert.IsTrue(registrarRecord.Bindings.Count == 1, "The SIP registration was not correctly added to the list.");
            }

            [Test]
            public void UpdateExistingRegistrationDifferentHeaderParamsUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testUsername = "user";
                string testDomain = "domain";
                string testHeader = "<sip:user@domain>";
                string testUA = "my useragent v1.0";

                SIPRegistrarRecord registrarRecord = new SIPRegistrarRecord(Guid.NewGuid(), SIPParameterlessURI.ParseSIPParamterlessURI("sip:user@domain"), testUsername, testDomain, false);
                SIPContactHeader testContactHeader = SIPContactHeader.ParseContactHeader("<sip:user@domain>;expires=10;ftag=12345")[0];

                SIPRequest regRequest = new SIPRequest(SIPMethodsEnum.REGISTER, SIPURI.ParseSIPURI("sip:user@domain"));
                SIPHeader regHeader = new SIPHeader(testContactHeader, SIPFromHeader.ParseFromHeader("testHeader"), SIPToHeader.ParseToHeader(testHeader), 1, "test");
                regRequest.Header = regHeader;
                regRequest.Header.UserAgent = testUA;

                SIPResponseStatusCodesEnum updBindResult;
                string updBindingRespMessage;
                registrarRecord.UpdateBinding(null, null, null, null, regRequest.Header.Contact, regRequest.Header.CallId, regRequest.Header.CSeq, regRequest.Header.Expires, regRequest.Header.UserAgent, out updBindResult, out updBindingRespMessage);

                SIPContactHeader testContactHeader2 = SIPContactHeader.ParseContactHeader("<sip:user@domain>;expires=70;ftag=12345")[0];
                regRequest.Header.Contact[0] = testContactHeader2;
                registrarRecord.UpdateBinding(null, null, null, null, regRequest.Header.Contact, regRequest.Header.CallId, regRequest.Header.CSeq, regRequest.Header.Expires, regRequest.Header.UserAgent, out updBindResult, out updBindingRespMessage);

                Console.WriteLine(registrarRecord.BindingsToString());

                Assert.IsTrue(registrarRecord.Bindings.Count == 1, "The SIP registration was not correctly added to the list.");
            }

            [Test]
            public void UpdateExistingRegistrationIPAddrDifferentHeaderParamsUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testUsername = "user";
                string testDomain = "192.168.1.1";
                string testURI = "sip:user@192.168.1.1";
                string testHeader = "<" + testURI + ">";
                string testUA = "my useragent v1.0";

                SIPRegistrarRecord registrarRecord = new SIPRegistrarRecord(Guid.NewGuid(), SIPParameterlessURI.ParseSIPParamterlessURI(testURI), testUsername, testDomain, false);
                SIPContactHeader testContactHeader = SIPContactHeader.ParseContactHeader(testHeader + ";expires=10;ftag=12345")[0];

                SIPRequest regRequest = new SIPRequest(SIPMethodsEnum.REGISTER, SIPURI.ParseSIPURI(testURI));
                SIPHeader regHeader = new SIPHeader(testContactHeader, SIPFromHeader.ParseFromHeader("testHeader"), SIPToHeader.ParseToHeader(testHeader), 1, "test");
                regRequest.Header = regHeader;
                regRequest.Header.UserAgent = testUA;

                SIPResponseStatusCodesEnum updBindResult;
                string updBindingRespMessage;
                registrarRecord.UpdateBinding(null, null, null, null, regRequest.Header.Contact, regRequest.Header.CallId, regRequest.Header.CSeq, regRequest.Header.Expires, regRequest.Header.UserAgent, out updBindResult, out updBindingRespMessage);

                SIPContactHeader testContactHeader2 = SIPContactHeader.ParseContactHeader(testHeader + ";expires=70;ftag=12345")[0];
                regRequest.Header.Contact[0] = testContactHeader2;
                registrarRecord.UpdateBinding(null, null, null, null, regRequest.Header.Contact, regRequest.Header.CallId, regRequest.Header.CSeq, regRequest.Header.Expires, regRequest.Header.UserAgent, out updBindResult, out updBindingRespMessage);

                Console.WriteLine(registrarRecord.BindingsToString());

                Assert.IsTrue(registrarRecord.Bindings.Count == 1, "The SIP registration was not correctly added to the list.");
            }

            [Test]
            public void UpdateExistingRegistrationIPAddrPort5060DifferentHeaderParamsUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testUsername = "user";
                string testDomain = "192.168.1.1";
                string testURI = "sip:user@192.168.1.1";
                string testHeader = "<" + testURI + ">";
                string testUA = "my useragent v1.0";

                SIPRegistrarRecord registrarRecord = new SIPRegistrarRecord(Guid.NewGuid(), SIPParameterlessURI.ParseSIPParamterlessURI(testURI), testUsername, testDomain, false);
                SIPContactHeader testContactHeader = SIPContactHeader.ParseContactHeader(testHeader + ";expires=10;ftag=12345")[0];

                SIPRequest regRequest = new SIPRequest(SIPMethodsEnum.REGISTER, SIPURI.ParseSIPURI(testURI));
                SIPHeader regHeader = new SIPHeader(testContactHeader, SIPFromHeader.ParseFromHeader("testHeader"), SIPToHeader.ParseToHeader(testHeader), 1, "test");
                regRequest.Header = regHeader;
                regRequest.Header.UserAgent = testUA;

                SIPResponseStatusCodesEnum updBindResult;
                string updBindingRespMessage;
                registrarRecord.UpdateBinding(null, null, null, null, regRequest.Header.Contact, regRequest.Header.CallId, regRequest.Header.CSeq, regRequest.Header.Expires, regRequest.Header.UserAgent, out updBindResult, out updBindingRespMessage);

                string testURI2 = "sip:user@192.168.1.1:5060";
                string testHeader2 = "<" + testURI2 + ">";
                SIPContactHeader testContactHeader2 = SIPContactHeader.ParseContactHeader(testHeader2 + ";expires=70;ftag=12345")[0];
                regRequest.Header.Contact[0] = testContactHeader2;
                registrarRecord.UpdateBinding(null, null, null, null, regRequest.Header.Contact, regRequest.Header.CallId, regRequest.Header.CSeq, regRequest.Header.Expires, regRequest.Header.UserAgent, out updBindResult, out updBindingRespMessage);

                Console.WriteLine(registrarRecord.BindingsToString());

                Assert.IsTrue(registrarRecord.Bindings.Count == 1, "The SIP registration was not correctly added to the list.");
            }

            [Test]
            public void UpdateExistingRegistrationPrivateAddressDifferentHeaderParamsUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testUsername = "user";
                string testDomain = "192.168.1.1";
                string testURI = "sip:user@192.168.1.1";
                string testHeader = "<" + testURI + ">";
                string testUA = "my useragent v1.0";
                IPEndPoint publicEndPoint = IPSocket.ParseSocketString("82.100.100.1:5060");

                SIPRegistrarRecord registrarRecord = new SIPRegistrarRecord(Guid.NewGuid(), SIPParameterlessURI.ParseSIPParamterlessURI(testURI), testUsername, testDomain, false);
                SIPContactHeader testContactHeader = SIPContactHeader.ParseContactHeader(testHeader + ";expires=10;ftag=12345")[0];

                SIPRequest regRequest = new SIPRequest(SIPMethodsEnum.REGISTER, SIPURI.ParseSIPURI(testURI));
                SIPHeader regHeader = new SIPHeader(testContactHeader, SIPFromHeader.ParseFromHeader("testHeader"), SIPToHeader.ParseToHeader(testHeader), 1, "test");
                regRequest.Header = regHeader;
                regRequest.Header.UserAgent = testUA;
                //MangleContactListForPrivateAddress(regRequest.Header.Contact, publicEndPoint);

                SIPResponseStatusCodesEnum updBindResult;
                string updBindingRespMessage;
                registrarRecord.UpdateBinding(null, null, null, null, regRequest.Header.Contact, regRequest.Header.CallId, regRequest.Header.CSeq, regRequest.Header.Expires, regRequest.Header.UserAgent, out updBindResult, out updBindingRespMessage);

                SIPContactHeader testContactHeader2 = SIPContactHeader.ParseContactHeader(testHeader + ";expires=70;ftag=12345")[0];
                regRequest.Header.Contact[0] = testContactHeader2;
                registrarRecord.UpdateBinding(null, null, null, null, regRequest.Header.Contact, regRequest.Header.CallId, regRequest.Header.CSeq, regRequest.Header.Expires, regRequest.Header.UserAgent, out updBindResult, out updBindingRespMessage);
                //MangleContactListForPrivateAddress(regRequest.Header.Contact, publicEndPoint);

                Console.WriteLine(registrarRecord.BindingsToString());

                Assert.IsTrue(registrarRecord.Bindings.Count == 1, "The SIP registration was not correctly added to the list.");
            }
        }

        #endif

        #endregion
    }
}
