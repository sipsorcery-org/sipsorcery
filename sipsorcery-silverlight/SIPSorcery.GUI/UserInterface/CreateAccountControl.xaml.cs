using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ServiceModel.DomainServices;
using System.ServiceModel.DomainServices.Client;
using System.ServiceModel.DomainServices.Client.ApplicationServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SIPSorcery.Entities;
using SIPSorcery.Sys;
using SIPSorcery.Entities.Services;

namespace SIPSorcery
{
    public partial class CreateAccountControl : UserControl
    {
        public event Action CloseClicked;
        public event Action<string> CustomerCreated;

        private SIPEntitiesDomainContext m_riaContext;
        public string InviteCode
        {
            set
            {
                Customer customer = (Customer)m_newCustomerDataForm.CurrentItem;
                customer.InviteCode = value;
            }
        }

        public CreateAccountControl()
        {
            InitializeComponent();

            m_newCustomerDataForm.CurrentItem = CreateNewCustomer();
        }

        public void SetRIAContext(SIPEntitiesDomainContext riaContext)
        {
            m_riaContext = riaContext;
        }

        private void CreateCustomerComplete(SubmitOperation so)
        {
            Customer customer = (Customer)so.UserState;

            if (so.HasError)
            {
                m_riaContext.Customers.Remove(customer);

                // Remove the error information the RIA domain services framework adds in and that will just confuse things.
                string errorMessage = Regex.Replace(so.Error.Message, @"Submit operation failed.", "");
                UIHelper.SetText(m_statusTextBlock, errorMessage);
                
                so.MarkErrorAsHandled();
            }
            else
            {
                CustomerCreated("A comfirmation email has been sent to " + customer.EmailAddress + ". " +
                    "Please click on the link contained in the email to activate your account. Accounts not activated within 24 hours are removed.");

                Reset();
            }
        }

        public void Reset()
        {
            m_newCustomerDataForm.CurrentItem = CreateNewCustomer();
            UIHelper.SetText(m_statusTextBlock, String.Empty);
        }

        private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            UIHelper.SetText(m_statusTextBlock, String.Empty);
            CloseClicked();
        }

        private void EditEnded(object sender, System.Windows.Controls.DataFormEditEndedEventArgs e)
        {
            if (e.EditAction == DataFormEditAction.Commit)
            {
                UIHelper.SetText(m_statusTextBlock, "Attempting to create your account, please wait...");

                Customer customer = (sender as DataForm).CurrentItem as Customer;
                m_riaContext.Customers.Add(customer);
                m_riaContext.SubmitChanges(CreateCustomerComplete, customer);
            }
        }

        private Customer CreateNewCustomer()
        {
            return new Customer()
            {
                ID = Guid.Empty.ToString(),
                Inserted = DateTime.Now.ToUniversalTime().ToString("o"),
                ServiceLevel = CustomerServiceLevels.Free.ToString()
            };
        }
    }
}