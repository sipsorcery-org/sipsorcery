// ============================================================================
// FileName: SIPRegistrationAgentPersistor.cs
//
// Description:
// Persists contacts that have been registered with 3rd party SIP Registrars. The contacts correspond to
// the registration requests specified in SIPProvider's.
//
// Author(s):
// Aaron Clauson
//
// History:
// 17 May 2008	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using BlueFace.VoIP.App;
using BlueFace.VoIP.App.SIP;
using BlueFace.VoIP.Authentication;
using BlueFace.VoIP.Net;
using BlueFace.VoIP.Net.SIP;
using BlueFace.VoIP.SIPServerCores.StatefulProxy;
using BlueFace.Sys;
using log4net;

namespace BlueFace.VoIP.SIPServer
{
    public class SIPRegistrationAgentPersistor
    {
        public const int MAX_DIRTYQUEUE_SIZE = 1000;        // Maximum number of items that can be added to the queue waiting to be updated to the database.
        public const int MAX_PERSISTOR_THREADS = 10;        // Maximum number of threads that can be specified to perform database updates (should typically be 3 to 5).
        public const string REGAGENT_THREADNAME_KEY = "regagent-persist-";
        public const int CHECK_DIRTYRECORDS_INTERVAL = 1000;// Period the thread will sleep for once all dirty records have been persisted.

        private static ILog logger = AppState.logger;

        public event SIPMonitorLogDelegate StatefulProxyLogEvent;

        private Queue<SIPProviderBinding> m_dirtyRegistrations = new Queue<SIPProviderBinding>();    // List of registrations that have an updated contact that needs persisting.
        private StorageLayer m_storageLayer;
        public Thread[] DBUpdateThreads;

        public bool Stop = false;

        public SIPRegistrationAgentPersistor(StorageTypes storageType, string dbConnStr)
        {
            m_storageLayer = new StorageLayer(storageType, dbConnStr);
        }

