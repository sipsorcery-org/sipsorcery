// ============================================================================
// FileName: SIPDomainManager.cs
//
// Description:
// Maintains a list of domains and domain aliases that can be used by various
// SIP Server agents. For example allows a SIP Registrar or Proxy to check the 
// domain on an incoming request to see if it is serviced at this location.
//
// Author(s):
// Aaron Clauson
//
// History:
// 27 Jul 2008	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.IO;
using System.Linq;
using System.Linq.Dynamic;
using System.Linq.Expressions;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using SIPSorcery.Persistence;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP.App
{
    public delegate string GetCanonicalDomainDelegate(string host, bool wildCardOk);     // Used to get the canonical domain from a host portion of a SIP URI.

    /// <summary>
    /// This class maintains a list of domains that are being maintained by this process.
    /// </summary>
    public class SIPDomainManager
    {
        public const string WILDCARD_DOMAIN_ALIAS = "*";
        public const string DEFAULT_LOCAL_DOMAIN = "local";

        private ILog logger = AppState.logger;

        private static readonly string m_storageFileName = AssemblyState.XML_DOMAINS_FILENAME;

        private Dictionary<string, SIPDomain> m_domains = new Dictionary<string, SIPDomain>();  // Records the domains that are being maintained.
        private SIPAssetPersistor<SIPDomain> m_sipDomainPersistor;
        private SIPDomain m_wildCardDomain;

        //public SIPDomainManager()
        //{ }

        public SIPDomainManager(StorageTypes storageType, string storageConnectionStr)
        {
            m_sipDomainPersistor = SIPAssetPersistorFactory<SIPDomain>.CreateSIPAssetPersistor(storageType, storageConnectionStr, m_storageFileName);
            m_sipDomainPersistor.Added += new SIPAssetDelegate<SIPDomain>(d => { LoadSIPDomains(); });
            m_sipDomainPersistor.Deleted += new SIPAssetDelegate<SIPDomain>(d => { LoadSIPDomains(); });
            m_sipDomainPersistor.Updated += new SIPAssetDelegate<SIPDomain>(d => { LoadSIPDomains(); });
            m_sipDomainPersistor.Modified += new SIPAssetsModifiedDelegate(() => { LoadSIPDomains(); });
            LoadSIPDomains();
        }

        private void LoadSIPDomains()
        {
            try
            {
                List<SIPDomain> sipDomains = m_sipDomainPersistor.Get(null, null, 0, Int32.MaxValue);
                m_domains.Clear();
                foreach (SIPDomain sipDomain in sipDomains)
                {
                    AddDomain(sipDomain);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception LoadSIPDomains. " + excp.Message);
                throw;
            }
        }

        public void AddDomain(SIPDomain sipDomain)
        {
            if (sipDomain == null)
            {
                throw new ArgumentException("The SIPDomainManager cannot add a null SIPDomain object.");
            }
            else
            {
                if (!m_domains.ContainsKey(sipDomain.Domain.ToLower()))
                {
                    logger.Debug(" SIPDomainManager added domain: " + sipDomain.Domain + ".");
                    sipDomain.Aliases.ForEach(a => Console.WriteLine(" added domain alias " + a + "."));
                    m_domains.Add(sipDomain.Domain.ToLower(), sipDomain);

                    foreach (string alias in sipDomain.Aliases) {
                        if (alias == WILDCARD_DOMAIN_ALIAS && m_wildCardDomain == null) {
                            m_wildCardDomain = sipDomain;
                            logger.Debug(" SIPDomainManager wildcard domain set to " + sipDomain.Domain + ".");
                        }
                    }
                }
                else
                {
                    logger.Warn("SIPDomainManager ignoring duplicate domain entry for " + sipDomain.Domain.ToLower() + ".");
                }
            }
        }

        public void RemoveDomain(SIPDomain sipDomain)
        {
            if (sipDomain != null)
            {
                if (m_domains.ContainsKey(sipDomain.Domain.ToLower()))
                {
                    m_domains.Remove(sipDomain.Domain.ToLower());
                } 
            }
        }

        /// <summary>
        /// Checks whether there the supplied hostname represents a serviced domain or alias.
        /// </summary>
        /// <param name="host">The hostname to check for a serviced domain for.</param>
        /// <returns>The canconical domain name for the host if found or null if not.</returns>
        public string GetDomain(string host, bool wilcardOk) {
            SIPDomain domain = GetSIPDomain(host, wilcardOk);
            return (domain != null) ? domain.Domain.ToLower() : null;
        }

        private SIPDomain GetSIPDomain(string host, bool wildcardOk)
        {
            //logger.Debug("SIPDomainManager GetDomain for " + host + ".");

            if (host == null)
            {
                return null;
            }
            else
            {
                if(m_domains.ContainsKey(host.ToLower()))
                {
                    return m_domains[host.ToLower()];
                }
                else
                {
                    foreach(SIPDomain sipDomain in m_domains.Values)
                    {
                        if (sipDomain.Aliases != null) {
                            foreach (string alias in sipDomain.Aliases) {
                                if (alias.ToLower() == host.ToLower()) {
                                    return sipDomain;
                                }
                            }
                        }
                    }

                    if (wildcardOk) {
                        return m_wildCardDomain;
                    }
                    else {
                        return null;
                    }
                }
            }
        }

        /// <summary>
        /// Checks whether a host name is in the list of supported domains and aliases.
        /// </summary>
        /// <param name="host"></param>
        /// <returns>True if the host is present as a domain or an alias, false otherwise.</returns>
        public bool HasDomain(string host, bool wildcardOk)
        {
           return GetDomain(host, wildcardOk) != null;
        }

        public List<SIPDomain> Get(Expression<Func<SIPDomain, bool>> whereClause, int offset, int count)
        {
            try
            {
                List<SIPDomain> subList = null;
                if (whereClause == null)
                {
                    subList = m_domains.Values.ToList<SIPDomain>();
                }
                else
                {
                    subList = m_domains.Values.Where(a => whereClause.Compile()(a)).ToList<SIPDomain>();
                }

                if (subList != null)
                {
                    if (offset >= 0)
                    {
                        if (count == 0 || count == Int32.MaxValue)
                        {
                            return subList.OrderBy(x => x.Domain).Skip(offset).ToList<SIPDomain>();
                        }
                        else
                        {
                            return subList.OrderBy(x => x.Domain).Skip(offset).Take(count).ToList<SIPDomain>();
                        }
                    }
                    else
                    {
                        return subList.OrderBy(x => x.Domain).ToList<SIPDomain>(); ;
                    }
                }

                return subList;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPDomainManager Get. " + excp.Message);
                return null;
            }
        }

        public void AddAlias(string domain, string alias) {
            try {
                if(domain.IsNullOrBlank() || alias.IsNullOrBlank()) {
                    logger.Warn("AddAlias was passed a null alias or domain.");
                }
                else if (!HasDomain(alias.ToLower(), false) && HasDomain(domain.ToLower(), false)) {
                    SIPDomain sipDomain = GetSIPDomain(domain.ToLower(), false);
                    if (alias == WILDCARD_DOMAIN_ALIAS) {
                        if (m_wildCardDomain != null) {
                            m_wildCardDomain = sipDomain;
                            logger.Debug(" SIPDomainManager wildcard domain set to " + sipDomain.Domain + ".");
                        }
                    }
                    else {
                        sipDomain.Aliases.Add(alias.ToLower());
                        logger.Debug(" SIPDomainManager added alias to " + sipDomain.Domain + " of " + alias.ToLower() + ".");
                    }
                }
                else {
                    logger.Warn("Could not add alias " + alias + " to domain " + domain + ".");
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SIPDomainManager AddAlias. " + excp.Message);
            }
        }

        public void RemoveAlias(string alias) {
            try {
                if (alias.IsNullOrBlank()) {
                    logger.Warn("RemoveAlias was passed a null alias.");
                }
                else if (HasDomain(alias.ToLower(), false)) {
                    SIPDomain sipDomain = GetSIPDomain(alias.ToLower(), false);
                    sipDomain.Aliases.Remove(alias.ToLower());
                }
                else {
                    logger.Warn("Could not remove alias " + alias + ".");
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SIPDomainManager RemoveAlias. " + excp.Message);
            }
        }
    }
}
