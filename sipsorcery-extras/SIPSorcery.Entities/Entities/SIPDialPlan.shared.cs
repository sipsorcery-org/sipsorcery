using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace SIPSorcery.Entities
{
    public partial class SIPDialPlan
    {
        public const string XML_DOCUMENT_ELEMENT_NAME = "sipdialplans";
        public const string XML_ELEMENT_NAME = "sipdialplan";
        public const string DEFAULT_DIALPLAN_NAME = "default";      // The default name a dialplan will be assigned if the owner's first dialplan and the name is not set.
        public const int DEFAULT_MAXIMUM_EXECUTION_COUNT = 3;       // The default value for the maximum allowed simultaneous executions of a dial plan.
        public const string ALL_APPS_AUTHORISED = "*";              // Used in the priviled application authorisation field when the dialplan is authorised for all applications.
        public const string PROPERTY_EXECUTIONCOUNT_NAME = "ExecutionCount";

        public static int TimeZoneOffsetMinutes;

        public SIPDialPlanScriptTypesEnum ScriptType
        {
            get
            {
                return (ScriptTypeDescription != null) ? SIPDialPlanScriptTypes.GetSIPDialPlanScriptType(ScriptTypeDescription) : SIPDialPlanScriptTypesEnum.Unknown;
            }
        }

        public DateTime LastUpdateLocal
        {
            get
            {
                return (LastUpdate != null) ? TimeZoneHelper.ApplyOffset(LastUpdate, TimeZoneOffsetMinutes) : DateTime.MinValue;
            }
        }

        public DateTime InsertedLocal
        {
            get
            {
                return (Inserted != null) ? TimeZoneHelper.ApplyOffset(Inserted, TimeZoneOffsetMinutes) : DateTime.MinValue;
            }
        }

#if !SILVERLIGHT

        /// <summary>
        /// Safe string for AJAX client.
        /// </summary>
        [IgnoreDataMember]
        public string SafeDialPlanScript
        {
            get
            {
                if (DialPlanScript == null || DialPlanScript.Trim().Length == 0)
                {
                    return null;
                }
                else
                {
                    return Convert.ToBase64String(UTF8Encoding.UTF8.GetBytes(DialPlanScript));
                }
            }

            set
            {
                if (value == null || value.Trim().Length == 0)
                {
                    DialPlanScript = null;
                }
                else
                {
                    DialPlanScript = value;
                }
            }
        }

#endif

    }
}
