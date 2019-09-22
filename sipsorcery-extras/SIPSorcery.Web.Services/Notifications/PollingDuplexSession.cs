using System;
using System.Collections.Generic;
using System.Text;

namespace SIPSorcery.Web.Services
{
    public class PollingDuplexSession
    {
        public string Address { get; set; }
        public string SessionId { get; set; }

        public PollingDuplexSession(string clientId, string sessionId)
        {
            this.Address = clientId;
            this.SessionId = sessionId;
        }
    }
}
