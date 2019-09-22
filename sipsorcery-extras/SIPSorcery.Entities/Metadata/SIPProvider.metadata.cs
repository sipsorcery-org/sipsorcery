using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace SIPSorcery.Entities
{
    [MetadataType(typeof(SIPProviderMetadata))]
    public partial class SIPProvider
    {
        internal class SIPProviderMetadata
        {
            [Editable(false, AllowInitialValue = true)]
            public string ID;

            [Required(ErrorMessage = "An owner must be specified for the SIP provider.")]
            public string Owner;

            [Required(ErrorMessage = "A provider name must be specified.")]
            [MyRegularExpressionAttribute(@"[^\.]+", ErrorMessage = "Provider names cannot contain a full stop '.' in order to avoid ambiguity with DNS host names, please remove the '.'.")]
            public string ProviderName;

            [Required(ErrorMessage = "A username must be specified for the provider.")]
            public string ProviderUsername;

            [MyRegularExpressionAttribute(@".+\..+", ErrorMessage = "The provider server should contain at least one '.' to be recognised as a valid hostname or IP address.")]
            public string ProviderServer;

            [MyRegularExpressionAttribute(@".+@.+\..+", ErrorMessage = "The register contact should be of the form user@server.com.")]
            public string RegisterContact;

            [MyRegularExpressionAttribute(@".+\..+", ErrorMessage = "The register server should contain at least one '.' to be recognised as a valid hostname or IP address.")]
            public string RegisterServer;
        }
    }
}
