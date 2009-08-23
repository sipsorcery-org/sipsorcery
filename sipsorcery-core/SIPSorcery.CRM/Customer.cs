// ============================================================================
// FileName: Customer.cs
//
// Description:
// Represents a customer record.
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
using System.Text.RegularExpressions;
using System.Xml;
using SIPSorcery.Sys;
using log4net;

#if !SILVERLIGHT
using System.Data;
using System.Data.Linq;
using System.Data.Linq.Mapping;
#endif

namespace SIPSorcery.CRM
{
    [Table(Name = "customers")]
    public class Customer : ISIPAsset
    {
        public const string XML_DOCUMENT_ELEMENT_NAME = "customers";
        public const string XML_ELEMENT_NAME = "customer";
        public const string TOPLEVEL_ADMIN_ID = "*";                    // If a customer record has their AdminId set to this value they are in charge!
        public const int DEFAULT_MAXIMUM_EXECUTION_COUNT = 5;           // The default value for the maximum allowed simultaneous executions of all the customer's dial plans.
        
        private const int MAX_FIELD_LENGTH = 64;
        private const int MIN_USERNAME_LENGTH = 5;
        private const int MAX_USERNAME_LENGTH = 20;
        private const int MIN_PASSWORD_LENGTH = 6;
        private const int MAX_PASSWORD_LENGTH = 20;
        private const int MAX_WEBSITE_FIELD_LENGTH = 256;
        
        public static readonly string USERNAME_ALLOWED_CHARS = @"a-zA-Z0-9_\-";     // The '.' character is not allowed in customer usernames in order to support a domain like structure for SIP account usernames.

        private static ILog logger = AppState.logger;
        private static string m_newLine = AppState.NewLine;

        [Column(Storage = "_id", Name = "id", DbType = "character varying(36)", IsPrimaryKey = true, CanBeNull = false)]
        public string Id { get; set; }

        [Column(Storage = "_customerusername", Name = "customerusername", DbType = "character varying(32)", CanBeNull = false)]
        public string CustomerUsername { get; set; }

        [Column(Storage = "_customerpassword", Name = "customerpassword", DbType = "character varying(32)", CanBeNull = false)]
        public string CustomerPassword { get; set; }

        [Column(Storage = "_emailaddress", Name = "emailaddress", DbType = "character varying(255)", CanBeNull = false)]
        public string EmailAddress { get; set; }

        [Column(Storage = "_lastname", Name = "lastname", DbType = "character varying(64)")]
        public string LastName { get; set; }

        [Column(Storage = "_firstname", Name = "firstname", DbType = "character varying(64)")]
        public string FirstName { get; set; }

        [Column(Storage = "_city", Name = "city", DbType = "character varying(64)")]
        public string City { get; set; }

        [Column(Storage = "_country", Name = "country", DbType = "character varying(64)")]
        public string Country { get; set; }

        [Column(Storage = "_website", Name = "website", DbType = "character varying(256)")]
        public string WebSite { get; set; }

        [Column(Storage = "_active", Name = "active", DbType = "boolean", CanBeNull = false)]
	    public bool Active{get; set;}

        [Column(Storage = "_suspended", Name = "suspended", DbType = "boolean", CanBeNull = false)]
	    public bool Suspended{get; set;}

        [Column(Storage = "_securityquestion", Name = "securityquestion", DbType = "character varying(256)")]
        public string SecurityQuestion { get; set; }

        [Column(Storage = "_securityanswer", Name = "securityanswer", DbType = "character varying(256)")]
        public string SecurityAnswer { get; set; }

        [Column(Storage = "_createdfromipaddress", Name = "createdfromipaddress", DbType = "character varying(15)")]
        public string CreatedFromIPAddress { get; set; }

        [Column(Storage = "_adminid", Name = "adminid", DbType = "character varying(32)", CanBeNull = true)]
        public string AdminId { get; set; }          // Like a whitelabelid. If set identifies this user as the administrative owner of all accounts that have the same value for their adminmemberid.

        [Column(Storage = "_adminmemberid", Name = "adminmemberid", DbType = "character varying(32)", CanBeNull = true)]
        public string AdminMemberId { get; set; }    // If set it designates this customer as a belonging to the administrative domain of the customer with the same adminid.

        [Column(Storage = "_timezone", Name = "timezone", DbType = "character varying(128)", CanBeNull = true)]
        public string TimeZone { get; set; }

        [Column(Storage = "_inserted", Name = "inserted", DbType = "timestamp", CanBeNull = false)]
        public DateTime InsertedUTC { get; set; }

        [Column(Storage = "_maxexecutioncount", Name = "maxexecutioncount", DbType = "integer", CanBeNull = false)]
        public int MaxExecutionCount { get; set; }

