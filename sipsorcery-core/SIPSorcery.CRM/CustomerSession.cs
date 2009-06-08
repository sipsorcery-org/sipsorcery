// ============================================================================
// FileName: CustomerSession.cs
//
// Description:
// Represents a session for an authenticated user.
//
// Author(s):
// Aaron Clauson
//
// History:
// 20 May 2009	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2009 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Collections.Generic;
using System.Data;
using System.Data.Linq;
using System.Data.Linq.Mapping;
using System.Linq;
using System.Text;
using System.Xml;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.CRM
{
    [Table(Name = "customersessions")]
    public class CustomerSession : ISIPAsset
    {
        public const string XML_DOCUMENT_ELEMENT_NAME = "customersessions";
        public const string XML_ELEMENT_NAME = "customersession";
        public const int MAX_SESSION_LIFETIME_MINUTES = 60;

        private static ILog logger = AppState.logger;
        private static string m_newLine = AppState.NewLine;

        [Column(Storage = "_id", Name = "id", DbType = "character varying(36)", IsPrimaryKey = true, CanBeNull = false)]
        public string Id { get; set; }

        [Column(Storage = "_customerusername", Name = "customerusername", DbType = "character varying(32)", CanBeNull = false)]
        public string CustomerUsername;

        [Column(Storage = "_inserted", Name = "inserted", DbType = "timestamp", CanBeNull = false)]
        public DateTime Inserted;

        [Column(Storage = "_expired", Name = "expired", DbType = "boolean", CanBeNull = false)]
        public bool Expired { get; set; }

        [Column(Storage = "_ipaddress", Name = "ipaddress", DbType = "character varying(15)", CanBeNull = false)]
        public string IPAddress;

        public object OrderProperty
        {
            get { return Inserted; }
            set { }
        }

        public CustomerSession() { }

        public CustomerSession(Guid id, string customerUsername, string ipAddress)
        {
            Id = id.ToString();
            CustomerUsername = customerUsername;
            Inserted = DateTime.Now;
            IPAddress = ipAddress;
        }

         public CustomerSession(DataRow customerSessionRow) {
            Load(customerSessionRow);
        }

        public void Load(DataRow customerSessionRow) {
            try {
                Id = customerSessionRow["id"] as string;
                CustomerUsername = customerSessionRow["customerusername"] as string;
                Inserted = Convert.ToDateTime(customerSessionRow["inserted"]);
                Expired = Convert.ToBoolean(customerSessionRow["expired"]);
                IPAddress = customerSessionRow["ipaddress"] as string;
            }
            catch (Exception excp) {
                logger.Error("Exception CustomerSession Load. " + excp.Message);
                throw;
            }
        }

        public Dictionary<Guid, object> Load(XmlDocument dom)
        {
            return SIPAssetXMLPersistor<CustomerSession>.LoadAssetsFromXMLRecordSet(dom);
        }

        public object GetOrderProperty()
        {
            return Inserted;
        }

        public string ToXML()
        {
            string customerSessionXML =
                "  <" + XML_ELEMENT_NAME + ">" + m_newLine +
               ToXMLNoParent() + m_newLine +
                "  </" + XML_ELEMENT_NAME + ">" + m_newLine;

            return customerSessionXML;
        }

        public string ToXMLNoParent()
        {
            string customerSessionXML =
                "    <id>" + Id + "</id>" + m_newLine +
                "    <customerusername>" + CustomerUsername + "</customerusername>" + m_newLine +
                "    <inserted>" + Inserted.ToString("dd MMM yyyy HH:mm:ss") + "</inserted>" + m_newLine +
                "    <expired>" + Expired + "</expired>" + m_newLine +
                "    <ipaddress>" + IPAddress + "</ipaddress>";

            return customerSessionXML;
        }

        public string GetXMLElementName()
        {
            return XML_ELEMENT_NAME;
        }

        public string GetXMLDocumentElementName()
        {
            return XML_DOCUMENT_ELEMENT_NAME;
        }
    }
}
