using System;
using System.Collections.Generic;

#nullable disable

namespace SIPAspNetServer.DataAccess
{
    public partial class SIPDomain
    {
        public SIPDomain()
        {
            SIPAccounts = new HashSet<SIPAccount>();
        }

        public Guid ID { get; set; }
        public string Domain { get; set; }
        public string AliasList { get; set; }
        public DateTimeOffset Inserted { get; set; }

        public virtual ICollection<SIPAccount> SIPAccounts { get; set; }
    }
}
