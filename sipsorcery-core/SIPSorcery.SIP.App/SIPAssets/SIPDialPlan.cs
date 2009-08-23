// ============================================================================
// FileName: SIPDialPlan.cs
//
// Description:
// Represents a user dialplan that can be passed to the SIPDialPlanEngine to process
// the user's calls.
//
// Author(s):
// Aaron Clauson
//
// History:
// 28 Sep 2008	Aaron Clauson	Created.
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
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
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
#endif

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP.App
{
    [Table(Name = "sipdialplans")]
    [DataContractAttribute]
    public class SIPDialPlan : INotifyPropertyChanged, ISIPAsset
    {
        public const string XML_DOCUMENT_ELEMENT_NAME = "sipdialplans";
        public const string XML_ELEMENT_NAME = "sipdialplan";
        public const string DEFAULT_DIALPLAN_NAME = "default";      // The default name a dialplan will be assigned if the owner's first dialplan and the name is not set.
        public const int DEFAULT_MAXIMUM_EXECUTION_COUNT = 3;       // The default value for the maximum allowed simultaneous executions of a dial plan.
        public const string ALL_APPS_AUTHORISED = "*";              // Used in the priviled application authorisation field when the dialplan is authorised for all applications.
        public const string PROPERTY_EXECUTIONCOUNT_NAME = "ExecutionCount";

        private static string m_newLine = AppState.NewLine;

        private ILog logger = AppState.logger;

        public static int TimeZoneOffsetMinutes;
        
        private string m_id;                  // Dial plan id used by the system. This is the database primary key and is not important for XML.
        [Column(Storage = "_id", Name = "id", DbType = "character varying(36)", IsPrimaryKey = true, CanBeNull = false)]
        [DataMember]
        public string Id
        {
            get { return m_id; }
            set { m_id = value; }
        }

        private string m_owner;                     // The username of the dialplan owner.
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

        private string m_dialPlanName;              // The name of the dialplan assigned by the owner, owner/name combinations must be unique
        [Column(Storage = "_dialplanname", Name = "dialplanname", DbType = "character varying(64)", CanBeNull = false)]
        [DataMember]
        public string DialPlanName {
            get { return m_dialPlanName; }
            set {
                m_dialPlanName = value;
                NotifyPropertyChanged("DialPlanName");
            }
        }

        private string m_traceEmailAddress;         // Optional email address to send dialplan traces to. if empty traces will not be used.
        [Column(Storage = "_traceemailaddress", Name = "traceemailaddress", DbType = "character varying(256)", CanBeNull = false)]
        [DataMember]
        public string TraceEmailAddress
        {
            get { return m_traceEmailAddress; }
            set
            {
                m_traceEmailAddress = value;
                NotifyPropertyChanged("TraceEmailAddress");
            }
        }

        private string m_dialPlanScript;            // The string representing the dialplan script (or asterisk extension lines).
        [Column(Storage = "_dialplanscript", Name = "dialplanscript", DbType = "character varying(20000)", CanBeNull = false)]
        [DataMember]
        public string DialPlanScript
        {
            get { return m_dialPlanScript; }
            set
            {
                m_dialPlanScript = value;
                NotifyPropertyChanged("DialPlanScript");
            }
        }

        private string m_scriptTypeDescription;     // Silverlight can't handle enum types across WCF boundaries.
        [Column(Storage = "_scripttypedescription", Name = "scripttypedescription", DbType = "character varying(12)", CanBeNull = false)]
        [DataMember]
        public string ScriptTypeDescription
        {
            get { return m_scriptTypeDescription; }
            set
            {
                m_scriptTypeDescription = value;
                NotifyPropertyChanged("ScriptTypeDescription");
            }
        }

        public SIPDialPlanScriptTypesEnum ScriptType
        {
            get { return SIPDialPlanScriptTypes.GetSIPDialPlanScriptType(m_scriptTypeDescription); }
        }

        private DateTime m_lastUpdateUTC;
        [Column(Storage = "_lastupdate", Name = "lastupdate", DbType = "timestamp", CanBeNull = true)]
        [DataMember]
        public DateTime LastUpdateUTC
        {
            get { return m_lastUpdateUTC; }
            set
            {
                m_lastUpdateUTC = value;
                NotifyPropertyChanged("LastUpdate");
            }
        }

        public DateTime LastUpdate {
            get { return LastUpdateUTC.AddMinutes(TimeZoneOffsetMinutes); }
        }

        private DateTime m_insertedUTC;
        [Column(Storage = "_inserted", Name = "inserted", DbType = "timestamp", CanBeNull = false)]
        [DataMember]
        public DateTime InsertedUTC {
            get { return m_insertedUTC; }
            set { m_insertedUTC = value; }
        }

        public DateTime Inserted {
            get { return InsertedUTC.AddMinutes(TimeZoneOffsetMinutes); }
        }

        private int m_maxExecutionCount = DEFAULT_MAXIMUM_EXECUTION_COUNT;
        [DataMember]
        [Column(Storage = "_maxexecutioncount", Name = "maxexecutioncount", DbType = "integer", CanBeNull = false)]
        public int MaxExecutionCount
        {
            get { return m_maxExecutionCount; }
            set { m_maxExecutionCount = value;}  
        }

        private int m_executionCount;
        [DataMember]
        [Column(Storage = "_executioncount", Name = "executioncount", DbType = "integer", CanBeNull = false)]
        public int ExecutionCount
        {
            get { return m_executionCount; }
            set { m_executionCount = value; }
        }

        private string m_authorisedApps;     // A semi-colon delimited list of privileged apps that this dialplan is authorised to use.
        [DataMember]
        [Column(Storage = "_authorisedapps", Name = "authorisedapps", DbType = "character varying(2048)", CanBeNull = true)]
        public string AuthorisedApps {
            get { return m_authorisedApps; }
            set {
                m_authorisedApps = value;
                NotifyPropertyChanged("AuthorisedApps");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public SIPDialPlan() { }

        public SIPDialPlan(string owner, string dialPlanName, string traceEmailAddress, string script, SIPDialPlanScriptTypesEnum scriptType)
        {
            m_id = Guid.NewGuid().ToString();
            m_owner = owner;
            m_dialPlanName = (dialPlanName != null && dialPlanName.Trim().Length > 0) ? dialPlanName : DEFAULT_DIALPLAN_NAME;
            m_traceEmailAddress = traceEmailAddress;
            m_dialPlanScript = script;
            m_scriptTypeDescription = scriptType.ToString();
            m_insertedUTC = DateTime.Now.ToUniversalTime();
            m_lastUpdateUTC = DateTime.Now.ToUniversalTime();
        }

#if !SILVERLIGHT

        public SIPDialPlan(DataRow dialPlanRow) {
            Load(dialPlanRow);
        }

        public void Load(DataRow dialPlanRow) {
            try {
                m_id = (dialPlanRow.Table.Columns.Contains("id") && dialPlanRow["id"] != null) ? dialPlanRow["id"] as string : Guid.NewGuid().ToString();
                m_owner = dialPlanRow["owner"] as string;
                AdminMemberId = (dialPlanRow.Table.Columns.Contains("adminmemberid") && dialPlanRow["adminmemberid"] != null) ? dialPlanRow["adminmemberid"] as string : null;
                m_dialPlanName = (dialPlanRow["dialplanname"] != null && dialPlanRow["dialplanname"].ToString().Trim().Length > 0) ? dialPlanRow["dialplanname"].ToString().Trim() : null;
                m_traceEmailAddress = (dialPlanRow.Table.Columns.Contains("traceemailaddress") && dialPlanRow["traceemailaddress"] != null) ? dialPlanRow["traceemailaddress"] as string : null;
                m_dialPlanScript = (dialPlanRow["dialplanscript"] as string).Trim();
                m_scriptTypeDescription = (dialPlanRow.Table.Columns.Contains("scripttype") && dialPlanRow["scripttype"] != null) ? SIPDialPlanScriptTypes.GetSIPDialPlanScriptType(dialPlanRow["scripttype"] as string).ToString() : SIPDialPlanScriptTypesEnum.Ruby.ToString();
                m_maxExecutionCount = (dialPlanRow.Table.Columns.Contains("maxexecutioncount") && dialPlanRow["maxexecutioncount"] != null) ? Convert.ToInt32(dialPlanRow["maxexecutioncount"]) : DEFAULT_MAXIMUM_EXECUTION_COUNT;
                m_executionCount = (dialPlanRow.Table.Columns.Contains("executioncount") && dialPlanRow["executioncount"] != null) ? Convert.ToInt32(dialPlanRow["executioncount"]) : DEFAULT_MAXIMUM_EXECUTION_COUNT;
                m_authorisedApps = (dialPlanRow.Table.Columns.Contains("authorisedapps") && dialPlanRow["authorisedapps"] != null) ? dialPlanRow["authorisedapps"] as string : null;
                InsertedUTC = (dialPlanRow.Table.Columns.Contains("inserted")&& dialPlanRow["inserted"] != null && dialPlanRow["inserted"] != DBNull.Value) ? InsertedUTC = Convert.ToDateTime(dialPlanRow["inserted"]) : DateTime.Now;
                LastUpdateUTC = (dialPlanRow.Table.Columns.Contains("lastupdate") && dialPlanRow["lastupdate"] != null && dialPlanRow["lastupdate"] != DBNull.Value) ? Convert.ToDateTime(dialPlanRow["lastupdate"]) : DateTime.Now;
            }
            catch (Exception excp) {
                logger.Error("Exception DialPlan Load. " + excp);
                throw excp;
            }
        }

        public Dictionary<Guid, object> Load(XmlDocument dom) {
            return SIPAssetXMLPersistor<SIPDialPlan>.LoadAssetsFromXMLRecordSet(dom);
        }

#endif

        public string ToXML()
        {
            string dialPlanXML =
                "  <" + XML_ELEMENT_NAME + ">" + m_newLine +
               ToXMLNoParent() +
                "  </" + XML_ELEMENT_NAME + ">" + m_newLine;

            return dialPlanXML;
        }

        public string ToXMLNoParent()
        {
            string dialPlanXML =
                "    <id>" + m_id + "</id>" + m_newLine +
                "    <owner>" + m_owner + "</owner>" + m_newLine +
                "    <adminmemberid>" + AdminMemberId + "</adminmemberid>" + m_newLine +
                "    <dialplanname>" + m_dialPlanName + "</dialplanname>" + m_newLine +
                "    <traceemailaddress>" + m_traceEmailAddress + "</traceemailaddress>" + m_newLine +
                "    <dialplanscript><![CDATA[" + m_dialPlanScript + "]]></dialplanscript>" + m_newLine +
                "    <scripttype>" + m_scriptTypeDescription + "</scripttype>" + m_newLine +
                "    <maxexecutioncount>" + m_maxExecutionCount + "</maxexecutioncount>" + m_newLine +
                "    <executioncount>" + m_executionCount + "</executioncount>" + m_newLine +
                "    <authorisedapps>" + m_authorisedApps + "</authorisedapps>" + m_newLine +
                "    <inserted>" + m_insertedUTC.ToString("o") + "</inserted>" + m_newLine +
                "    <lastupdate>" + m_lastUpdateUTC.ToString("o") + "</lastupdate>" + m_newLine;

            return dialPlanXML;
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
