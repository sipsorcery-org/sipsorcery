﻿// ============================================================================
// FileName: SIPProvider.cs
//
// Description:
// Represents a SIP provider (or SIP trunk) that can be used in the dial plan.
//
// Author(s):
// Aaron Clauson
//
// History:
// 06 Feb 2008  Aaron Clauson   Created.
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
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using SIPSorcery.Sys;
using log4net;

#if !SILVERLIGHT && !NETSTANDARD2_0
using System.Data;
using System.Data.Linq.Mapping;
#endif

namespace SIPSorcery.SIP.App
{
    public enum ProviderTypes
    {
        SIP = 0,
        GoogleVoice = 1,
    }

    public enum GoogleVoiceCallbackTypes
    {
        Home = 1,
        Mobile = 2,
        Work = 3,
        Gizmo = 7,
    }

    [Table(Name = "sipproviders")]
    [DataContractAttribute]
    public class SIPProvider : INotifyPropertyChanged, ISIPAsset
    {
        public const string XML_DOCUMENT_ELEMENT_NAME = "sipproviders";
        public const string XML_ELEMENT_NAME = "sipprovider";

        public const char CUSTOM_HEADERS_SEPARATOR = '|';
        public const int REGISTER_DEFAULT_EXPIRY = 3600;
        public const int REGISTER_MINIMUM_EXPIRY = 60;            // The minimum interval a registration will be accepted for. Anything less than this interval will use this minimum value.
        public const int REGISTER_MAXIMUM_EXPIRY = 3600;

        //public static string SelectProvider = "select * from sipproviders where id = ?1";

        public static string DisallowedServerPatterns;            // If set will be used as a regex pattern to prevent certain strings being used in the Provider Server and RegisterServer fields.

        private static string m_newLine = AppState.NewLine;
        private static ILog logger = AppState.logger;

