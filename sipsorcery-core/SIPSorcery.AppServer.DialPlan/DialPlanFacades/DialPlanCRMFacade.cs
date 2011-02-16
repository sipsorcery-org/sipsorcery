// ============================================================================
// FileName: DialPlanCRMFacade.cs
//
// Description:
// Facade class to allow easy integration with 3rd party CRM systems from dial plan
// scripts.
//
// Author(s):
// Aaron Clauson
//
// History:
// 04 Feb 2011  Aaron Clauson   Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2011 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Ltd. 
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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using SIPSorcery.CRM.ThirtySevenSignals;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.AppServer.DialPlan
{
    public class DialPlanCRMFacade
    {
        private static ILog logger = AppState.logger;

        private SIPMonitorLogDelegate LogToMonitor;
        private DialPlanContext m_context;

        public DialPlanCRMFacade(SIPMonitorLogDelegate logDelegate, DialPlanContext context)
        {
            LogToMonitor = logDelegate;
            m_context = context;
        }

        public void LookupHighriseContact(string url, string authToken, SIPFromHeader from)
        {
            LookupHighriseContact(url, authToken, from.FromName);
        }

        public void LookupHighriseContact(string url, string authToken, string name)
        {
            LogToMonitor(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Looking up Highrise contact on " + url + " for " + name + ".", m_context.Owner));

            m_context.SetCallerDetails(new CRMHeaders() { Pending = true });

            ThreadPool.QueueUserWorkItem(delegate { DoLookup(url, authToken, name); });
        }

        private void DoLookup(string url, string authToken, string name)
        {
            try
            {
                PersonRequest personRequest = new PersonRequest(url, authToken);
                People people = personRequest.GetByName(name);

                if (people != null && people.PersonList != null && people.PersonList.Count > 0)
                {
                    Person person = people.PersonList[0];
                    string companyName = null;

                    if (person.CompanyID != null)
                    {
                        CompanyRequest companyRequest = new CompanyRequest(url, authToken);
                        Company company = companyRequest.GetByID(person.CompanyID.Value);

                        if (company != null)
                        {
                            companyName = company.Name;
                        }
                    }

                    LogToMonitor(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Highrise contact match " + person.FirstName + " " + person.LastName + " of " + companyName + ".", m_context.Owner));
                    m_context.SetCallerDetails(new CRMHeaders(person.FirstName + " " + person.LastName, companyName, person.AvatarURL));
                }
                else
                {
                    LogToMonitor(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "No Highrise contact match.", m_context.Owner));
                    m_context.SetCallerDetails(new CRMHeaders() { Pending = false, LookupError = "No Highrise contact match." });
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception LookupHighriseContact. " + excp.Message);
                LogToMonitor(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Error looking up Highrise contact.", m_context.Owner));
                m_context.SetCallerDetails(new CRMHeaders() { Pending = false, LookupError = "Error looking up Highrise contact." });
            }
        }
    }
}
