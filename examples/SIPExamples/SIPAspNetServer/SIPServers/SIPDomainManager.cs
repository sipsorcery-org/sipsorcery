// ============================================================================
// FileName: SIPDomainManager.cs
//
// Description:
// Maintains a list of domains and domain aliases that can be used by various
// SIP Server agents. For example allows a SIP Registrar or Proxy to check the 
// domain on an incoming request to see if it is serviced at this location.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 27 Jul 2008	Aaron Clauson	Created, Hobart, Australia.
// 30 Dec 2020  Aaron Clauson   Added to server project.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SIPAspNetServer.DataAccess;

namespace SIPAspNetServer
{
    /// <summary>
    /// This class maintains a list of domains that are being maintained by this process.
    /// </summary>
    public class SIPDomainManager
    {
        public const string WILDCARD_DOMAIN_ALIAS = "*";
        public const string DEFAULT_LOCAL_DOMAIN = "local";

        private readonly ILogger Logger = SIPSorcery.LogFactory.CreateLogger<SIPDomainManager>();

        private Dictionary<string, SIPDomain> m_domains = new Dictionary<string, SIPDomain>();  // Records the domains that are being maintained.
        private SIPDomain m_wildCardDomain;

        public SIPDomainManager(DbSet<SIPDomain> SIPDomains)
        {
            if (SIPDomains == null || SIPDomains.Count() == 0)
            {
                throw new ApplicationException("No SIP domains could be loaded from the database. There needs to be at least one domain.");
            }
            else
            {
                m_domains.Clear();

                foreach (SIPDomain SIPDomain in SIPDomains)
                {
                    AddDomain(SIPDomain);
                }
            }
        }

        public void AddDomain(SIPDomain SIPDomain)
        {
            if (SIPDomain == null)
            {
                throw new ArgumentException("SIPDomainManager cannot add a null SIPDomain object.");
            }
            else
            {
                if (!m_domains.ContainsKey(SIPDomain.Domain.ToLower()))
                {
                    Logger.LogDebug($"SIPDomainManager added domain: {SIPDomain.Domain} with alias list {SIPDomain.AliasList}.");

                    m_domains.Add(SIPDomain.Domain.ToLower(), SIPDomain);
                    
                    if (m_wildCardDomain == null && SIPDomain.Aliases.Contains(WILDCARD_DOMAIN_ALIAS))
                    {
                        m_wildCardDomain = SIPDomain;
                        Logger.LogDebug($"SIPDomainManager wildcard domain set to {SIPDomain.Domain}.");
                    }
                }
                else
                {
                    Logger.LogWarning($"SIPDomainManager ignoring duplicate domain entry for {SIPDomain.Domain.ToLower()}.");
                }
            }
        }

        /// <summary>
        /// Checks whether there the supplied hostname represents a serviced domain or alias.
        /// </summary>
        /// <param name="host">The hostname to check for a serviced domain for.</param>
        /// <returns>The canconical domain name for the host if found or null if not.</returns>
        public string GetDomain(string host, bool wilcardOk)
        {
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
                if (m_domains.ContainsKey(host.ToLower()))
                {
                    return m_domains[host.ToLower()];
                }
                else
                {
                    foreach (SIPDomain SIPDomain in m_domains.Values)
                    {
                        if (SIPDomain.Aliases != null)
                        {
                            foreach (string alias in SIPDomain.Aliases)
                            {
                                if (alias.ToLower() == host.ToLower())
                                {
                                    return SIPDomain;
                                }
                            }
                        }
                    }

                    if (wildcardOk)
                    {
                        return m_wildCardDomain;
                    }
                    else
                    {
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
    }
}
