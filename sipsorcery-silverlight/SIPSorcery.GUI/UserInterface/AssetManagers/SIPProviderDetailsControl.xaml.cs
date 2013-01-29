using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SIPSorcery.Entities;
using SIPSorcery.SIP;
using SIPSorcery.Sys;

namespace SIPSorcery
{
    public partial class SIPProviderDetailsControl : UserControl
    {
        private static readonly string m_defaultContactHost = App.DefaultSIPDomain;
        //private static bool m_disableProviderRegistrations = App.DisableProviderRegistrations;

        private static char m_customHeadersSeparator = SIPProvider.CUSTOM_HEADERS_SEPARATOR;
        private static int m_defaultRegisterExpiry = SIPProvider.REGISTER_DEFAULT_EXPIRY;

        private SIPProviderUpdateDelegate SIPProviderAdd_External;
        private SIPProviderUpdateDelegate SIPProviderUpdate_External;
        private ControlClosedDelegate ControlClosed_External;

        private DetailsControlModesEnum m_detailsMode;
        private SIPProvider m_sipProvider;
        private string m_owner;

        public SIPProviderDetailsControl()
        {
            InitializeComponent();
        }

        public SIPProviderDetailsControl(
            DetailsControlModesEnum mode,
            SIPProvider sipProvider,
            string owner,
            SIPProviderUpdateDelegate sipProviderAdd,
            SIPProviderUpdateDelegate sipProviderUpdate,
            ControlClosedDelegate closed)
        {
            InitializeComponent();

            m_advancedProviderSettings.Visibility = Visibility.Collapsed;
            m_detailsMode = mode;
            m_sipProvider = sipProvider;
            m_owner = owner;
            SIPProviderAdd_External = sipProviderAdd;
            SIPProviderUpdate_External = sipProviderUpdate;
            ControlClosed_External = closed;

            //if (m_disableProviderRegistrations)
            //{
            //    m_providerRegister.IsEnabled = false;
            //    m_providerRegisterContact.IsEnabled = false;
            //    m_providerRegisterExpiry.IsEnabled = false;
            //    m_providerRegisterServer.IsEnabled = false;
            //}

            if (mode == DetailsControlModesEnum.Edit)
            {
                m_providerTypeCanvas.Visibility = Visibility.Collapsed;
                m_gvSettingsPanel.Visibility = System.Windows.Visibility.Collapsed;
                m_applyButton.Content = "Update";
                PopulateDataFields(m_sipProvider);
            }
            else
            {
                m_providerIdCanvas.Visibility = Visibility.Collapsed;
                m_providerTypeNameCanvas.Visibility = Visibility.Collapsed;
                m_gvSettingsPanel.Visibility = System.Windows.Visibility.Collapsed;
                m_applyButton.Content = "Add";
            }
        }

        public void WriteStatusMessage(MessageLevelsEnum status, string message)
        {
            if (status == MessageLevelsEnum.Error)
            {
                UIHelper.SetColouredText(m_statusTextBlock, message, MessageLevelsEnum.Error);
            }
            else if (status == MessageLevelsEnum.Warn)
            {
                UIHelper.SetColouredText(m_statusTextBlock, message, MessageLevelsEnum.Warn);
            }
            else
            {
                UIHelper.SetColouredText(m_statusTextBlock, message, MessageLevelsEnum.Info);
            }
        }

