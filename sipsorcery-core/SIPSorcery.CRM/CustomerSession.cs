using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIPSorcery.CRM
{
    public class CustomerSession
    {
        public Guid CustomerId;
        public Guid SessionId;
        public DateTime Created;

        public CustomerSession(Guid customerId, Guid sessionId)
        {
            CustomerId = customerId;
            SessionId = sessionId;
        }
    }
}
