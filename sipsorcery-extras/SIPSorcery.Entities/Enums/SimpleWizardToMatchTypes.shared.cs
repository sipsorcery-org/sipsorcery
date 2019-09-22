using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIPSorcery.Entities
{
    public enum SimpleWizardToMatchTypes
    {
        None = 0,
        Any = 1,
        ToSIPAccount = 2,
        ToSIPProvider = 3,
        Regex = 4,
    }
}