        private void PopulateDataFields(SIPProvider sipProvider)
        {
            m_providerId.Text = sipProvider.ID.ToString();
            m_providerTypeName.Text = sipProvider.ProviderType.ToString();
            m_providerName.Text = sipProvider.ProviderName;
            m_providerUsername.Text = sipProvider.ProviderUsername;
            m_providerPassword.Text = sipProvider.ProviderPassword;

            if (String.Equals(sipProvider.ProviderType, ProviderTypes.SIP.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                m_providerServer.Text = sipProvider.ProviderServer.ToString();
                m_providerAuthUsername.Text = (sipProvider.ProviderAuthUsername != null) ? sipProvider.ProviderAuthUsername : String.Empty;
                m_providerFromHeader.Text = (sipProvider.ProviderFrom != null) ? sipProvider.ProviderFrom : String.Empty;
                m_providerRegisterRealm.Text = (sipProvider.RegisterRealm != null) ? sipProvider.RegisterRealm : String.Empty;

                //if (!m_disableProviderRegistrations)
                //{
                m_providerRegister.IsChecked = sipProvider.RegisterEnabled;
                m_providerSendMWISubscribe.IsChecked = sipProvider.SendMWISubscribe;
                m_providerRegisterContact.Text = (sipProvider.RegisterContact != null) ? sipProvider.RegisterContact.ToString() : String.Empty;
                m_providerRegisterExpiry.Text = sipProvider.RegisterExpiry.ToString();
                m_providerRegisterServer.Text = (sipProvider.RegisterServer != null) ? sipProvider.RegisterServer.ToString() : String.Empty;

                m_providerRegisterContact.IsEnabled = m_providerRegister.IsChecked.Value;
                m_providerRegisterExpiry.IsEnabled = m_providerRegister.IsChecked.Value;
                m_providerRegisterServer.IsEnabled = m_providerRegister.IsChecked.Value;
                //}

                if (sipProvider.CustomHeaders != null && sipProvider.CustomHeaders.Trim().Length > 0)
                {
                    string[] customHeaders = sipProvider.CustomHeaders.Split(m_customHeadersSeparator);

                    if (customHeaders != null && customHeaders.Length > 0)
                    {
                        foreach (string customHeader in customHeaders)
                        {
                            if (customHeader != null && customHeader.Trim().Length > 0)
                            {
                                m_providerCustomHeaders.Items.Add(customHeader.Trim());
                            }
                        }
                    }
                }
            }
            else
            {
                GoogleVoiceTypeClicked(null, null);

                m_gvCallbackNumber.Text = sipProvider.GVCallbackNumber;
                m_gvCallbackPattern.Text = sipProvider.GVCallbackPattern;

                if (sipProvider.GVCallbackType != null)
                {
                    for (int index = 0; index < m_gvCallbackType.Items.Count; index++)
                    {
                        if (((TextBlock)m_gvCallbackType.Items[index]).Text == sipProvider.GVCallbackType)
                        {
                            m_gvCallbackType.SelectedIndex = index;
                            break;
                        }
                    }
                }
            }
        }

        private void ApplyButtonClicked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (m_detailsMode == DetailsControlModesEnum.Edit)
            {
                Update();
            }
            else
            {
                Add();
            }
        }

