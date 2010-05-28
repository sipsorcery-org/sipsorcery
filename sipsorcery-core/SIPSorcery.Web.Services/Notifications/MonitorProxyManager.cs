using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Configuration;
using System.Threading;
using System.Web;
using System.Web.Configuration;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Web.Services
{
    /// <summary>
    /// Manages one or more WCF client connections to SIP monitor event publisher servers.
    /// </summary>
    [CallbackBehavior(ConcurrencyMode = ConcurrencyMode.Multiple, UseSynchronizationContext = false)]
    //public class MonitorProxyManager : ISIPMonitorPublisher, ISIPMonitorNotificationReady
    public class MonitorProxyManager : ISIPMonitorPublisher
    {
        private const string CLIENT_CONTRACT = "SIPSorcery.SIP.App.ISIPMonitorPublisher";
        private const int RETRY_FAILED_PROXY = 5000;
        private const int PROXY_TIMEOUT_RETRY_INTERVAL = 60;    // If a proxy times out then this interval will be used to determine when to attempt to re-use it.
        private const int PROXY_TIMEOUT_COUNT_LIMIT = 3;        // After a proxy has this many timeouts in a row it will be classified as failed and not used until a retry interval expires.

        private ILog logger = AppState.logger;

        private static TimeSpan m_proxyOperationTimeout = new TimeSpan(0, 0, 3);
        private Dictionary<string, SIPMonitorPublisherProxy> m_proxies = new Dictionary<string, SIPMonitorPublisherProxy>();
        private Dictionary<string, DateTime?> m_proxyLastTimeout = new Dictionary<string, DateTime?>();     // Keeps track of when a proxy was considered down.
        private Dictionary<string, int> m_proxyTimeoutCount = new Dictionary<string, int>();                // Keeps count of proxy timeouts.
        //private Dictionary<string, string> m_sessionIDMap = new Dictionary<string, string>();                                   // Maps a single session ID to the multiple session IDs for each proxy.
        //private Dictionary<string, Action<string>> m_clientsWaiting = new Dictionary<string, Action<string>>();                 // <Address> List of address endpoints that are waiting for a notification.

        public event Action<string> NotificationReady;          // Not used across WCF channels, for in memory only.
        public event Func<SIPMonitorEvent, bool> MonitorEventReady;// Not used.

        public MonitorProxyManager()
        {
            InitialiseProxies();
        }

        /// <summary>
        /// Interrogates the app.config file to get a list of WCF client end points that implement the ISIPMonitorPublisher contract.
        /// This is to allow this class to connect to multiple notification servers if needed.
        /// </summary>
        private List<string> GetClientEndPointNames()
        {
            List<string> clientEndPointNames = new List<string>();

            // This service will only ever be hosted within IIS. Local installs do not need the notifications service endpoint as they will communicate 
            // directly with the monitor event publisher in memory.
            ServiceModelSectionGroup serviceModelSectionGroup = ServiceModelSectionGroup.GetSectionGroup(WebConfigurationManager.OpenWebConfiguration("~"));
            foreach (ChannelEndpointElement client in serviceModelSectionGroup.Client.Endpoints)
            {
                if (client.Contract == CLIENT_CONTRACT && !clientEndPointNames.Contains(client.Name))
                {
                    logger.Debug("MonitorProxyManager found client endpoint for ISIPMonitorPublisher, name=" + client.Name + ".");
                    clientEndPointNames.Add(client.Name);
                }
            }

            return clientEndPointNames;
        }

        private void InitialiseProxies()
        {
            List<string> proxyNames = GetClientEndPointNames();

            foreach (string proxyName in proxyNames)
            {
                try
                {
                    SIPMonitorPublisherProxy proxy = new SIPMonitorPublisherProxy(proxyName);
                    proxy.InnerChannel.OperationTimeout = m_proxyOperationTimeout;
                    m_proxies.Add(proxyName, proxy);
                    m_proxyLastTimeout.Add(proxyName, null);
                    m_proxyTimeoutCount.Add(proxyName, 0);
                    logger.Debug("Proxy added for " + proxyName + " on " + proxy.Endpoint.Address.ToString() + ".");
                    //CreateProxy(proxyName);
                }
                catch (Exception excp)
                {
                    logger.Error("Exception InitialiseProxies. " + excp.Message);
                }
            }
        }

        private void CreateProxy(string proxyName)
        {
            try
            {
                logger.Debug("Attempting to create new proxy created for " + proxyName + ".");

                //InstanceContext callbackContext = new InstanceContext(this);
                SIPMonitorPublisherProxy proxy = new SIPMonitorPublisherProxy(proxyName);
                m_proxies.Add(proxyName, proxy);
                proxy.IsAlive();
                logger.Debug("Successfully connected to proxy " + proxyName + ".");

                /*ThreadPool.QueueUserWorkItem(delegate
                {
                    string name = proxyName;

                    try
                    {
                        proxy.IsAlive();
                       // ((ICommunicationObject)proxy).Faulted += ProxyChannelFaulted;
                        m_proxies.Add(name, proxy);
                        logger.Debug("Successfully connected to proxy " + name + ".");
                    }
                    catch (Exception excp)
                    {
                        logger.Warn("Could not connect to proxy " + name + ". " + excp.Message);
                        Timer retryProxy = new Timer(delegate { CreateProxy(name); }, null, RETRY_FAILED_PROXY, Timeout.Infinite);
                    }
                });*/
            }
            catch (Exception excp)
            {
                logger.Error("Exception CreateProxy (" + proxyName + "). " + excp.Message);
            }
        }

        /// <summary>
        /// This method is an event handler for communication fualts on a proxy channel. When a fault occurs ALL
        /// the available proxies will be checked for a fault and those in a faulted state will be closed and replaced.
        /// </summary>
        /// <remarks>This occurs when the channel to the SIP monitoring server that is publishing the events
        /// is faulted. This can occur if the SIP monitoring server is shutdown which will close the socket.</remarks>
        private void ProxyChannelFaulted(object sender, EventArgs e)
        {
            for (int index = 0; index < m_proxies.Count; index++)
            {
                KeyValuePair<string, SIPMonitorPublisherProxy> proxyEntry = m_proxies.ElementAt(index);
                SIPMonitorPublisherProxy proxy = proxyEntry.Value;

                if (proxy.State == CommunicationState.Faulted)
                {
                    logger.Debug("Removing faulted proxy for " + proxyEntry.Key + ".");
                    m_proxies.Remove(proxyEntry.Key);
                    CreateProxy(proxyEntry.Key);
                    index--;

                    logger.Warn("MonitorProxyManager received a fault on proxy channel, comms state=" + proxy.InnerChannel.State + ".");

                    try
                    {
                        proxy.Abort();
                    }
                    catch (Exception abortExcp)
                    {
                        logger.Error("Exception ProxyChannelFaulted Abort. " + abortExcp.Message);
                    }
                }
            }
        }

        public bool IsAlive()
        {
            return true;
        }

        public string Subscribe(string customerUsername, string adminId, string address, string sessionID, string subject, string filter, int expiry, string udpSocket, out string subscribeError)
        {
            subscribeError = null;

            if (m_proxies.Count == 0)
            {
                logger.Debug("Subscribe had no available proxies.");
                return null;
            }
            else
            {
                bool subscribeSuccess = false;

                for (int index = 0; index < m_proxies.Count; index++)
                {
                    KeyValuePair<string, SIPMonitorPublisherProxy> proxyEntry = m_proxies.ElementAt(index);

                    if (IsProxyAvailable(proxyEntry.Key))
                    {
                        try
                        {
                            logger.Debug("Attempting to subscribe to proxy " + proxyEntry.Key + ".");
                            string proxySubscribeError = null;
                            string proxySessionID = proxyEntry.Value.Subscribe(customerUsername, adminId, address, sessionID, subject, filter, expiry, udpSocket, out proxySubscribeError);
                            m_proxyTimeoutCount[proxyEntry.Key] = 0;

                            if (proxySubscribeError != null)
                            {
                                logger.Warn("Subscription in MonitorProxyManager failed. " + proxySubscribeError);
                                subscribeError = proxySubscribeError;
                                subscribeSuccess = false;
                                break;
                            }
                            else
                            {
                                logger.Debug("Subscription created for proxy " + proxyEntry.Key + ", proxy sessionID=" + proxySessionID + ", client sessionID=" + sessionID + ".");
                                //m_sessionIDMap.Add(proxySessionID, sessionID);
                                subscribeSuccess = true;
                            }
                        }
                        catch (TimeoutException)
                        {
                            logger.Warn("Proxy " + proxyEntry.Key + " timed out for Subscribe."); 
                            m_proxyTimeoutCount[proxyEntry.Key] = m_proxyTimeoutCount[proxyEntry.Key] + 1;
                        }
                        catch (System.ServiceModel.CommunicationException commExcp)
                        {
                            logger.Warn("CommunicationException MonitorProxyManager Proxy Subscribe for " + proxyEntry.Key + ". " + commExcp.Message);
                        }
                        catch (Exception excp)
                        {
                            logger.Error("Exception MonitorProxyManager Proxy Subscribe for " + proxyEntry.Key + ". " + excp.Message);
                        }
                    }
                }

                if (subscribeSuccess)
                {
                    return sessionID;
                }
                else
                {
                    return null;
                }
            }
        }

        public List<string> GetNotifications(string address, out string sessionID, out string sessionError)
        {
            sessionID = null;
            sessionError = null;

            try
            {
                if (m_proxies.Count == 0)
                {
                    //logger.Debug("GetNotifications had no available proxies.");
                    return null;
                }
                else
                {
                    List<string> collatedNotifications = new List<string>();

                    for (int index = 0; index < m_proxies.Count; index++)
                    {
                        KeyValuePair<string, SIPMonitorPublisherProxy> proxyEntry = m_proxies.ElementAt(index);

                        if (IsProxyAvailable(proxyEntry.Key))
                        {
                            try
                            {
                                //logger.Debug("Retrieving notifications for proxy " + proxyEntry.Key + ".");

                                List<string> notifications = proxyEntry.Value.GetNotifications(address, out sessionID, out sessionError);
                                m_proxyTimeoutCount[proxyEntry.Key] = 0;
                                if (notifications != null && notifications.Count > 0 && sessionError == null)
                                {
                                    //logger.Debug("Proxy " + proxyEntry.Key + " returned " + notifications.Count + ".");
                                    // if (m_sessionIDMap.ContainsKey(proxySessionID))
                                    //{
                                    //sessionID = m_sessionIDMap[proxySessionID];
                                    //sessionError = null;
                                    collatedNotifications.AddRange(notifications);
                                    break;
                                    //}
                                    //else
                                    //{
                                    //    logger.Warn("Notifications received for unknown sessionID, closing session on proxy.");
                                    //    proxyEntry.Value.CloseSession(address, sessionID);
                                    // Try the same proxy again.
                                    //    index--;
                                    //}
                                }
                            }
                            catch (TimeoutException)
                            {
                                logger.Warn("Proxy " + proxyEntry.Key + " timed out for GetNotifications.");
                                m_proxyTimeoutCount[proxyEntry.Key] = m_proxyTimeoutCount[proxyEntry.Key] + 1;
                            }
                            catch (System.ServiceModel.CommunicationException commExcp)
                            {
                                logger.Warn("CommunicationException MonitorProxyManager GetNotifications. " + commExcp.Message);
                                //sessionID = null;
                                //sessionError = "Communications error";
                                //return null;
                            }
                            catch (Exception excp)
                            {
                                logger.Error("Exception MonitorProxyManager GetNotifications (" + excp.GetType() + "). " + excp.Message);
                            }
                        }
                    }

                    //sessionError = null;
                    //sessionID = null;
                    return collatedNotifications;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception MonitorProxyManager.GetNotifications. " + excp.Message);
                throw;
            }
        }

        public string ExtendSession(string address, string sessionID, int expiry)
        {
            try
            {
                //var proxySessions = (from session in m_sessionIDMap where session.Value == sessionID select session.Key).ToList();

                //if (proxySessions != null && proxySessions.Count() > 0)
                //{
                for (int index = 0; index < m_proxies.Count; index++)
                {
                    KeyValuePair<string, SIPMonitorPublisherProxy> proxyEntry = m_proxies.ElementAt(index);

                    if (IsProxyAvailable(proxyEntry.Key))
                    {
                        //foreach (string proxySession in proxySessions)
                        //{
                        try
                        {
                            logger.Debug("Extending session for address " + address + ", session " + sessionID + ", proxy " + proxyEntry.Key + ".");
                            string proxyExtensionResult = proxyEntry.Value.ExtendSession(address, sessionID, expiry);
                            m_proxyTimeoutCount[proxyEntry.Key] = 0;

                            if (proxyExtensionResult != null)
                            {
                                return proxyExtensionResult;
                            }
                        }
                        catch (TimeoutException)
                        {
                            logger.Warn("Proxy " + proxyEntry.Key + " timed out for ExtendSession.");
                            m_proxyTimeoutCount[proxyEntry.Key] = m_proxyTimeoutCount[proxyEntry.Key] + 1;
                        }
                        catch (System.ServiceModel.CommunicationException commExcp)
                        {
                            logger.Warn("CommunicationException MonitorProxyManager ExtendSession. " + commExcp.Message);
                        }
                        catch (Exception excp)
                        {
                            logger.Error("Exception MonitorProxyManager ExtendSession (" + excp.GetType() + "). " + excp.Message);
                        }
                    }
                }

                return null;
                //}
                //}
            }
            catch (Exception excp)
            {
                logger.Error("Exception ExtendSession. " + excp.Message);
                return excp.Message;
            }
        }

        public void CloseSession(string address, string sessionID)
        {
            try
            {
                if (m_proxies.Count == 0)
                {
                    logger.Debug("CloseConnection had no available proxies.");
                }
                else
                {
                    // var proxySessions = (from session in m_sessionIDMap where session.Value == sessionID select session.Key).ToList();

                    // if (proxySessions != null && proxySessions.Count() > 0)
                    //{
                    for (int index = 0; index < m_proxies.Count; index++)
                    {
                        KeyValuePair<string, SIPMonitorPublisherProxy> proxyEntry = m_proxies.ElementAt(index);

                        if (IsProxyAvailable(proxyEntry.Key))
                        {
                            // foreach (string proxySession in proxySessions)
                            //{
                            try
                            {
                                logger.Debug("Closing session for address " + address + ", session " + sessionID + ", proxy " + proxyEntry.Key + ".");
                                proxyEntry.Value.CloseSession(address, sessionID);
                                m_proxyTimeoutCount[proxyEntry.Key] = 0;
                            }
                            catch (TimeoutException)
                            {
                                logger.Warn("Proxy " + proxyEntry.Key + " timed out for CloseSession.");
                                m_proxyTimeoutCount[proxyEntry.Key] = m_proxyTimeoutCount[proxyEntry.Key] + 1;
                            }
                            catch (System.ServiceModel.CommunicationException commExcp)
                            {
                                logger.Warn("CommunicationException MonitorProxyManager CloseConnection. " + commExcp.Message);
                            }
                            catch (Exception excp)
                            {
                                logger.Error("Exception MonitorProxyManager CloseConnection (" + excp.GetType() + "). " + excp.Message);
                            }
                            //}
                        }
                        //}

                        //foreach (string proxySession in proxySessions)
                        //{
                        //   m_sessionIDMap.Remove(proxySession);
                        //}
                        // }
                        //else
                        //{
                        //    logger.Warn("No proxy session IDs were found in the session map for " + sessionID + ".");
                        //}
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception CloseSession. " + excp.Message);
            }
        }

        public void CloseConnection(string address)
        {
            //if (m_clientsWaiting.ContainsKey(address))
            //{
            //    m_clientsWaiting.Remove(address);
            //}

            if (m_proxies.Count == 0)
            {
                logger.Debug("CloseConnection had no available proxies.");
            }
            else
            {
                for (int index = 0; index < m_proxies.Count; index++)
                {
                    KeyValuePair<string, SIPMonitorPublisherProxy> proxyEntry = m_proxies.ElementAt(index);

                    if (IsProxyAvailable(proxyEntry.Key))
                    {
                        try
                        {
                            logger.Debug("Closing connection for address " + address + " on proxy " + proxyEntry.Key + ".");
                            proxyEntry.Value.CloseConnection(address);
                            m_proxyTimeoutCount[proxyEntry.Key] = 0;
                        }
                        catch (TimeoutException)
                        {
                            logger.Warn("Proxy " + proxyEntry.Key + " timed out for CloseConnection.");
                            m_proxyTimeoutCount[proxyEntry.Key] = m_proxyTimeoutCount[proxyEntry.Key] + 1;
                        }
                        catch (System.ServiceModel.CommunicationException commExcp)
                        {
                            logger.Warn("CommunicationException MonitorProxyManager CloseConnection. " + commExcp.Message);
                        }
                        catch (Exception excp)
                        {
                            logger.Error("Exception MonitorProxyManager CloseConnection (" + excp.GetType() + "). " + excp.Message);
                        }
                    }
                }
            }
        }

        public bool IsNotificationReady(string address)
        {
            throw new NotImplementedException();
        }

        // public void RegisterListener(string address)
        //{
        //    throw new NotImplementedException();
        // }

        /*public void RegisterListener(string address, Action<string> notificationsReady)
        {
             if (m_proxies.Count == 0)
            {
                logger.Debug("RegisterListener had no available proxies.");
            }
            else
            {
                if (!m_clientsWaiting.ContainsKey(address))
                {
                    m_clientsWaiting.Add(address, notificationsReady);
                }
                else
                {
                    m_clientsWaiting[address] = notificationsReady;
                }

                for (int index = 0; index < m_proxies.Count; index++)
                {
                    KeyValuePair<string, SIPMonitorPublisherProxy> proxyEntry = m_proxies.ElementAt(index);

                    if (proxyEntry.Value.State != CommunicationState.Faulted)
                    {
                        try
                        {
                            proxyEntry.Value.RegisterListener(address);
                        }
                        catch (System.ServiceModel.CommunicationException commExcp)
                        {
                            logger.Warn("CommunicationException MonitorProxyManager Proxy RegisterListener for " + proxyEntry.Key + ". " + commExcp.Message);
                        }
                        catch (Exception excp)
                        {
                            logger.Error("Exception MonitorProxyManager Proxy RegisterListener for " + proxyEntry.Key + ". " + excp.Message);
                        }
                    }
                }
            }
        }*/

        private bool IsProxyAvailable(string proxyName)
        {
            try
            {
                if (m_proxyTimeoutCount[proxyName] >= PROXY_TIMEOUT_COUNT_LIMIT)
                {
                    logger.Debug("Proxy " + proxyName + " has reached the timeout limit and is being excluded for " + PROXY_TIMEOUT_RETRY_INTERVAL + "s.");
                    m_proxyLastTimeout[proxyName] = DateTime.Now;
                    m_proxyTimeoutCount[proxyName] = 0;
                    return false;
                }
                else if (m_proxyLastTimeout[proxyName] == null)
                {
                    return true;
                }
                else if (DateTime.Now.Subtract(m_proxyLastTimeout[proxyName].Value).TotalSeconds > PROXY_TIMEOUT_RETRY_INTERVAL)
                {
                    logger.Debug("Proxy " + proxyName + " is being used again after timeout retry interval has been reached.");
                    m_proxyLastTimeout[proxyName] = null;
                    m_proxyTimeoutCount[proxyName] = 0;
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception IsProxyAvailable. " + excp.Message);
                return true;
            }
        }

        public void MonitorEventReceived(SIPMonitorEvent monitorEvent)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Callback method that will be used by the publisher to let this service know a notification
        /// is ready for a particular client connection.
        /// </summary>
        /// <param name="address">The address of the client whose notification is ready.</param>
        /*public void NotificationReady(string address)
        {
            try
            {
                //logger.Debug("SIPNotifierService NotificationReady for " + address + ".");

                if (m_clientsWaiting.ContainsKey(address))
                {
                    m_clientsWaiting[address](address);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception NotificationReady. " + excp.Message);
            }
        }*/
    }
}