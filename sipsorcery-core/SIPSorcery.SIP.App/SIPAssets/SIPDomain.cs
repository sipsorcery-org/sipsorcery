// ============================================================================
// FileName: SIPDomain.cs
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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using SIPSorcery.Persistence;
using SIPSorcery.Sys;
using log4net;

#if !SILVERLIGHT
using System.Data;
using System.Data.Linq;
using System.Data.Linq.Mapping;
using System.Xml.Linq;
#endif

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP.App
{
    /// <remarks>
    /// The mechanism to load the SIP domain records differs for XML and SQL data stores. Because the domain and domain alias
    /// are hierarchical the XML stores the records in a nested node structure. With SQL the nested structure is not possible 
    /// and instead a flat table is used where the domain alias are stored as a delimited list in a single column field.
    /// </remarks>
    [DataContractAttribute]
    [Table(Name = "sipdomains")]
    public class SIPDomain : ISIPAsset
    {
        private const char ALIAS_SEPERATOR_CHAR = ';';
        public const string XML_DOCUMENT_ELEMENT_NAME = "sipdomains";
        public const string XML_ELEMENT_NAME = "sipdomain";

        private static ILog logger = AppState.logger;
        private static string m_newLine = AppState.NewLine;

        [DataMember]
        [Column(Name = "id", DbType = "varchar(36)", IsPrimaryKey = true, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public Guid Id { get; set; }

        [DataMember]
        [Column(Name = "domain", DbType = "varchar(128)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string Domain { get; set; }

        [DataMember]
        [Column(Name = "owner", DbType = "varchar(32)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public string Owner { get; set; }

        private DateTimeOffset? m_inserted;
        [Column(Name = "inserted", DbType = "datetimeoffset", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public DateTimeOffset? Inserted
        {
            get { return m_inserted; }
            set
            {
                if (value != null)
                {
                    m_inserted = value.Value.ToUniversalTime();
                }
                else
                {
                    m_inserted = null;
                }
            }
        }

        [Column(Name = "aliaslist", DbType = "varchar(1024)", CanBeNull = true)]
        public string AliasList
        {
            get
            {
                if (Aliases != null && Aliases.Count > 0)
                {
                    string aliasList = null;

                    Aliases.ForEach(a => aliasList += a + ALIAS_SEPERATOR_CHAR);
                    return aliasList;
                }
                else
                {
                    return null;
                }
            }
            set
            {
                Aliases = ParseAliases(value);
            }
        }

        public List<string> Aliases = new List<string>();

        public SIPDomain()
        { }

        public SIPDomain(string domain, string owner, List<string> aliases)
        {
            Id = Guid.NewGuid();
            Domain = domain;
            Owner = owner;
            Aliases = aliases;
            Inserted = DateTime.UtcNow;
        }

#if !SILVERLIGHT

        public SIPDomain(DataRow row)
        {
            Load(row);
        }

        public DataTable GetTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add(new DataColumn("id", typeof(String)));
            table.Columns.Add(new DataColumn("domain", typeof(String)));
            table.Columns.Add(new DataColumn("owner", typeof(String)));
            table.Columns.Add(new DataColumn("inserted", typeof(DateTimeOffset)));
            table.Columns.Add(new DataColumn("aliaslist", typeof(String)));
            return table;
        }

        public void Load(DataRow row)
        {
            Id = (row.Table.Columns.Contains("id") && row["id"] != DBNull.Value) ? new Guid(row["id"] as string) : Guid.NewGuid();
            Domain = row["domain"] as string;
            Owner = (row.Table.Columns.Contains("owner") && row["owner"] != DBNull.Value) ? row["owner"] as string : null;
            if (row.Table.Columns.Contains("inserted") & row["inserted"] != DBNull.Value && !(row["inserted"] as string).IsNullOrBlank())
            {
                Inserted = DateTimeOffset.Parse(row["inserted"] as string);
            }

            string aliasList = (row.Table.Columns.Contains("aliaslist") & row["aliaslist"] != DBNull.Value) ? row["aliaslist"] as string : null;
            Aliases = ParseAliases(aliasList);
        }

        public Dictionary<Guid, object> Load(XmlDocument dom)
        {
            try
            {
                Dictionary<Guid, object> sipDomains = new Dictionary<Guid, object>();

                XDocument sipDomainsDoc = XDocument.Parse(dom.OuterXml);

                var xmlSIPDomains = from domain in sipDomainsDoc.Document.Descendants(XML_ELEMENT_NAME)
                                    select new SIPDomain()
                                    {
                                        Id = Guid.NewGuid(),
                                        Domain = domain.Element("domain").Value,
                                        Owner = (domain.Element("owner") != null && !domain.Element("owner").Value.IsNullOrBlank()) ? domain.Element("owner").Value : null,
                                        Aliases =
                                            (from alias in domain.Element("sipdomainaliases").Descendants("domainalias")
                                             select alias.Value).ToList()
                                    };

                foreach (SIPDomain xmlSIPDomain in xmlSIPDomains)
                {
                    sipDomains.Add(xmlSIPDomain.Id, xmlSIPDomain);
                }

                return sipDomains;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPDomain Load. " + excp.Message);
                throw;
            }
        }

#endif

        public string ToXML()
        {
            string sipDomainXML =
                "  <" + XML_ELEMENT_NAME + ">" + m_newLine +
               ToXMLNoParent() + m_newLine +
                "  </" + XML_ELEMENT_NAME + ">" + m_newLine;

            return sipDomainXML;
        }

        public string ToXMLNoParent()
        {
            throw new NotImplementedException();
        }

        public string GetXMLElementName()
        {
            return XML_ELEMENT_NAME;
        }

        public string GetXMLDocumentElementName()
        {
            return XML_DOCUMENT_ELEMENT_NAME;
        }

        private List<string> ParseAliases(string aliasString)
        {
            List<string> aliasList = new List<string>();

            if (!aliasString.IsNullOrBlank())
            {
                string[] aliases = aliasString.Split(ALIAS_SEPERATOR_CHAR);
                if (aliases != null && aliases.Length > 0)
                {
                    foreach (string alias in aliases)
                    {
                        if (!alias.IsNullOrBlank() && !aliasList.Contains(alias.ToLower()))
                        {
                            aliasList.Add(alias.ToLower());
                        }
                    }
                }
            }

            return aliasList;
        }
    }
}
