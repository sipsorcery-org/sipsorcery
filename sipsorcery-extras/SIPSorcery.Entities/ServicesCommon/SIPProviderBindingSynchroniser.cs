using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Entities
{
    public class SIPProviderBindingSynchroniser
    {
        private static ILog logger = AppState.logger;

        public static void SIPProviderAdded(SIPProvider sipProvider)
        {
            try
            {
                logger.Debug("SIPProviderBindingSynchroniser SIPProviderAdded for " + sipProvider.Owner + " and " + sipProvider.ProviderName + " (Provider ID=" + sipProvider.ID + ").");

                if (sipProvider.RegisterEnabled)
                {
                    using (SIPSorceryEntities sipSorceryEntities = new SIPSorceryEntities())
                    {
                        SIPProvider existingProvider = (from provider in sipSorceryEntities.SIPProviders
                                                        where provider.ID == sipProvider.ID
                                                        select provider).FirstOrDefault();

                        if (existingProvider != null)
                        {
                            AddNewBindingForProvider(sipSorceryEntities, existingProvider);
                        }
                        else
                        {
                            logger.Warn("The SIP provider entry was not in the database when attempting to add a provider binding.");
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPProviderBindingSynchroniser SIPProviderAdded. " + excp.Message);
            }
        }

        public static void SIPProviderUpdated(SIPProvider sipProvider)
        {
            try
            {
                logger.Debug("SIPProviderBindingSynchroniser SIPProviderUpdated for " + sipProvider.Owner + " and " + sipProvider.ProviderName + ".");

                using (SIPSorceryEntities sipSorceryEntities = new SIPSorceryEntities())
                {
                    SIPProviderBinding existingBinding = (from binding in sipSorceryEntities.SIPProviderBindings
                                                          where binding.ProviderID == sipProvider.ID
                                                          select binding).FirstOrDefault();

                    if (sipProvider.RegisterEnabled)
                    {
                        if (existingBinding == null)
                        {
                            AddNewBindingForProvider(sipSorceryEntities, sipProvider);
                        }
                        else
                        {
                            existingBinding.SetProviderFields(sipProvider);
                            existingBinding.NextRegistrationTime = DateTime.UtcNow.ToString("o");
                            sipSorceryEntities.SaveChanges();
                        }
                    }
                    else
                    {
                        if (existingBinding != null)
                        {
                            if (existingBinding.IsRegistered)
                            {
                                // Let the registration agent know the existing binding should be expired.
                                existingBinding.BindingExpiry = 0;
                                existingBinding.NextRegistrationTime = DateTime.UtcNow.ToString("o");
                                sipSorceryEntities.SaveChanges();
                            }
                            else
                            {
                                sipSorceryEntities.SIPProviderBindings.Remove(existingBinding);
                                sipSorceryEntities.SaveChanges();
                            }
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPProviderBindingSynchroniser SIPProviderUpdated. " + excp.Message);
            }
        }

        public static void SIPProviderDeleted(SIPProvider sipProvider)
        {
            try
            {
                logger.Debug("SIPProviderBindingSynchroniser SIPProviderDeleted for " + sipProvider.Owner + " and " + sipProvider.ProviderName + ".");

                using (SIPSorceryEntities sipSorceryEntities = new SIPSorceryEntities())
                {

                    SIPProviderBinding existingBinding = (from binding in sipSorceryEntities.SIPProviderBindings
                                                          where binding.ProviderID == sipProvider.ID
                                                          select binding).FirstOrDefault();

                    if (existingBinding != null)
                    {
                        if (existingBinding.IsRegistered)
                        {
                            // Let the registration agent know the existing binding should be expired.
                            existingBinding.BindingExpiry = 0;
                            existingBinding.NextRegistrationTime = DateTime.UtcNow.ToString("o");
                            sipSorceryEntities.SaveChanges();
                        }
                        else
                        {
                            sipSorceryEntities.SIPProviderBindings.Remove(existingBinding);
                            sipSorceryEntities.SaveChanges();
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPProviderBindingSynchroniser SIPProviderDeleted. " + excp.Message);
            }
        }

        private static void AddNewBindingForProvider(SIPSorceryEntities sipSorceryEntities, SIPProvider sipProvider)
        {
            try
            {
                logger.Debug("AddNewBindingForProvider provider ID=" + sipProvider.ID + ".");
                SIPProviderBinding newBinding = sipSorceryEntities.SIPProviderBindings.Create();
                newBinding.SetProviderFields(sipProvider);
                newBinding.ID = Guid.NewGuid().ToString();
                newBinding.NextRegistrationTime = DateTimeOffset.UtcNow.ToString("o");
                newBinding.ProviderID = sipProvider.ID;
                newBinding.Owner = sipProvider.Owner;
            
                sipSorceryEntities.SIPProviderBindings.Add(newBinding);
                sipSorceryEntities.SaveChanges();
            }
            catch (Exception excp)
            {
                logger.Error("Exception AddNewBindingForProvider. " + excp.Message);
                logger.Error(excp);
            }
        }
    }
}
