using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading;
using SIPSorcery.CRM;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorcery.Web.Services;
using log4net;
using NSsh.Common.Utility;

namespace SIPSorcery.SSHServer
{
    public class SIPSorceryVT100Server
    {
        private const int MAX_COMMAND_LENGTH = 2000;
        private const string FILTER_COMMAND_PROMPT = "filter=>";
        private const string DEFAULT_FILTER = "event *";
        private const string CRLF = "\r\n";
        private const int DEFAULT_SESSION_LENGTH = 600;     // Default length of session to establish with monitor server.
        private const int RENEW_SESSION_LENGTH = 500;       // The number of seconds after which to renew the session with monitor server.

        private static ILog logger = AppState.logger;

        private ISIPMonitorPublisher m_publisher;
        private string m_notificationsAddress;
        private string m_notificationsSessionID;
        private Customer m_customer;
        private DateTime m_lastMonitorSessionRenewal;

        public BlockingMemoryStream InStream = new BlockingMemoryStream();
        public BlockingMemoryStream OutStream = new BlockingMemoryStream();
        public BlockingMemoryStream ErrorStream = new BlockingMemoryStream();
        public string Username;
        public string AdminId;
        public bool HasClosed;

        public event EventHandler Closed;

        public SIPSorceryVT100Server(Customer customer)
        {
            m_customer = customer;
            Username = customer.CustomerUsername;
            AdminId = customer.AdminId;
            m_notificationsAddress = Guid.NewGuid().ToString();

            try
            {
                m_publisher = Dependency.Resolve<ISIPMonitorPublisher>();
                m_publisher.NotificationReady += NotificationReady;
                m_publisher.MonitorEventReady += MonitorEventAvailable;
            }
            catch (ApplicationException appExcp)
            {
                logger.Debug("Unable to resolve ISIPMonitorPublisher. " + appExcp.Message);
            }

            WriteWelcomeMessage(customer);
            OutStream.Write(Encoding.ASCII.GetBytes(FILTER_COMMAND_PROMPT));
            ThreadPool.QueueUserWorkItem(delegate { Listen(); });
        }

