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
using System.Linq;
using System.Text;
using System.Xml;
using SIPSorcery.Sys;
using SIPSorcery.Persistence;
using log4net;

#if !SILVERLIGHT
using System.Data;
using System.Data.Linq;
using System.Data.Linq.Mapping;
#endif

namespace SIPSorcery.CRM {

    [Table(Name = "customersessions")]
    public class CustomerSession : ISIPAsset {

        public const string XML_DOCUMENT_ELEMENT_NAME = "customersessions";
        public const string XML_ELEMENT_NAME = "customersession";
        public const int INITIAL_SESSION_LIFETIME_MINUTES = 60;             // 1 hour initially on a session lifetime.
        public const int MAX_SESSION_LIFETIME_MINUTES = 600;                // 10 hour maximum on a session lifetime.

        private static ILog logger = AppState.logger;
        private static string m_newLine = AppState.NewLine;

        [Column(Name = "id", DbType = "varchar(36)", IsPrimaryKey = true, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public Guid Id { get; set; }

        [Column(Name = "sessionid", DbType = "varchar(96)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string SessionID { get; set; }

        [Column(Name = "customerusername", DbType = "varchar(32)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string CustomerUsername { get; set; }

        [Column(Name = "inserted", DbType = "datetimeoffset", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public DateTimeOffset Inserted { get; set; }

        [Column(Name = "expired", DbType = "bit", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public bool Expired { get; set; }

        [Column(Name = "ipaddress", DbType = "varchar(15)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string IPAddress { get; set; }

        [Column(Name = "timelimitminutes", DbType = "int", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public int TimeLimitMinutes { get; set; }

        public CustomerSession() { }

        public CustomerSession(Guid id, string sessionID, string customerUsername, string ipAddress) {
            Id = id;
            SessionID = sessionID;
            CustomerUsername = customerUsername;
            Inserted = DateTimeOffset.UtcNow;
            IPAddress = ipAddress;
            TimeLimitMinutes = INITIAL_SESSION_LIFETIME_MINUTES;
        }

#if !SILVERLIGHT

        public CustomerSession(DataRow customerSessionRow) {
            Load(customerSessionRow);
        }

        public DataTable GetTable() {
            DataTable table = new DataTable();
            table.Columns.Add(new DataColumn("id", typeof(String)));
            table.Columns.Add(new DataColumn("sessionid", typeof(String)));
            table.Columns.Add(new DataColumn("customerusername", typeof(String)));
            table.Columns.Add(new DataColumn("inserted", typeof(DateTime)));
            table.Columns.Add(new DataColumn("expired", typeof(Boolean)));
            table.Columns.Add(new DataColumn("ipaddress", typeof(String)));
            table.Columns.Add(new DataColumn("timelimitminutes", typeof(Int32)));
            return table;
        }

        public void Load(DataRow customerSessionRow) {
            try {
                Id = new Guid(customerSessionRow["id"] as string);
                SessionID = customerSessionRow["sessionid"] as string;
                CustomerUsername = customerSessionRow["customerusername"] as string;
                Inserted = DateTimeOffset.Parse(customerSessionRow["inserted"] as string);
                Expired = Convert.ToBoolean(customerSessionRow["expired"]);
                IPAddress = customerSessionRow["ipaddress"] as string;
                TimeLimitMinutes = Convert.ToInt32(customerSessionRow["timelimitminutes"]);
            }
            catch (Exception excp) {
                logger.Error("Exception CustomerSession Load. " + excp.Message);
                throw;
            }
        }

        public Dictionary<Guid, object> Load(XmlDocument dom) {
            return SIPAssetXMLPersistor<CustomerSession>.LoadAssetsFromXMLRecordSet(dom);
        }

#endif

        public string ToXML() {
            string customerSessionXML =
                "  <" + XML_ELEMENT_NAME + ">" + m_newLine +
               ToXMLNoParent() + m_newLine +
                "  </" + XML_ELEMENT_NAME + ">" + m_newLine;

            return customerSessionXML;
        }

        public string ToXMLNoParent() {
            string customerSessionXML =
                "    <id>" + Id + "</id>" + m_newLine +
                "    <sessionid>" + SessionID + "</sessionid>" + m_newLine +
                "    <customerusername>" + CustomerUsername + "</customerusername>" + m_newLine +
                "    <inserted>" + Inserted.ToString() + "</inserted>" + m_newLine +
                "    <expired>" + Expired + "</expired>" + m_newLine +
                "    <ipaddress>" + IPAddress + "</ipaddress>" + m_newLine +
                "    <timelimitminutes>" + TimeLimitMinutes + "</timelimitminutes>";

            return customerSessionXML;
        }

        public string GetXMLElementName() {
            return XML_ELEMENT_NAME;
        }

        public string GetXMLDocumentElementName() {
            return XML_DOCUMENT_ELEMENT_NAME;
        }
    }
}
