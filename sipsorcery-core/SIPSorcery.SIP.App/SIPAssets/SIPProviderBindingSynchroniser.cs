using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App {
    
    public class SIPProviderBindingSynchroniser {

        private static ILog logger = AppState.logger;

        private SIPAssetPersistor<SIPProviderBinding> m_bindingPersistor;

        public SIPProviderBindingSynchroniser(SIPAssetPersistor<SIPProviderBinding> sipProviderBindingsPersistor) {
            m_bindingPersistor = sipProviderBindingsPersistor;
        }

        public void SIPProviderAdded(SIPProvider sipProvider) {
            try {
                logger.Debug("SIPProviderBindingSynchroniser SIPProviderAdded for " + sipProvider.Owner + " and " + sipProvider.ProviderName + ".");

                if (sipProvider.RegisterEnabled) {
                    SIPProviderBinding binding = new SIPProviderBinding(sipProvider);
                    m_bindingPersistor.Add(binding);
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SIPProviderBindingSynchroniser SIPProviderAdded. " + excp.Message);
            }
        }

        public void SIPProviderUpdated(SIPProvider sipProvider) {
            try {
                logger.Debug("SIPProviderBindingSynchroniser SIPProviderUpdated for " + sipProvider.Owner + " and " + sipProvider.ProviderName + ".");

                SIPProviderBinding existingBinding = m_bindingPersistor.Get(b => b.ProviderId == sipProvider.Id);

                if (sipProvider.RegisterEnabled) {
                    if (existingBinding == null) {
                        SIPProviderBinding newBinding = new SIPProviderBinding(sipProvider);
                        m_bindingPersistor.Add(newBinding);
                    }
                    else {
                        existingBinding.SetProviderFields(sipProvider);
                        existingBinding.NextRegistrationTime = DateTime.Now;
                        m_bindingPersistor.Update(existingBinding);
                    }
                }
                else {
                    if (existingBinding != null) {
                        if (existingBinding.IsRegistered) {
                            // Let the registration agent know the existing binding should be expired.
                            existingBinding.BindingExpiry = 0;
                            existingBinding.NextRegistrationTime = DateTime.Now;
                            m_bindingPersistor.Update(existingBinding);
                        }
                        else {
                            m_bindingPersistor.Delete(existingBinding);
                        }
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SIPProviderBindingSynchroniser SIPProviderUpdated. " + excp.Message);
            }
        }

        public void SIPProviderDeleted(SIPProvider sipProvider) {
            try {
                logger.Debug("SIPProviderBindingSynchroniser SIPProviderDeleted for " + sipProvider.Owner + " and " + sipProvider.ProviderName + ".");

                SIPProviderBinding existingBinding = m_bindingPersistor.Get(b => b.ProviderId == sipProvider.Id);
                if (existingBinding != null) {
                    if (existingBinding.IsRegistered) {
                        // Let the registration agent know the existing binding should be expired.
                        existingBinding.BindingExpiry = 0;
                        existingBinding.NextRegistrationTime = DateTime.Now;
                        m_bindingPersistor.Update(existingBinding);
                    }
                    else {
                        m_bindingPersistor.Delete(existingBinding);
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SIPProviderBindingSynchroniser SIPProviderDeleted. " + excp.Message);
            }
        }

    }
}