        private void NotificationReady(string addressID)
        {
            try
            {
                if (addressID == m_notificationsAddress)
                {
                    string sessionID = null;
                    string sessionError = null;
                    List<string> notifications = m_publisher.GetNotifications(m_notificationsAddress, out sessionID, out sessionError);

                    if (sessionError != null)
                    {
                        logger.Warn("SIPSorceryVT100Server error on get notifications. " + sessionError + ".");
                    }
                    else if (m_notificationsSessionID == sessionID)
                    {
                        foreach (string notification in notifications)
                        {
                            MonitorEventAvailable(SIPMonitorEvent.ParseEventCSV(notification));
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPSorceryVT100Server NotificationReady. " + excp.Message);
            }
        }

        public bool MonitorEventAvailable(SIPMonitorEvent monitorEvent)
        {
            try
            {
                SIPMonitorConsoleEvent consoleEvent = monitorEvent as SIPMonitorConsoleEvent;
                if (consoleEvent != null && consoleEvent.SessionID == m_notificationsSessionID)
                {
                    OutStream.Write(Encoding.ASCII.GetBytes(consoleEvent.ToConsoleString(AdminId)));
                    OutStream.Flush();
                    return true;
                }

                return false;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPSorceryVT100Server NotificationReady. " + excp.Message);
                return false;
            }
        }

        public void Close()
        {
            try
            {
                if (m_notificationsAddress != null)
                {
                    m_publisher.CloseConnection(m_notificationsAddress);
                }
            }
            catch (Exception excp)
            {
                logger.Warn("Exception closing notifications connection. " + excp.Message);
            }

            InStream.Close();
            OutStream.Close();
            ErrorStream.Close();
            HasClosed = true;
            //StopPolling = true;

            if (Closed != null)
            {
                Closed(this, new EventArgs());
            }
        }

        private void RenewSession()
        {
            try
            {
                string sessionID = m_notificationsSessionID;

                Thread.Sleep(RENEW_SESSION_LENGTH * 1000);

                while (!HasClosed && sessionID == m_notificationsSessionID)
                {
                    m_publisher.ExtendSession(m_notificationsAddress, m_notificationsSessionID, DEFAULT_SESSION_LENGTH);

                    Thread.Sleep(RENEW_SESSION_LENGTH * 1000);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPSorceryVT100Server RenewSession. " + excp.Message);
            }
        }

        //public void NotificationReady(string address)
        //{
        //    GetNotifications();
        //}

        /*public void GetNotifications()
        {
            try
            {
                if (m_notificationsAddress == null)
                {
                    return;
                }

                string sessionID;
                string sessionError;

                List<string> notifications = m_publisher.GetNotifications(m_notificationsAddress, out sessionID, out sessionError);

                while (sessionError == null && notifications != null && notifications.Count > 0)
                {
                    foreach (string notification in notifications)
                    {
                        OutStream.Write(Encoding.ASCII.GetBytes(notification));
                        OutStream.Flush();
                    }

                    notifications = m_publisher.GetNotifications(m_notificationsAddress, out sessionID, out sessionError);
                }

                if (sessionError != null)
                {
                    throw new ApplicationException(sessionError);
                }
                //else 
                //{
                //m_publisher.RegisterListener(m_notificationsAddress, NotificationReady);
                //}
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetNotifications. " + excp.Message);
                throw;
            }
        }*/

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// From vt100.net and the vt100 user guide:
        ///
        /// Control Character Octal Code Action Taken 
        /// NUL 000 Ignored on input (not stored in input buffer; see full duplex protocol). 
        /// ENQ 005 Transmit answerback message. 
        /// BEL 007 Sound bell tone from keyboard. 
        /// BS 010 Move the cursor to the left one character position, unless it is at the left margin, in which case no action occurs. 
        /// HT 011 Move the cursor to the next tab stop, or to the right margin if no further tab stops are present on the line. 
        /// LF 012 This code causes a line feed or a new line operation. (See new line mode). 
        /// VT 013 Interpreted as LF. 
        /// FF 014 Interpreted as LF. 
        /// CR 015 Move cursor to the left margin on the current line. 
        /// SO 016 Invoke G1 character set, as designated by SCS control sequence. 
        /// SI 017 Select G0 character set, as selected by ESC ( sequence. 
        /// XON 021 Causes terminal to resume transmission. 
        /// XOFF 023 Causes terminal to stop transmitted all codes except XOFF and XON. 
        /// CAN 030 If sent during a control sequence, the sequence is immediately terminated and not executed. It also causes the error character to be displayed. 
        /// SUB 032 Interpreted as CAN. 
        /// ESC 033 Invokes a control sequence. 
        /// DEL 177 Ignored on input (not stored in input buffer). 
        /// </remarks>
        private void Listen()
        {
            try
            {
                List<char> command = new List<char>();
                int cursorPosn = 0;

                while (!HasClosed)
                {
                    byte[] inBuffer = new byte[1024];
                    int bytesRead = InStream.Read(inBuffer, 0, 1024);

                    if (bytesRead == 0)
                    {
                        // Connection has been closed.
                        logger.Debug("SIPSorcery SSH connection closed by remote client.");
                        HasClosed = true;
                        break;
                    }

                    //logger.Debug("ssh input received=" + Encoding.ASCII.GetString(inBuffer, 0, bytesRead));

                    // Process any recognised commands.
                    if (inBuffer[0] == 0x03)
                    {
                        // Ctrl-C, disconnect session.
                        Close();
                    }
                    else if (inBuffer[0] == 0x08 || inBuffer[0] == 0x7f)
                    {
                        //logger.Debug("BackSpace");
                        // Backspace, move the cursor left one character and delete the right most character of the current command.
                        if (command.Count > 0)
                        {
                            command.RemoveAt(command.Count - 1);
                            cursorPosn--;
                            // ESC [ 1 D for move left, ESC [ Pn P (Pn=1) for erase single character.
                            OutStream.Write(new byte[] { 0x1b, 0x5b, 0x31, 0x44, 0x1b, 0x5b, 0x31, 0x50 });
                        }
                    }
                    else if (inBuffer[0] == 0x0d || inBuffer[0] == 0x0a)
                    {
                        string commandStr = (command.Count > 0) ? new string(command.ToArray()) : null;
                        logger.Debug("User " + Username + " requested filter=" + commandStr + ".");
                        ProcessCommand(commandStr);

                        command.Clear();
                        cursorPosn = 0;
                    }
                    else if (inBuffer[0] == 0x1b)
                    {
                        // ESC command sequence.
                        //logger.Debug("ESC command sequence: " + BitConverter.ToString(inBuffer, 0, bytesRead));
                        if (inBuffer[1] == 0x5b)
                        {
                            // Arrow scrolling command.
                            if (inBuffer[2] == 0x44)
                            {
                                // Left arrow.
                                if (command.Count > 0 && cursorPosn > 0)
                                {
                                    cursorPosn--;
                                    OutStream.Write(new byte[] { 0x1b, 0x5b, 0x44 });
                                }
                            }
                            else if (inBuffer[2] == 0x43)
                            {
                                // Right arrow.
                                if (command.Count > 0 && cursorPosn < command.Count)
                                {
                                    cursorPosn++;
                                    OutStream.Write(new byte[] { 0x1b, 0x5b, 0x43 });
                                }
                            }
                        }
                    }
                    else if (inBuffer[0] <= 0x1f)
                    {
                        //logger.Debug("Unknown control sequence: " + BitConverter.ToString(inBuffer, 0, bytesRead));
                    }
                    else if (m_notificationsSessionID != null)
                    {
                        if (inBuffer[0] == 's' || inBuffer[0] == 'S')
                        {
                            // Stop events.
                            //StopPolling = true;
                            logger.Debug("Closing session " + m_notificationsSessionID + ".");
                            m_publisher.CloseSession(m_notificationsAddress, m_notificationsSessionID);
                            m_notificationsSessionID = null;
                            OutStream.Write(Encoding.ASCII.GetBytes(CRLF + FILTER_COMMAND_PROMPT));
                        }
                    }
                    else
                    {
                        for (int index = 0; index < bytesRead; index++)
                        {
                            if (command.Count == 0 || cursorPosn == command.Count)
                            {
                                command.Add((char)inBuffer[index]);
                                cursorPosn++;
                            }
                            else
                            {
                                command[cursorPosn] = (char)inBuffer[index];
                                cursorPosn++;
                            }

                            if (command.Count > MAX_COMMAND_LENGTH)
                            {
                                command.Clear();
                                cursorPosn = 0;
                                WriteError("Command too long.");
                                break;
                            }
                            else
                            {
                                // Echo the character back to the client.
                                OutStream.WriteByte(inBuffer[index]);
                            }
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPSorceryVT100Server Listen. " + excp.Message);
            }
        }

        /// <summary>
        /// Simple state machine that processes commands from the client.
        /// </summary>
        private void ProcessCommand(string command)
        {
            try
            {
                if (command.IsNullOrBlank())
                {
                    command = DEFAULT_FILTER;
                }

                string subscribeError = null;
                m_notificationsSessionID = Guid.NewGuid().ToString();
                m_publisher.Subscribe(Username, AdminId, m_notificationsAddress, m_notificationsSessionID, SIPMonitorClientTypesEnum.Console.ToString(), command, DEFAULT_SESSION_LENGTH, null, out subscribeError);

                if (subscribeError != null)
                {
                    throw new ApplicationException(subscribeError);
                }
                else
                {
                    m_lastMonitorSessionRenewal = DateTime.Now;
                    ThreadPool.QueueUserWorkItem(delegate { RenewSession(); });
                    SIPMonitorFilter filter = new SIPMonitorFilter(command);
                    WriteFilterDescription(filter.GetFilterDescription());

                    /*if (m_publisher is SIPSorcery.Servers.SIPMonitorClientManager)
                    {
                        logger.Debug("VT100Server is connected to a publisher that fires an event when a notification is ready.");
                        // The publisher will fire an event when a new notification is ready.
                        m_publisher.NotificationReady += (address) => { GetNotifications(); };
                    }
                    else
                    {
                        logger.Debug("VT100Server is connected to a publisher that needs to be polled for events.");

                        // The publisher does not supply an event when a notification is ready and must be polled instead.
                        StopPolling = false;
                        ThreadPool.QueueUserWorkItem(delegate
                        {
                            try
                            {
                                while (!HasClosed && !StopPolling)
                                {
                                    GetNotifications();
                                    Thread.Sleep(POLL_FOR_NOTIFICATIONS_PERIOD);
                                }

                                logger.Debug("Stopped polling for events for address " + m_notificationsAddress + ".");
                            }
                            catch (ApplicationException appExcp)
                            {
                                WriteError(appExcp.Message);
                            }
                            catch (Exception excp)
                            {
                                logger.Error("Exception SIPSorceryVT100Server ProcessCommand on GetNotifications. " + excp.Message);
                            }
                        });
                    }*/
                }
            }
            catch (ApplicationException appExcp)
            {
                WriteError(appExcp.Message);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPSorceryVT100Server ProcessCommand. " + excp.Message);
            }
        }

        private void WriteWelcomeMessage(Customer customer)
        {
            OutStream.Write(new byte[] { 0x1B, 0x5B, 0x34, 0x37, 0x3b, 0x33, 0x34, 0x6d });
            OutStream.Write(Encoding.ASCII.GetBytes("Welcome " + customer.FirstName));
            OutStream.Write(new byte[] { 0x1B, 0x5B, 0x30, 0x6d });
            OutStream.Flush();
            OutStream.Write(Encoding.ASCII.GetBytes(CRLF));
        }

        private void WriteFilterDescription(string filterDescription)
        {
            OutStream.Write(new byte[] { 0x1B, 0x5B, 0x34, 0x37, 0x3b, 0x33, 0x34, 0x6d });
            OutStream.Write(Encoding.ASCII.GetBytes(CRLF + filterDescription.TrimEnd()));
            OutStream.Write(new byte[] { 0x1B, 0x5B, 0x30, 0x6d });
            OutStream.Flush();
            OutStream.Write(Encoding.ASCII.GetBytes(CRLF));
        }

        private void WriteError(string errorMessage)
        {
            OutStream.Write(new byte[] { 0x1B, 0x5B, 0x33, 0x31, 0x3b, 0x34, 0x37, 0x6d });
            OutStream.Write(Encoding.ASCII.GetBytes(CRLF + errorMessage.TrimEnd()));
            OutStream.Write(new byte[] { 0x1B, 0x5B, 0x30, 0x6d });
            OutStream.Flush();
            OutStream.Write(Encoding.ASCII.GetBytes(CRLF + FILTER_COMMAND_PROMPT));
        }
    }
}
