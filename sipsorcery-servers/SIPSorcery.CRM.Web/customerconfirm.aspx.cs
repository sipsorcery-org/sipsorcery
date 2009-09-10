using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using SIPSorcery.CRM;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.CRM.Web {
    
    public partial class CustomerEmailConfirmation : System.Web.UI.Page {

        private ILog logger = WebState.logger;

        private StorageTypes m_crmStorageType = WebState.CRMStorageType;
        private string m_crmStorageConnStr = WebState.CRMStorageConnStr;

        private SIPAssetPersistor<Customer> m_crmPersistor;

        protected bool m_confirmed = false;

        protected void Page_Load(object sender, EventArgs e) {

            try {
                string id = this.Request.QueryString["id"];
                if (!id.IsNullOrBlank()) {
                    Guid customerId = new Guid(id);
                    m_crmPersistor = CustomerPersistorFactory.CreateCustomerPersistor(m_crmStorageType, m_crmStorageConnStr);
                    Customer customer = m_crmPersistor.Get(customerId);

                    if (customer != null) {
                        if (!customer.EmailAddressConfirmed) {
                            customer.EmailAddressConfirmed = true;
                            m_crmPersistor.Update(customer);
                            m_confirmed = true;
                            m_confirmMessage.Text = "Thank you, your account has now been activated.";
                        }
                        else {
                             m_confirmed = true;
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
    }
}
