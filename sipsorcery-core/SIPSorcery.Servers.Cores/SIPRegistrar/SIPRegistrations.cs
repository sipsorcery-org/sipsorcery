// ============================================================================
// FileName: SIPRegistrations.cs
//
// Description:
// Maintains a list of all the registered users for the SIP Registrar.
//
// Author(s):
// Aaron Clauson
//
// History:
// 23 Aug 2008	Aaron Clauson	Created, refactored from RegistrarCore.cs.
// 10 Nov 2008  Aaron Clauson   Moved the Registrar Record persistence to SIPRegistrarRecordManager class.
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
using System.Linq;
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
    /// This class maintains a list of all the registered users for the SIP Registrar.
    /// </summary>
    public class SIPRegistrations
    {
        private const int CHECK_REGEXPIRY_DURATION = 1000;   // Period at which to check for expired bindings.
        public const int NATKEEPALIVE_DEFAULTSEND_INTERVAL = 15;

        private static ILog logger = AppState.GetLogger("registrar");

        private SIPMonitorLogDelegate SIPMonitorEventLog_External;
        private SIPAssetUpdateDelegate<SIPRegistrarBinding> UpdateBinding_External;
        private SendNATKeepAliveDelegate SendNATKeepAlive_External;
        private GetCanonicalDomainDelegate GetCanonicalDomain_External;

        private Dictionary<string, SIPRegistrarRecord> m_registrations = new Dictionary<string, SIPRegistrarRecord>();	// [<address of record>,<registrar record>] Store registrations.

        public int NATKeepAliveSendInterval = NATKEEPALIVE_DEFAULTSEND_INTERVAL;        // Send NATKeepAlives at this period in seconds.
        private Thread m_checkRegExpiryThread;
        public bool Stop = false;

        public SIPRegistrations(
            GetCanonicalDomainDelegate getCanonicalDomain, 
            SIPMonitorLogDelegate sipMonitorEventLog, 
            SIPAssetGetDelegate<SIPAccount> getSIPAccount,
            SIPAssetUpdateDelegate<SIPRegistrarBinding> updateBinding, 
            SendNATKeepAliveDelegate sendNATKeepAlive)
        {
            GetCanonicalDomain_External = getCanonicalDomain;
            SIPMonitorEventLog_External = sipMonitorEventLog;
            GetSIPAccount_External = getSIPAccount;
            UpdateBinding_External = updateBinding;
            SendNATKeepAlive_External = sendNATKeepAlive;
        }

        public void StartCheckContacts()
        {
            // This thread is needed to remove expired registration records from the SIP Registrar. If a user agent drops offline then this
            // thread will detect the expired binding and add it to the dirtyregistrations list.
            m_checkRegExpiryThread = new Thread(new ThreadStart(CheckContacts));
            m_checkRegExpiryThread.Start();
        }
        
        public SIPRegistrarRecord Lookup(SIPParameterlessURI uri)
        {
            SIPParameterlessURI canonicalAddressOfRecord = CanonicaliseAddressOfRecord(uri.Scheme, uri.User, uri.Host);

            if (canonicalAddressOfRecord == null)
            {
                return null;
            }
            else
            {
                SIPRegistrarRecord registrarRecord = Get(canonicalAddressOfRecord);
                SIPAccount sipAccount = GetSIPAccount_External("sipusername='" + uri.User + "' and sipdomain='" + uri.Host + "'");

                if (registrarRecord != null)
                {
                    // Refresh password.
                    try
                    {
                        if (sipAccount != null)
                        {
                            SIPRegistrarRecord persistedRecord = new SIPRegistrarRecord(SIPMonitorEventLog_External, sipAccount);
                            registrarRecord.Password = persistedRecord.Password;
                            registrarRecord.NATSendKeepAlives = persistedRecord.NATSendKeepAlives;
                            registrarRecord.SIPAccountId = persistedRecord.SIPAccountId;    // Had a case where a user deleted and re-created SIP account with same name!
                        }
                        else
                        {
                            logger.Debug("User record " + canonicalAddressOfRecord.ToString() + " no longer in persistence store, remove entry from registrar.");
                            Remove(canonicalAddressOfRecord);

                            return null;
                        }
                    }
                    catch (Exception excp)
                    {
                        logger.Error("Exception SIPRegistrations Lookup. " + excp.Message);
                        throw excp;
                    }

                    return registrarRecord;
                }
                else
                {
                    try
                    {
                        if (sipAccount != null)
                        {
                            SIPRegistrarRecord newRegistrarRecord = new SIPRegistrarRecord(SIPMonitorEventLog_External, sipAccount);
                            Add(newRegistrarRecord);
                            return newRegistrarRecord;
                        }
                        else
                        {
                            return null;
                        }
                    }
                    catch (Exception excp)
                    {
                        logger.Error("Exception SIPRegistrations Lookup. " + excp.Message);
                        throw excp;
                    }
                }
            }
        }

        /// <summary>
        /// Adds a registration record to the dirty registrations queue indicating it should be persisted if persistence is being used.
        /// </summary>
        public void AddDirtyRegistration(SIPRegistrarRecord registrarRecord)
        {
            try
            {
                if (UpdateBinding_External != null)
                {
                    List<SIPRegistrarBinding> updatedBindings = registrarRecord.GetDirtyBindings();
                    if (updatedBindings != null)
                    {
                        for (int index = 0; index < updatedBindings.Count; index++)
                        {
                            //logger.Debug("Persisting binding " + updatedBindings[index].ContactURI.ToString() + ", expiry=" + updatedBindings[index].Expiry + ", removal reason=" + updatedBindings[index].RemovalReason  + ".");

                            UpdateBinding_External(updatedBindings[index]);
                            updatedBindings[index].Dirty = false;

                            if (updatedBindings[index].RemovalReason != SIPBindingRemovalReason.Unknown || updatedBindings[index].ExpiryTime < DateTime.Now)
                            {
                                //logger.Debug("Removing binding " + updatedBindings[index].ContactURI.ToString() + ".");

                                lock (registrarRecord.Bindings)
                                {
                                    registrarRecord.Bindings.Remove(updatedBindings[index].ContactURI);
                                }

                                FireSIPMonitorEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.BindingRemoval, "Removing binding " + updatedBindings[index].ContactURI.ToString() + " due to " + updatedBindings[index].RemovalReason.ToString() + " (last register " + updatedBindings[index].LastUpdate.ToString("HH:mm:ss") + ")", registrarRecord.AuthUser));
                                FireSIPMonitorEvent(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPRegistrarBindingRemoval, updatedBindings[index].Owner, updatedBindings[index].RemoteSIPEndPoint.SocketEndPoint, updatedBindings[index].BindingId.ToString()));
                            }
                            else
                            {
                                FireSIPMonitorEvent(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPRegistrarBindingUpdate, updatedBindings[index].Owner, updatedBindings[index].RemoteSIPEndPoint.SocketEndPoint, updatedBindings[index].BindingId.ToString()));
                            }
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception AddDirtyRegistration. " + excp.Message);
            }
        }

        public bool Exists(SIPParameterlessURI addressOfRecord)
        {
            return m_registrations.ContainsKey(addressOfRecord.ToString());
        }

        public SIPRegistrarRecord Get(SIPParameterlessURI uri)
        {
            if (m_registrations.ContainsKey(uri.ToString()))
            {
                return m_registrations[uri.ToString()];
            }
            else
            {
                return null;
            }
        }

        public void UpdateContactSDPOwner(SIPParameterlessURI addressOfRecord, SIPContactHeader contact, string sdpOwner)
        {
            SIPRegistrarRecord registrarRecord = Lookup(addressOfRecord);

            if (registrarRecord != null)
            {
                registrarRecord.UpdateContactSDPOwner(contact.ContactURI, sdpOwner);
            }
        }

        private SIPParameterlessURI CanonicaliseAddressOfRecord(SIPSchemesEnum scheme, string user, string host)
        {
            SIPParameterlessURI canonicalAddress = new SIPParameterlessURI(scheme, host, user);
            
            string canonicalHost = GetCanonicalDomain_External(host);
            if (canonicalHost != null)
            {
                canonicalAddress.Host = canonicalHost;
                return canonicalAddress;
            }
            else
            {
                return null;
            }
        }

        private void CheckContacts()
        {
            try
            {
                while (!Stop)
                {
                    foreach (SIPRegistrarRecord regRecord in m_registrations.Values)
                    {
                        if (regRecord.HasDirtyBinding())
                        {
                            // One or more bindings have changed, need to persist.
                            //logger.Debug(regRecord.AddressOfRecord.ToString() + " has a binding that needs to be updated.");
                            AddDirtyRegistration(regRecord);
                        }

                        if (regRecord.HasCurrentBinding())
                        {
                            SendNATKeepAlives(regRecord);
                        }
                    }

                    Thread.Sleep(CHECK_REGEXPIRY_DURATION);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception CheckContacts. " + excp.Message);
            }
        }

        private void SendNATKeepAlives(SIPRegistrarRecord regRecord)
        {
            try
            {
                lock (regRecord.Bindings)
                {
                    if (regRecord.Bindings.Count > 0)
                    {
                        foreach (SIPRegistrarBinding binding in regRecord.GetBindings())
                        {
                            if (regRecord.NATSendKeepAlives && SendNATKeepAlive_External != null && binding.MangledContactURI != null)
                            {
                                // If a user has been specified as requiring NAT keep-alives to be sent then they are identified here and a message is sent to
                                // the SIP proxy with the contact socket the keep-alive should be sent to.
                                if (binding.LastNATKeepAliveSendTime == null || DateTime.Now.Subtract(binding.LastNATKeepAliveSendTime.Value).TotalSeconds > NATKeepAliveSendInterval)
                                {
                                    IPEndPoint sendFromEndPoint = (binding.ProxyEndPoint == null) ? binding.RegistrarEndPoint : binding.ProxyEndPoint;
                                    SendNATKeepAlive_External(new NATKeepAliveMessage(new SIPEndPoint(SIPProtocolsEnum.udp, IPSocket.ParseSocketString(binding.MangledContactURI.Host)), sendFromEndPoint));
                                    binding.LastNATKeepAliveSendTime = DateTime.Now;
                                    FireSIPMonitorEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.NATKeepAlive, SIPMonitorEventTypesEnum.NATKeepAlive, "Requesting NAT keep-alive from proxy socket " + sendFromEndPoint + " to " + binding.ContactURI.Protocol + ":" + binding.MangledContactURI.ToString() + " for " + regRecord.AddressOfRecord.ToString() + ".", regRecord.AuthUser));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendNATKeepAlives. " + excp.Message);
            }
        }

        private void Add(SIPRegistrarRecord registrarRecord)
        {
            logger.Debug("Adding SIPRegistrarRecord for " + registrarRecord.AddressOfRecord.ToString() + ".");

            if (m_registrations.ContainsKey(registrarRecord.AddressOfRecord.ToString()))
            {
                m_registrations.Remove(registrarRecord.AddressOfRecord.ToString());
            }

            m_registrations.Add(registrarRecord.AddressOfRecord.ToString(), registrarRecord);
        }

        private void Remove(SIPParameterlessURI addressOfRecord)
        {
            if (m_registrations.ContainsKey(addressOfRecord.ToString()))
            {
                SIPRegistrarRecord registrarRecord = m_registrations[addressOfRecord.ToString()];

                m_registrations.Remove(addressOfRecord.ToString());

                List<SIPRegistrarBinding> bindings = new List<SIPRegistrarBinding>();
                foreach (SIPRegistrarBinding binding in registrarRecord.Bindings.Values)
                {
                    bindings.Add(binding);
                }

                for (int index = 0; index < bindings.Count; index++)
                {
                    if(bindings[index].RemovalReason == SIPBindingRemovalReason.Unknown)
                    {
                        bindings[index].RemovalReason = SIPBindingRemovalReason.Administrative;
                        UpdateBinding_External(bindings[index]);
                    }
                }
            }
        }

        private void FireSIPMonitorEvent(SIPMonitorEvent monitorEvent)
        {
            if (SIPMonitorEventLog_External != null)
            {
                try
                {
                    SIPMonitorEventLog_External(monitorEvent);
                }
                catch (Exception excp)
                {
                    logger.Error("Exception FireSIPMonitorEvent SIPRegistrations. " + excp.Message);
                }
            }
        }

        #region Unit testing.

        #if UNITTEST
	
		[TestFixture]
		public class SIPRegistrationsUnitTest
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
            public void LookupRegistrationUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                /*SIPRegistrations.m_registrations = new Dictionary<string, List<SIPRegistration>>();

                string testUsername = "user";
                string testDomain = "domain";
                string testUA = "my useragent v1.0";
                SIPContactHeader testContactHeader = SIPContactHeader.ParseContactHeader("<sip:user@sip.domain.com;ftag=abcd>;expires=20")[0];

                SIPRegistrations.UpdateContact(new SIPRegistration(testUsername, testDomain, testContactHeader, testUA));

                List<SIPContactHeader> contacts = SIPRegistrations.Lookup(testUsername + "@" + testDomain);

                Assert.IsTrue(contacts.Count == 1, "The number of contacts returned by the lookup was incorrect.");
                Assert.IsTrue(contacts[0].ToString() == "<sip:user@sip.domain.com;ftag=abcd>;expires=20", "The contact returned by the lookup was incorrect.");
                Assert.IsTrue(contacts[0].Expires == 20, "The contact expiry was incorrect.");
                Assert.IsTrue(contacts[0].ContactURI.Parameters.Get("ftag") == "abcd", "The contact URI ftag parameter was incorrect.");*/
            }
        }

        #endif

        #endregion
    }
}
