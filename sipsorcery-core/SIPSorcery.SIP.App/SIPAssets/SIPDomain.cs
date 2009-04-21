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
    [Table(Name = "sipdomains")]
    [DataContractAttribute]
    public class SIPDomain : ISIPAsset
    {
        public const string XML_DOCUMENT_ELEMENT_NAME = "sipdomains";
        public const string XML_ELEMENT_NAME = "sipdomain";

        private static ILog logger = AssemblyState.logger;
        private static string m_newLine = AppState.NewLine;

        [Column(Storage = "_id", Name = "id", DbType = "character varying(36)", IsPrimaryKey = true, CanBeNull = false)]
        [DataMember]
        public string Id { get; set; }

        [Column(Storage = "_domain", Name = "domain", DbType = "character varying(128)", IsPrimaryKey = true, CanBeNull = false)]
        [DataMember]
        public string Domain {get; set;}

        [Column(Storage = "_owner", Name = "owner", DbType = "character varying(32)", CanBeNull = false)]
        [DataMember]
        public string Owner { get; set; }
                
        public List<string> Aliases;

        [Column(Storage = "_inserted", Name = "inserted", DbType = "timestamp", CanBeNull = false)]
        public DateTime? Inserted { get; set; }

        public SIPDomain()
        { }

        public SIPDomain(string domain, string owner, List<string> aliases)
        {
            Id = Guid.NewGuid().ToString();
            Domain = domain;
            Owner = owner;
            Aliases = aliases;
            Inserted = DateTime.Now;
        }

#if !SILVERLIGHT

        public SIPDomain(XmlNode sipDomainNode)
        {
            Domain = sipDomainNode.Attributes.GetNamedItem("name").Value;
            Owner = (sipDomainNode.Attributes.GetNamedItem("owner") != null) ? sipDomainNode.Attributes.GetNamedItem("owner").Value : null;
            Aliases = new List<string>();

            XmlNodeList aliasNodes = sipDomainNode.SelectNodes("alias");
            if (aliasNodes != null)
            {
                foreach (XmlNode aliasNode in aliasNodes)
                {
                    if (aliasNode.InnerText != null && !Aliases.Contains(aliasNode.InnerText.Trim().ToLower()))
                    {
                        Aliases.Add(aliasNode.InnerText.Trim().ToLower());
                    }
                }
            }
         }

        public SIPDomain(DataRow row) {
            Aliases = new List<string>();
            Load(row);
        }

        public void Load(DataRow row) {
            Id = (row.Table.Columns.Contains("id") && row["id"] != DBNull.Value) ? row["id"] as string : Guid.NewGuid().ToString();
            Domain = row["domain"] as string;
            Owner = row["owner"] as string;
            if (row.Table.Columns.Contains("inserted") & row["inserted"] != DBNull.Value) {
                Convert.ToDateTime(row["inserted"]);
            }
            if (row.Table.Columns.Contains("domainalias") & row["domainalias"] != DBNull.Value) {
                Aliases.Add(row["domainalias"] as string);
            }
        }

        public Dictionary<Guid, object> Load(XmlDocument dom) {
            try {
                Dictionary<Guid, object> sipDomains = new Dictionary<Guid, object>();

                XDocument sipDomainsDoc = XDocument.Parse(dom.OuterXml);

                var xmlSIPDomains = from domain in sipDomainsDoc.Document.Descendants(XML_ELEMENT_NAME)
                                    select new SIPDomain() {
                                        Id = Guid.NewGuid().ToString(),
                                        Domain = domain.Element("domain").Value,
                                        Owner = domain.Element("owner").Value,
                                        Aliases =
                                            (from alias in domain.Element("sipdomainaliases").Descendants("domainalias")
                                             select alias.Value).ToList()
                                    };

                foreach (SIPDomain xmlSIPDomain in xmlSIPDomains) {
                    sipDomains.Add(new Guid(xmlSIPDomain.Id), xmlSIPDomain);
                }

                return sipDomains;
            }
            catch (Exception excp) {
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

          public string ToXMLNoParent() {
              string sipDomainXML =
                  "  <id>" + Id + "</id>" + m_newLine;

              return sipDomainXML;
          }

        public string GetXMLElementName() {
            return XML_ELEMENT_NAME;
        }

        public string GetXMLDocumentElementName() {
            return XML_DOCUMENT_ELEMENT_NAME;
        }
    }
}
