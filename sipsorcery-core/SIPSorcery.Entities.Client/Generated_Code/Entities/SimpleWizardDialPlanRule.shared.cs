using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIPSorcery.Entities
{
    public partial class SimpleWizardDialPlanRule
    {
        public SimpleWizardRuleTypes RuleType
        {
            get { return (SimpleWizardRuleTypes)Enum.Parse(typeof(SimpleWizardRuleTypes), RuleTypeID.ToString(), true); }
        }
    }
}
