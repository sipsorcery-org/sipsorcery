using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.ServiceModel;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SIPSorcery.CRM;
using SIPSorcery.Persistence;
using SIPSorcery.Sys;

namespace SIPSorcery
{
    public partial class CustomerSettingsControl : UserControl
	{
        private ActivityMessageDelegate LogActivityMessage_External;
        private LogoutDelegate Logout_External;

        private SIPSorceryPersistor m_persistor;
        private string m_owner;
        private Customer m_customer;
        private bool m_timezoneUpdateRequired;

        public CustomerSettingsControl()
        {
            InitializeComponent();
        }

        public CustomerSettingsControl(
            ActivityMessageDelegate logActivityMessage,
            LogoutDelegate logout,
            SIPSorceryPersistor persistor,
            string owner)
		{
			InitializeComponent();

            LogActivityMessage_External = logActivityMessage;
            Logout_External = logout;
            m_persistor = persistor;
            m_owner = owner;
            UIHelper.SetText(m_accountDetailsTextBlock, "Account Details: " + m_owner);

            m_persistor.GetCustomerComplete += GetCustomerComplete;
            m_persistor.UpdateCustomerPasswordComplete += UpdateCustomerPasswordComplete;
            m_persistor.UpdateCustomerComplete += UpdateCustomerComplete;
		}

        public void Load() {
            SetUpdateButtonsEnabled(false);
            UIHelper.SetText(m_statusTextBlock, "Loading details, please wait...");
            m_persistor.GetCustomerAsync(m_owner);
        }

        private void GetCustomerComplete(SIPSorcery.SIPSorceryProvisioningClient.GetCustomerCompletedEventArgs e) {
            if (e.Error != null) {
                UIHelper.SetText(m_statusTextBlock, e.Error.Message);
            }
            else {
                m_customer = e.Result;
                UIHelper.SetText(m_statusTextBlock, String.Empty);
                UIHelper.SetText(m_firstNameTextBox, m_customer.FirstName);
                UIHelper.SetText(m_lastNameTextBox, m_customer.LastName);
                UIHelper.SetText(m_emailAddressTextBox, m_customer.EmailAddress);
                UIHelper.SetText(m_securityAnswerTextBox, m_customer.SecurityAnswer);
                UIHelper.SetText(m_cityTextBox, m_customer.City);

                if (m_customer.WebSite != null)
                {
                    UIHelper.SetText(m_webSiteTextBox, m_customer.WebSite);
                }

                for (int questionIndex = 0; questionIndex < m_securityQuestionListBox.Items.Count; questionIndex++) {
                    if (((TextBlock)m_securityQuestionListBox.Items[questionIndex]).Text == m_customer.SecurityQuestion) {
                        m_securityQuestionListBox.SelectedIndex = questionIndex;
                        break;
                    }
                }

                for (int countryIndex = 0; countryIndex < m_countryListBox.Items.Count; countryIndex++) {
                    if (((TextBlock)m_countryListBox.Items[countryIndex]).Text == m_customer.Country) {
                        m_countryListBox.SelectedIndex = countryIndex;
                        break;
                    }
                }

                if (!m_customer.TimeZone.IsNullOrBlank()) {
                    for (int timezoneIndex = 0; timezoneIndex < m_timezoneListBox.Items.Count; timezoneIndex++) {
                        if (((TextBlock)m_timezoneListBox.Items[timezoneIndex]).Text == m_customer.TimeZone) {
                            m_timezoneListBox.SelectedIndex = timezoneIndex;
                            break;
                        }
                    }
                }

                SetUpdateButtonsEnabled(true);
            }
        }

        private void UpdateCustomerComplete(AsyncCompletedEventArgs e) {
            if (e.Error != null) {
                UIHelper.SetText(m_statusTextBlock, e.Error.Message);
            }
            else {
                UIHelper.SetText(m_statusTextBlock, "Details successfully updated.");

                if (m_timezoneUpdateRequired) {
                    // The on competed event is handled in userpage.xaml.cs.
                    m_persistor.GetTimeZoneOffsetMinutesAsync();
                }
            }

            SetUpdateButtonsEnabled(true);
        }

