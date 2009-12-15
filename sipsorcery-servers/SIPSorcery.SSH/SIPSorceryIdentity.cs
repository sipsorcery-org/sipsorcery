using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Text;
using SIPSorcery.CRM;

namespace SIPSorcery.SSHServer
{
    public class SIPSorceryIdentity : IIdentity
    {
        public string AuthenticationType
        {
            get { throw new NotImplementedException(); }
        }

        public bool IsAuthenticated
        {
            get { return true; }
        }

        public string Name { get; private set; }
        public Customer Customer { get; private set; }

        public SIPSorceryIdentity(Customer customer)
        {
            Name = customer.CustomerUsername;
            Customer = customer;
        }
    }
}
