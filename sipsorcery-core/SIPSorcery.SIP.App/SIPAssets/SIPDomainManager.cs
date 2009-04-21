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
using System.Linq.Expressions;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP.App
{
    public delegate string GetCanonicalDomainDelegate(string host);     // Used to get the canonical domain from a host portion of a SIP URI.

    /// <summary>
    /// This class maintains a list of domains that are being maintained by this process.
    /// </summary>
    public class SIPDomainManager 
    {
        private ILog logger = AppState.logger;

        private Dictionary<string, SIPDomain> m_domains = new Dictionary<string, SIPDomain>();  // Records the domains that are being maintained.
        
        private SIPAssetPersistor<SIPDomain> m_sipDomainPersistor;
        public SIPAssetPersistor<SIPDomain> SIPDomainPersistor { get { return m_sipDomainPersistor; } private set { } }

        public SIPDomainManager()
        { }

        public SIPDomainManager(StorageTypes storageType, string storageConnectionStr)
        {
            m_sipDomainPersistor = SIPAssetPersistorFactory.CreateSIPDomainPersistor(storageType, storageConnectionStr);

            LoadSIPDomains();
        }

        private void LoadSIPDomains()
        {
            List<SIPDomain> sipDomains = m_sipDomainPersistor.Get(null, 0, Int32.MaxValue);
            m_domains.Clear();
            foreach (SIPDomain sipDomain in sipDomains)
            {
                AddDomain(sipDomain);
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
                if (m_domains.ContainsKey(sipDomain.Domain.ToLower()))
                {
                    m_domains.Remove(sipDomain.Domain.ToLower());
                }

                m_domains.Add(sipDomain.Domain.ToLower(), sipDomain);
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
        public string GetDomain(string host)
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
                    return host.ToLower();
                }
                else
                {
                    foreach(SIPDomain sipDomain in m_domains.Values)
                    {
                        if (sipDomain.Aliases != null) {
                            foreach (string alias in sipDomain.Aliases) {
                                if (alias.ToLower() == host.ToLower()) {
                                    return sipDomain.Domain;
                                }
                            }
                        }
                    }

                    return null;
                }
            }
        }

        /// <summary>
        /// Checks whether a host name is in the list of supported domains and aliases.
        /// </summary>
        /// <param name="host"></param>
        /// <returns>True if the host is present as a domain or an alias, false otherwise.</returns>
        public bool HasDomain(string host)
        {
           return GetDomain(host) != null;
        }

        public List<SIPDomain> Get(Expression<Func<SIPDomain, bool>> whereClause, int offset, int count)
        {
            return m_sipDomainPersistor.Get(whereClause, offset, count);
        }
    }
}
