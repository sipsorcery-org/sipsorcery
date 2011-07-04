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
                return (Created != null) ? TimeZoneHelper.ApplyOffset(Created, TimeZoneOffsetMinutes) : DateTime.MinValue;
            }
        }

        public DateTime AnsweredTimeLocal
        {
            get
            {
                return (AnsweredTime != null) ? TimeZoneHelper.ApplyOffset(AnsweredTime, TimeZoneOffsetMinutes) : DateTime.MinValue;
            }
        }

        public DateTime HungupTimeLocal
        {
            get
            {
                return (HungupTime != null) ? TimeZoneHelper.ApplyOffset(HungupTime, TimeZoneOffsetMinutes) : DateTime.MinValue;
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