        private void UpdateCustomerPasswordComplete(AsyncCompletedEventArgs e) {
            if (e.Error != null) {
                UIHelper.SetText(m_statusTextBlock, e.Error.Message);
            }
            else {
                UIHelper.SetText(m_statusTextBlock, "Password successfully updated.");
            }

            SetUpdateButtonsEnabled(true);
        }

        private void SetUpdateButtonsEnabled(bool areEnabled) {
            UIHelper.SetIsEnabled(m_updateAccountButton, areEnabled);
            UIHelper.SetIsEnabled(m_updatePasswordButton, areEnabled);
        }

        private void ClearPasswordFields() {
            UIHelper.SetText(m_oldPasswordTextBox, String.Empty);
            UIHelper.SetText(m_newPasswordTextBox, String.Empty);
            UIHelper.SetText(m_retypeNewPasswordTextBox, String.Empty);
        }

        private void UpdateCustomerButton_Click(object sender, System.Windows.RoutedEventArgs e) {
            m_timezoneUpdateRequired = false;
            string oldTimeZone = m_customer.TimeZone;

            m_customer.FirstName = m_firstNameTextBox.Text.Trim();
            m_customer.LastName = m_lastNameTextBox.Text.Trim();
            m_customer.EmailAddress = m_emailAddressTextBox.Text.Trim();
            m_customer.SecurityQuestion = ((TextBlock)m_securityQuestionListBox.SelectedItem).Text;
            m_customer.SecurityAnswer = m_securityAnswerTextBox.Text.Trim();
            m_customer.City = m_cityTextBox.Text.Trim();
            m_customer.Country = ((TextBlock)m_countryListBox.SelectedItem).Text;
            m_customer.WebSite = m_webSiteTextBox.Text.Trim();
            m_customer.TimeZone = ((TextBlock)m_timezoneListBox.SelectedItem).Text;

            string validationError = Customer.ValidateAndClean(m_customer);
            if (validationError != null) {
                m_statusTextBlock.Text = validationError;
            }
            else {
                SetUpdateButtonsEnabled(false);
                UIHelper.SetText(m_statusTextBlock, "Updating details, please wait...");
                m_persistor.UpdateCustomerAsync(m_customer);

                if (oldTimeZone != m_customer.TimeZone) {
                    m_timezoneUpdateRequired = true;
                }
            }
        }

        private void UpdatePasswordButton_Click(object sender, System.Windows.RoutedEventArgs e) {

            if (m_oldPasswordTextBox.Password.IsNullOrBlank()) {
                m_statusTextBlock.Text = "The old password must be specified.";
            }
            else if (m_newPasswordTextBox.Password.IsNullOrBlank()) {
                m_statusTextBlock.Text = "The new password must be specified.";
            }
            else if (m_retypeNewPasswordTextBox.Password.IsNullOrBlank()) {
                m_statusTextBlock.Text = "The retyped new password must be specified.";
            }
            else if (m_newPasswordTextBox.Password.Trim() != m_retypeNewPasswordTextBox.Password.Trim()) {
                m_statusTextBlock.Text = "The new password did not match the retyped password.";
            }
            else {
                SetUpdateButtonsEnabled(false);
                UIHelper.SetText(m_statusTextBlock, "Updating password, please wait...");
                m_persistor.UpdateCustomerPassword(m_owner, m_oldPasswordTextBox.Password.Trim(), m_newPasswordTextBox.Password.Trim()) ;
                ClearPasswordFields();
            }
        }

        private void DeleteAccountButton_Click(object sender, System.Windows.RoutedEventArgs e) {
            try {
                MessageBoxResult confirmDelete = MessageBox.Show("Important! If you have active SIP Provider bindings you need to deactivate them BEFORE deleting your account.\nPress Ok to delete all your account details.", "Confirm Delete", MessageBoxButton.OKCancel);
                if (confirmDelete == MessageBoxResult.OK) {
                    m_persistor.DeleteCustomerComplete += (eargs) => { Logout_External(false); };
                    m_persistor.DeleteCustomerAsync(m_owner);
                }
            }
            catch (Exception excp) {
                LogActivityMessage_External(MessageLevelsEnum.Error, "Exception deleting account. " + excp.Message);
            }
        }
	}
}