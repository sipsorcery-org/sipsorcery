using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace SIPSorcery.Entities
{
    public partial class SIPAccount
    {
        private const string BANNED_SIPACCOUNT_NAME = "dispatcher";

        public static int TimeZoneOffsetMinutes;

        public DateTime InsertedLocal
        {
            get
            {
                return (Inserted != null) ? TimeZoneHelper.ApplyOffset(Inserted, TimeZoneOffsetMinutes) : DateTime.MinValue;
            }
        }

        public bool IsDisabled
        {
            get { return IsUserDisabled || IsAdminDisabled; }
        }

        public static SIPAccount Create(
            string owner,
            string domain,
            string username,
            string password,
            string outDialPlanName)
        {
            return new SIPAccount()
            {
                ID = Guid.NewGuid().ToString(),
                Inserted = DateTimeOffset.UtcNow.ToString("o"),
                SIPUsername = username,
                SIPDomain = domain,
                Owner = owner,
                SIPPassword = password,
                OutDialPlanName = outDialPlanName
            };
        }

#if !SILVERLIGHT

        public static string Validate(SIPAccount sipAccount)
        {
            TypeDescriptor.AddProviderTransparent(new AssociatedMetadataTypeTypeDescriptionProvider(typeof(SIPAccount), typeof(SIPAccountMetadata)), typeof(SIPAccount));

            var validationContext = new ValidationContext(sipAccount, null, null);
            var validationResults = new List<ValidationResult>();
            Validator.TryValidateObject(sipAccount, validationContext, validationResults);

            if (validationResults.Count > 0)
            {
                return validationResults.First().ErrorMessage;
            }
            else
            {
                Guid testGuid = Guid.Empty;

                if(!Guid.TryParse(sipAccount.ID, out testGuid))
                {
                    return "The ID was not a valid GUID.";
                }
                else if (String.Compare(sipAccount.SIPUsername, BANNED_SIPACCOUNT_NAME, true) == 0)
                {
                    return "The username you have requested is not permitted.";
                }
                else if (sipAccount.SIPUsername.Contains(".") &&
                    (sipAccount.SIPUsername.Substring(sipAccount.SIPUsername.LastIndexOf(".") + 1).Trim().Length >= SIPAccount.USERNAME_MIN_LENGTH &&
                    sipAccount.SIPUsername.Substring(sipAccount.SIPUsername.LastIndexOf(".") + 1).Trim() != sipAccount.Owner))
                {
                    return "You are not permitted to create this username. Only user " + sipAccount.SIPUsername.Substring(sipAccount.SIPUsername.LastIndexOf(".") + 1).Trim() + " can create SIP accounts ending in " + sipAccount.SIPUsername.Substring(sipAccount.SIPUsername.LastIndexOf(".")).Trim() + ".";
                }
            }

            return null;
        }

#endif

        public static void Clean(SIPAccount sipAccount)
        {
            sipAccount.Owner = sipAccount.Owner.Trim();
            sipAccount.SIPUsername = sipAccount.SIPUsername.Trim();
            sipAccount.SIPPassword = (sipAccount.SIPPassword == null) ? null : sipAccount.SIPPassword.Trim();
            sipAccount.SIPDomain = sipAccount.SIPDomain.Trim();
        }
    }
}