        public void StartPersistorThreads(int numberThreads)
        {
            try
            {
                logger.Debug("SIPRegistrationAgentPersistor threads starting.");

                if (numberThreads <= 0)
                {
                    numberThreads = 1;
                }
                else if (numberThreads > MAX_PERSISTOR_THREADS)
                {
                    numberThreads = MAX_PERSISTOR_THREADS;
                }

                DBUpdateThreads = new Thread[numberThreads];
                for (int index = 0; index < numberThreads; index++)
                {
                    DBUpdateThreads[index] = new Thread(new ThreadStart(PersistUserRegistrationToDB));
                    DBUpdateThreads[index].Name = REGAGENT_THREADNAME_KEY + index.ToString();
                    DBUpdateThreads[index].Start();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception StartPersistorThreads. " + excp.Message);
                throw excp;
            }
        }

        public void RegistrationStateChanged(SIPProviderBinding userRegistration)
        {
            if (!m_dirtyRegistrations.Contains(userRegistration))
            {
                if (m_dirtyRegistrations.Count < MAX_DIRTYQUEUE_SIZE)
                {
                    m_dirtyRegistrations.Enqueue(userRegistration);
                }
                else
                {
                    logger.Error("Dirty registration could not be added to SIPRegistrationAgent queue as maximum dirty queue size of " + MAX_DIRTYQUEUE_SIZE + " has been reached.");
                }
            }
        }

        private void PersistUserRegistrationToDB()
        {
            try
            {
                while (!Stop)
                {
                    string userRegDescr = null;

                    try
                    {
                        SIPProviderBinding dirtyRecord = null;

                        lock (m_dirtyRegistrations)
                        {
                            if (m_dirtyRegistrations.Count > 0)
                            {
                                dirtyRecord = m_dirtyRegistrations.Dequeue();
                            }
                        }

                        if (dirtyRecord != null)
                        {
                            userRegDescr = dirtyRecord.Owner + " with " + dirtyRecord.Registrar;

                            if (dirtyRecord.Disabled)
                            {
                                m_storageLayer.ExecuteNonQuery("delete from sipprovidercontacts where providerid = '" + dirtyRecord.RegistrationId + "'");
                                m_storageLayer.ExecuteNonQuery("update sipproviders set registerenabled = '0', registerdisabledreason = 'Disabled by agent: " + dirtyRecord.FailureMessage.Replace("'", "''") + "' where providerid = '" + dirtyRecord.RegistrationId + "'");
                            }
                            else if (dirtyRecord.DeleteRequired)
                            {
                                m_storageLayer.ExecuteNonQuery("delete from sipprovidercontacts where providerid = '" + dirtyRecord.RegistrationId + "'");
                            }
                            else
                            {
                                string registeredContactStr = (dirtyRecord.Contact != null) ? "'" + dirtyRecord.Contact.ToString().Replace("'", "''") + "'" : "null";
                                string failureMessgeStr = (dirtyRecord.FailureMessage != null) ? "'" + dirtyRecord.FailureMessage.Replace("'", "''") + "'" : "null";
                                string lastRegisterStr = (dirtyRecord.LastRegisterAttempt == DateTime.MinValue) ? "null" : "'" + dirtyRecord.LastRegisterAttempt.ToString("dd MMM yyyy HH:mm:ss") + "'";
                                string nextRegisterStr = (dirtyRecord.NextRegistrationTime == DateTime.MinValue) ? "null" : "'" + dirtyRecord.NextRegistrationTime.ToString("dd MMM yyyy HH:mm:ss") + "'";

                                bool exists = Convert.ToInt32(m_storageLayer.ExecuteScalar("select count(*) from sipprovidercontacts where providerid = '" + dirtyRecord.RegistrationId + "'")) > 0;

                                if (!exists)
                                {
                                    string insertSQL =
                                        "insert into sipprovidercontacts (" +
                                        "providercontactid, " +
                                        "providerid, " +
                                        "registeredcontact, " +
                                        "protocol, " +
                                        "failuremessage, " +
                                        "expiry, " +
                                        "lastregistersent, " +
                                        "nextregister, " +
                                        "retryinterval, " +
                                        "registrationagentserver) values (" +
                                        "'" + Guid.NewGuid() + "', " +
                                        "'" + dirtyRecord.RegistrationId + "', " +
                                        registeredContactStr + ", " +
                                        "'" + dirtyRecord.Protocol + "', " +
                                        failureMessgeStr + ", " +
                                        dirtyRecord.ExpirySeconds + ", " +
                                        lastRegisterStr + ", " +
                                        nextRegisterStr + ", " +
                                        dirtyRecord.RegistrationRetryInterval + ", " +
                                        "'" + dirtyRecord.RegistrationAgentIPEndPoint + "')";

                                    //logger.Debug(insertSQL);

                                    m_storageLayer.ExecuteNonQuery(insertSQL);
                                }
                                else
                                {
                                    string updateSQL =
                                        "update sipprovidercontacts set " +
                                        "registeredcontact = " + registeredContactStr + ", " +
                                        "protocol = '" + dirtyRecord.Protocol + "', " +
                                        "failuremessage = " + failureMessgeStr + ", " +
                                        "expiry = " + dirtyRecord.ExpirySeconds + ", " +
                                        "lastregistersent = " + lastRegisterStr + ", " +
                                        "nextregister = " + nextRegisterStr + ", " +
                                        "retryinterval = " + dirtyRecord.RegistrationRetryInterval + ", " +
                                        "registrationagentserver = '" + dirtyRecord.RegistrationAgentIPEndPoint + "' " +
                                        "where providerid = '" + dirtyRecord.RegistrationId + "'";

                                    //logger.Debug(updateSQL);

                                    m_storageLayer.ExecuteNonQuery(updateSQL);
                                }
                            }
                        }
                        else
                        {
                            Thread.Sleep(CHECK_DIRTYRECORDS_INTERVAL);
                        }
                    }
                    catch (Exception dbExcp)
                    {
                        logger.Error("Exception SIPRegistrationAgentPersistor updating db registration (" + userRegDescr + "). " + dbExcp.Message);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception PersistUserRegistrationToDB. " + excp.Message);
                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.Error, "Exception PersistUserRegistrationToDB STOPPED. " + excp.Message, null));
            }
        }

        private void FireProxyLogEvent(SIPMonitorEvent monitorEvent)
        {
            if (StatefulProxyLogEvent != null)
            {
                try
                {
                    StatefulProxyLogEvent(monitorEvent);
                }
                catch (Exception excp)
                {
                    logger.Error("Exception FireProxyLogEvent SIPRegistrationAgentPersistor. " + excp.Message);
                }
            }
        }
    }
}
