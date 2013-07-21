using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

#if !SILVERLIGHT
using SIPSorcery.SIP;
using SIPSorcery.Sys;
#endif

namespace SIPSorcery.Entities
{
    public partial class SIPProvider
    {
        public const char CUSTOM_HEADERS_SEPARATOR = '|';
        public const int REGISTER_DEFAULT_EXPIRY = 3600;
        public const int REGISTER_MINIMUM_EXPIRY = 60;            // The minimum interval a registration will be accepted for. Anything less than this interval will use this minimum value.
        public const int REGISTER_MAXIMUM_EXPIRY = 3600;
        public const string DEFAULT_GV_CALLBACK_PATTERN = ".*";

        public static string ProhibitedServerPatterns;            // If set will be used as a regex pattern to prevent certain strings being used in the Provider Server and RegisterServer fields.

        public bool RegisterActive
        {
            get { return RegisterAdminEnabled && RegisterEnabled; }
            private set {}
        }

#if !SILVERLIGHT

        /// <summary>
        /// Normally the registrar server will just be the main Provider server however in some cases they will be different.
        /// </summary>
        public SIPURI GetRegistrar()
        {
            return (RegisterServer.NotNullOrBlank()) ? SIPURI.ParseSIPURIRelaxed(RegisterServer) : SIPURI.ParseSIPURIRelaxed(ProviderServer);
        }

        public static string Validate(SIPProvider sipProvider)
        {
            TypeDescriptor.AddProviderTransparent(new AssociatedMetadataTypeTypeDescriptionProvider(typeof(SIPProvider), typeof(SIPProviderMetadata)), typeof(SIPProvider));

            var validationContext = new ValidationContext(sipProvider, null, null);
            var validationResults = new List<ValidationResult>();
            Validator.TryValidateObject(sipProvider, validationContext, validationResults);

            if (validationResults.Count > 0)
            {
                return validationResults.First().ErrorMessage;
            }
            else
            {
                Guid testGuid = Guid.Empty;
                if (!Guid.TryParse(sipProvider.ID, out testGuid))
                {
                    return "The ID was not a valid GUID.";
                }

                if (sipProvider.ProviderType == ProviderTypes.SIP.ToString())
                {
                    if (sipProvider.ProviderServer.IsNullOrBlank())
                    {
                        return "A value for Server must be specified.";
                    }
                    if (sipProvider.RegisterEnabled && sipProvider.RegisterContact == null)
                    {
                        return "A valid contact must be supplied to enable a provider registration.";
                    }
                    //else if (sipProvider.RegisterServer != null && sipProvider.m_registerServer.Host.IndexOf('.') == -1)
                    //{
                    //    return "Your register server entry appears to be invalid. A valid hostname or IP address should contain at least one '.'.";
                    //}
                    //else if (sipProvider.RegisterContact != null && sipProvider.m_registerContact.Host.IndexOf('.') == -1)
                    //{
                    //    return "Your register contact entry appears to be invalid. A valid hostname or IP address should contain at least one '.'.";
                    //}
                    //else if (sipProvider.RegisterContact != null && sipProvider.RegisterContact.User.IsNullOrBlank())
                    //{
                    //    return "Your register contact entry appears to be invalid, the user portion was missing. Contacts must be of the form user@host.com, e.g. joe@sipsorcery.com.";
                    //}
                    else if (ProhibitedServerPatterns != null && Regex.Match(sipProvider.ProviderServer, ProhibitedServerPatterns).Success)
                    {
                        return "The Provider Server contains a disallowed string. If you are trying to create a Provider entry pointing to sipsorcery.com it is not permitted.";
                    }
                    else if (ProhibitedServerPatterns != null && sipProvider.RegisterServer != null && Regex.Match(sipProvider.RegisterServer, ProhibitedServerPatterns).Success)
                    {
                        return "The Provider Register Server contains a disallowed string. If you are trying to create a Provider entry pointing to sipsorcery.com it is not permitted.";
                    }
                    else if (!SIPURI.TryParse(sipProvider.ProviderServer))
                    {
                        return "The Provider Server could not be parsed as a valid SIP URI.";
                    }
                    else if (sipProvider.RegisterServer != null && !SIPURI.TryParse(sipProvider.RegisterServer))
                    {
                        return "The Register Server could not be parsed as a valid SIP URI.";
                    }
                }
                else if (sipProvider.ProviderType == ProviderTypes.GoogleVoice.ToString())
                {
                    if (sipProvider.ProviderPassword.IsNullOrBlank())
                    {
                        return "A password is required for Google Voice entries.";
                    }
                    else if (sipProvider.GVCallbackNumber.IsNullOrBlank())
                    {
                        return "A callback number is required for Google Voice entries.";
                    }
                    else if (Regex.Match(sipProvider.GVCallbackNumber, @"\D").Success)
                    {
                        return "The callback number contains an invalid character. Only digits are permitted.";
                    }
                }
            }

            return null;
        }

#endif

    }
}
