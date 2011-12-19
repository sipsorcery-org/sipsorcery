using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.ServiceModel;
using System.ServiceModel.DomainServices.Client;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SIPSorcery.Entities;
using SIPSorcery.Entities.Services;
using SIPSorcery.Persistence;
using SIPSorcery.Sys;

namespace SIPSorcery
{
    public partial class CustomerSettingsControl : UserControl
    {
        private ActivityMessageDelegate LogActivityMessage_External;
        private LogoutDelegate Logout_External;

        private SIPEntitiesDomainContext m_riaContext;
        private string m_owner;
        private Customer m_customer;

        public CustomerSettingsControl()
        {
            InitializeComponent();
        }

        public CustomerSettingsControl(
            ActivityMessageDelegate logActivityMessage,
            LogoutDelegate logout,
            string owner,
            SIPEntitiesDomainContext riaContext)
        {
            InitializeComponent();

            LogActivityMessage_External = logActivityMessage;
            Logout_External = logout;
            m_owner = owner;
            m_riaContext = riaContext;
            UIHelper.SetText(m_accountDetailsTextBlock, "Account Details: " + m_owner);
            //m_persistor.GetCustomerComplete += GetCustomerComplete;
            //m_persistor.UpdateCustomerPasswordComplete += UpdateCustomerPasswordComplete;
            //m_persistor.UpdateCustomerComplete += UpdateCustomerComplete;
        }

        public void Load()
        {
            SetUpdateButtonsEnabled(false);
            UIHelper.SetText(m_statusTextBlock, "Loading details, please wait...");

            m_riaContext.Customers.Clear();
            var query = m_riaContext.GetCustomerQuery();

            // Make sure the timezons are loaded first.
            if (Page.TimeZones == null)
            {
                m_riaContext.GetTimeZones(Page.GetTimeZonesCompleted, new Action(() => { 
                    m_timezoneListBox.ItemsSource = Page.TimeZones;
                    m_riaContext.Load(query, LoadBehavior.RefreshCurrent, GetCustomerCompleted, null);
                }));
            }
            else
            {
                m_timezoneListBox.ItemsSource = Page.TimeZones;
                m_riaContext.Load(query, LoadBehavior.RefreshCurrent, GetCustomerCompleted, null);
            }
        }

        private void GetCustomerCompleted(LoadOperation<Customer> lo)
        {
            if (lo.HasError)
            {
                UIHelper.SetText(m_statusTextBlock, lo.Error.Message);
                lo.MarkErrorAsHandled();
            }
            else
            {
                m_customer = m_riaContext.Customers.FirstOrDefault();

                if (m_customer == null)
                {
                    UIHelper.SetText(m_statusTextBlock, "There was an error retrieving your details. Please re-login and try again.");
                }
                else
                {
                    this.DataContext = m_customer;
                    UIHelper.SetText(m_statusTextBlock, "Details successfully loaded.");
                    SetUpdateButtonsEnabled(true);
                }
            }
        }

        private void SetUpdateButtonsEnabled(bool areEnabled)
        {
            UIHelper.SetIsEnabled(m_updateAccountButton, areEnabled);
            UIHelper.SetIsEnabled(m_updatePasswordButton, areEnabled);
        }

        private void ClearPasswordFields()
        {
            UIHelper.SetText(m_oldPasswordTextBox, String.Empty);
            UIHelper.SetText(m_newPasswordTextBox, String.Empty);
            UIHelper.SetText(m_retypeNewPasswordTextBox, String.Empty);
        }

        private void UpdateCustomerButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            SetUpdateButtonsEnabled(false);
            UIHelper.SetText(m_statusTextBlock, "Updating details, please wait...");
            m_riaContext.SubmitChanges(UpdateCustomerComplete, null);
        }

        private void UpdateCustomerComplete(SubmitOperation so)
        {
            if (so.HasError)
            {
                if (!m_customer.HasValidationErrors)
                {
                    // Only display the exception message if it's not already being set in the validation summary.

                    // Remove the error information the RIA domain services framework adds in and that will just confuse things.
                    string errorMessage = Regex.Replace(so.Error.Message, @"Submit operation failed.", "");
                    UIHelper.SetText(m_statusTextBlock, errorMessage);
                }
                else
                {
                    UIHelper.SetText(m_statusTextBlock, "There was a validation error updating your details.");
                }

                so.MarkErrorAsHandled();
            }
            else
            {
                UIHelper.SetText(m_statusTextBlock, "Details successfully updated.");
            }

            SetUpdateButtonsEnabled(true);
        }

        private void UpdatePasswordButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (m_oldPasswordTextBox.Password.IsNullOrBlank())
            {
                m_statusTextBlock.Text = "The old password must be specified.";
            }
            else if (m_newPasswordTextBox.Password.IsNullOrBlank())
            {
                m_statusTextBlock.Text = "The new password must be specified.";
            }
            else if (m_retypeNewPasswordTextBox.Password.IsNullOrBlank())
            {
                m_statusTextBlock.Text = "The retyped new password must be specified.";
            }
            else if (m_newPasswordTextBox.Password.Trim() != m_retypeNewPasswordTextBox.Password.Trim())
            {
                m_statusTextBlock.Text = "The new password did not match the retyped password.";
            }
            else
            {
                SetUpdateButtonsEnabled(false);
                UIHelper.SetText(m_statusTextBlock, "Updating password, please wait...");
                m_riaContext.ChangePassword(m_oldPasswordTextBox.Password.Trim(), m_newPasswordTextBox.Password.Trim(), UpdateCustomerPasswordComplete, null);
                ClearPasswordFields();
            }
        }

        private void UpdateCustomerPasswordComplete(InvokeOperation io)
        {
            if (io.HasError)
            {
                UIHelper.SetText(m_statusTextBlock, io.Error.Message);
                io.MarkErrorAsHandled();
            }
            else
            {
                UIHelper.SetText(m_statusTextBlock, "Password successfully updated.");
            }

            SetUpdateButtonsEnabled(true);
        }

        private void DeleteAccountButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            MessageBoxResult confirmDelete = MessageBox.Show("Important! If you have active SIP Provider bindings you need to deactivate them BEFORE deleting your account.\nPress Ok to delete all your account details.", "Confirm Delete", MessageBoxButton.OKCancel);
            if (confirmDelete == MessageBoxResult.OK)
            {
                m_riaContext.Customers.Remove(m_customer);
                m_riaContext.SubmitChanges(DeleteComplete, null);
            }
        }

        private void DeleteComplete(SubmitOperation so)
        {
            if (so.HasError)
            {
                UIHelper.SetText(m_statusTextBlock, "Error deleting account. " + so.Error.Message);
                so.MarkErrorAsHandled();
            }
            else
            {
                Logout_External(false);
            }
        }
    }
}