using System;
using System.Collections.Generic;

#nullable disable

namespace SIPAspNetServer.DataAccess
{
    public partial class CDR
    {
        public CDR()
        {
            SIPDialogs = new HashSet<SIPDialog>();
        }

        public Guid ID { get; set; }
        public DateTimeOffset Inserted { get; set; }
        public string Direction { get; set; }
        public DateTimeOffset created { get; set; }
        public string DstUser { get; set; }
        public string DstHost { get; set; }
        public string DstUri { get; set; }
        public string FromUser { get; set; }
        public string FromName { get; set; }
        public string FromHeader { get; set; }
        public string CallID { get; set; }
        public string LocalSocket { get; set; }
        public string RemoteSocket { get; set; }
        public Guid? BridgeID { get; set; }
        public DateTimeOffset? InProgressAt { get; set; }
        public int? InProgressStatus { get; set; }
        public string InProgressReason { get; set; }
        public int? RingDuration { get; set; }
        public DateTimeOffset? AnsweredAt { get; set; }
        public int? AnsweredStatus { get; set; }
        public string AnsweredReason { get; set; }
        public int? Duration { get; set; }
        public DateTimeOffset? HungupAt { get; set; }
        public string HungupReason { get; set; }

        public virtual ICollection<SIPDialog> SIPDialogs { get; set; }
    }
}