        [Column(Storage = "_executioncount", Name = "executioncount", DbType = "integer", CanBeNull = false)]
        public int ExecutionCount { get; set; }

        [Column(Storage = "_authorisedapps", Name = "authorisedapps", DbType = "character varying(2048)", CanBeNull = true)]
        public string AuthorisedApps { get; set; }

        public Customer() { }

        public static string ValidateAndClean(Customer customer) {

            if (customer.FirstName.IsNullOrBlank()) {
                return "A first name must be specified.";
            }
            else if (customer.FirstName.Trim().Length > MAX_FIELD_LENGTH) {
                return "The first name length must be less than " + MAX_FIELD_LENGTH + ".";
            }
            else if (customer.LastName.IsNullOrBlank()) {
                return "A last name must be specified.";
            }
            else if (customer.LastName.Trim().Length > MAX_FIELD_LENGTH) {
                return "The last name length must be less than " + MAX_FIELD_LENGTH + ".";
            }
            else if (customer.EmailAddress.IsNullOrBlank()) {
                return "An email address must be specified.";
            }
            else if (customer.EmailAddress.Trim().Length > MAX_FIELD_LENGTH) {
                return "The email address length must be less than " + MAX_FIELD_LENGTH + ".";
            }
            else if (customer.CustomerUsername.IsNullOrBlank()) {
                return "A username must be specified.";
            }
            else if (customer.CustomerUsername.Trim().Length > MAX_USERNAME_LENGTH || customer.CustomerUsername.Trim().Length < MIN_USERNAME_LENGTH) {
                return "The username length must be between " + MIN_USERNAME_LENGTH + " and " + MAX_USERNAME_LENGTH + ".";
            }
            else if (Regex.Match(customer.CustomerUsername.Trim(), "[^" + USERNAME_ALLOWED_CHARS + "]").Success) {
                return "The username had an invalid character, characters permitted are alpha-numeric and -_ (no full stop characters '.' are allowed).";
            }
            else if (customer.CustomerPassword.IsNullOrBlank()) {
                return "A password must be specified.";
            }
            else if (customer.CustomerPassword.Trim().Length > MAX_PASSWORD_LENGTH || customer.CustomerPassword.Trim().Length < MIN_PASSWORD_LENGTH) {
                return "The password length must be between " + MIN_PASSWORD_LENGTH + " and " + MAX_PASSWORD_LENGTH + ".";
            }
            else if (customer.SecurityAnswer.IsNullOrBlank()) {
                return "The answer to the security question must be specified.";
            }
            else if (customer.SecurityAnswer.Trim().Length > MAX_FIELD_LENGTH) {
                return "The security question answer length must be less than " + MAX_FIELD_LENGTH + ".";
            }
            else if (customer.City.IsNullOrBlank()) {
                return "Your city must be specified. If you don't live in a city please enter the one closest to you.";
            }
            else if (customer.City.Trim().Length > MAX_FIELD_LENGTH) {
                return "The city length must be less than " + MAX_FIELD_LENGTH + ".";
            }
            else if (!customer.WebSite.IsNullOrBlank() && customer.WebSite.Trim().Length > MAX_WEBSITE_FIELD_LENGTH) {
                return "The web site length must be less than " + MAX_WEBSITE_FIELD_LENGTH + ".";
            }

            return null;
        }

#if !SILVERLIGHT

        public Customer(DataRow customerRow) {
            Load(customerRow);
        }

