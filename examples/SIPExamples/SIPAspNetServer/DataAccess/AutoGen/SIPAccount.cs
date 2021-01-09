using System;
using System.Collections.Generic;

#nullable disable

namespace demo.DataAccess
{
    public partial class SIPAccount
    {
        public SIPAccount()
        {
            SIPRegistrarBindings = new HashSet<SIPRegistrarBinding>();
        }

        public Guid ID { get; set; }
        public Guid DomainID { get; set; }
        public Guid? SIPDialPlanID { get; set; }
        public string SIPUsername { get; set; }
        public string SIPPassword { get; set; }
        public bool IsDisabled { get; set; }
        public DateTime Inserted { get; set; }

        public virtual SIPDomain Domain { get; set; }
        public virtual SIPDialPlan SIPDialPlan { get; set; }
        public virtual ICollection<SIPRegistrarBinding> SIPRegistrarBindings { get; set; }
    }
}