        private void Add()
        {
            try
            {
                SIPProvider sipProvider = null;

                ProviderTypes providerType = (m_providerTypeSIPRadio.IsChecked != null && m_providerTypeSIPRadio.IsChecked.Value) ? ProviderTypes.SIP : ProviderTypes.GoogleVoice;
                string providerName = m_providerName.Text.Trim();
                string providerUsername = m_providerUsername.Text.Trim();
                string providerPassword = m_providerPassword.Text.Trim();

                if (providerType == ProviderTypes.SIP)
                {
                    SIPURI providerServer = (!m_providerServer.Text.IsNullOrBlank()) ? SIPURI.ParseSIPURIRelaxed(m_providerServer.Text.Trim()) : null;
                    bool registerEnabled = m_providerRegister.IsChecked.Value;
                    bool sendMWISubscribe = m_providerSendMWISubscribe.IsChecked.Value;
                    SIPURI registerContact = (!m_providerRegisterContact.Text.IsNullOrBlank()) ? SIPURI.ParseSIPURIRelaxed(m_providerRegisterContact.Text.Trim()) : null;
                    string authUsername = m_providerAuthUsername.Text.Trim();
                    string providerFrom = m_providerFromHeader.Text.Trim();
                    string registerRealm = m_providerRegisterRealm.Text.Trim();
                    SIPURI registerServer = (!m_providerRegisterServer.Text.IsNullOrBlank()) ? SIPURI.ParseSIPURIRelaxed(m_providerRegisterServer.Text.Trim()) : null;

                    int registerExpiry = m_defaultRegisterExpiry;
                    Int32.TryParse(m_providerRegisterExpiry.Text, out registerExpiry);

                    string customHeaders = null;
                    if (m_providerCustomHeaders.Items.Count > 0)
                    {
                        foreach (string customHeader in m_providerCustomHeaders.Items)
                        {
                            customHeaders += (m_sipProvider.CustomHeaders != null && m_sipProvider.CustomHeaders.Trim().Length > 0) ? m_customHeadersSeparator.ToString() : null;
                            customHeaders += customHeader;
                        }
                    }

                    sipProvider = new SIPProvider()
                    {
                        ID = Guid.NewGuid().ToString(),
                        ProviderType = ProviderTypes.SIP.ToString(),
                        Owner = m_owner,
                        ProviderName = providerName,
                        ProviderUsername = providerUsername,
                        ProviderPassword = providerPassword,
                        ProviderServer = (providerServer != null) ? providerServer.ToString() : null,
                        ProviderFrom = providerFrom,
                        CustomHeaders = customHeaders,
                        RegisterContact = (registerContact != null) ? registerContact.ToString() : null,
                        RegisterExpiry = registerExpiry,
                        RegisterServer = (registerServer != null) ? registerServer.ToString() : null,
                        ProviderAuthUsername = authUsername,
                        RegisterRealm = registerRealm,
                        RegisterEnabled = registerEnabled,
                        SendMWISubscribe = sendMWISubscribe,
                        RegisterAdminEnabled = true,
                        Inserted = DateTime.Now.ToString("o"),
                        LastUpdate = DateTime.Now.ToString("o")
                    };
                }
                else
                {
                    //string gvCallbackNumber = m_gvCallbackNumber.Text;
                    //string gvCallbackPattern = m_gvCallbackNumber.Text;
                    //GoogleVoiceCallbackTypes callbackType = (GoogleVoiceCallbackTypes)Enum.Parse(typeof(GoogleVoiceCallbackTypes), ((TextBlock)m_gvCallbackType.SelectedValue).Text, true);

                    //sipProvider = new SIPProvider(ProviderTypes.GoogleVoice, m_owner, providerName, providerUsername, providerPassword, null, null, null, null,
                    //    null, 0, null, null, null, false, true, gvCallbackNumber, gvCallbackPattern, callbackType);

                    sipProvider = new SIPProvider()
                    {
                        ID = Guid.NewGuid().ToString(),
                        ProviderType = ProviderTypes.GoogleVoice.ToString(),
                        Owner = m_owner,
                        ProviderName = providerName,
                        ProviderUsername = providerUsername,
                        ProviderPassword = providerPassword,
                        GVCallbackNumber = m_gvCallbackNumber.Text,
                        GVCallbackPattern = m_gvCallbackPattern.Text,
                        GVCallbackType = ((TextBlock)m_gvCallbackType.SelectedValue).Text,
                        Inserted = DateTime.Now.ToString("o"),
                        LastUpdate = DateTime.Now.ToString("o")
                    };
                }

                if (sipProvider.HasValidationErrors)
                {
                    WriteStatusMessage(MessageLevelsEnum.Warn, sipProvider.ValidationErrors.First().ErrorMessage);
                }
                else
                {
                    WriteStatusMessage(MessageLevelsEnum.Info, "Adding SIP Provider please wait...");
                    SIPProviderAdd_External(sipProvider);
                }
            }
            catch (Exception excp)
            {
                WriteStatusMessage(MessageLevelsEnum.Error, "Add SIPProvider Exception. " + excp.Message);
            }
        }

