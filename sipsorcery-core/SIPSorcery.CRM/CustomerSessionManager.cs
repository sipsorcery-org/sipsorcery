using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.CRM
{
    public delegate CustomerSession AuthenticateCustomerDelegate(string username, string password);
    public delegate CustomerSession AuthenticateTokenDelegate(string token);
    public delegate void ExpireTokenDelegate(string token);

    public class CustomerSessionManager {

        private static ILog logger = AppState.logger;

        private SIPAssetPersistor<Customer> m_customerPersistor;
        private Dictionary<Guid, CustomerSession> m_customerSessions = new Dictionary<Guid, CustomerSession>();

        public CustomerSessionManager(SIPAssetPersistor<Customer> customerPersistor) {
            m_customerPersistor = customerPersistor;
        }

        public CustomerSession Authenticate(string username, string password) {
            try {
                Customer customer = m_customerPersistor.Get(c => c.CustomerUsername == username && c.CustomerPassword == password);

                if (customer != null) {
                    logger.Debug("Login successful for " + username + ".");

                    Guid sessionId = Guid.NewGuid();
                    CustomerSession customerSession = new CustomerSession(new Guid(customer.Id), sessionId);

                    lock (m_customerSessions) {
                        m_customerSessions.Add(sessionId, customerSession);
                    }

                    return customerSession;
                }
                else {
                    logger.Debug("Login failed for " + username + ".");
                    return null;
                }
            }
            catch (Exception excp) {
                logger.Error("Exception Authenticate CustomerSessionManager. " + excp.Message);
                throw;
            }
        }

        public CustomerSession Authenticate(string sessionId) {
            try {
                if (m_customerSessions.ContainsKey(new Guid(sessionId))) {
                    //logger.Debug("Authentication token valid for " + sessionId + ".");
                    return m_customerSessions[new Guid(sessionId)];
                }
                else {
                    logger.Warn("Authentication token invalid for " + sessionId + ".");
                    return null;
                }
            }
            catch (Exception excp) {
                logger.Error("Exception Authenticate CustomerSessionManager. " + excp.Message);
                throw;
            }
        }

        public void ExpireToken(string sessionId) {

            try {
                if (m_customerSessions.ContainsKey(new Guid(sessionId))) {
                    lock (m_customerSessions) {
                        m_customerSessions.Remove(new Guid(sessionId));
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception ExpireToken CustomerSessionManager. " + excp.Message);
                throw;
            }
        }
    }
}
