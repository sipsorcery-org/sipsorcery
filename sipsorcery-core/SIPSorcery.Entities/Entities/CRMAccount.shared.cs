using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIPSorcery.Entities
{
    public partial class CRMAccount
    {
        public CRMAccountTypes CRMAccountType
        {
            get { return (CRMAccountTypes)Enum.Parse(typeof(CRMAccountTypes), this.CRMTypeID.ToString(), true); }
        }
    }
}