        private void Update()
        {
            try
            {
                m_sipProvider.ProviderName = m_providerName.Text;
                m_sipProvider.ProviderUsername = m_providerUsername.Text;
                m_sipProvider.ProviderPassword = m_providerPassword.Text;
                m_sipProvider.ProviderServer = (!m_providerServer.Text.IsNullOrBlank()) ? SIPURI.ParseSIPURIRelaxed(m_providerServer.Text.Trim()).ToString() : null;
                m_sipProvider.RegisterEnabled = m_providerRegister.IsChecked.Value;
                m_sipProvider.SendMWISubscribe = m_providerSendMWISubscribe.IsChecked.Value;
                m_sipProvider.RegisterContact = (!m_providerRegisterContact.Text.IsNullOrBlank()) ? SIPURI.ParseSIPURIRelaxed(m_providerRegisterContact.Text.Trim()).ToString() : null;
                m_sipProvider.ProviderAuthUsername = m_providerAuthUsername.Text;
                m_sipProvider.ProviderFrom = m_providerFromHeader.Text;
                m_sipProvider.RegisterRealm = m_providerRegisterRealm.Text;
                m_sipProvider.RegisterServer = (!m_providerRegisterServer.Text.IsNullOrBlank()) ? SIPURI.ParseSIPURIRelaxed(m_providerRegisterServer.Text.Trim()).ToString() : null;

                int registerExpiry = m_defaultRegisterExpiry;
                if (Int32.TryParse(m_providerRegisterExpiry.Text, out registerExpiry))
                {
                    m_sipProvider.RegisterExpiry = registerExpiry;
                }

                m_sipProvider.CustomHeaders = null;
                if (m_providerCustomHeaders.Items.Count > 0)
                {
                    foreach (string customHeader in m_providerCustomHeaders.Items)
                    {
                        m_sipProvider.CustomHeaders += (m_sipProvider.CustomHeaders != null && m_sipProvider.CustomHeaders.Trim().Length > 0) ? m_customHeadersSeparator.ToString() : null;
                        m_sipProvider.CustomHeaders += customHeader;
                    }
                }

                if (m_sipProvider.RegisterEnabled && m_sipProvider.RegisterAdminEnabled)
                {
                    m_sipProvider.RegisterDisabledReason = null;
                }

                m_sipProvider.GVCallbackNumber = m_gvCallbackNumber.Text;
                m_sipProvider.GVCallbackPattern = m_gvCallbackPattern.Text;
                m_sipProvider.GVCallbackType = ((TextBlock)m_gvCallbackType.SelectedValue).Text;

                if (m_sipProvider.HasValidationErrors)
                {
                    WriteStatusMessage(MessageLevelsEnum.Warn, m_sipProvider.ValidationErrors.First().ErrorMessage);
                }
                else
                {
                    WriteStatusMessage(MessageLevelsEnum.Info, "Updating SIP Provider please wait...");
                    SIPProviderUpdate_External(m_sipProvider);
                }
            }
            catch (Exception excp)
            {
                WriteStatusMessage(MessageLevelsEnum.Error, "Update Exception. " + excp.Message);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            ControlClosed_External();
        }

        private void AdvancedSettings_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (m_advancedProviderSettings.Visibility != Visibility.Visible)
            {
                m_advancedSettingsMenu.Text = "hide advanced settings";
                m_advancedProviderSettings.Visibility = Visibility.Visible;
            }
            else
            {
                m_advancedSettingsMenu.Text = "show advanced settings";
                m_advancedProviderSettings.Visibility = Visibility.Collapsed;
            }
        }

        private void AddCustomHeader_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            m_providerCustomHeaders.Items.Add(m_providerCustom.Text);
            m_providerCustom.Text = String.Empty;
        }

        private void CustomHeaders_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (m_providerCustomHeaders.SelectedItem != null)
            {
                m_providerCustom.Text = m_providerCustomHeaders.SelectedItem as string;
                m_providerCustomHeaders.Items.Remove(m_providerCustomHeaders.SelectedItem);
            }
        }

        private void ProviderRegister_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            //if (!m_disableProviderRegistrations)
            //{
            m_providerRegisterContact.IsEnabled = true;
            m_providerRegisterExpiry.IsEnabled = true;
            m_providerRegisterServer.IsEnabled = true;

            if (m_providerRegisterExpiry.Text.Trim().Length == 0)
            {
                m_providerRegisterExpiry.Text = m_defaultRegisterExpiry.ToString();
            }

            if (m_providerRegisterContact.Text.Trim().Length == 0)
            {
                m_providerRegisterContact.Text = m_owner + "@" + m_defaultContactHost;
            }
            //}
        }

        private void ProviderRegister_Unchecked(object sender, System.Windows.RoutedEventArgs e)
        {
            m_providerRegisterContact.IsEnabled = false;
            m_providerRegisterExpiry.IsEnabled = false;
            m_providerRegisterServer.IsEnabled = false;
        }

        private void SIPTypeClicked(object sender, System.Windows.RoutedEventArgs e)
        {
            m_gvSettingsPanel.Visibility = System.Windows.Visibility.Collapsed;
            m_providerServerCanvas.Visibility = Visibility.Visible;
            m_providerRegisterCanvas.Visibility = Visibility.Visible;
            m_provideRegisterContactCanvas.Visibility = Visibility.Visible;
            m_advancedSettingsMenu.Visibility = Visibility.Visible;
        }

        private void GoogleVoiceTypeClicked(object sender, System.Windows.RoutedEventArgs e)
        {
            m_gvSettingsPanel.Visibility = System.Windows.Visibility.Visible;
            m_advancedProviderSettings.Visibility = Visibility.Collapsed;
            m_providerServerCanvas.Visibility = System.Windows.Visibility.Collapsed;
            m_providerRegisterCanvas.Visibility = System.Windows.Visibility.Collapsed;
            m_provideRegisterContactCanvas.Visibility = System.Windows.Visibility.Collapsed;
            m_advancedSettingsMenu.Visibility = System.Windows.Visibility.Collapsed;
        }
    }
}