using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data.Entity.Core.Objects.DataClasses;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace SIPSorcery.Entities
{
    [MetadataType(typeof(SIPAccountMetadata))]
    public partial class SIPAccount
    {
        private const int PASSWORD_MIN_LENGTH = 6;
        private const int PASSWORD_MAX_LENGTH = 15;
        private const int USERNAME_MIN_LENGTH = 5;
        private const int USERNAME_MAX_LENGTH = 32;
        private const string USERNAME_LENGTH_ERROR_MESSAGE = "The username must be between 5 and 32 characters.";
        private const string PASSWORD_LENGTH_ERROR_MESSAGE = "The password must be between 6 and 15 characters.";

        internal class SIPAccountMetadata
        {
            [Key]
            [Editable(false, AllowInitialValue = true)]
            public string ID;

            [Required(ErrorMessage = "An owner must be specified for the SIP account.")]
            public string Owner;

            [Required(ErrorMessage = "A username must be specified for the SIP account.")]
            [StringLength(SIPAccount.USERNAME_MAX_LENGTH, MinimumLength = SIPAccount.USERNAME_MIN_LENGTH, ErrorMessage = SIPAccount.USERNAME_LENGTH_ERROR_MESSAGE)]
            [MyRegularExpressionAttribute(@"[a-zA-Z0-9_\-\.]+", ErrorMessage = "The username contained an illegal character. Only alpha-numeric characters and .-_ are allowed.")]
            public string SIPUsername;

            [Required(ErrorMessage = "A password must be specified for the SIP account.")]
            [StringLength(SIPAccount.PASSWORD_MAX_LENGTH, MinimumLength = SIPAccount.PASSWORD_MIN_LENGTH, ErrorMessage = SIPAccount.PASSWORD_LENGTH_ERROR_MESSAGE)]
            public string SIPPassword;

            //[Required(ErrorMessage = "A domain must be specified for the SIP account.")]
            //public string SIPDomain;
        }
    }
}
