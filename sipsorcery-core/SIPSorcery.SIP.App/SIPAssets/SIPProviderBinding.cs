// ============================================================================
// FileName: SIPProviderBinding.cs
//
// Description:
// Represents a SIP provider binding for a registration or registration attempt with
// an external SIP provider.
//
// Author(s):
// Aaron Clauson
//
// History:
// 13 Apr 2009  Aaron Clauson   Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.ComponentModel;
using System.Net;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using SIPSorcery.Persistence;
using SIPSorcery.Sys;
using log4net;

#if !SILVERLIGHT
using System.Data;
using System.Data.Linq;
using System.Data.Linq.Mapping;
#endif

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP.App
{
    [Table(Name = "sipproviderbindings")]
    [DataContractAttribute]
    public class SIPProviderBinding : INotifyPropertyChanged, ISIPAsset
    {
        public const string XML_DOCUMENT_ELEMENT_NAME = "sipproviderbindings";
        public const string XML_ELEMENT_NAME = "sipproviderbinding";
        public const string REGAGENT_CONTACT_ID_KEY = "rinstance";

        //public static readonly string SelectBinding = "select * from sipproviderbindings where id = ?1";
        //public static readonly string SelectNextScheduledBinding = "select * from sipproviderbindings where nextregistrationtime <= ?1 order by nextregistrationtime asc limit 1";

        private static string m_newLine = AppState.NewLine;
        private static ILog logger = AppState.logger;

        public static int TimeZoneOffsetMinutes;

        private Guid m_id;
        [Column(Name = "id", DbType = "varchar(36)", IsPrimaryKey = true, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public Guid Id
        {
            get { return m_id; }
            set { m_id = value; }
        }

        private Guid m_providerId;
        [Column(Name = "providerid", DbType = "varchar(36)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public Guid ProviderId
        {
            get { return m_providerId; }
            set { m_providerId = value; }
        }

        [DataMember]
        [Column(Name = "providername", DbType = "varchar(50)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string ProviderName { get; set; }

        private string m_owner;                             // The username of the account that owns this SIP provider configuration.
        [Column(Name = "owner", DbType = "varchar(32)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string Owner
        {
            get { return m_owner; }
            set
            {
                m_owner = value;
                NotifyPropertyChanged("Owner");
            }
        }

        [Column(Name = "adminmemberid", DbType = "varchar(32)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public string AdminMemberId { get; set; }    // If set it designates this asset as a belonging to a user with the matching adminid.

        private string m_registrationFailureMessage;         // Used to record why a registration failed if it does so.
        [Column(Name = "registrationfailuremessage", DbType = "varchar(1024)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string RegistrationFailureMessage
        {
            get { return m_registrationFailureMessage; }
            set
            {
                m_registrationFailureMessage = value;
                NotifyPropertyChanged("RegistrationFailureMessage");
            }
        }

        private DateTimeOffset? m_lastRegisterTime = null;
        [Column(Name = "lastregistertime", DbType = "datetimeoffset", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public DateTimeOffset? LastRegisterTime
        {
            get { return m_lastRegisterTime; }
            set
            {
                if (value != null) {
                    m_lastRegisterTime = value.Value.ToUniversalTime();
                }
                else {
                    m_lastRegisterTime = null;
                }
                NotifyPropertyChanged("LastRegisterTime");
            }
        }

        public DateTimeOffset? LastRegisterTimeLocal
        {
            get {
                if (LastRegisterTime != null) {
                    return LastRegisterTime.Value.AddMinutes(TimeZoneOffsetMinutes);
                }
                else {
                    return null;
                }
            }
        }

        private DateTimeOffset? m_lastRegisterAttempt = null;
        [DataMember]
        [Column(Name = "lastregisterattempt", DbType = "datetimeoffset", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public DateTimeOffset? LastRegisterAttempt {
            get { return m_lastRegisterAttempt; }
            set {
                if (value != null) {
                    m_lastRegisterAttempt = value.Value.ToUniversalTime();
                }
                else {
                    m_lastRegisterAttempt = null;
                }
                NotifyPropertyChanged("LastRegisterAttempt");
            }
        }

        public DateTimeOffset? LastRegisterAttemptLocal
        {
            get {
                if (LastRegisterAttempt != null) {
                    return LastRegisterAttempt.Value.AddMinutes(TimeZoneOffsetMinutes);
                }
                else {
                    return null;
                }
            }
        }

        private DateTimeOffset m_nextRegistrationTime = DateTimeOffset.MaxValue;    // The time at which the next registration attempt should be sent.
        [Column(Name = "nextregistrationtime", DbType = "datetimeoffset", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public DateTimeOffset NextRegistrationTime
        {
            get { return m_nextRegistrationTime; }
            set {
                m_nextRegistrationTime = value.ToUniversalTime();
                NotifyPropertyChanged("NextRegistrationTime");
            }
        }

        public DateTimeOffset NextRegistrationTimeLocal
        {
            get {return NextRegistrationTime.AddMinutes(TimeZoneOffsetMinutes); }
        }

        private bool m_isRegistered;
        [Column(Name = "isregistered", DbType = "bit", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public bool IsRegistered
        {
            get { return m_isRegistered; }
            set
            {
                m_isRegistered = value;
                NotifyPropertyChanged("IsRegistered");
            }
        }

        private int m_bindingExpiry;
        [Column(Name = "bindingexpiry", DbType = "int", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public int BindingExpiry
        {
            get { return m_bindingExpiry; }
            set
            {
                m_bindingExpiry = value;
                NotifyPropertyChanged("BindingExpiry");
            }
        }

        private SIPURI m_bindingURI; // When registered this holds the binding being maintained by the agent, it's derived from the RegisterContact field but can have an additional parameter added.
        [Column(Name = "bindinguri", DbType = "varchar(256)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string BindingURI {
            get { return (m_bindingURI != null) ? m_bindingURI.ToString() : null; }
            set { m_bindingURI = (!value.IsNullOrBlank()) ? SIPURI.ParseSIPURI(value) : null; }
        }

        public SIPURI BindingSIPURI {
            get { return m_bindingURI; }
            set { m_bindingURI = value; }
        }

        [DataMember]
        [Column(Name = "registrarsipsocket", DbType = "varchar(256)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public string RegistrarSIPSocket {
            get { return (RegistrarSIPEndPoint != null) ? RegistrarSIPEndPoint.ToString() : null; }
            set { RegistrarSIPEndPoint = (!value.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(value) : null;}
        }

        [Column(Name = "cseq", DbType = "int", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public int CSeq { get; set; }                               // The SIP Header CSeq used in requests to the Registrar server

        public List<SIPContactHeader> ContactsList;                 // List of contacts reported back from the registrar server.
        public int NonceCount = 1;                                  // When a WWW-Authenticate header is received a cnonce needs to be sent, this value increments each time a new cnonce is generated.
        public SIPEndPoint LocalSIPEndPoint;                        // The SIP end point the registration agent sent the request from.
        public SIPEndPoint RegistrarSIPEndPoint;                    // The SIP end point of the remote SIP Registrar.

        // Fields populated and re-populated by the SIPProvider entry whenever a registration is initiated or refereshed.
        // The details are persisted and used for authentication of previous register requests or to remove existing bindings.
        public SIPURI RegistrarServer;
        public string ProviderAuthUsername;
        public string ProviderPassword;
        public string RegistrarRealm;

        public event PropertyChangedEventHandler PropertyChanged;

        public SIPProviderBinding()
        { }

        public SIPProviderBinding(SIPProvider sipProvider)
        {
            SetProviderFields(sipProvider);

            m_id = Guid.NewGuid();           

            // All set, let the Registration Agent know the binding is ready to be processed.
            NextRegistrationTime = DateTimeOffset.UtcNow;
        }
        
#if !SILVERLIGHT

        public SIPProviderBinding(DataRow bindingRow) {
            Load(bindingRow);
        }

        public DataTable GetTable() {
            DataTable table = new DataTable();
            table.Columns.Add(new DataColumn("id", typeof(String)));
            table.Columns.Add(new DataColumn("providerid", typeof(String)));
            table.Columns.Add(new DataColumn("providername", typeof(String)));
            table.Columns.Add(new DataColumn("owner", typeof(String)));
            table.Columns.Add(new DataColumn("adminmemberid", typeof(String)));
            table.Columns.Add(new DataColumn("isregistered", typeof(Boolean)));
            table.Columns.Add(new DataColumn("bindinguri", typeof(String)));
            table.Columns.Add(new DataColumn("bindingexpiry", typeof(Int32)));
            table.Columns.Add(new DataColumn("cseq", typeof(Int32)));
            table.Columns.Add(new DataColumn("lastregistertime", typeof(DateTimeOffset)));
            table.Columns.Add(new DataColumn("lastregisterattempt", typeof(DateTimeOffset)));
            table.Columns.Add(new DataColumn("nextregistrationtime", typeof(DateTimeOffset)));
            table.Columns.Add(new DataColumn("registrarsipsocket", typeof(String)));
            table.Columns.Add(new DataColumn("registrationfailuremessage", typeof(String)));
            return table;
        }

        public void Load(DataRow bindingRow) {
            try {
                m_id = (bindingRow.Table.Columns.Contains("id") && bindingRow["id"] != DBNull.Value && bindingRow["id"] != null) ? new Guid(bindingRow["id"] as string) : Guid.NewGuid();
                m_providerId = new Guid(bindingRow["providerid"] as string);
                ProviderName = bindingRow["providername"] as string;
                m_owner = bindingRow["owner"] as string;
                AdminMemberId = bindingRow["adminmemberid"] as string;
                m_isRegistered = (bindingRow.Table.Columns.Contains("isregistered") && bindingRow["isregistered"] != DBNull.Value && bindingRow["isregistered"] != null) ? Convert.ToBoolean(bindingRow["isregistered"]) : false;

                if (bindingRow.Table.Columns.Contains("bindinguri") && bindingRow["bindinguri"] != DBNull.Value && bindingRow["bindinguri"] != null && !bindingRow["bindinguri"].ToString().IsNullOrBlank()) {
                    m_bindingURI = SIPURI.ParseSIPURI(bindingRow["bindinguri"] as string);
                }
                else {
                    logger.Warn("Could not load BindingURI for SIPProviderBinding with id=" + m_id + ".");
                }

                if (bindingRow.Table.Columns.Contains("bindingexpiry") && bindingRow["bindingexpiry"] != DBNull.Value && bindingRow["bindingexpiry"] != null) {
                    m_bindingExpiry = Convert.ToInt32(bindingRow["bindingexpiry"]);
                }

                if (bindingRow.Table.Columns.Contains("cseq") && bindingRow["cseq"] != DBNull.Value && bindingRow["cseq"] != null && bindingRow["cseq"].ToString().Length > 0) {
                    CSeq = Convert.ToInt32(bindingRow["cseq"]);
                }

                if (bindingRow.Table.Columns.Contains("lastregistertime") && bindingRow["lastregistertime"] != DBNull.Value && bindingRow["lastregistertime"] != null && !(bindingRow["lastregistertime"] as string).IsNullOrBlank()) {
                    LastRegisterTime = DateTimeOffset.Parse(bindingRow["lastregistertime"] as string);
                }

                if (bindingRow.Table.Columns.Contains("lastregisterattempt") && bindingRow["lastregisterattempt"] != DBNull.Value && bindingRow["lastregisterattempt"] != null && !(bindingRow["lastregisterattempt"] as string).IsNullOrBlank()) {
                    LastRegisterAttempt = DateTimeOffset.Parse(bindingRow["lastregisterattempt"] as string);
                }

                if (bindingRow.Table.Columns.Contains("nextregistrationtime") && bindingRow["nextregistrationtime"] != DBNull.Value && bindingRow["nextregistrationtime"] != null && !(bindingRow["nextregistrationtime"] as string).IsNullOrBlank()) {
                    NextRegistrationTime = DateTimeOffset.Parse(bindingRow["nextregistrationtime"] as string);
                }

                if (bindingRow.Table.Columns.Contains("registrarsipsocket") && bindingRow["registrarsipsocket"] != DBNull.Value && bindingRow["registrarsipsocket"] != null && bindingRow["registrarsipsocket"].ToString().Length > 0) {
                    RegistrarSIPEndPoint = SIPEndPoint.ParseSIPEndPoint(bindingRow["registrarsipsocket"] as string);
                }

                m_registrationFailureMessage = bindingRow["registrationfailuremessage"] as string; 

                //logger.Debug(" loaded SIPProviderBinding for " + Owner + " and " + ProviderName + " and binding " + BindingURI.ToString() + ".");
            }
            catch (Exception excp) {
                logger.Error("Exception SIPProviderBinding Load. " + excp.Message);
                throw excp;
            }
        }

        public Dictionary<Guid, object> Load(XmlDocument dom) {
            return SIPAssetXMLPersistor<SIPProviderBinding>.LoadAssetsFromXMLRecordSet(dom);
        }

#endif

        public void SetProviderFields(SIPProvider sipProvider) {

            m_providerId = sipProvider.Id;
            m_owner = sipProvider.Owner;
            AdminMemberId = sipProvider.AdminMemberId;
            ProviderName = sipProvider.ProviderName;
            ProviderAuthUsername = (!sipProvider.ProviderAuthUsername.IsNullOrBlank()) ? sipProvider.ProviderAuthUsername : sipProvider.ProviderUsername;
            ProviderPassword = sipProvider.ProviderPassword;
            RegistrarServer = sipProvider.Registrar.CopyOf();
            RegistrarRealm = (!sipProvider.RegisterRealm.IsNullOrBlank()) ? sipProvider.RegisterRealm : RegistrarServer.Host;

            if (sipProvider.RegisterEnabled) {
                BindingExpiry = sipProvider.RegisterExpiry;
            }
            else {
                BindingExpiry = 0;
            }

            string bindingId = null;
            if (m_bindingURI != null && m_bindingURI.Parameters.Has(REGAGENT_CONTACT_ID_KEY)) {
                bindingId = m_bindingURI.Parameters.Get(REGAGENT_CONTACT_ID_KEY);
            }

            if (!sipProvider.RegisterContact.IsNullOrBlank()) {
                m_bindingURI = SIPURI.ParseSIPURI(sipProvider.RegisterContact);
                if (!bindingId.IsNullOrBlank()) {
                    m_bindingURI.Parameters.Set(REGAGENT_CONTACT_ID_KEY, bindingId);
                }
            }
            else {
                // The register contact field on the SIP Provider is empty. 
                // This condition needs to be trearted as the binding being disbaled and it needs to be removed.
                BindingExpiry = 0;
            }
        }

        public string ToXML()
        {
            string providerXML =
                " <" + XML_ELEMENT_NAME + ">" + m_newLine +
                ToXMLNoParent() +
                " </" + XML_ELEMENT_NAME + ">" + m_newLine;

            return providerXML;
        }

        public string ToXMLNoParent()
        {
            string lastRegisterTimeStr = (m_lastRegisterTime != null) ? m_lastRegisterTime.Value.ToString("o") : null;
            string lastRegisterAttemptStr = (m_lastRegisterTime != null) ? m_lastRegisterAttempt.Value.ToString("o") : null;
            string nextRegistrationTimeStr = (m_nextRegistrationTime != DateTimeOffset.MaxValue) ? m_nextRegistrationTime.ToString("o") : null;
            string bindingExpiryStr = (m_bindingExpiry > 0) ? m_bindingExpiry.ToString() : null;
            string bindingURIStr = (BindingURI != null) ? BindingURI.ToString() : null;
            string contactsListStr = null;
            if (ContactsList != null)
            {
                foreach (SIPContactHeader contact in ContactsList)
                {
                    contactsListStr += contact.ToString();
                }
            }

            string providerBindingXML =
                "   <id>" + m_id + "</id>" + m_newLine +
                "   <providerid>" + m_providerId + "</providerid>" + m_newLine +
                "   <providername>" + ProviderName + "</providername>" + m_newLine +
                "   <owner>" + m_owner + "</owner>" + m_newLine +
                "   <adminmemberid>" + AdminMemberId + "</adminmemberid>" + m_newLine +
                "   <bindinguri>" + bindingURIStr + "</bindinguri>" + m_newLine +
                "   <cseq>" + CSeq + "</cseq>" + m_newLine +
                "   <contactheader>" + SafeXML.MakeSafeXML(contactsListStr) + "</contactheader>" + m_newLine +
                "   <registrationfailuremessage>" + SafeXML.MakeSafeXML(m_registrationFailureMessage) + "</registrationfailuremessage>" + m_newLine +
                "   <lastregistertime>" + lastRegisterTimeStr + "</lastregistertime>" + m_newLine +
                "   <lastregisterattempt>" + lastRegisterAttemptStr + "</lastregisterattempt>" + m_newLine +
                "   <nextregistrationtime>" + nextRegistrationTimeStr + "</nextregistrationtime>" + m_newLine +
                "   <bindingexpiry>" + bindingExpiryStr + "</bindingexpiry>" + m_newLine +
                "   <isregistered>" + m_isRegistered + "</isregistered>" + m_newLine +
                "   <registrarsipsocket>" + RegistrarSIPSocket + "</registrarsipsocket>" + m_newLine;

            return providerBindingXML;
        }

        public string GetXMLElementName() {
            return XML_ELEMENT_NAME;
        }

        public string GetXMLDocumentElementName() {
            return XML_DOCUMENT_ELEMENT_NAME;
        }

        private void NotifyPropertyChanged(string propertyName) {
            if (PropertyChanged != null) {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
