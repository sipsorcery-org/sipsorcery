using System;
using System.Collections.Generic;

#nullable disable

namespace SIPAspNetServer.DataAccess
{
    public partial class SIPCall
    {
        public Guid ID { get; set; }
        public Guid? CDRID { get; set; }
        public string LocalTag { get; set; }
        public string RemoteTag { get; set; }
        public string CallID { get; set; }
        public int CSeq { get; set; }
        public Guid BridgeID { get; set; }
        public string RemoteTarget { get; set; }
        public string LocalUserField { get; set; }
        public string RemoteUserField { get; set; }
        public string ProxySIPSocket { get; set; }
        public string RouteSet { get; set; }
        public int? CallDurationLimit { get; set; }
        public string Direction { get; set; }
        public DateTimeOffset Inserted { get; set; }

        public virtual CDR CDR { get; set; }
    }
}
