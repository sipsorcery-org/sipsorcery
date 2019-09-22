using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIPSorcery.Entities
{
    public partial class SIPRegistrarBinding
    {
        public const int MAX_BINDING_LIFETIME = 3600;       // Bindings are currently not being expired once the expires time is reached and this is the maximum amount of time 
        // a binding can stay valid for with probing before it is removed and the binding must be freshed with a REGISTER.

        public static int TimeZoneOffsetMinutes;

        public DateTime LastUpdateLocal
        {
            get
            {
                return (LastUpdate != null) ? TimeZoneHelper.ApplyOffset(LastUpdate, TimeZoneOffsetMinutes) : DateTime.MinValue;
            }
        }
    }
}