        public void Load(DataRow customerRow) {
            try {
                Id = customerRow["id"] as string;
                CustomerUsername = customerRow["customerusername"] as string;
                CustomerPassword = customerRow["customerpassword"] as string;
                EmailAddress = (customerRow.Table.Columns.Contains("emailaddress") && customerRow["emailaddress"] != null) ?  customerRow["emailaddress"] as string : null;
                AdminId = (customerRow.Table.Columns.Contains("adminid") && customerRow["adminid"] != null) ? customerRow["adminid"] as string : null;
                AdminMemberId = (customerRow.Table.Columns.Contains("adminmemberid") && customerRow["adminmemberid"] != null) ? customerRow["adminmemberid"] as string : null;
                FirstName = (customerRow.Table.Columns.Contains("firstname") && customerRow["firstname"] != null) ? customerRow["firstname"] as string : null;
                LastName = (customerRow.Table.Columns.Contains("lastname") && customerRow["lastname"] != null) ? customerRow["lastname"] as string : null;
                City = (customerRow.Table.Columns.Contains("city") && customerRow["city"] != null) ? customerRow["city"] as string : null;
                Country = (customerRow.Table.Columns.Contains("country") && customerRow["country"] != null) ? customerRow["country"] as string : null;
                SecurityQuestion = (customerRow.Table.Columns.Contains("securityquestion") && customerRow["securityquestion"] != null) ? customerRow["securityquestion"] as string : null;
                SecurityAnswer = (customerRow.Table.Columns.Contains("securityanswer") && customerRow["securityanswer"] != null) ? customerRow["securityanswer"] as string : null;
                WebSite = (customerRow.Table.Columns.Contains("website") && customerRow["website"] != null) ? customerRow["website"] as string : null;
                CreatedFromIPAddress = (customerRow.Table.Columns.Contains("createdfromipaddress") && customerRow["createdfromipaddress"] != null) ? customerRow["createdfromipaddress"] as string : null;
                InsertedUTC = (customerRow.Table.Columns.Contains("inserted") && customerRow["inserted"] != null) ? Convert.ToDateTime(customerRow["inserted"]) : DateTime.MinValue;
                Suspended = (customerRow.Table.Columns.Contains("suspended") && customerRow["suspended"] != null) ? Convert.ToBoolean(customerRow["suspended"]) : false;
                ExecutionCount = (customerRow.Table.Columns.Contains("executioncount") && customerRow["executioncount"] != null) ? Convert.ToInt32(customerRow["executioncount"]) : 0;
                MaxExecutionCount = (customerRow.Table.Columns.Contains("maxexecutioncount") && customerRow["maxexecutioncount"] != null) ? Convert.ToInt32(customerRow["maxexecutioncount"]) : DEFAULT_MAXIMUM_EXECUTION_COUNT;
                AuthorisedApps = (customerRow.Table.Columns.Contains("authorisedapps") && customerRow["authorisedapps"] != null) ? customerRow["authorisedapps"] as string : null;
            }
            catch (Exception excp) {
                logger.Error("Exception Customer Load. " + excp.Message);
                throw;
            }
        }

        public Dictionary<Guid, object> Load(XmlDocument dom) {
            return SIPAssetXMLPersistor<Customer>.LoadAssetsFromXMLRecordSet(dom);
        }

#endif

        public string ToXML()
        {
            string customerXML =
                " <" + XML_ELEMENT_NAME + ">" + m_newLine +
               ToXMLNoParent() + m_newLine +
                " </" + XML_ELEMENT_NAME + ">" + m_newLine;

            return customerXML;
        }

         public string ToXMLNoParent() {
            string customerXML =
                "  <id>" + Id + "</id>" + m_newLine +
                "  <customerusername>" + CustomerUsername + "</customerusername>" + m_newLine +
                "  <customerpassword>" + CustomerPassword + "</customerpassword>" + m_newLine +
                "  <emailaddress>" + EmailAddress + "</emailaddress>" + m_newLine +
                "  <firstname>" + SafeXML.MakeSafeXML(FirstName) + "</firstname>" + m_newLine +
                "  <lastname>" + SafeXML.MakeSafeXML(LastName) + "</lastname>" + m_newLine +
                "  <city>" + SafeXML.MakeSafeXML(City) + "</city>" + m_newLine +
                "  <country>" + Country + "</country>" + m_newLine +
                "  <adminid>" + AdminId + "</adminid>" + m_newLine +
                "  <adminmemberid>" + AdminMemberId + "</adminmemberid>" + m_newLine +
                "  <website>" + SafeXML.MakeSafeXML(WebSite) + "</website>" + m_newLine +
                "  <securityquestion>" + SecurityQuestion + "</securityquestion>" + m_newLine +
                "  <securityanswer>" + SafeXML.MakeSafeXML(SecurityAnswer) + "</securityanswer>" + m_newLine +
                "  <createdfromipaddress>" + CreatedFromIPAddress + "</createdfromipaddress>" + m_newLine +
                "  <inserted>" + InsertedUTC.ToString("dd MMM yyyy HH:mm:ss") + "</inserted>" + m_newLine +
                "  <active>" + Active + "</active>" + m_newLine +
                "  <suspended>" + Suspended + "</suspended>" + m_newLine +
                "  <executioncount>" + ExecutionCount + "</executioncount>" + m_newLine +
                "  <maxexecutioncount>" + MaxExecutionCount + "</maxexecutioncount>" + m_newLine +
                "  <authorisedapps>" + AuthorisedApps + "</authorisedapps>" + m_newLine;

             return customerXML;
         }

        public string GetXMLElementName() {
            return XML_ELEMENT_NAME;
        }

        public string GetXMLDocumentElementName() {
            return XML_DOCUMENT_ELEMENT_NAME;
        }
    }
}
