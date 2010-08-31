using System;
using System.Windows;
using System.Windows.Browser;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SIPSorcery.CRM;
using SIPSorcery.Sys;

namespace SIPSorcery
{
	public partial class CreateAccountControl : UserControl
	{
        public CreateCustomerDelegate CreateCustomer_External;
        //public LoginDelegate Login_External;

        private string m_emailAddress;
        public string InviteCode;

        public CreateAccountControl() {
            InitializeComponent();
        }

        public void SetDataEntryEnabled(bool enabled) {
            Visibility dataEntryVisibility = (enabled) ? Visibility.Visible : Visibility.Collapsed;
            UIHelper.SetVisibility(m_firstNameTextBox, dataEntryVisibility);
            UIHelper.SetVisibility(m_lastNameTextBox, dataEntryVisibility);
            UIHelper.SetVisibility(m_emailAddressTextBox, dataEntryVisibility);
            UIHelper.SetVisibility(m_usernameTextBox, dataEntryVisibility);
            UIHelper.SetVisibility(m_passwordTextBox, dataEntryVisibility);
            UIHelper.SetVisibility(m_retypePasswordTextBox, dataEntryVisibility);
            UIHelper.SetVisibility(m_securityAnswerTextBox, dataEntryVisibility);
            UIHelper.SetVisibility(m_cityTextBox, dataEntryVisibility);
            UIHelper.SetVisibility(m_securityQuestionListBox, dataEntryVisibility);
            UIHelper.SetVisibility(m_countryListBox, dataEntryVisibility);
            UIHelper.SetVisibility(m_webSiteTextBox, dataEntryVisibility);
            UIHelper.SetVisibility(m_createAccountButton, dataEntryVisibility);
            UIHelper.SetVisibility(m_firstNameLabel, dataEntryVisibility);
            UIHelper.SetVisibility(m_lastNameLabel, dataEntryVisibility);
            UIHelper.SetVisibility(m_emailAddressLabel, dataEntryVisibility);
            UIHelper.SetVisibility(m_usernameLabel, dataEntryVisibility);
            UIHelper.SetVisibility(m_passwordLabel, dataEntryVisibility);
            UIHelper.SetVisibility(m_retypedPasswordLabel, dataEntryVisibility);
            UIHelper.SetVisibility(m_securityQuestionLabel, dataEntryVisibility);
            UIHelper.SetVisibility(m_securityAnswerLabel, dataEntryVisibility);
            UIHelper.SetVisibility(m_cityLabel, dataEntryVisibility);
            UIHelper.SetVisibility(m_countryLabel, dataEntryVisibility);
            UIHelper.SetVisibility(m_webSiteLabel, dataEntryVisibility);
            UIHelper.SetVisibility(m_timezoneLabel, dataEntryVisibility);
            UIHelper.SetVisibility(m_timezoneListBox, dataEntryVisibility);
        }

        private void CreateCustomerButton_Click(object sender, System.Windows.RoutedEventArgs e) {
            try {
                m_statusTextBlock.Text = String.Empty;

                Customer customer = new Customer() {
                    Id = Guid.NewGuid(),
                    FirstName = m_firstNameTextBox.Text.Trim(),
                    LastName = m_lastNameTextBox.Text.Trim(),
                    EmailAddress = m_emailAddressTextBox.Text.Trim(),
                    CustomerUsername = m_usernameTextBox.Text.Trim(),
                    CustomerPassword = m_passwordTextBox.Password.Trim(),
                    SecurityQuestion = ((TextBlock)m_securityQuestionListBox.SelectedItem).Text,
                    SecurityAnswer = m_securityAnswerTextBox.Text.Trim(),
                    City = m_cityTextBox.Text.Trim(),
                    Country = ((TextBlock)m_countryListBox.SelectedItem).Text,
                    WebSite = m_webSiteTextBox.Text.Trim(),
                    TimeZone = ((TextBlock)m_timezoneListBox.SelectedItem).Text,
                    InviteCode = InviteCode,
                    Inserted = DateTime.Now.ToUniversalTime()
                };

                string validationError = Customer.ValidateAndClean(customer);
                if (validationError != null) {
                    m_statusTextBlock.Text = validationError;
                }
                else {

                    if (m_retypePasswordTextBox.Password.IsNullOrBlank()) {
                        m_statusTextBlock.Text = "The retyped password must be specified.";
                    }
                    else if (m_retypePasswordTextBox.Password.Trim() != customer.CustomerPassword.Trim()) {
                        m_statusTextBlock.Text = "The password and retyped password did not match.";
                    }
                    else {
                        SetDataEntryEnabled(false);
                        UIHelper.SetText(m_statusTextBlock, "Attempting to create new customer, please wait...");

                        CreateCustomer_External(customer);

                        m_emailAddress = customer.EmailAddress;
                    }
                }
            }
            catch (Exception excp) {
                SetDataEntryEnabled(true);
                m_statusTextBlock.Text = "Exception creating customer. " + excp.Message;
            }
        }

        public void CustomerCreated(System.ComponentModel.AsyncCompletedEventArgs e) {
            if (e.Error == null) {
                Clear();
                UIHelper.SetText(m_statusTextBlock, "A comfirmation email has been sent to " + m_emailAddress + ". " +
                    "Please click on the link contained in the email to activate your account. Accounts not activated within 24 hours are removed.");
            }
            else {
                SetDataEntryEnabled(true);
                m_statusTextBlock.Text = e.Error.Message;
            }
        }

        public void Clear() {
            UIHelper.SetText(m_statusTextBlock, String.Empty);
            UIHelper.SetText(m_firstNameTextBox, String.Empty);
            UIHelper.SetText(m_lastNameTextBox, String.Empty);
            UIHelper.SetText(m_emailAddressTextBox, String.Empty);
            UIHelper.SetText(m_usernameTextBox, String.Empty);
            UIHelper.SetText(m_passwordTextBox, String.Empty);
            UIHelper.SetText(m_retypePasswordTextBox, String.Empty);
            UIHelper.SetText(m_securityAnswerTextBox, String.Empty);
            UIHelper.SetText(m_cityTextBox, String.Empty);
            UIHelper.SetText(m_webSiteTextBox, String.Empty);
            UIHelper.SetComboBoxSelectedId(m_securityQuestionListBox, 0);
            UIHelper.SetComboBoxSelectedId(m_countryListBox, 14);
            UIHelper.SetComboBoxSelectedId(m_timezoneListBox, 31);
        }
	}
}