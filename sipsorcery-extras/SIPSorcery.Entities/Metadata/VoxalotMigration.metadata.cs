using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace SIPSorcery.Entities
{
    [MetadataType(typeof(VoxalotMigrationMetadata))]
    public partial class VoxalotMigration
    {
        internal class VoxalotMigrationMetadata
        {
            //[Editable(false, AllowInitialValue = true)]
            //public string ID;

            //[Required(ErrorMessage = "An owner must be specified for a dial plan rule.")]
            //public string Owner;

            //[Required(ErrorMessage = "A pattern must be specified for a dial plan rule.")]
            //public string Pattern;
        }
    }
}
