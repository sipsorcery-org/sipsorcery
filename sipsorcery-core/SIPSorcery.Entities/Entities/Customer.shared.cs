using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.Serialization;

namespace SIPSorcery.Entities
{
    [CustomValidation(typeof(SIPSorcery.Entities.CustomerValidator), "IsValid")]
    public partial class Customer
    {
        public const string TOPLEVEL_ADMIN_ID = "*";                    // If a customer record has their AdminId set to this value they are in charge!
        public const int DEFAULT_MAXIMUM_EXECUTION_COUNT = 5;           // The default value for the maximum allowed simultaneous executions of all the customer's dial plans.
        public const int FREE_MAXIMUM_EXECUTION_COUNT = 5;              // The maximum allowed simultaneous executions for a customer on the free service level.
        public const int API_KEY_LENGTH = 96;                           // 384 bits of entropy.

        /// <summary>
        /// Use explicit setter and accessor with private variable to keep the RIA services auto-generation logic happy.
        /// </summary>
        private string m_retypedPassword;
        [DataMember]
        public string RetypedPassword 
        { 
            get { return m_retypedPassword;}
            set { m_retypedPassword = value;}
        }

        private int m_timeZoneOffsetMinutes;
        [DataMember]
        public int TimeZoneOffsetMinutes 
        {
            get { return m_timeZoneOffsetMinutes; }
            set { m_timeZoneOffsetMinutes = value; }
        }

        [DataMember]
        public IEnumerable<string> Roles
        {
            get
            {
                return null;
            }
            set
            {
                
            }
        }
    }
}
