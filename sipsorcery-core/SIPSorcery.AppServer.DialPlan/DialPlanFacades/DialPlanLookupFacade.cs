// ============================================================================
// FileName: DialPlanLookupFacade.cs
//
// Description:
// Facade class to allow dial plan scripts to retrieve data from user populated lookup
// tables. Typically used in dial plan wizard scenarios.
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
using System.Linq;
using System.Text;
using SIPSorcery.Entities;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.AppServer.DialPlan
{
    public class DialPlanLookupFacade
    {
        private static ILog logger = AppState.logger;

        private SIPMonitorLogDelegate LogToMonitor;
        private string m_owner;

        public DialPlanLookupFacade(SIPMonitorLogDelegate logDelegate, string owner)
        {
            LogToMonitor = logDelegate;
            m_owner = owner;
        }

        public DialPlanSettings GetSettings(string dialplanName)
        {
            List<SIPDialplanLookup> lookups = GetLookups(dialplanName);
            List<SIPDialplanRoute> routes = GetRoutes(dialplanName);
            Dictionary<string, SIPDialplanProvider> providers = GetProviders(dialplanName);
            SIPDialplanOption options = GetOptions(dialplanName);

            return new DialPlanSettings(lookups, routes, providers, options);
        }

        public List<SIPDialplanLookup> GetLookups(string dialplanName)
        {
            try
            {
                using (var ssEntities = new SIPSorceryEntities())
                {
                    if (!dialplanName.IsNullOrBlank())
                    {
                        return (from lookup in ssEntities.SIPDialplanLookups
                                join dialplan in ssEntities.SIPDialPlans on lookup.DialPlanID equals dialplan.ID
                                where lookup.Owner == m_owner && dialplan.DialPlanName.Contains(dialplanName)
                                select lookup).ToList();
                    }
                    else
                    {
                        return (from lookup in ssEntities.SIPDialplanLookups
                                where lookup.Owner == m_owner
                                select lookup).ToList();
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetLookups. " + excp.Message);
                return null;
            }
        }

        public List<SIPDialplanRoute> GetRoutes(string dialplanName)
        {
            try
            {
                using (var ssEntities = new SIPSorceryEntities())
                {
                    if (!dialplanName.IsNullOrBlank())
                    {
                        return (from route in ssEntities.SIPDialplanRoutes
                                join dialplan in ssEntities.SIPDialPlans on route.DialPlanID equals dialplan.ID
                                where route.Owner == m_owner && dialplan.DialPlanName.Contains(dialplanName)
                                select route).ToList();
                    }
                    else
                    {
                        return (from route in ssEntities.SIPDialplanRoutes
                                where route.Owner == m_owner
                                select route).ToList();
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetRoutes. " + excp.Message);
                return null;
            }
        }

        public Dictionary<string, SIPDialplanProvider> GetProviders(string dialplanName)
        {
            try
            {
                using (var ssEntities = new SIPSorceryEntities())
                {
                    if (!dialplanName.IsNullOrBlank())
                    {
                        return (from provider in ssEntities.SIPDialplanProviders
                                join dialplan in ssEntities.SIPDialPlans on provider.DialPlanID equals dialplan.ID
                                where provider.Owner == m_owner && dialplan.DialPlanName.Contains(dialplanName)
                                select provider).ToDictionary(x => x.ProviderName);
                    }
                    else
                    {
                        return (from provider in ssEntities.SIPDialplanProviders
                                where provider.Owner == m_owner
                                select provider).ToDictionary(x => x.ProviderName);
                    }
               }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetProviders. " + excp.Message);
                return null;
            }
        }

        public SIPDialplanOption GetOptions(string dialplanName)
        {
            try
            {
                using (var ssEntities = new SIPSorceryEntities())
                {
                    if (!dialplanName.IsNullOrBlank())
                    {
                        return (from option in ssEntities.SIPDialplanOptions
                                join dialplan in ssEntities.SIPDialPlans on option.DialPlanID equals dialplan.ID
                                where option.Owner == m_owner && dialplan.DialPlanName == dialplanName
                                select option).FirstOrDefault();
                    }
                    else
                    {
                        return (from option in ssEntities.SIPDialplanOptions
                                where option.Owner == m_owner
                                select option).FirstOrDefault();
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetOptions. " + excp.Message);
                return null;
            }
        }
    }
}
