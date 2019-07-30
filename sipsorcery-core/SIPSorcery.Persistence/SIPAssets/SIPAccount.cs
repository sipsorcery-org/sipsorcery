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
// Copyright (c) 2008 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
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
    /// <remarks>
    /// SIP account usernames can be treated by some SIP Sorcery server agents as domain name like structures where a username of
    /// "x.username" will match the "username" account for receiving calls. To facilitate this SIP accounts with a '.' character in them
    /// can only be created where the suffix "username" portion matches the Owner field. This allows users to create SIP accounts with '.'
    /// in them but will prevent a different user from being able to hijack an "x.username" account and caue unexpected behaviour.
    /// </remarks>
    [Table(Name = "sipaccounts")]
    [DataContractAttribute]
    public class SIPAccount : ISIPAccount, INotifyPropertyChanged, ISIPAsset
    {
        public const string XML_DOCUMENT_ELEMENT_NAME = "sipaccounts";
        public const string XML_ELEMENT_NAME = "sipaccount";
        public const int PASSWORD_MIN_LENGTH = 6;
        public const int PASSWORD_MAX_LENGTH = 15;
        public const int USERNAME_MIN_LENGTH = 5;
        private const string BANNED_SIPACCOUNT_NAMES = "dispatcher";

        //public static readonly string SelectQuery = "select * from sipaccounts where sipusername = ?1 and sipdomain = ?2";

        // Only non-printable non-alphanumeric ASCII characters missing are ; \ and space. The semi-colon isn't accepted by 
        // Netgears and the space has the potential to create too much confusion with the users and \ with the system.
        public static readonly char[] NONAPLPHANUM_ALLOWED_PASSWORD_CHARS = new char[]{'!','"','$','%','&','(',')','*',
                                           '+',',','.','/',':','<','=','>','?','@','[',']','^','_','`','{','|','}','~'};
        public static readonly string USERNAME_ALLOWED_CHARS = @"a-zA-Z0-9_\-\.";

        private static ILog logger = AppState.logger;
        private static string m_newLine = AppState.NewLine;

        public static int TimeZoneOffsetMinutes;

        private Guid m_id;
        [Column(Name = "id", DbType = "varchar(36)", IsPrimaryKey = true, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public Guid Id
        {
            get { return m_id; }
            set { m_id = value; }
        }

        private string m_owner;                 // The username of the account that owns this SIP account.
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

        private string m_sipUsername;
        [Column(Name = "sipusername", DbType = "varchar(32)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
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
        [Column(Name = "sippassword", DbType = "varchar(32)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
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
        [Column(Name = "sipdomain", DbType = "varchar(128)", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
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
        [Column(Name = "sendnatkeepalives", DbType = "bit", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
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
        [Column(Name = "isincomingonly", DbType = "bit", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
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
        [Column(Name = "outdialplanname", DbType = "varchar(64)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
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
        [Column(Name = "indialplanname", DbType = "varchar(64)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
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
        [Column(Name = "isuserdisabled", DbType = "bit", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
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
        [Column(Name = "isadmindisabled", DbType = "bit", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
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
        [Column(Name = "admindisabledreason", DbType = "varchar(256)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
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
        [Column(Name = "networkid", DbType = "varchar(16)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
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
        [Column(Name = "ipaddressacl", DbType = "varchar(256)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
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

        private DateTimeOffset m_inserted;
        [Column(Name = "inserted", DbType = "datetimeoffset", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public DateTimeOffset Inserted
        {
            get { return m_inserted; }
            set { m_inserted = value.ToUniversalTime(); }
        }

        private bool m_isSwitchboardEnabled = true;
        [Column(Name = "isswitchboardenabled", DbType = "bit", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public bool IsSwitchboardEnabled
        {
            get { return m_isSwitchboardEnabled; }
            set
            {
                m_isSwitchboardEnabled = value;
                NotifyPropertyChanged("IsSwitchboardEnabled");
            }
        }

        private bool m_dontMangleEnabled = false;
        [Column(Name = "dontmangleenabled", DbType = "bit", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public bool DontMangleEnabled
        {
            get { return m_dontMangleEnabled; }
            set
            {
                m_dontMangleEnabled = value;
                NotifyPropertyChanged("DontMangleEnabled");
            }
        }

        private string m_avatarURL;
        [Column(Name = "avatarurl", DbType = "varchar(1024)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string AvatarURL
        {
            get { return m_avatarURL; }
            set
            {
                m_avatarURL = value;
                NotifyPropertyChanged("AvatarURL");
            }
        }

        private string m_accountCode;
        [Column(Name = "accountcode", DbType = "varchar(36)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string AccountCode
        {
            get { return m_accountCode; }
            set
            {
                m_accountCode = value;
                NotifyPropertyChanged("AccountCode");
            }
        }

        private string m_description;
        [Column(Name = "description", DbType = "varchar(1024)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        [DataMember]
        public string Description
        {
            get { return m_description; }
            set
            {
                m_description = value;
                NotifyPropertyChanged("Description");
            }
        }

        public DateTimeOffset InsertedLocal
        {
            get { return Inserted.AddMinutes(TimeZoneOffsetMinutes); }
        }

        public bool IsDisabled
        {
            get { return m_isUserDisabled || m_isAdminDisabled; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public SIPAccount() { }

#if !SILVERLIGHT && !NETSTANDARD2_0

        public SIPAccount(DataRow sipAccountRow)
        {
            Load(sipAccountRow);
        }

        public DataTable GetTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add(new DataColumn("id", typeof(String)));
            table.Columns.Add(new DataColumn("sipusername", typeof(String)));
            table.Columns.Add(new DataColumn("sippassword", typeof(String)));
            table.Columns.Add(new DataColumn("sipdomain", typeof(String)));
            table.Columns.Add(new DataColumn("owner", typeof(String)));
            table.Columns.Add(new DataColumn("adminmemberid", typeof(String)));
            table.Columns.Add(new DataColumn("sendnatkeepalives", typeof(Boolean)));
            table.Columns.Add(new DataColumn("isincomingonly", typeof(Boolean)));
            table.Columns.Add(new DataColumn("isuserdisabled", typeof(Boolean)));
            table.Columns.Add(new DataColumn("isadmindisabled", typeof(Boolean)));
            table.Columns.Add(new DataColumn("admindisabledreason", typeof(String)));
            table.Columns.Add(new DataColumn("outdialplanname", typeof(String)));
            table.Columns.Add(new DataColumn("indialplanname", typeof(String)));
            table.Columns.Add(new DataColumn("inserted", typeof(DateTimeOffset)));
            table.Columns.Add(new DataColumn("networkid", typeof(String)));
            table.Columns.Add(new DataColumn("ipaddressacl", typeof(String)));
            table.Columns.Add(new DataColumn("isswitchboardenabled", typeof(Boolean)));
            table.Columns.Add(new DataColumn("dontmangleenabled", typeof(Boolean)));
            table.Columns.Add(new DataColumn("avatarurl", typeof(String)));
            table.Columns.Add(new DataColumn("description", typeof(String)));
            table.Columns.Add(new DataColumn("accountcode", typeof(String)));
            return table;
        }

        public void Load(DataRow sipAccountRow)
        {
            try
            {
                m_id = (sipAccountRow.Table.Columns.Contains("id") && sipAccountRow["id"].ToString().Trim().Length > 0) ? new Guid(sipAccountRow["id"] as string) : Guid.NewGuid();
                m_sipUsername = sipAccountRow["sipusername"] as string;
                m_sipPassword = sipAccountRow["sippassword"] as string;
                m_sipDomain = sipAccountRow["sipdomain"] as string;
                m_owner = (sipAccountRow.Table.Columns.Contains("owner") && sipAccountRow["owner"] != null) ? sipAccountRow["owner"] as string : SIPUsername;
                AdminMemberId = (sipAccountRow.Table.Columns.Contains("adminmemberid") && sipAccountRow["adminmemberid"] != null) ? sipAccountRow["adminmemberid"] as string : null;
                m_sendNATKeepAlives = (sipAccountRow.Table.Columns.Contains("sendnatkeepalives") && sipAccountRow["sendnatkeepalives"] != null && sipAccountRow["sendnatkeepalives"] != DBNull.Value) ? Convert.ToBoolean(sipAccountRow["sendnatkeepalives"]) : false;
                m_isIncomingOnly = (sipAccountRow.Table.Columns.Contains("isincomingonly") && sipAccountRow["isincomingonly"] != null && sipAccountRow["isincomingonly"] != DBNull.Value) ? Convert.ToBoolean(sipAccountRow["isincomingonly"]) : false;
                m_outDialPlanName = (sipAccountRow.Table.Columns.Contains("outdialplanname") && sipAccountRow["outdialplanname"] != null && sipAccountRow["outdialplanname"].ToString().Trim().Length > 0) ? sipAccountRow["outdialplanname"] as string : null;
                m_inDialPlanName = (sipAccountRow.Table.Columns.Contains("indialplanname") && sipAccountRow["indialplanname"] != null && sipAccountRow["indialplanname"].ToString().Trim().Length > 0) ? sipAccountRow["indialplanname"] as string : null;
                m_isUserDisabled = (sipAccountRow.Table.Columns.Contains("isuserdisabled") && sipAccountRow["isuserdisabled"] != null && sipAccountRow["isuserdisabled"] != DBNull.Value) ? Convert.ToBoolean(sipAccountRow["isuserdisabled"]) : false;
                m_isAdminDisabled = (sipAccountRow.Table.Columns.Contains("isadmindisabled") && sipAccountRow["isadmindisabled"] != null && sipAccountRow["isadmindisabled"] != DBNull.Value) ? Convert.ToBoolean(sipAccountRow["isadmindisabled"]) : false;
                m_adminDisabledReason = (sipAccountRow.Table.Columns.Contains("admindisabledreason") && sipAccountRow["admindisabledreason"] != null) ? sipAccountRow["admindisabledreason"] as string : null;
                m_inserted = (sipAccountRow.Table.Columns.Contains("inserted") && sipAccountRow["inserted"] != null) ? DateTimeOffset.Parse(sipAccountRow["inserted"] as string) : DateTimeOffset.UtcNow;
                m_networkId = (sipAccountRow.Table.Columns.Contains("networkid") && sipAccountRow["networkid"] != null) ? sipAccountRow["networkid"] as string : null;
                m_ipAddressACL = (sipAccountRow.Table.Columns.Contains("ipaddressacl") && sipAccountRow["ipaddressacl"] != null) ? sipAccountRow["ipaddressacl"] as string : null;
                m_isSwitchboardEnabled = (sipAccountRow.Table.Columns.Contains("isswitchboardenabled") && sipAccountRow["isswitchboardenabled"] != null && sipAccountRow["isswitchboardenabled"] != DBNull.Value) ? Convert.ToBoolean(sipAccountRow["isswitchboardenabled"]) : false;
                m_dontMangleEnabled = (sipAccountRow.Table.Columns.Contains("dontmangleenabled") && sipAccountRow["dontmangleenabled"] != null && sipAccountRow["dontmangleenabled"] != DBNull.Value) ? Convert.ToBoolean(sipAccountRow["dontmangleenabled"]) : false;
                m_avatarURL = (sipAccountRow.Table.Columns.Contains("avatarurl") && sipAccountRow["avatarurl"] != null) ? sipAccountRow["avatarurl"] as string : null;
                m_accountCode = (sipAccountRow.Table.Columns.Contains("accountcode") && sipAccountRow["accountcode"] != null) ? sipAccountRow["accountcode"] as string : null;
                m_description = (sipAccountRow.Table.Columns.Contains("description") && sipAccountRow["description"] != null) ? sipAccountRow["description"] as string : null;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPAccount Load. " + excp);
                throw excp;
            }
        }

        //        public Dictionary<Guid, object> Load(XmlDocument dom) {
        //            return SIPAssetXMLPersistor<SIPAccount>.LoadAssetsFromXMLRecordSet(dom);
        //        }

#endif

        public SIPAccount(string owner, string sipDomain, string sipUsername, string sipPassword, string outDialPlanName)
        {
            try
            {
                m_id = Guid.NewGuid();
                m_owner = owner;
                m_sipDomain = sipDomain;
                m_sipUsername = sipUsername;
                m_sipPassword = sipPassword;
                m_outDialPlanName = (outDialPlanName != null && outDialPlanName.Trim().Length > 0) ? outDialPlanName.Trim() : null;
                m_inserted = DateTimeOffset.UtcNow;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPAccount (ctor). " + excp);
                throw excp;
            }
        }

        public static string ValidateAndClean(SIPAccount sipAccount)
        {

            if (sipAccount.Owner.IsNullOrBlank())
            {
                return "The owner must be specified when creating a new SIP account.";
            }
            if (sipAccount.SIPUsername.IsNullOrBlank())
            {
                return "The username must be specified when creating a new SIP account.";
            }
            if (sipAccount.SIPDomain.IsNullOrBlank())
            {
                return "The domain must be specified when creating a new SIP account.";
            }
            else if (sipAccount.SIPUsername.Length < USERNAME_MIN_LENGTH)
            {
                return "The username must be at least " + USERNAME_MIN_LENGTH + " characters long.";
            }
            else if (Regex.Match(sipAccount.SIPUsername, BANNED_SIPACCOUNT_NAMES).Success)
            {
                return "The username you have requested is not permitted.";
            }
            else if (Regex.Match(sipAccount.SIPUsername, "[^" + USERNAME_ALLOWED_CHARS + "]").Success)
            {
                return "The username had an invalid character, characters permitted are alpha-numeric and .-_.";
            }
            else if (sipAccount.SIPUsername.Contains(".") &&
                (sipAccount.SIPUsername.Substring(sipAccount.SIPUsername.LastIndexOf(".") + 1).Trim().Length >= USERNAME_MIN_LENGTH &&
                sipAccount.SIPUsername.Substring(sipAccount.SIPUsername.LastIndexOf(".") + 1).Trim() != sipAccount.Owner))
            {
                return "You are not permitted to create this username. Only user " + sipAccount.SIPUsername.Substring(sipAccount.SIPUsername.LastIndexOf(".") + 1).Trim() + " can create SIP accounts ending in " + sipAccount.SIPUsername.Substring(sipAccount.SIPUsername.LastIndexOf(".")).Trim() + ".";
            }
            else if (!sipAccount.IsIncomingOnly || !sipAccount.SIPPassword.IsNullOrBlank())
            {
                if (sipAccount.SIPPassword.IsNullOrBlank())
                {
                    return "A password must be specified.";
                }
                else if (sipAccount.SIPPassword.Length < PASSWORD_MIN_LENGTH || sipAccount.SIPPassword.Length > PASSWORD_MAX_LENGTH)
                {
                    return "The password field must be at least " + PASSWORD_MIN_LENGTH + " characters and no more than " + PASSWORD_MAX_LENGTH + " characters.";
                }
                else
                {
                    #region Check the password illegal characters.

                    char[] passwordChars = sipAccount.SIPPassword.ToCharArray();

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
                        return "Your password has an invalid character " + illegalChar + " it can only contain a to Z, 0 to 9 and characters in this set " + SafeXML.MakeSafeXML(new String(NONAPLPHANUM_ALLOWED_PASSWORD_CHARS)) + ".";
                    }
                }
            }

            sipAccount.Owner = sipAccount.Owner.Trim();
            sipAccount.SIPUsername = sipAccount.SIPUsername.Trim();
            sipAccount.SIPPassword = (sipAccount.SIPPassword.IsNullOrBlank()) ? null : sipAccount.SIPPassword.Trim();
            sipAccount.SIPDomain = sipAccount.SIPDomain.Trim();

            return null;
        }

        public string ToXML()
        {
            string sipAccountXML =
                " <" + XML_ELEMENT_NAME + ">" + m_newLine +
               ToXMLNoParent() + m_newLine +
                " </" + XML_ELEMENT_NAME + ">" + m_newLine;

            return sipAccountXML;
        }

        public string ToXMLNoParent()
        {
            string sipAccountXML =
                "  <id>" + m_id + "</id>" + m_newLine +
                "  <owner>" + m_owner + "</owner>" + m_newLine +
                "  <sipusername>" + m_sipUsername + "</sipusername>" + m_newLine +
                "  <sippassword>" + m_sipPassword + "</sippassword>" + m_newLine +
                "  <sipdomain>" + m_sipDomain + "</sipdomain>" + m_newLine +
                "  <sendnatkeepalives>" + m_sendNATKeepAlives + "</sendnatkeepalives>" + m_newLine +
                "  <isincomingonly>" + m_isIncomingOnly + "</isincomingonly>" + m_newLine +
                "  <outdialplanname>" + m_outDialPlanName + "</outdialplanname>" + m_newLine +
                "  <indialplanname>" + m_inDialPlanName + "</indialplanname>" + m_newLine +
                "  <isuserdisabled>" + m_isUserDisabled + "</isuserdisabled>" + m_newLine +
                "  <isadmindisabled>" + m_isAdminDisabled + "</isadmindisabled>" + m_newLine +
                "  <disabledreason>" + m_adminDisabledReason + "</disabledreason>" + m_newLine +
                "  <networkid>" + m_networkId + "</networkid>" + m_newLine +
                "  <ipaddressacl>" + SafeXML.MakeSafeXML(m_ipAddressACL) + "</ipaddressacl>" + m_newLine +
                "  <inserted>" + m_inserted.ToString("o") + "</inserted>" + m_newLine +
                "  <isswitchboardenabled>" + m_isSwitchboardEnabled + "</isswitchboardenabled>" + m_newLine +
                "  <dontmangleenabled>" + m_dontMangleEnabled + "</dontmangleenabled>" + m_newLine +
                "  <avatarurl>" + m_avatarURL + "</avatarurl>";

            return sipAccountXML;
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
