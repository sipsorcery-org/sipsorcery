using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace SIPSorcery.Entities
{
    [MetadataType(typeof(RateMetadata))]
    public partial class Rate
    {
        internal class RateMetadata
        {
            [DisplayFormat(DataFormatString = "{0:n5}", ApplyFormatInEditMode = true)]
            public decimal Rate1 { get; set; }

            [DisplayFormat(DataFormatString = "{0:n5}", ApplyFormatInEditMode = true)]
            public decimal SetupCost { get; set; }
        }
    }
}
