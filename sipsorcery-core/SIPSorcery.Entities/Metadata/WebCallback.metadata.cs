using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace SIPSorcery.Entities
{
    [MetadataType(typeof(WebCallbackMetadata))]
    public partial class WebCallback
    {
        internal class WebCallbackMetadata
        {
            [Required]
            public string Description;

            [Required]
            public string DialString1;

            [Required]
            public string DialString2;
        }
    }
}
