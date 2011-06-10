using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIPSorcery.Entities
{
    public partial class CDR
    {
        public static int TimeZoneOffsetMinutes;

        public DateTime CreatedLocal
        {
            get
            {
                return TimeZoneHelper.ApplyOffset(Created, TimeZoneOffsetMinutes);
            }
        }

        public DateTime AnsweredTimeLocal
        {
            get
            {
                return TimeZoneHelper.ApplyOffset(AnsweredTime, TimeZoneOffsetMinutes);
            }
        }

        public DateTime HungupTimeLocal
        {
            get
            {
                return TimeZoneHelper.ApplyOffset(HungupTime, TimeZoneOffsetMinutes);
            }
        }

#if !SILVERLIGHT

        /// <summary>
        /// Safe string for AJAX clients.
        /// </summary>
        public string SafeFromHeader
        {
            get { return System.Web.HttpUtility.HtmlEncode(FromHeader); }
            private set { }
        }

        /// <summary>
        /// Safe string for AJAX clients.
        /// </summary>
        public string SafeDestinationURI
        {
            get { return System.Web.HttpUtility.HtmlEncode(DstURI); }
            private set { }
        }

#endif

    }
}
