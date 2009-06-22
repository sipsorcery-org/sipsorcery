// ============================================================================
// FileName: SIPAccount.cs
//
// Description:
// Represents a SIP account that holds authentication information and additional settings
// for SIP accounts.
//
// Author(s):
// Aaron Clauson
//
// History:
// 10 May 2008  Aaron Clauson   Created.
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
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
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
    [Table(Name = "sipaccounts")]
    [DataContractAttribute]
    public class SIPAccount : INotifyPropertyChanged, ISIPAsset
    {
        public const string XML_DOCUMENT_ELEMENT_NAME = "sipaccounts";
        public const string XML_ELEMENT_NAME = "sipaccount";
        public const int PASSWORD_MIN_LENGTH = 6;
        public const int PASSWORD_MAX_LENGTH = 15;
        public const int USERNAME_MIN_LENGTH = 5;

        // Only non-printable non-alphanumeric ASCII characters missing are ; \ and space. The semi-colon isn't accepted by 
        // Netgears and the space has the potential to create too much confusion with the users and \ with the system.
        public static readonly char[] NONAPLPHANUM_ALLOWED_PASSWORD_CHARS = new char[]{'!','"','$','%','&','(',')','*',
										   '+',',','.','/',':','<','=','>','?','@','[',']','^','_','`','{','|','}','~'};
        public static readonly string USERNAME_ALLOWED_CHARS = @"a-zA-Z0-9_\-";

        private static ILog logger = AppState.logger;
        private static string m_newLine = AppState.NewLine;

        private string m_id;
        [Column(Storage = "_id", Name = "id", DbType = "character varying(36)", IsPrimaryKey = true, CanBeNull = false)]
        [DataMember]
        public string Id
        {
            get { return m_id; }
            set { m_id = value; }
        }

        private string m_owner;                 // The username of the account that owns this SIP account.
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

        private string m_sipUsername;
        [Column(Storage = "_sipusername", Name = "sipusername", DbType = "character varying(32)", CanBeNull = false)]
        [DataMember]
        public string SIPUsername
        {
            get { return m_sipUsername; }
            set
            {
                m_sipUsername = value;
                NotifyPropertyChanged("SIPUsername");
            }
        }

        private string m_sipPassword;
        [Column(Storage = "_sippassword", Name = "sippassword", DbType = "character varying(32)", CanBeNull = true)]
        [DataMember]
        public string SIPPassword
        {
            get { return m_sipPassword; }
            set
            {
                m_sipPassword = value;
                NotifyPropertyChanged("SIPPassword");
            }
        }

        private string m_sipDomain;
        [Column(Storage = "_sipdomain", Name = "sipdomain", DbType = "character varying(128)", CanBeNull = false)]
        [DataMember]
        public string SIPDomain
        {
            get { return m_sipDomain; } 
            set 
            {
                m_sipDomain = value;
                NotifyPropertyChanged("SIPDomain"); 
            } 
        }

        private bool m_sendNATKeepAlives;
        [Column(Storage = "_sendnatkeepalives", Name = "sendnatkeepalives", DbType = "boolean", CanBeNull = false)]
        [DataMember]
        public bool SendNATKeepAlives
        {
            get { return m_sendNATKeepAlives; }
            set
            {
                m_sendNATKeepAlives = value;
                NotifyPropertyChanged("SendNATKeepAlives");
            }
        }

        private bool m_isIncomingOnly;          // For SIP accounts that can only be used to receive incoming calls.
        [Column(Storage = "_isincomingonly", Name = "isincomingonly", DbType = "boolean", CanBeNull = false)]
        [DataMember]
        public bool IsIncomingOnly
        {
            get { return m_isIncomingOnly; }
            set
            {
                m_isIncomingOnly = value;
                NotifyPropertyChanged("IsIncomingOnly");
            }
        }

        private string m_outDialPlanName;       // The dialplan that will be used for outgoing calls.
        [Column(Storage = "_outdialplanname", Name = "outdialplanname", DbType = "character varying(64)", CanBeNull = true)]
        [DataMember]
        public string OutDialPlanName
        {
            get { return m_outDialPlanName; }
            set
            {
                m_outDialPlanName = value;
                NotifyPropertyChanged("OutDialPlanName");
            }
        }

        private string m_inDialPlanName;        // The dialplan that will be used for incoming calls. If this field is empty incoming calls will be forwarded to the account's current bindings.
        [Column(Storage = "_indialplanname", Name = "indialplanname", DbType = "character varying(64)", CanBeNull = true)]
        [DataMember]
        public string InDialPlanName
        {
            get { return m_inDialPlanName; }
            set
            {
                m_inDialPlanName = value;
                NotifyPropertyChanged("InDialPlanName");
            }
        }

        private bool m_isUserDisabled;              // Allows owning user disabling of accounts.
        [Column(Storage = "_isuserdisabled", Name = "isuserdisabled", DbType = "boolean", CanBeNull = false)]
        [DataMember]
        public bool IsUserDisabled
        {
            get { return m_isUserDisabled; }
            set
            {
                m_isUserDisabled = value;
                NotifyPropertyChanged("IsUserDisabled");
                NotifyPropertyChanged("IsDisabled");
            }
        }

        private bool m_isAdminDisabled;              // Allows administrative disabling of accounts.
        [Column(Storage = "_isadmindisabled", Name = "isadmindisabled", DbType = "boolean", CanBeNull = false)]
        [DataMember]
        public bool IsAdminDisabled
        {
            get { return m_isAdminDisabled; }
            set
            {
                m_isAdminDisabled = value;
                NotifyPropertyChanged("IsAdminDisabled");
                NotifyPropertyChanged("IsDisabled");
            }
        }

        private string m_adminDisabledReason;
        [Column(Storage = "_admindisabledreason", Name = "admindisabledreason", DbType = "character varying(256)", CanBeNull = true)]
        [DataMember]
        public string AdminDisabledReason
        {
            get { return m_adminDisabledReason; }
            set
            {
                m_adminDisabledReason = value;
                NotifyPropertyChanged("AdminDisabledReason");
            }
        }

        private string m_networkId;                 // SIP accounts with the ame network id will not have their Contact headers or SDP mangled for private IP address.
        [Column(Storage = "_networkid", Name = "networkid", DbType = "character varying(16)", CanBeNull = true)]
        [DataMember]
        public string NetworkId
        {
            get { return m_networkId; }
            set
            {
                m_networkId = value;
                NotifyPropertyChanged("NetworkId");
            }
        }

        private string m_ipAddressACL;              // A regular expression that acts as an IP address Access Control List for SIP request authorisation.
        [Column(Storage = "_ipaddressacl", Name = "ipaddressacl", DbType = "character varying(256)", CanBeNull = true)]
        [DataMember]
        public string IPAddressACL
        {
            get { return m_ipAddressACL; }
            set
            {
                m_ipAddressACL = value;
                NotifyPropertyChanged("IPAddressACL");
            }
        }

        private DateTime m_inserted;
        [Column(Storage = "_inserted", Name = "inserted", DbType = "timestamp", CanBeNull = false)]
        [DataMember]
        public DateTime Inserted {
            get { return m_inserted; }
            set {
                m_inserted = value;
                NotifyPropertyChanged("Inserted");
            }
        }

        public bool IsDisabled
        {
            get { return m_isUserDisabled || m_isAdminDisabled; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public SIPAccount() { }

#if !SILVERLIGHT

        public SIPAccount(DataRow sipAccountRow) {
            Load(sipAccountRow);
        }

        public void Load(DataRow sipAccountRow)
        {
            try
            {
                m_id = (sipAccountRow.Table.Columns.Contains("id") && sipAccountRow["id"].ToString().Trim().Length > 0) ? sipAccountRow["id"] as string : Guid.NewGuid().ToString();
                m_sipUsername = sipAccountRow["sipusername"] as string;
                m_sipPassword = sipAccountRow["sippassword"] as string;
                m_sipDomain = sipAccountRow["domain"] as string;
                m_owner = (sipAccountRow.Table.Columns.Contains("owner") && sipAccountRow["owner"] != null) ? sipAccountRow["owner"] as string : SIPUsername;
                m_sendNATKeepAlives = (sipAccountRow.Table.Columns.Contains("sendnatkeepalives") && sipAccountRow["sendnatkeepalives"] != null && sipAccountRow["sendnatkeepalives"] != DBNull.Value) ? Convert.ToBoolean(sipAccountRow["sendnatkeepalives"]) : false;
                m_isIncomingOnly = (sipAccountRow.Table.Columns.Contains("isincomingonly") && sipAccountRow["isincomingonly"] != null && sipAccountRow["isincomingonly"] != DBNull.Value) ? Convert.ToBoolean(sipAccountRow["isincomingonly"]) : false;
                m_outDialPlanName = (sipAccountRow.Table.Columns.Contains("outdialplanname") && sipAccountRow["outdialplanname"] != null && sipAccountRow["outdialplanname"].ToString().Trim().Length > 0) ? sipAccountRow["outdialplanname"] as string : null;
                m_inDialPlanName = (sipAccountRow.Table.Columns.Contains("indialplanname") && sipAccountRow["indialplanname"] != null && sipAccountRow["indialplanname"].ToString().Trim().Length > 0) ? sipAccountRow["indialplanname"] as string : null;
                m_isUserDisabled = (sipAccountRow.Table.Columns.Contains("isuserdisabled") && sipAccountRow["isuserdisabled"] != null && sipAccountRow["isuserdisabled"] != DBNull.Value) ? Convert.ToBoolean(sipAccountRow["isuserdisabled"]) : false;
                m_isAdminDisabled = (sipAccountRow.Table.Columns.Contains("isadmindisabled") && sipAccountRow["isadmindisabled"] != null && sipAccountRow["isadmindisabled"] != DBNull.Value) ? Convert.ToBoolean(sipAccountRow["isadmindisabled"]) : false;
                m_adminDisabledReason = (sipAccountRow.Table.Columns.Contains("admindisabledreason") && sipAccountRow["admindisabledreason"] != null) ? sipAccountRow["admindisabledreason"] as string : null;
                m_inserted = (sipAccountRow.Table.Columns.Contains("inserted") && sipAccountRow["inserted"] != null) ? Convert.ToDateTime(sipAccountRow["inserted"]) : DateTime.Now;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPAccount Load. " + excp);
                throw excp;
            }
        }

        public Dictionary<Guid, object> Load(XmlDocument dom) {
            return SIPAssetXMLPersistor<SIPAccount>.LoadAssetsFromXMLRecordSet(dom);
        }

#endif

        public SIPAccount(string owner, string sipDomain, string sipUsername, string sipPassword, string outDialPlanName)
        {
            try
            {
                if (owner == null || owner.Trim().Length == 0)
                {
                    throw new ApplicationException("The owner must be specified when creating a new SIP account.");
                }
                if (sipUsername == null || sipUsername.Trim().Length == 0)
                {
                    throw new ApplicationException("The username must be specified when creating a new SIP account.");
                }
                if (sipDomain == null || sipDomain.Trim().Length == 0)
                {
                    throw new ApplicationException("The domain must be specified when creating a new SIP account.");
                }
                else if (Regex.Match(sipUsername, "[^" + USERNAME_ALLOWED_CHARS + "]").Success)
                {
                    throw new ArgumentException("The username had an invalid character, characters permitted are alpha-numeric and .-_.");
                }
                else if (sipPassword == null || sipPassword.Trim().Length == 0)
                {
                    throw new ArgumentException("A password must be specified.");
                }
                else if (sipPassword.Length < PASSWORD_MIN_LENGTH || sipPassword.Length > PASSWORD_MAX_LENGTH)
                {
                    throw new ArgumentException("The password field must be at least " + PASSWORD_MIN_LENGTH + " characters and no more than " + PASSWORD_MAX_LENGTH + " characters.");
                }
                else
                {
                    #region Check the password illegal characters.

                    char[] passwordChars = sipPassword.ToCharArray();

                    bool illegalCharFound = false;
                    char illegalChar = ' ';

                    foreach (char passwordChar in passwordChars)
                    {
                        if (Regex.Match(passwordChar.ToString(), "[a-zA-Z0-9]").Success)
                        {
                            continue;
                        }
                        else
                        {
                            bool validChar = false;
                            foreach (char allowedChar in NONAPLPHANUM_ALLOWED_PASSWORD_CHARS)
                            {
                                if (allowedChar == passwordChar)
                                {
                                    validChar = true;
                                    break;
                                }
                            }

                            if (validChar)
                            {
                                continue;
                            }
                            else
                            {
                                illegalCharFound = true;
                                illegalChar = passwordChar;
                                break;
                            }
                        }
                    }

                    #endregion

                    if (illegalCharFound)
                    {
                        throw new ArgumentException("Your password has an invalid character " + illegalChar + " it can only contain a to Z, 0 to 9 and characters in this set " + SafeXML.MakeSafeXML(new String(NONAPLPHANUM_ALLOWED_PASSWORD_CHARS)) + ".");
                    }
                }

                m_id = Guid.NewGuid().ToString();
                m_owner = owner.Trim();
                m_sipDomain = sipDomain.Trim();
                m_sipUsername = sipUsername.Trim();
                m_sipPassword = sipPassword.Trim();
                m_outDialPlanName = (outDialPlanName != null && outDialPlanName.Trim().Length > 0) ? outDialPlanName.Trim() : null;
                m_inserted = DateTime.Now;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPAccount (ctor). " + excp);
                throw excp;
            }
        }

        public string ToXML()
        {
            string sipAccountXML =
                "  <" + XML_ELEMENT_NAME + ">" + m_newLine +
               ToXMLNoParent() + m_newLine +
                "  </" + XML_ELEMENT_NAME + ">" + m_newLine;

            return sipAccountXML;
        }

        public string ToXMLNoParent()
        {
            string sipAccountXML =
                "    <id>" + m_id + "</id>" + m_newLine +
                "    <owner>" + m_owner + "</owner>" + m_newLine +
                "    <sipusername>" + m_sipUsername + "</sipusername>" + m_newLine +
                "    <sippassword>" + m_sipPassword + "</sippassword>" + m_newLine +
                "    <domain>" + m_sipDomain + "</domain>" + m_newLine +
                "    <sendnatkeepalives>" + m_sendNATKeepAlives + "</sendnatkeepalives>" + m_newLine +
                "    <isincomingonly>" + m_isIncomingOnly + "</isincomingonly>" + m_newLine +
                "    <outdialplanname>" + m_outDialPlanName + "</outdialplanname>" + m_newLine +
                "    <indialplanname>" + m_inDialPlanName + "</indialplanname>" + m_newLine +
                "    <isuserdisabled>" + m_isUserDisabled + "</isuserdisabled>" + m_newLine +
                "    <isadmindisabled>" + m_isAdminDisabled + "</isadmindisabled>" + m_newLine +
                "    <disabledreason>" + m_adminDisabledReason + "</disabledreason>" + m_newLine;

            return sipAccountXML;
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
