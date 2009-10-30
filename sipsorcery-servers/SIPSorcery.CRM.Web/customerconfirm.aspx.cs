using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using SIPSorcery.CRM;
using SIPSorcery.Persistence;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.CRM.Web {
    
    public partial class CustomerEmailConfirmation : System.Web.UI.Page {

        private ILog logger = WebState.logger;

        private StorageTypes m_crmStorageType = WebState.CRMStorageType;
        private string m_crmStorageConnStr = WebState.CRMStorageConnStr;
        private Dictionary<string, string> m_validationRules = WebState.ValidationRules;

        private SIPAssetPersistor<Customer> m_crmPersistor;

        protected bool m_confirmed = false;

        protected void Page_Load(object sender, EventArgs e) {

            try {
                logger.Debug("CustomerEmailConfirmation request from " + this.Context.Request.UserHostAddress + " for " + this.Request.QueryString["id"] + ".");

                string id = this.Request.QueryString["id"];
                if (!id.IsNullOrBlank()) {
                    Guid customerId = new Guid(id);
                    m_crmPersistor = SIPAssetPersistorFactory<Customer>.CreateSIPAssetPersistor(m_crmStorageType, m_crmStorageConnStr, null);
                    Customer customer = m_crmPersistor.Get(customerId);

                    if (customer != null) {
                        if (!customer.EmailAddressConfirmed) {
                            customer.CreatedFromIPAddress = this.Context.Request.UserHostAddress;
                            customer.EmailAddressConfirmed = true;

                            if (IsValidCustomer(m_validationRules, customer)) {
                                m_crmPersistor.Update(customer);
                                m_confirmed = true;
                                m_confirmMessage.Text = "Thank you, your account has now been activated.";
                            }
                            else {
                                customer.Suspended = true;
                                m_crmPersistor.Update(customer);
                                m_confirmed = false;
                                m_confirmMessage.Text = "Your account has been confirmed but not approved. You will receive an email within 48 hours if it is approved.";
                            }
                        }
                        else {
                            m_confirmed = false;
                             m_confirmMessage.Text = "Your account has already been confirmed.";
                        }
                    }
                    else {
                        m_confirmMessage.Text = "No matching customer record could be found. Please check that you entered the confirmation URL correctly.";
                    }
                }
                else {
                    m_confirmMessage.Text = "Your account could not be confirmed. Please check that you entered the confirmation URL correctly.";
                }
            }
            catch (Exception excp) {
                logger.Error("Exception CustomerEmailConfirmation. " + excp.Message);
                m_confirmMessage.Text = "There was an error confirming your account. Please check that you entered the confirmation URL correctly.";
            }
        }

        private bool IsValidCustomer(Dictionary<string, string> rules, Customer customer) {
            try {
                if (rules != null) {
                    foreach (KeyValuePair<string, string> rule in rules) {

                        if (rule.Key == "CustomerUsername") {
                            if (Regex.Match(customer.CustomerUsername, rule.Value).Success) {
                                logger.Debug("CustomerUsername rule invalidated account " + customer.CustomerUsername + ".");
                                return false;
                            }
                        }
                        else if (rule.Key == "EmailAddress") {
                            if (Regex.Match(customer.EmailAddress, rule.Value).Success) {
                                logger.Debug("EmailAddress rule invalidated account " + customer.CustomerUsername + ".");
                                return false;
                            }
                        }
                        else if (rule.Key == "CreatedFromIPAddress") {
                            if (Regex.Match(customer.CreatedFromIPAddress, rule.Value).Success) {
                                logger.Debug("CreatedFromIPAddress rule invalidated account " + customer.CustomerUsername + ".");
                                return false;
                            }
                        }
                    }
                }

                return true;
            }
            catch (Exception excp) {
                logger.Error("Exception CheckValidationRules. " + excp.Message);
                return true;
            }
        }
    }
}