        private ProviderTypes m_providerType;            // Identifies whether the provider entry is a SIP or Google Voice entry.
        [Column(Name = "providertype", DbType = "varchar(16)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public ProviderTypes ProviderType
        {
            get { return m_providerType; }
            set
            {
                m_providerType = value;
                NotifyPropertyChanged("ProviderType");
            }
        }

        private Guid m_id;
        [Column(Name = "id", DbType = "varchar(36)", IsPrimaryKey = true, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public Guid Id
        {
            get { return m_id; }
            set { m_id = value; }
        }

        private string m_owner;                         // The username of the account that owns this SIP provider configuration.
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

        private string m_providerName;
        [Column(Name = "providername", DbType = "varchar(50)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string ProviderName
        {
            get { return m_providerName; }
            set
            {
                m_providerName = value;
                NotifyPropertyChanged("ProviderName");
            }
        }

        private string m_providerUsername;
        [Column(Name = "providerusername", DbType = "varchar(32)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string ProviderUsername
        {
            get { return m_providerUsername; }
            set
            {
                m_providerUsername = value;
                NotifyPropertyChanged("ProviderUsername");
            }
        }

        private string m_providerPassword;
        [Column(Name = "providerpassword", DbType = "varchar(32)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string ProviderPassword
        {
            get { return m_providerPassword; }
            set
            {
                m_providerPassword = value;
                NotifyPropertyChanged("ProviderPassword");
            }
        }

        private SIPURI m_providerServer;
        [Column(Name = "providerserver", DbType = "varchar(256)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string ProviderServer
        {
            get { return (m_providerServer != null) ? m_providerServer.ToString() : null; }
            set
            {
                m_providerServer = (!value.IsNullOrBlank()) ? SIPURI.ParseSIPURIRelaxed(value) : null;
                NotifyPropertyChanged("ProviderServer");
            }
        }

        private string m_providerAuthUsername;             // An optional setting if authusername differs from username for authentication.
        [Column(Name = "providerauthusername", DbType = "varchar(32)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string ProviderAuthUsername
        {
            get { return m_providerAuthUsername; }
            set
            {
                m_providerAuthUsername = value;
                NotifyPropertyChanged("ProviderAuthUsername");
            }
        }

        private string m_providerOutboundProxy;
        [Column(Name = "provideroutboundproxy", DbType = "varchar(256)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string ProviderOutboundProxy
        {
            get { return m_providerOutboundProxy; }
            set
            {
                m_providerOutboundProxy = value;
                NotifyPropertyChanged("ProviderOutboundProxy");
            }
        }

        private string m_providerFrom;                     // If set determines how the From header will be set for calls to the SIP provider.
        [Column(Name = "providerfrom", DbType = "varchar(256)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string ProviderFrom
        {
            get { return m_providerFrom; }
            set
            {
                m_providerFrom = value;
                NotifyPropertyChanged("ProviderFrom");
            }
        }

        private string m_customHeaders;                  // An optional list of custom SIP headers that will be added to calls to the SIP provider.
        [Column(Name = "customheaders", DbType = "varchar(1024)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string CustomHeaders
        {
            get { return m_customHeaders; }
            set
            {
                m_customHeaders = value;
                NotifyPropertyChanged("CustomHeaders");
            }
        }

        private SIPURI m_registerContact;
        [Column(Name = "registercontact", DbType = "varchar(256)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string RegisterContact
        {
            get { return (m_registerContact != null) ? m_registerContact.ToString() : null; }
            set
            {
                m_registerContact = (!value.IsNullOrBlank()) ? SIPURI.ParseSIPURIRelaxed(value) : null;
                NotifyPropertyChanged("RegisterContact");
            }
        }

        private int m_registerExpiry = REGISTER_DEFAULT_EXPIRY;
        [Column(Name = "registerexpiry", DbType = "int", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public int RegisterExpiry
        {
            get { return m_registerExpiry; }
            set
            {
                m_registerExpiry = (value < REGISTER_MINIMUM_EXPIRY) ? REGISTER_MINIMUM_EXPIRY : value;
                NotifyPropertyChanged("RegisterExpiry");
            }
        }

        private SIPURI m_registerServer;                    // If set this host address will be used for the Registrar server, if not set the ProviderServer is used.
        [Column(Name = "registerserver", DbType = "varchar(256)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string RegisterServer
        {
            get { return (m_registerServer != null) ? m_registerServer.ToString() : null; }
            set
            {
                m_registerServer = (!value.IsNullOrBlank()) ? SIPURI.ParseSIPURIRelaxed(value) : null;
                NotifyPropertyChanged("RegisterServer");
            }
        }

        private string m_registerRealm;                     // An optional setting if the register realm differs from the provider server.
        [Column(Name = "registerrealm", DbType = "varchar(256)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string RegisterRealm
        {
            get { return m_registerRealm; }
            set
            {
                m_registerRealm = value;
                NotifyPropertyChanged("RegisterRealm");
            }
        }

        private bool m_registerEnabled;                     // If the registration has been disabled this will be set to false.
        [Column(Name = "registerenabled", DbType = "bit", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public bool RegisterEnabled
        {
            get { return m_registerEnabled; }
            set
            {
                m_registerEnabled = value;
                NotifyPropertyChanged("RegisterEnabled");
            }
        }

        private bool m_registerAdminEnabled;                // This setting allows and administrator to override the user setting and disable a registration.
        [Column(Name = "registeradminenabled", DbType = "bit", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public bool RegisterAdminEnabled
        {
            get { return m_registerAdminEnabled; }
            set
            {
                m_registerAdminEnabled = value;
                NotifyPropertyChanged("RegisterAdminEnabled");
            }
        }

        public bool RegisterActive
        {
            get { return RegisterEnabled && RegisterAdminEnabled; }
        }

        private string m_registerDisabledReason;            // If the registration agent disabled the registration it will set a reason.
        [Column(Name = "registerdisabledreason", DbType = "varchar(256)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string RegisterDisabledReason
        {
            get { return m_registerDisabledReason; }
            set
            {
                m_registerDisabledReason = value;
                NotifyPropertyChanged("RegisterDisabledReason");
            }
        }

        private string m_gvCallbackNumber;            // The callback number for Google Voice calls.
        [Column(Name = "gvcallbacknumber", DbType = "varchar(16)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string GVCallbackNumber
        {
            get { return m_gvCallbackNumber; }
            set
            {
                m_gvCallbackNumber = value;
                NotifyPropertyChanged("GVCallbackNumber");
            }
        }

        private string m_gvCallbackPattern;            // The callback number for Google Voice calls.
        [Column(Name = "gvcallbackpattern", DbType = "varchar(32)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string GVCallbackPattern
        {
            get { return m_gvCallbackPattern; }
            set
            {
                m_gvCallbackPattern = value;
                NotifyPropertyChanged("GVCallbackPattern");
            }
        }

        private GoogleVoiceCallbackTypes? m_gvCallbackType;
        [Column(Name = "gvcallbacktype", DbType = "varchar(16)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public GoogleVoiceCallbackTypes? GVCallbackType
        {
            get { return m_gvCallbackType; }
            set
            {
                m_gvCallbackType = value;
                NotifyPropertyChanged("GVCallbackType");
            }
        }

        private DateTimeOffset m_lastUpdate;
        [Column(Name = "lastupdate", DbType = "datetimeoffset", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public DateTimeOffset LastUpdate
        {
            get { return m_lastUpdate; }
            set
            {
                m_lastUpdate = value.ToUniversalTime();
                NotifyPropertyChanged("LastUpdate");
            }
        }

        private DateTimeOffset m_inserted;
        [Column(Name = "inserted", DbType = "datetimeoffset", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public DateTimeOffset Inserted
        {
            get { return m_inserted; }
            set { m_inserted = value.ToUniversalTime(); }
        }

        private bool m_isReadOnly;                     
        [Column(Name = "isreadonly", DbType = "bit", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public bool IsReadOnly
        {
            get { return m_isReadOnly; }
            set
            {
                m_isReadOnly = value;
                NotifyPropertyChanged("IsReadOnly");
            }
        }

        private bool m_sendMWISubscribe;                     // If this is set and the provider is being registered then an MWI subscription will be sent on a successful registration.
        [Column(Name = "sendmwisubscribe", DbType = "bit", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public bool SendMWISubscribe
        {
            get { return m_sendMWISubscribe; }
            set
            {
                m_sendMWISubscribe = value;
                NotifyPropertyChanged("SendMWISubscribe");
            }
        }

        /// <summary>
        /// Normally the registrar server will just be the main Provider server however in some cases they will be different.
        /// </summary>
        public SIPURI Registrar
        {
            get { return (m_registerServer != null) ? m_registerServer : m_providerServer; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public SIPProvider()
        { }

        public SIPProvider(
            ProviderTypes providerType,
            string owner,
            string name,
            string username,
            string password,
            SIPURI server,
            string outboundProxy,
            string from,
            string custom,
            SIPURI registerContact,
            int registerExpiry,
            SIPURI registerServer,
            string authUsername,
            string registerRealm,
            bool registerEnabled,
            bool registerEnabledAdmin,
            string gvCallbackNumber,
            string gvCallbackPattern,
            GoogleVoiceCallbackTypes? gvCallbackType)
        {
            m_providerType = providerType;
            m_owner = owner;
            m_id = Guid.NewGuid();
            m_providerName = name;
            m_providerUsername = username;
            m_providerPassword = password;
            m_providerServer = server;
            m_providerOutboundProxy = outboundProxy;
            m_providerFrom = from;
            m_customHeaders = custom;
            m_registerContact = registerContact;
            m_registerExpiry = (registerExpiry < REGISTER_MINIMUM_EXPIRY) ? REGISTER_MINIMUM_EXPIRY : registerExpiry;
            m_registerServer = registerServer;
            m_providerAuthUsername = authUsername;
            m_registerRealm = registerRealm;
            m_registerEnabled = registerEnabled;
            m_registerAdminEnabled = registerEnabledAdmin;
            m_gvCallbackNumber = gvCallbackNumber;
            m_gvCallbackPattern = gvCallbackPattern;
            m_gvCallbackType = gvCallbackType;
            Inserted = DateTimeOffset.UtcNow;
            LastUpdate = DateTimeOffset.UtcNow;

            //if (m_registerContact != null)
            //{
            //    m_registerContact.Parameters.Set(CONTACT_ID_KEY, Crypto.GetRandomString(6));
            //}

            //if (m_registerContact == null && m_registerEnabled)
            //{
            //    m_registerEnabled = false;
            //    m_registerDisabledReason = "No Contact URI was specified for the registration.";
            //    logger.Warn("Registrations for provider " + m_providerName + " owned by " + m_owner + " have been disabled due to an empty or invalid Contact URI.");
            //}
        }

        public static string ValidateAndClean(SIPProvider sipProvider)
        {
            if (sipProvider.ProviderName.IsNullOrBlank())
            {
                return "A value for Provider Name must be specified.";
            }
            else if (sipProvider.ProviderName.Contains("."))
            {
                return "The Provider Name cannot contain a full stop '.' in order to avoid ambiguity with DNS host names, please remove the '.'.";
            }

            if (sipProvider.ProviderType == ProviderTypes.SIP)
            {
                if (sipProvider.ProviderServer.IsNullOrBlank())
                {
                    return "A value for Server must be specified.";
                }
                if (sipProvider.RegisterEnabled && sipProvider.m_registerContact == null)
                {
                    return "A valid contact must be supplied to enable a provider registration.";
                }
                else if (sipProvider.m_providerServer.Host.IndexOf('.') == -1)
                {
                    return "Your provider server entry appears to be invalid. A valid hostname or IP address should contain at least one '.'.";
                }
                else if (sipProvider.m_registerServer != null && sipProvider.m_registerServer.Host.IndexOf('.') == -1)
                {
                    return "Your register server entry appears to be invalid. A valid hostname or IP address should contain at least one '.'.";
                }
                else if (sipProvider.m_registerContact != null && sipProvider.m_registerContact.Host.IndexOf('.') == -1)
                {
                    return "Your register contact entry appears to be invalid. A valid hostname or IP address should contain at least one '.'.";
                }
                else if (sipProvider.m_registerContact != null && sipProvider.m_registerContact.User.IsNullOrBlank())
                {
                    return "Your register contact entry appears to be invalid, the user portion was missing. Contacts must be of the form user@host.com, e.g. joe@sipsorcery.com.";
                }
                else if (DisallowedServerPatterns != null && Regex.Match(sipProvider.m_providerServer.Host, DisallowedServerPatterns).Success)
                {
                    return "The Provider Server contains a disallowed string. If you are trying to create a Provider entry pointing to sipsorcery.com it is not permitted.";
                }
                else if (DisallowedServerPatterns != null && sipProvider.m_registerServer != null && Regex.Match(sipProvider.m_registerServer.Host, DisallowedServerPatterns).Success)
                {
                    return "The Provider Register Server contains a disallowed string. If you are trying to create a Provider entry pointing to sipsorcery.com it is not permitted.";
                }
            }
            else
            {
                if (sipProvider.ProviderUsername.IsNullOrBlank())
                {
                    return "A username is required for Google Voice entries.";
                }
                else if (sipProvider.ProviderPassword.IsNullOrBlank())
                {
                    return "A password is required for Google Voice entries.";
                }
                else if (sipProvider.GVCallbackNumber.IsNullOrBlank())
                {
                    return "A callback number is required for Google Voice entries.";
                }
                else if (Regex.Match(sipProvider.GVCallbackNumber, @"\D").Success)
                {
                    return "The callback number contains an invalid character. Only digits are permitted.";
                }
            }

            return null;
        }

#if !SILVERLIGHT && !NETSTANDARD2_0

        public SIPProvider(DataRow bindingRow)
        {
            Load(bindingRow);
        }

        public DataTable GetTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add(new DataColumn("id", typeof(String)));
            table.Columns.Add(new DataColumn("providertype", typeof(String)));
            table.Columns.Add(new DataColumn("owner", typeof(String)));
            table.Columns.Add(new DataColumn("adminmemberid", typeof(String)));
            table.Columns.Add(new DataColumn("providername", typeof(String)));
            table.Columns.Add(new DataColumn("providerusername", typeof(String)));
            table.Columns.Add(new DataColumn("providerpassword", typeof(String)));
            table.Columns.Add(new DataColumn("providerserver", typeof(String)));
            table.Columns.Add(new DataColumn("providerauthusername", typeof(String)));
            table.Columns.Add(new DataColumn("provideroutboundproxy", typeof(String)));
            table.Columns.Add(new DataColumn("providerfrom", typeof(String)));
            table.Columns.Add(new DataColumn("customheaders", typeof(String)));
            table.Columns.Add(new DataColumn("registercontact", typeof(String)));
            table.Columns.Add(new DataColumn("registerexpiry", typeof(Int32)));
            table.Columns.Add(new DataColumn("registerserver", typeof(String)));
            table.Columns.Add(new DataColumn("registerrealm", typeof(String)));
            table.Columns.Add(new DataColumn("registerenabled", typeof(Boolean)));
            table.Columns.Add(new DataColumn("registeradminenabled", typeof(Boolean)));
            table.Columns.Add(new DataColumn("registerdisabledreason", typeof(String)));
            table.Columns.Add(new DataColumn("maxexecutioncount", typeof(Int32)));
            table.Columns.Add(new DataColumn("gvcallbacknumber", typeof(String)));
            table.Columns.Add(new DataColumn("gvcallbackpattern", typeof(String)));
            table.Columns.Add(new DataColumn("gvcallbacktype", typeof(String)));
            table.Columns.Add(new DataColumn("inserted", typeof(DateTimeOffset)));
            table.Columns.Add(new DataColumn("lastupdate", typeof(DateTimeOffset)));
            table.Columns.Add(new DataColumn("isreadonly", typeof(Boolean)));
            table.Columns.Add(new DataColumn("sendmwisubscribe", typeof(Boolean)));
            return table;
        }

        public void Load(DataRow providerRow)
        {
            try
            {
                m_id = (providerRow.Table.Columns.Contains("id") && providerRow["id"] != DBNull.Value && providerRow["id"] != null) ? new Guid(providerRow["id"] as string) : Guid.NewGuid();
                m_providerType = (ProviderTypes)Enum.Parse(typeof(ProviderTypes), providerRow["providertype"] as string, true);
                m_owner = providerRow["owner"] as string;
                AdminMemberId = (providerRow.Table.Columns.Contains("adminmemberid") && providerRow["adminmemberid"] != null) ? providerRow["adminmemberid"] as string : null;
                m_providerName = providerRow["providername"] as string;
                m_providerUsername = providerRow["providerusername"] as string;
                m_providerPassword = providerRow["providerpassword"] as string;
                m_providerServer = SIPURI.ParseSIPURIRelaxed(providerRow["providerserver"] as string);
                m_providerAuthUsername = (providerRow.Table.Columns.Contains("providerauthusername") && providerRow["providerauthusername"] != null) ? providerRow["providerauthusername"] as string : null;
                m_providerOutboundProxy = (providerRow.Table.Columns.Contains("provideroutboundproxy") && providerRow["provideroutboundproxy"] != null) ? providerRow["provideroutboundproxy"] as string : null;
                m_providerFrom = (providerRow.Table.Columns.Contains("providerfrom") && providerRow["providerfrom"] != null) ? providerRow["providerfrom"] as string : null;
                m_customHeaders = (providerRow.Table.Columns.Contains("customheaders") && providerRow["customheaders"] != null) ? providerRow["customheaders"] as string : null;
                m_registerContact = (providerRow.Table.Columns.Contains("registercontact") && providerRow["registercontact"] != DBNull.Value && providerRow["registercontact"] != null && providerRow["registercontact"].ToString().Length > 0) ? SIPURI.ParseSIPURIRelaxed(providerRow["registercontact"] as string) : null;
                m_registerExpiry = (providerRow.Table.Columns.Contains("registerexpiry") && providerRow["registerexpiry"] != DBNull.Value && providerRow["registerexpiry"] != null) ? Convert.ToInt32(providerRow["registerexpiry"]) : REGISTER_DEFAULT_EXPIRY;
                m_registerServer = (providerRow.Table.Columns.Contains("registerserver") && providerRow["registerserver"] != null) ? SIPURI.ParseSIPURIRelaxed(providerRow["registerserver"] as string) : null;
                m_registerRealm = (providerRow.Table.Columns.Contains("registerrealm") && providerRow["registerrealm"] != null) ? providerRow["registerrealm"] as string : null;
                m_registerEnabled = (providerRow.Table.Columns.Contains("registerenabled") && providerRow["registerenabled"] != DBNull.Value && providerRow["registerenabled"] != null) ? Convert.ToBoolean(providerRow["registerenabled"]) : false;
                m_registerAdminEnabled = (providerRow.Table.Columns.Contains("registeradminenabled") && providerRow["registeradminenabled"] != DBNull.Value && providerRow["registeradminenabled"] != null) ? Convert.ToBoolean(providerRow["registeradminenabled"]) : true;
                m_registerDisabledReason = (providerRow.Table.Columns.Contains("registerdisabledreason") && providerRow["registerdisabledreason"] != DBNull.Value && providerRow["registerdisabledreason"] != null) ? providerRow["registerdisabledreason"] as string : null;
                m_gvCallbackNumber = (providerRow.Table.Columns.Contains("gvcallbacknumber") && providerRow["gvcallbacknumber"] != null) ? providerRow["gvcallbacknumber"] as string : null;
                m_gvCallbackPattern = (providerRow.Table.Columns.Contains("gvcallbackpattern") && providerRow["gvcallbackpattern"] != null) ? providerRow["gvcallbackpattern"] as string : null;
                m_gvCallbackType = (providerRow.Table.Columns.Contains("gvcallbacktype") && providerRow["gvcallbacktype"] != DBNull.Value && providerRow["gvcallbacktype"] != null && (providerRow["gvcallbacktype"] as string).NotNullOrBlank()) ? (GoogleVoiceCallbackTypes)Enum.Parse(typeof(GoogleVoiceCallbackTypes), providerRow["gvcallbacktype"] as string, true) : (GoogleVoiceCallbackTypes?)null;
                LastUpdate = (providerRow.Table.Columns.Contains("lastupdate") && providerRow["lastupdate"] != DBNull.Value && providerRow["lastupdate"] != null) ? DateTimeOffset.Parse(providerRow["lastupdate"] as string) : DateTimeOffset.UtcNow;
                Inserted = (providerRow.Table.Columns.Contains("inserted") && providerRow["inserted"] != DBNull.Value && providerRow["inserted"] != null) ? DateTimeOffset.Parse(providerRow["inserted"] as string) : DateTimeOffset.UtcNow;
                m_isReadOnly = (providerRow.Table.Columns.Contains("isreadonly") && providerRow["isreadonly"] != DBNull.Value && providerRow["isreadonly"] != null) ? Convert.ToBoolean(providerRow["isreadonly"]) : false;
                m_sendMWISubscribe = (providerRow.Table.Columns.Contains("sendmwisubscribe") && providerRow["sendmwisubscribe"] != DBNull.Value && providerRow["sendmwisubscribe"] != null) ? Convert.ToBoolean(providerRow["sendmwisubscribe"]) : false;

                if (m_registerContact == null && m_registerEnabled)
                {
                    m_registerEnabled = false;
                    m_registerDisabledReason = "No Contact URI was specified for the registration.";
                     logger.Warn("Registrations for provider " + m_providerName + " owned by " + m_owner + " have been disabled due to an empty or invalid Contact URI.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPProvider Load. " + excp.Message);
                throw;
            }
        }

        //public Dictionary<Guid, object> Load(XmlDocument dom)
        //{
        //    return SIPAssetXMLPersistor<SIPProvider>.LoadAssetsFromXMLRecordSet(dom);
        //}

#endif

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
            string registrarStr = (m_registerServer != null) ? m_registerServer.ToString() : null;
            string registerContactStr = (m_registerContact != null) ? SafeXML.MakeSafeXML(m_registerContact.ToString()) : null;

            string providerXML =
                "   <id>" + m_id + "</id>" + m_newLine +
                "   <providertype>" + m_providerType + "<providertype>" + m_newLine +
                "   <owner>" + m_owner + "</owner>" + m_newLine +
                "   <adminmemberid>" + AdminMemberId + "</adminmemberid>" + m_newLine +
                "   <providername>" + m_providerName + "</providername>" + m_newLine +
                "   <providerusername>" + m_providerUsername + "</providerusername>" + m_newLine +
                "   <providerpassword>" + m_providerPassword + "</providerpassword>" + m_newLine +
                "   <providerserver>" + SafeXML.MakeSafeXML(m_providerServer.ToString()) + "</providerserver>" + m_newLine +
                "   <providerauthusername>" + m_providerAuthUsername + "</providerauthusername>" + m_newLine +
                "   <provideroutboundproxy>" + SafeXML.MakeSafeXML(m_providerOutboundProxy) + "</provideroutboundproxy>" + m_newLine +
                "   <providerfrom>" + SafeXML.MakeSafeXML(m_providerFrom) + "</providerfrom>" + m_newLine +
                "   <customheaders>" + SafeXML.MakeSafeXML(m_customHeaders) + "</customheaders>" + m_newLine +
                "   <registercontact>" + registerContactStr + "</registercontact>" + m_newLine +
                "   <registerexpiry>" + m_registerExpiry + "</registerexpiry>" + m_newLine +
                "   <registerserver>" + registrarStr + "</registerserver>" + m_newLine +
                "   <registerrealm>" + SafeXML.MakeSafeXML(m_registerRealm) + "</registerrealm>" + m_newLine +
                "   <registerenabled>" + m_registerEnabled + "</registerenabled>" + m_newLine +
                "   <registerenabledadmin>" + m_registerAdminEnabled + "</registerenabledadmin>" + m_newLine +
                "   <registerdisabledreason>" + SafeXML.MakeSafeXML(m_registerDisabledReason) + "</registerdisabledreason>" + m_newLine +
                "   <inserted>" + m_inserted.ToString("o") + "</inserted>" + m_newLine +
                "   <lastupdate>" + m_lastUpdate.ToString("o") + "</lastupdate>" + m_newLine +
                "   <isreadonly>" + m_isReadOnly + "</isreadonly>" + m_newLine +
                "   <sendmwisubscribe>" + m_sendMWISubscribe + "</sendmwisubscribe>" + m_newLine;

            return providerXML;
        }

        public string GetXMLElementName()
        {
            return XML_ELEMENT_NAME;
        }

        public string GetXMLDocumentElementName()
        {
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
