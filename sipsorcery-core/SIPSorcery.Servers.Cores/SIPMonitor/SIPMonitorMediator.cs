// ============================================================================
// FileName: SIPMonitorMediator.cs
//
// Description:
// Hosts client connections to the public sockets on the SIP Monitor Server and then mediates
// the events sent to each and commands received from those clients capable of sending commands.
//
// Author(s):
// Aaron Clauson
//
// History:
// 01 May 2006	Aaron Clauson	Created.
// 14 Nov 2008  Aaron Clauson   Renamed from ProxyMonitor to SIPMonitorMediator.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.CRM;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{	   
    /// <summary>
    /// Hosts client connections to the public sockets on the SIP Monitor Server and then mediates
    /// the events sent to each and commands received from those clients capable of sending commands.
    /// </summary>
	public class SIPMonitorMediator
	{
		private const int MAX_THREAD_WAITTIME = 1;		// 1 second.
		private const int EVENTLOG_QUEUE_THRESHHOLD = 100;
		private const int SIGNALLOG_QUEUE_THRESHHOLD = 100;
		private const int MAX_USERAGENT_LENGTH = 256;
        private const int MAX_CONTROL_CLIENT_CONNECTION_COUNT = 50;     // Maximum number of simultaneous administrative control connections that will be accepted.
        private const int MAX_MACHINE_CONNECTION_COUNT = 50;            // Maximum number of simultaneous machine connections that will be accepted.
        private const int MAX_LOGIN_ATTEMPTS = 3;
        private const int MAX_LOGFILE_SIZE = 1000000;   // Maximum log file size, approx. 1MB. Once that size oldest messages will be truncated.

		private static ILog logger = AppState.logger;

        private static readonly string m_topLevelAdminId = Customer.TOPLEVEL_ADMIN_ID;
        private static readonly int m_sessionIDLength = CustomerSessionManager.SESSION_ID_STRING_LENGTH;

        private AuthenticateCustomerDelegate AuthenticateUser_External = (u, p, i) => { return null; };
        private AuthenticateTokenDelegate AuthenticateSession_External = (s) => { return null; };
        private SIPAssetGetDelegate<Customer> GetCustomer_External;

        private string m_fileLogDirectory;
        private bool m_listenForEvents = true;
        private bool m_listenForClients = true;

        private int m_eventListenerPort = 0;            // The loopback port that the monitor process will listen on for events from SIP Server agents.
        private UdpClient m_udpEventListener;           // The UDP listener that this process will listen for events from SIP Server agents on.
        private IPEndPoint[] m_controlClientEndPoints;  // Connections to these sockets are processed as human users creating tracing and control sessions.
        private IPEndPoint[] m_machineEndPoints;         // Connections to these sockets are processed as machine users receiving one way event notifications.
        private static List<SIPMonitorControlClient> m_controlClients = new List<SIPMonitorControlClient>();    // List of connected human control clients.
        private static List<SIPMonitorMachineClient> m_machineClients = new List<SIPMonitorMachineClient>();    // List of connected machine clients.

        public SIPMonitorMediator(
           IPEndPoint[] controlClientEndPoints,
           int eventListenerPort,
           string fileLogDirectory)
        {
            m_controlClientEndPoints = controlClientEndPoints;
            m_eventListenerPort = eventListenerPort;
            m_fileLogDirectory = fileLogDirectory;
        }

        public SIPMonitorMediator(
            IPEndPoint[] controlClientEndPoints,
            IPEndPoint[] machineEndPoints, 
            int eventListenerPort, 
            string fileLogDirectory,
            AuthenticateCustomerDelegate authenticateUser,
            AuthenticateTokenDelegate authenticateSession,
            SIPAssetGetDelegate<Customer> getCustomer)
		{
            m_controlClientEndPoints = controlClientEndPoints;
            m_machineEndPoints = machineEndPoints;
            m_eventListenerPort = eventListenerPort;
            m_fileLogDirectory = fileLogDirectory;
            AuthenticateUser_External = authenticateUser ?? AuthenticateUser_External;
            AuthenticateSession_External = authenticateSession ?? AuthenticateSession_External;
            GetCustomer_External = getCustomer;
		}

        public void StartMonitoring()
        {
            Thread monitorThread = new Thread(new ThreadStart(StartEventProcessing));
            monitorThread.Start();
        }

        private void StartEventProcessing()
		{
			try
			{
                // This socket is to listen for the events from SIP Servers.
                m_udpEventListener = new UdpClient(m_eventListenerPort, AddressFamily.InterNetwork);
				
                // Start listening for connections from human control clients.
                foreach (IPEndPoint controlClientEndPoint in m_controlClientEndPoints)
                {
                    ThreadPool.QueueUserWorkItem(new WaitCallback(AcceptControlClients), controlClientEndPoint);
                }

                // Start listening for connections from machine clients.
                if (m_machineEndPoints != null)
                {
                    foreach (IPEndPoint machineClientEndPoint in m_machineEndPoints)
                    {
                        ThreadPool.QueueUserWorkItem(new WaitCallback(AcceptMachineClients), machineClientEndPoint);
                    }
                }

                // Wait for events from the SIP Server agents and when received pass them onto the client connections for processing and dispatching.
                IPEndPoint remoteEP = null;
                while (m_listenForEvents)
                {
                    byte[] buffer = m_udpEventListener.Receive(ref remoteEP);

                    if (buffer != null && buffer.Length > 0 && (m_controlClients.Count > 0 || m_machineClients.Count > 0))
                    {
                        SIPMonitorEvent sipMonitorEvent = SIPMonitorEvent.ParseEventCSV(Encoding.ASCII.GetString(buffer, 0, buffer.Length));
                        
                        if (sipMonitorEvent != null)
                        {
                            if (sipMonitorEvent.ClientType == SIPMonitorClientTypesEnum.ControlClient)
                            {
                                if (m_controlClients.Count > 0)
                                {
                                    ProcessClientControlEvent((SIPMonitorControlClientEvent)sipMonitorEvent);
                                }
                            }
                            else if (sipMonitorEvent.ClientType == SIPMonitorClientTypesEnum.Machine)
                            {
                                if (m_machineClients.Count > 0)
                                {
                                    ProcessMachineEvent((SIPMonitorMachineEvent)sipMonitorEvent);
                                }
                            }
                        }
                    }
                }

                m_udpEventListener.Close();
			}
			catch(Exception excp)
			{
                logger.Error("Exception SIPMonitorMediator StartEventProcessing. " + excp.Message);
			}
		}
		
		public void AcceptControlClients(object state)
		{
            try
            {
                IPEndPoint monitorEndPoint = (IPEndPoint)state;
                TcpListener monitorServer = new TcpListener(monitorEndPoint);
                //m_monitorServers.Add(monitorServer);
                monitorServer.Start();

                logger.Debug("ProxyMonitor listening for clients on " + monitorEndPoint.ToString() + ".");

                while (m_listenForClients)
                {
                    Socket clientSocket = monitorServer.AcceptSocket();
                    IPEndPoint clientEndPoint = (IPEndPoint)clientSocket.RemoteEndPoint;

                    logger.Debug("SIPMonitorMediator new control client connection from " + clientEndPoint + ".");

                    ThreadPool.QueueUserWorkItem(new WaitCallback(StartControlClient), clientSocket);
                }

                //monitorServer.Stop();
            }
            catch (Exception excp)
            {
                logger.Error("Exception ProxyMonitor AcceptClients. " + excp.Message);
            }
		}

        private void AcceptMachineClients(object state)
        {
            try
            {
                IPEndPoint listenEndPoint = (IPEndPoint)state;
                TcpListener monitorServer = new TcpListener(listenEndPoint);
                monitorServer.Start();

                logger.Debug("SIPMonitorMediator listening for machine connections on " + listenEndPoint + ".");

                while (m_listenForClients)
                {
                    Socket machineClientSocket = monitorServer.AcceptSocket();

                    logger.Debug("SIPMonitorMediator new machine connection from " + machineClientSocket.RemoteEndPoint + ".");

                    ThreadPool.QueueUserWorkItem(delegate { StartMachineClient(machineClientSocket); });
                }

                //monitorServer.Stop();   
            }
            catch (Exception excp)
            {
                logger.Error("Exception AcceptMachineClients. " + excp.Message);
            }
        }

        /// <summary>
        /// Adds a new log client to the list. Can only be used for log file and email clients and since this call can only come
        /// from the owning process no authentication is necessary.
        /// </summary>
        public static void AddLogClient(SIPMonitorControlClient controlClient)
        {
            lock (m_controlClients)
            {
                m_controlClients.Add(controlClient);
            }
        }

        public static void RemoveLogClient(SIPMonitorControlClient controlClient)
        {
            lock (m_controlClients)
            {
                m_controlClients.Remove(controlClient);
            }
        }
		
		private void ProcessClientControlEvent(SIPMonitorEvent monitorEvent)
		{
            try
            {
                List<SIPMonitorControlClient> removals = new List<SIPMonitorControlClient>();
                
                lock (m_controlClients)
                {
                    if (m_controlClients.Count > 0)
                    {
                        foreach (SIPMonitorControlClient client in m_controlClients)
                        {
                            if (client.Remove)
                            {
                                if (!removals.Contains(client))
                                {
                                    removals.Add(client);
                                }

                                continue;
                            }
                            
                            //logger.Debug("event=" + proxyEvent.EventType.ToString() + ", username= " + proxyEvent.Username);

                            SIPMonitorFilter filter = client.Filter;
                            if (filter != null && filter.ShowSIPMonitorEvent(monitorEvent))
                            {
                                string socketEventMessage = monitorEvent.EventType.ToString() + " " + monitorEvent.Created.ToString("HH:mm:ss:fff");

                                // Special case for dialplan events and super user. Add the username of the event to the start of the monitor message.
                                if (filter.Username == SIPMonitorFilter.WILDCARD && monitorEvent.Username != null)
                                {
                                    socketEventMessage += " " + monitorEvent.Username;
                                }

                                if (monitorEvent.EventType == SIPMonitorEventTypesEnum.FullSIPTrace)
                                {
                                    socketEventMessage += ":\r\n" + monitorEvent.Message + "\r\n";
                                }
                                else
                                {
                                    socketEventMessage += ": " + monitorEvent.Message + "\r\n";
                                }

                                byte[] socketEventBytes = Encoding.ASCII.GetBytes(socketEventMessage);

                                if (client.ClientSocket != null)
                                {
                                    client.ClientSocket.Send(socketEventBytes);
                                }
                                else if (client.FileStream != null)
                                {
                                    // Check the duration of the filelog has not been exceeded.
                                    if (DateTime.Now.Subtract(client.Created).TotalMinutes > client.LogDurationMinutes)
                                    {
                                        logger.Debug("Closing file log " + client.Filename + ".");

                                        if (client.FileStream != null)
                                        {
                                            client.FileStream.Close();
                                        }

                                        client.Remove = true;
                                    }
                                    else
                                    {
                                        if(client.FileStream.Length > MAX_LOGFILE_SIZE)
                                        {
                                            client.FileStream.SetLength(MAX_LOGFILE_SIZE);  // Truncate the file, oldest messages will be lost.
                                        }

                                        client.FileStream.Write(socketEventBytes, 0, socketEventBytes.Length);
                                        client.FileStream.Flush();
                                    }
                                }
                                else
                                {
                                    client.Remove = true;
                                }
                            }
                        }
                    }
                }

                if (removals.Count > 0)
                {
                    lock (m_controlClients)
                    {
                        foreach (SIPMonitorControlClient removal in removals)
                        {
                            string clientIdentifier = (removal.ClientSocket != null) ? removal.ClientSocket.ToString() : removal.Filename;
                            logger.Debug("Removing monitor client " + clientIdentifier + ".");
                            m_controlClients.Remove(removal);
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorMediator ProcessClientControlEvent. " + excp.Message);
            }
		}

        private void ProcessMachineEvent(SIPMonitorMachineEvent machineEvent) {
            try {
                List<SIPMonitorMachineClient> removals = new List<SIPMonitorMachineClient>();

                for (int index = 0; index < m_machineClients.Count; index++) {
                    SIPMonitorMachineClient machineClient = m_machineClients[index];
                    if (machineClient != null && machineClient.ClientSocket != null && machineClient.ClientSocket.Connected) {
                        try {
                            if (machineClient.Owner != null && machineClient.Owner == machineEvent.Username) {
                                machineClient.ClientSocket.Send(Encoding.ASCII.GetBytes(machineEvent.ToCSV()));
                            }
                            else
                            {
                                // For anonymous connections only the type of event and the network address of the remote socket are sent.
                                //machineClient.ClientSocket.Send(Encoding.ASCII.GetBytes(machineEvent.ToAnonymousCSV()));
                            }
                        }
                        catch (SocketException sockeExcp) {
                            logger.Debug("Socket exception sending to machine client at " + machineClient.RemoteEndPoint + ". " + sockeExcp.Message);
                            removals.Add(machineClient);
                        }
                        catch (Exception excp) {
                            logger.Error("Exception ProcessMachineEvent Sending. " + excp.Message);
                        }
                    }
                    else {
                        removals.Add(machineClient);
                    }
                }

                foreach (SIPMonitorMachineClient machineClient in removals) {
                    logger.Debug("Removing machine client " + machineClient.RemoteEndPoint + ".");
                    m_machineClients.Remove(machineClient);

                    try {
                        machineClient.ClientSocket.Close();
                    }
                    catch { }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SIPMonitorMediator ProcessMachineEvent. " + excp.Message);
            }
        }

        private void StartMachineClient(Socket machineSocket) {

            try {
                IPEndPoint machineEndPoint = (IPEndPoint)machineSocket.RemoteEndPoint;

                if (m_controlClients.Count >= MAX_MACHINE_CONNECTION_COUNT) {
                    logger.Debug("Machine client connection dropped from " + machineEndPoint + " as maximum connections exceeded.");
                    machineSocket.Close();
                }
                else {
                    byte[] buffer = new byte[m_sessionIDLength];
                    int bytesRead = machineSocket.Receive(buffer);

                    string token = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    CustomerSession session = AuthenticateSession_External(token);

                    if (session != null) {
                        logger.Debug("Adding machine client for " + session.CustomerUsername + " on " + machineEndPoint + ".");
                        SIPMonitorMachineClient machineClient = new SIPMonitorMachineClient(machineSocket);
                        machineClient.Owner = session.CustomerUsername;
                        m_machineClients.Add(machineClient);
                    }
                    else {
                        logger.Debug("Invalid token supplied by machine client at " + machineEndPoint + " closing socket.");
                        machineSocket.Close();
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception StartMachineClient. " + excp.Message);
            }
        }

        /// <summary>
        /// Each client that connects gets its own thread that runs this method. The main job of the method is to authenticate the user, request
        /// thier filter and then listen for keystrokes from the client actioning any control characters such as s and q.
        /// </summary>
        /// <param name="state">Not used.</param>
		private void StartControlClient(object state)
		{
            Socket clientSocket = null;
			IPEndPoint clientEndPoint = null;
            SIPMonitorControlClient controlClient = null;
            SIPMonitorFilter monitorFilter = null;
			
			try
			{
                clientSocket = (Socket)state;
				clientEndPoint = (IPEndPoint)clientSocket.RemoteEndPoint;

                if (m_controlClients.Count >= MAX_CONTROL_CLIENT_CONNECTION_COUNT)
				{
					logger.Debug("ProxyMonitor client connection dropped from " + IPSocket.GetSocketString(clientEndPoint) + " as maximum connections exceeded.");
					clientSocket.Send(Encoding.ASCII.GetBytes("maximum connection limit exceeded."));
					clientSocket.Close();
				}
				else
				{
                    Customer authenticatedCustomer = AuthenticateClient(clientSocket);
                    if (authenticatedCustomer != null)
                    {
                        byte[] buffer = new byte[1024];
                        int bytesRead = 1;

                        monitorFilter = GetClientFilter(clientSocket, authenticatedCustomer);
                        if (monitorFilter == null)
                        {
                            // No filter so close connection
                            return;
                        }

                        clientSocket.Send(Encoding.ASCII.GetBytes("filter=" + monitorFilter.GetFilterDescription() + "\r\n"));

                        controlClient = new SIPMonitorControlClient(clientSocket, monitorFilter, authenticatedCustomer.CustomerUsername);
                        m_controlClients.Add(controlClient);

                        while (bytesRead > 0 && m_listenForClients)
                        {
                            bytesRead = clientSocket.Receive(buffer, 0, 1024, SocketFlags.None);
                            string option = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();

                            if (option == "q") // for quit.
                            {
                                break;
                            }
                            else if (option == "s")
                            {
                                controlClient.Filter = null;
                                // Update the customer record in case persmissions have changed since the original filter was set.
                                authenticatedCustomer = GetCustomer_External(c => c.CustomerUsername == authenticatedCustomer.CustomerUsername);
                                monitorFilter = GetClientFilter(clientSocket, authenticatedCustomer);

                                if (monitorFilter == null)
                                {
                                    // Could not get a filter so close connection
                                    break;
                                }

                                clientSocket.Send(Encoding.ASCII.GetBytes("filter=" + monitorFilter.GetFilterDescription() + "\r\n"));

                                controlClient.Filter = monitorFilter;
                            }
                        }
                    }

                    if (clientSocket != null)
                    {
                        clientSocket.Close();
                    }
				}
			}
			catch(Exception excp)
			{
				logger.Error("Exception SIPMonitorMediator StartControlClient. " + excp.Message); 
			}
			finally
            {
                #region Removing any expired clients.

                try
				{
                    if (controlClient != null && m_controlClients.Contains(controlClient))
                    {
                        lock (m_controlClients)
                        {
                            m_controlClients.Remove(controlClient);
                        }
                    }

					if(clientSocket != null)
					{
						clientSocket.Close();
					}
				}
				catch{ }

                #endregion
            }
		}

        private SIPMonitorFilter GetClientFilter(Socket clientSocket, Customer customer)
        {
            SIPMonitorFilter monitorFilter = null;
            string filter = null;

            byte[] buffer = new byte[1024];
            int bytesRead = 1;

            clientSocket.Send(Encoding.ASCII.GetBytes("\r\nfilter=> "));
         
            while (monitorFilter == null)
            {
                while (bytesRead > 0)
                {
                    bytesRead = clientSocket.Receive(buffer, 0, 1024, SocketFlags.None);
                    filter += Encoding.ASCII.GetString(buffer, 0, bytesRead);

                    if (filter.Trim() == "q")
                    {
                        return null;
                    }
                    else if (filter != null && Regex.Match(filter, @"\r\n").Success)
                    {
                        break;
                    }
                }

                try
                {
                    filter = filter.Trim();
                    logger.Debug("Setting filter username for username=" + customer.CustomerUsername + ", adminid=" + customer.AdminId + ", request filter=" + filter + ".");
                    monitorFilter = new SIPMonitorFilter(filter);

                    // If the filter request is for a full SIP trace the user field must not be used since it's
                    // tricky to decide which user a SIP message belongs to prior to authentication. If a full SIP
                    // trace is requested instead of a user filter a regex will be set that matches the username in
                    // the From or To header. If a full SIP trace is not specified then the user filer will be set.
                    if (customer.AdminId != m_topLevelAdminId)
                    {
                        // If this user is not the top level admin there are tight restrictions on what filter can be set.
                        if (monitorFilter.EventFilterDescr == "full")
                        {
                            SIPMonitorFilter userFilter = new SIPMonitorFilter("event full and regex :" + customer.CustomerUsername + "@");
                            userFilter.SIPRequestFilter = monitorFilter.SIPRequestFilter;
                            monitorFilter = userFilter;
                        }
                        else
                        {
                            SIPMonitorFilter userFilter = new SIPMonitorFilter("user " + customer.CustomerUsername);
                            userFilter.EventFilterDescr = monitorFilter.EventFilterDescr;
                            monitorFilter = userFilter;
                        }
                    }

                    //monitorFilter.Username = (customerSession.Customer.SIPConsoleFilter != null) ? customerSession.Customer.SIPConsoleFilter : customerSession.Customer.CustomerUsername;

                    // If a file log has been specified it is created as a separate proxy client.
                    if (monitorFilter.FileLogname != null && monitorFilter.FileLogname.Trim().Length > 0)
                    {
                        logger.Debug("Monitor client log filename=" + monitorFilter.FileLogname + ".");

                        if (!Regex.Match(monitorFilter.FileLogname, @"\/|\\|\:").Success)
                        {
                            string fileName = m_fileLogDirectory + monitorFilter.FileLogname;
                            logger.Error("Monitor client log filename=" + fileName + ", duration=" + monitorFilter.FileLogDuration + ".");
                            m_controlClients.Add(new SIPMonitorControlClient(fileName, monitorFilter, customer.CustomerUsername));
                        }
                        else
                        {
                            logger.Error("Illegal monitor client log filename=" + monitorFilter.FileLogname + ".");
                            clientSocket.Send(Encoding.ASCII.GetBytes("Illegal file name."));
                            clientSocket.Close();
                        }
                    }

                    return monitorFilter;
                }
                catch
                {
                    clientSocket.Send(Encoding.ASCII.GetBytes("filter was invalid, please try again, or q quit.\r\nfilter=> "));
                    filter = null;
                }
            }

            return null;
        }

        private Customer AuthenticateClient(Socket clientSocket)
        {
            byte[] buffer = new byte[1024];
            int bytesRead = 1;
            int attempts = 0;

            while (attempts < MAX_LOGIN_ATTEMPTS)
            {
                string username = null;
                string password = null;

                clientSocket.Send(Encoding.ASCII.GetBytes("username=> "));
                while (bytesRead > 0)
                {
                    bytesRead = clientSocket.Receive(buffer, 0, 1024, SocketFlags.None);
                    username += Encoding.ASCII.GetString(buffer, 0, bytesRead);

                    if (username != null && Regex.Match(username, @"\r\n").Success)
                    {
                        username = Regex.Replace(username, @"\s*$", "");
                        break;
                    }
                }

                clientSocket.Send(Encoding.ASCII.GetBytes("password=> "));
                while (bytesRead > 0)
                {
                    bytesRead = clientSocket.Receive(buffer, 0, 1024, SocketFlags.None);
                    password += Encoding.ASCII.GetString(buffer, 0, bytesRead);

                    if (password != null && Regex.Match(password, @"\r\n").Success)
                    {
                        password = Regex.Replace(password, @"\s*$", "");
                        break;
                    }
                }

                CustomerSession customerSession = AuthenticateUser_External(username, password, ((IPEndPoint)clientSocket.RemoteEndPoint).Address.ToString());
                if (customerSession != null)
                {
                    Customer customer = GetCustomer_External(c => c.CustomerUsername == username);
                    clientSocket.Send(Encoding.ASCII.GetBytes("welcome " + customer.CustomerUsername + "\r\n"));
                    return customer;
                }
                else
                {
                    clientSocket.Send(Encoding.ASCII.GetBytes("login failed.\r\n"));
                    attempts++;
                }
            }

            return null;
        }

        public void Stop()
        {
            try
            {
               m_listenForEvents = false;
               m_listenForClients = false;

               try
               {
                   m_udpEventListener.Close();
               }
               catch (Exception serverExcp)
               {
                   logger.Warn("SIPMonitorMediator Stop exception shutting the UDP event listening socket. " + serverExcp.Message);
               }

               /*try
               {
                   foreach (TcpListener monitorServer in m_monitorServers)
                   {
                       monitorServer.Stop();
                   }
               }
               catch (Exception clientAccExcp)
               {
                   logger.Warn("ProxyMonitor Stop exception shutting client accepting socket. " + clientAccExcp.Message);
               }*/

               lock (m_controlClients)
               {
                   foreach (SIPMonitorControlClient client in m_controlClients)
                   {
                       try
                       {
                           client.ClientSocket.Close();
                       }
                       catch (Exception proxyClientExcp)
                       {
                           logger.Warn("SIPMonitorMediator exception closing client control socket. " + proxyClientExcp.Message);
                       }
                   }
               }

               logger.Debug("SIPMonitorMediator Stopped.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorMediator Stop. " + excp.Message);
            }
        }
	}
}
