using System;
using System.Collections.Generic;

#nullable disable

namespace demo.DataAccess
{
    public partial class SIPDialPlan
    {
        public SIPDialPlan()
        {
            SIPAccounts = new HashSet<SIPAccount>();
        }

        public Guid ID { get; set; }
        public string DialPlanName { get; set; }
        public string DialPlanScript { get; set; }
        public DateTime Inserted { get; set; }
        public DateTime LastUpdate { get; set; }
        public bool AcceptNonInvite { get; set; }

        public virtual ICollection<SIPAccount> SIPAccounts { get; set; }
    }
}
