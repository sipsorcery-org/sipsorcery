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
        public const string REGAGENT_CONTACT_ID_KEY = "sipsorceryid";

        private static string m_newLine = AppState.NewLine;
        private static ILog logger = AppState.logger;

        private string m_id;
        [Column(Storage = "_id", Name = "id", DbType = "character varying(36)", IsPrimaryKey = true, CanBeNull = false)]
        [DataMember]
        public string Id
        {
            get { return m_id; }
            set { m_id = value; }
        }

        private string m_providerId;
        [Column(Storage = "_providerid", Name = "providerid", DbType = "character varying(36)", CanBeNull = false)]
        [DataMember]
        public string ProviderId
        {
            get { return m_providerId; }
            set { m_providerId = value; }
        }

        [DataMember]
        [Column(Storage = "_providername", Name = "providername", DbType = "character varying(50)", CanBeNull = false)]
        public string ProviderName { get; set; }

        private string m_owner;                             // The username of the account that owns this SIP provider configuration.
        [Column(Storage = "_owner", Name = "owner", DbType = "character varying(32)", CanBeNull = false)]
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

        [Column(Storage = "_adminmemberid", Name = "adminmemberid", DbType = "character varying(32)", CanBeNull = true)]
        public string AdminMemberId { get; private set; }    // If set it designates this asset as a belonging to a user with the matching adminid.

        private string m_registrationFailureMessage;         // Used to record why a registration failed if it does so.
        [Column(Storage = "_registrationfailuremessage", Name = "registrationfailuremessage", DbType = "character varying(1024)", CanBeNull = true)]
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

        private DateTime? m_lastRegisterTime = null;
        [Column(Storage = "_lastregistertime", Name = "lastregistertime", DbType = "timestamp", CanBeNull = true)]
        [DataMember]
        public DateTime? LastRegisterTime
        {
            get { return m_lastRegisterTime; }
            set
            {
                m_lastRegisterTime = value;
                NotifyPropertyChanged("LastRegisterTime");
            }
        }

        private DateTime? m_lastRegisterAttempt = null;
        [DataMember]
        [Column(Storage = "_lastregisterattempt", Name = "lastregisterattempt", DbType = "timestamp", CanBeNull = true)]
        public DateTime? LastRegisterAttempt {
            get { return m_lastRegisterAttempt; }
            set {
                m_lastRegisterAttempt = value;
                NotifyPropertyChanged("LastRegisterAttempt");
            }
        }

        private DateTime m_nextRegistrationTime = DateTime.MaxValue;    // The time at which the next registration attempt should be sent.
        [Column(Storage = "_nextregistrationtime", Name = "nextregistrationtime", DbType = "timestamp", CanBeNull = false)]
        [DataMember]
        public DateTime NextRegistrationTime
        {
            get { return m_nextRegistrationTime; }
            set
            {
                m_nextRegistrationTime = value;
                NotifyPropertyChanged("NextRegistrationTime");
            }
        }

        private bool m_isRegistered;
        [Column(Storage = "_isregistered", Name = "isregistered", DbType = "boolean", CanBeNull = false)]
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
        [Column(Storage = "_bindingexpiry", Name = "bindingexpiry", DbType = "int", CanBeNull = false)]
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
        [Column(Storage = "_bindinguri", Name = "bindinguri", DbType = "character varying(256)", CanBeNull = false)]
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
        [Column(Storage = "_registrarsipsocket", Name = "registrarsipsocket", DbType = "character varying(256)", CanBeNull = true)]
        public string RegistrarSIPSocket {
            get { return (RegistrarSIPEndPoint != null) ? RegistrarSIPEndPoint.ToString() : null; }
            set { RegistrarSIPEndPoint = (!value.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(value) : null;}
        }

        [Column(Storage = "_cseq", Name = "cseq", DbType = "int", CanBeNull = false)]
        public int CSeq { get; set; }                               // The SIP Header CSeq used in requests to the Registrar server

        public List<SIPContactHeader> ContactsList;                 // List of contacts reported back from the registrar server.
        public int NonceCount = 1;                                  // When a WWW-Authenticate header is received a cnonce needs to be sent, this value increments each time a new cnonce is generated.
        public SIPEndPoint LocalSIPEndPoint;                        // The SIP end point the registration agent sent the request from.
        public SIPEndPoint RegistrarSIPEndPoint;                    // The SIP end point of the remote SIP Registrar.
        //public string BindingTagId;                                 // Unique identifier for the reg agent to place in the binding contact to allow identification.

        // Fields populated and re-populated by the SIPProvider entry whenever a registration is initiated or refereshed.
        // The details are presisted and used for authenitcation of previous register requests or to remove existing bindings.
        public SIPURI RegistrarServer;
        public string ProviderUsername;
        public string ProviderPassword;
        public string RegistrarRealm;

        public object OrderProperty
        {
            get { return ProviderName; }
            set { }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public SIPProviderBinding()
        { }

        public SIPProviderBinding(SIPProvider sipProvider)
        {
            SetProviderFields(sipProvider);

            m_id = Guid.NewGuid().ToString();           

            // All set, let the Registration Agent know the binding is ready to be processed.
            NextRegistrationTime = DateTime.Now;
        }
        
#if !SILVERLIGHT

        public SIPProviderBinding(DataRow bindingRow) {
            Load(bindingRow);
        }

        public void Load(DataRow bindingRow) {

            m_id = (bindingRow.Table.Columns.Contains("id") && bindingRow["id"] != DBNull.Value && bindingRow["id"] != null) ? bindingRow["id"] as string : Guid.NewGuid().ToString();
            m_providerId = bindingRow["providerid"] as string;
            m_owner = bindingRow["owner"] as string;
            AdminMemberId = bindingRow["adminmemberid"] as string;
            m_isRegistered = (bindingRow.Table.Columns.Contains("isregistered") && bindingRow["isregistered"] != DBNull.Value && bindingRow["isregistered"] != null) ? Convert.ToBoolean(bindingRow["isregistered"]) : false;

            if (bindingRow.Table.Columns.Contains("bindinguri") && bindingRow["bindinguri"] != DBNull.Value && bindingRow["bindinguri"] != null && bindingRow["bindinguri"].ToString().Length > 0) {
                m_bindingURI = SIPURI.ParseSIPURI(bindingRow["bindinguri"] as string);
            }

            if (bindingRow.Table.Columns.Contains("bindingexpiry") && bindingRow["bindingexpiry"] != DBNull.Value && bindingRow["bindingexpiry"] != null && bindingRow["bindingexpiry"].ToString().Length > 0) {
                Int32.TryParse(bindingRow["bindingexpiry"] as string, out m_bindingExpiry);
            }

            if (bindingRow.Table.Columns.Contains("contactheader") && bindingRow["contactheader"] != DBNull.Value && bindingRow["contactheader"] != null && bindingRow["contactheader"].ToString().Length > 0) {
                ContactsList = SIPContactHeader.ParseContactHeader(bindingRow["contactheader"] as string);
            }

            if (bindingRow.Table.Columns.Contains("cseq") && bindingRow["cseq"] != DBNull.Value && bindingRow["cseq"] != null && bindingRow["cseq"].ToString().Length > 0) {
                int cseq = 0;
                if (Int32.TryParse(bindingRow["cseq"] as string, out cseq)) {
                    CSeq = cseq;
                }
            }

            if (bindingRow.Table.Columns.Contains("lastregistertime") && bindingRow["lastregistertime"] != DBNull.Value && bindingRow["lastregistertime"] != null && bindingRow["lastregistertime"].ToString().Length > 0) {
                m_lastRegisterTime = Convert.ToDateTime(bindingRow["lastregistertime"]);
            }

            if (bindingRow.Table.Columns.Contains("lastregisterattempt") && bindingRow["lastregisterattempt"] != DBNull.Value && bindingRow["lastregisterattempt"] != null && bindingRow["lastregisterattempt"].ToString().Length > 0) {
                m_lastRegisterAttempt = Convert.ToDateTime(bindingRow["lastregisterattempt"]);
            }

            if (bindingRow.Table.Columns.Contains("nextregistrationtime") && bindingRow["nextregistrationtime"] != DBNull.Value && bindingRow["nextregistrationtime"] != null && bindingRow["nextregistrationtime"].ToString().Length > 0) {
                m_nextRegistrationTime = Convert.ToDateTime(bindingRow["nextregistrationtime"]);
            }

            if (bindingRow.Table.Columns.Contains("registrarsipsocket") && bindingRow["registrarsipsocket"] != DBNull.Value && bindingRow["registrarsipsocket"] != null && bindingRow["registrarsipsocket"].ToString().Length > 0) {
                RegistrarSIPEndPoint = SIPEndPoint.ParseSIPEndPoint(bindingRow["registrarsipsocket"] as string);
            }

            logger.Debug(" loaded SIPProviderBinding for " + Owner + " and " + ProviderName + " and binding " + BindingURI.ToString() + ".");
        }

        public Dictionary<Guid, object> Load(XmlDocument dom) {
            return SIPAssetXMLPersistor<SIPProviderBinding>.LoadAssetsFromXMLRecordSet(dom);
        }

#endif

        public object GetOrderProperty()
        {
            return ProviderName;
        }

        public void SetProviderFields(SIPProvider sipProvider) {
            if (!sipProvider.RegisterEnabled) {
                throw new ApplicationException("Cannot create a new SIProviderBinding from a SIPProvider with RegisterEnabled set to false.");
            }
            else if (sipProvider.Registrar == null) {
                throw new ApplicationException("Cannot create a new SIProviderBinding from a SIPProvider with an emtpy RegistrarServer.");
            }
            else if (sipProvider.RegisterContact == null) {
                throw new ApplicationException("Cannot create a new SIProviderBinding from a SIPProvider with an emtpy RegisterContact.");
            }

            m_providerId = sipProvider.Id;
            m_owner = sipProvider.Owner;
            AdminMemberId = sipProvider.AdminMemberId;
            ProviderName = sipProvider.ProviderName;
            ProviderUsername = (!sipProvider.ProviderAuthUsername.IsNullOrBlank()) ? sipProvider.ProviderAuthUsername : sipProvider.ProviderUsername;
            ProviderPassword = sipProvider.ProviderPassword;
            RegistrarServer = sipProvider.Registrar.CopyOf();
            RegistrarRealm = (!sipProvider.RegisterRealm.IsNullOrBlank()) ? sipProvider.RegisterRealm : RegistrarServer.Host;
            BindingExpiry = sipProvider.RegisterExpiry;

            string bindingId = null;
            if (m_bindingURI != null && m_bindingURI.Parameters.Has(REGAGENT_CONTACT_ID_KEY)) {
                bindingId = m_bindingURI.Parameters.Get(REGAGENT_CONTACT_ID_KEY);
            }
            m_bindingURI = SIPURI.ParseSIPURI(sipProvider.RegisterContact);
            if (!bindingId.IsNullOrBlank()) {
                m_bindingURI.Parameters.Set(REGAGENT_CONTACT_ID_KEY, bindingId); 
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
            string lastRegisterTimeStr = (m_lastRegisterTime != null) ? m_lastRegisterTime.Value.ToString("dd MMM yyyy HH:mm:ss") : null;
            string lastRegisterAttemptStr = (m_lastRegisterTime != null) ? m_lastRegisterAttempt.Value.ToString("dd MMM yyyy HH:mm:ss") : null;
            string nextRegistrationTimeStr = (m_nextRegistrationTime != DateTime.MaxValue) ? m_nextRegistrationTime.ToString("dd MMM yyyy HH:mm:ss") : null;
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

        private void NotifyPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
