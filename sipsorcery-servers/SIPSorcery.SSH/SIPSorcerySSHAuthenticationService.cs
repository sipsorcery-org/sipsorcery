using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Text;
using SIPSorcery.CRM;
using NSsh.Server.Services;

namespace SIPSorcery.SSHServer
{
    public class SIPSorcerySSHAuthenticationService : IPasswordAuthenticationService
    {
        private CustomerSessionManager m_customerSessionManager;

        public SIPSorcerySSHAuthenticationService(CustomerSessionManager customerSessionManager)
        {
            m_customerSessionManager = customerSessionManager;
        }

        public IIdentity CreateIdentity(string username, string password)
        {
            CustomerSession customerSession = m_customerSessionManager.Authenticate(username, password, null);
            if (customerSession != null)
            {
                Customer customer = m_customerSessionManager.CustomerPersistor.Get(c => c.CustomerUsername == customerSession.CustomerUsername);
                return new SIPSorceryIdentity(customer);
            }
            else
            {
                return null;
            }
        }
    }
}
