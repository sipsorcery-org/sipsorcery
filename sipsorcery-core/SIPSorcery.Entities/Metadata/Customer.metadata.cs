
using System.ComponentModel.DataAnnotations;
using SIPSorcery.Sys;

namespace SIPSorcery.Entities
{
    [MetadataType(typeof(CustomerMetadata))]
    public partial class Customer //: IUser
    {
        internal class CustomerMetadata
        {
            [Editable(false, AllowInitialValue=true)]
            public string ID;

            //[Key]
            [Required(ErrorMessage = "Please choose a username.")]
            [StringLength(32, MinimumLength = 5, ErrorMessage = "The username is an invalid length. The minimum length required is 5 characters and the maximum length allowed is 32 characters.")]
            [MyRegularExpressionAttribute(@"[a-zA-Z0-9_\-]*", ErrorMessage = "The username contained an illegal character. Only characters a-zA-Z0-9_- are permitted.")]            
            public string Name;

            [Required(ErrorMessage = "A password must be specified.")]
            [StringLength(32, MinimumLength = 6, ErrorMessage = "The password is an invalid length. The minimum length required is 6 characters and the maximum length allowed is 32 characters.")]
            public string CustomerPassword;

            [StringLength(32, MinimumLength = 6, ErrorMessage = "The password is an invalid length. The minimum length required is 6 characters and the maximum length allowed is 32 characters.")]
            public string RetypedPassword;

            [Required(ErrorMessage = "Please enter your email address. A confirmation email will be sent to this address before your account is activated.")]
            [StringLength(255, ErrorMessage = "The email address is too long. The maximum length allowed is 255 characters.")]
            [MyRegularExpressionAttribute(AppState.EMAIL_VALIDATION_REGEX, ErrorMessage = "The value was not recognised as a valid email address.")]
            public string EmailAddress;

            [Required(ErrorMessage = "Please enter your first name.")]
            [StringLength(64, ErrorMessage = "The first name is too long. The maximum length allowed is 64 characters.")]
            public string Firstname;

            [Required(ErrorMessage = "Please enter your last name.")]
            [StringLength(64, ErrorMessage = "The last name is too long. The maximum length allowed is 64 characters.")]
            public string Lastname;

            //[Required(ErrorMessage = "Please enter the city you live in or that is closest to where you live.")]
            [StringLength(64, ErrorMessage = "The City value is too long. The maximum length allowed is 64 characters.")]
            public string City;

            //[Required(ErrorMessage = "Please select the country you live in from the list.")]
            public string Country;

            [StringLength(256, ErrorMessage = "The web site is too long. The maximum length allowed is 256 characters.")]
            public string WebSite;

            [Editable(false)]
            public bool Active;

            [Editable(false)]
            public bool Suspended;

            [Editable(false)]
            public string SuspendedReason;

            [Required(ErrorMessage = "Please select a security question from the list.")]
            public string SecurityQuestion;

            [Required(ErrorMessage = "Please enter an answer to the security question.")]
            [StringLength(256, ErrorMessage = "The security answer is too long. The maximum length allowed is 256 characters.")]
            public string SecurityAnswer;

            [Editable(false)]
            public string CreatedFromIPAddress;

            [Editable(false)]
            public string AdminID;

            [Editable(false)]
            public string AdminMemberID;

            [Editable(false)]
            public int MaxExecutionCount;

            [Editable(false)]
            public int ExecutionCount;

            [Editable(false)]
            public string AuthorisedApps;

            [Required(ErrorMessage = "Please select your timezone from the list.")]
            public string Timezone;

            [Editable(false)]
            public bool EmailAddressConfirmed;

            [StringLength(36, ErrorMessage = "The invite code is the wrong length is must be 36 characters long.")]
            public string InviteCode;

            [Editable(false, AllowInitialValue=true)]
            public string Inserted;

            [Editable(false)]
            public string PasswordResetID;

            [Editable(false)]
            public string PasswordResetIDSetAt;
        }
    }
}
