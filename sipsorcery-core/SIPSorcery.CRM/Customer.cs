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
    [Table(Name = "customers")]
    public class Customer : ISIPAsset
    {
        public const string XML_DOCUMENT_ELEMENT_NAME = "customers";
        public const string XML_ELEMENT_NAME = "customer";
        public const string TOPLEVEL_ADMIN_ID = "*";    // If a customer record has their AdminId set to this value they are in charge!

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

        [Column(Storage = "_inserted", Name = "inserted", DbType = "timestamp", CanBeNull = false)]
        public DateTime Inserted { get; set; }

        public Customer() { }

        public Customer(DataRow customerRow) {
            Load(customerRow);
        }

        public void Load(DataRow customerRow) {
            try {
                Id = customerRow["id"] as string;
                CustomerUsername = customerRow["customerusername"] as string;
                CustomerPassword = customerRow["customerpassword"] as string;
                EmailAddress = customerRow["emailaddress"] as string;
                AdminId = (customerRow.Table.Columns.Contains("adminid") && customerRow["adminid"] != null) ? customerRow["adminid"] as string : CustomerUsername;
                AdminMemberId = (customerRow.Table.Columns.Contains("adminmemberid") && customerRow["adminmemberid"] != null) ? customerRow["adminmemberid"] as string : CustomerUsername;
            }
            catch (Exception excp) {
                logger.Error("Exception Customer Load. " + excp.Message);
                throw;
            }
        }

        public Dictionary<Guid, object> Load(XmlDocument dom) {
            return SIPAssetXMLPersistor<Customer>.LoadAssetsFromXMLRecordSet(dom);
        }

         public string ToXML()
        {
            string customerXML =
                "  <" + XML_ELEMENT_NAME + ">" + m_newLine +
               ToXMLNoParent() + m_newLine +
                "  </" + XML_ELEMENT_NAME + ">" + m_newLine;

            return customerXML;
        }

         public string ToXMLNoParent() {
             string customerXML =
                 "  <id>" + Id + "</id>" + m_newLine +
                 "  <customerusername>" + CustomerUsername + "</customerusername>" + m_newLine +
                 "  <customerpassword>" + CustomerPassword + "</customerpassword>" + m_newLine +
                 "  <emailaddress>" + EmailAddress + "</emailaddress>" + m_newLine +
                 "  <adminid>" + AdminId + "</adminid>" + m_newLine +
                 "  <adminmemberid>" + AdminMemberId + "</adminmemberid>" + m_newLine;

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
