using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

#if !SILVERLIGHT
using SIPSorcery.SIP;
using SIPSorcery.Sys;
#endif

namespace SIPSorcery.Entities
{
    public partial class SIPProviderBinding
    {
        public const string REGAGENT_CONTACT_ID_KEY = "rinstance";

        public static int TimeZoneOffsetMinutes;

        public DateTime LastRegisterTimeLocal
        {
            get
            {
                return (LastRegisterTime != null) ? TimeZoneHelper.ApplyOffset(LastRegisterTime, TimeZoneOffsetMinutes) : DateTime.MinValue;
            }
        }

        public DateTime LastRegisterAttemptLocal
        {
            get
            {
                return (LastRegisterAttempt != null) ? TimeZoneHelper.ApplyOffset(LastRegisterAttempt, TimeZoneOffsetMinutes) : DateTime.MinValue;
            }
        }

        public DateTime NextRegistrationTimeLocal
        {
            get
            {
                return (NextRegistrationTime != null) ? TimeZoneHelper.ApplyOffset(NextRegistrationTime, TimeZoneOffsetMinutes) : DateTime.MinValue;
            }
        }

#if !SILVERLIGHT

        // Fields populated and re-populated by the SIPProvider entry whenever a registration is initiated or refereshed.
        // The details are NOT persisted and will be re-populated from the SIP Provider entry by the registration agent whenever required.
        public SIPURI RegistrarServer;
        public string ProviderAuthUsername;
        public string ProviderPassword;
        public string RegistrarRealm;
        public string ProviderOutboundProxy;

        public void SetProviderFields(SIPProvider sipProvider)
        {
            ProviderID = sipProvider.ID;
            Owner = sipProvider.Owner;
            AdminMemberID = sipProvider.AdminMemberID;
            ProviderName = sipProvider.ProviderName;
            ProviderAuthUsername = (!sipProvider.ProviderAuthUsername.IsNullOrBlank()) ? sipProvider.ProviderAuthUsername : sipProvider.ProviderUsername;
            ProviderPassword = sipProvider.ProviderPassword;
            RegistrarServer = sipProvider.GetRegistrar();
            RegistrarRealm = (!sipProvider.RegisterRealm.IsNullOrBlank()) ? sipProvider.RegisterRealm : RegistrarServer.Host;
            ProviderOutboundProxy = sipProvider.ProviderOutboundProxy;

            if (sipProvider.RegisterEnabled)
            {
                BindingExpiry = (sipProvider.RegisterExpiry.HasValue) ? sipProvider.RegisterExpiry.Value : 0;
            }
            else
            {
                BindingExpiry = 0;
            }

            string bindingId = null;
            SIPURI binding = (!BindingURI.IsNullOrBlank()) ? SIPURI.ParseSIPURIRelaxed(BindingURI) : null;
            if (binding != null && binding.Parameters.Has(REGAGENT_CONTACT_ID_KEY))
            {
                bindingId = binding.Parameters.Get(REGAGENT_CONTACT_ID_KEY);
            }

            if (!sipProvider.RegisterContact.IsNullOrBlank())
            {
                binding = SIPURI.ParseSIPURI(sipProvider.RegisterContact);
                if (!bindingId.IsNullOrBlank())
                {
                    binding.Parameters.Set(REGAGENT_CONTACT_ID_KEY, bindingId);
                }

                if (binding != null)
                {
                    BindingURI = binding.ToString();
                }
                else
                {
                    BindingURI = null;
                    BindingExpiry = 0;
                }
            }
            else
            {
                // The register contact field on the SIP Provider is empty. 
                // This condition needs to be trearted as the binding being disabled and it needs to be removed.
                BindingExpiry = 0;
            }
        }

#endif

    }
}
