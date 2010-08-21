using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using SIPSorcery.CRM;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Servers {

    public class BlockingMemoryStream : MemoryStream {

        private ManualResetEvent m_dataReady = new ManualResetEvent(false);
        private List<byte> m_buffer = new List<byte>();

        public void Write(byte[] buffer) {
            m_buffer.AddRange(buffer);
            m_dataReady.Set();
        }

        public override void Write(byte[] buffer, int offset, int count) {
            m_buffer.AddRange(buffer.ToList().Skip(offset).Take(count));
            m_dataReady.Set();
        }

        public override void WriteByte(byte value) {
            m_buffer.Add(value);
            m_dataReady.Set();
        }

        public override int ReadByte() {
            if (m_buffer.Count == 0) {
                // Block until the stream has some more data.
                m_dataReady.Reset();
                m_dataReady.WaitOne();
            }

            byte firstByte = m_buffer[0];
            m_buffer = m_buffer.Skip(1).ToList();
            return firstByte;
        }

        public override int Read(byte[] buffer, int offset, int count) {
            if (m_buffer.Count == 0) {
                // Block until the stream has some more data.
                m_dataReady.Reset();
                m_dataReady.WaitOne();
            }

            if (m_buffer.Count >= count) {
                // More bytes available than were requested.
                Array.Copy(m_buffer.ToArray(), 0, buffer, offset, count);
                m_buffer = m_buffer.Skip(count).ToList();
                return count;
            }
            else {
                int length = m_buffer.Count;
                Array.Copy(m_buffer.ToArray(), 0, buffer, offset, length);
                m_buffer.Clear();
                return length;
            }
        }
    }

    public class SIPMonitorClientConnection {

        private const int MAX_COMMAND_LENGTH = 2000;
        private const string FILTER_COMMAND_PROMPT = "filter=>";
        private const string CRLF = "\r\n";

        private static readonly string m_topLevelAdminId = Customer.TOPLEVEL_ADMIN_ID;

        private static ILog logger = AppState.logger;

        public BlockingMemoryStream InStream = new BlockingMemoryStream();
        public BlockingMemoryStream OutStream = new BlockingMemoryStream();
        public BlockingMemoryStream ErrorStream = new BlockingMemoryStream();
        public string Username;
        public string AdminId;
        public SIPMonitorFilter Filter;
        public bool HasClosed;

        public event EventHandler Closed;

        public SIPMonitorClientConnection(string username, string adminId) {
            Username = username;
            AdminId = adminId;
        }

        public void Close() {
            InStream.Close();
            OutStream.Close();
            ErrorStream.Close();
            HasClosed = true;

            if (Closed != null) {
                Closed(this, new EventArgs());
            }
        }

        public void Start() {
            OutStream.Write(Encoding.ASCII.GetBytes(FILTER_COMMAND_PROMPT));
            ThreadPool.QueueUserWorkItem(delegate { Listen(); });
        }

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
        private void Listen() {
            try {
                List<char> command = new List<char>();
                int cursorPosn = 0;

                while (!HasClosed) {

                    byte[] inBuffer = new byte[1024];
                    int bytesRead = InStream.Read(inBuffer, 0, 1024);

                    if (bytesRead == 0) {
                        // Connection has been closed.
                        logger.Debug("Monitor SSH connection closed by remote client.");
                        HasClosed = true;
                        break;
                    }

                    //logger.Debug("input character received=" + inputChar + " (" + ((int)inputChar) + ")");

                     // Process any recognised commands.
                    if (inBuffer[0] == 0x03) {
                        // Ctrl-C, disconnect session.
                        Close();
                    }
                    else if (inBuffer[0] == 0x08 || inBuffer[0] == 0x7f) {
                        //logger.Debug("BackSpace");
                        // Backspace, move the cursor left one character and delete the right most character of the current command.
                        if (command.Count > 0) {
                            command.RemoveAt(command.Count - 1);
                            cursorPosn--;
                            // ESC [ 1 D for move left, ESC [ Pn P (Pn=1) for erase single character.
                            OutStream.Write(new byte[] { 0x1b, 0x5b, 0x31, 0x44, 0x1b, 0x5b, 0x31, 0x50 });
                        }
                    }
                    else if (inBuffer[0] == 0x0d || inBuffer[0] == 0x0a) {
                        string commandStr = (command.Count > 0) ? new string(command.ToArray()) : null;
                        logger.Debug("User " + Username + " requested filter=" + commandStr + ".");
                        ProcessCommand(commandStr);

                        command.Clear();
                        cursorPosn = 0;
                    }
                    else if (inBuffer[0] == 0x1b) {
                        // ESC command sequence.
                        //logger.Debug("ESC command sequence: " + BitConverter.ToString(inBuffer, 0, bytesRead));
                        if (inBuffer[1] == 0x5b) {
                            // Arrow scrolling command.
                            if (inBuffer[2] == 0x44) {
                                // Left arrow.
                                if (command.Count > 0 && cursorPosn > 0) {
                                    cursorPosn--;
                                    OutStream.Write(new byte[] { 0x1b, 0x5b, 0x44 });
                                }
                            }
                            else if (inBuffer[2] == 0x43) {
                                // Right arrow.
                                if (command.Count > 0 && cursorPosn < command.Count) {
                                    cursorPosn++;
                                    OutStream.Write(new byte[] { 0x1b, 0x5b, 0x43 });
                                }
                            }
                        }
                    }
                    else if (inBuffer[0] <= 0x1f) {
                        //logger.Debug("Unknown control sequence: " + BitConverter.ToString(inBuffer, 0, bytesRead));
                    }
                    else if (Filter != null) {
                        if (inBuffer[0] == 's' || inBuffer[0] == 'S') {
                            // Stop events.
                            Filter = null;
                            OutStream.Write(Encoding.ASCII.GetBytes(CRLF + FILTER_COMMAND_PROMPT));
                        }
                    }
                    else {
                        for (int index = 0; index < bytesRead; index++) {
                            if (command.Count == 0 || cursorPosn == command.Count) {
                                command.Add((char)inBuffer[index]);
                                cursorPosn++;
                            }
                            else {
                                command[cursorPosn] = (char)inBuffer[index];
                                cursorPosn++;
                            }

                            if (command.Count > MAX_COMMAND_LENGTH) {
                                command.Clear();
                                cursorPosn = 0;
                                WriteError("Command too long.");
                                break;
                            }
                            else {
                                // Echo the character back to the client.
                                OutStream.WriteByte(inBuffer[index]);
                            }
                        }
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SIPMonitorClientConnection Listen. " + excp.Message);
            }
        }

        /// <summary>
        /// Simple state machine that processes commands from the client.
        /// </summary>
        private void ProcessCommand(string command) {
            try {
                Filter = new SIPMonitorFilter(command);

                // If the filter request is for a full SIP trace the user field must not be used since it's
                // tricky to decide which user a SIP message belongs to prior to authentication. If a full SIP
                // trace is requested instead of a user filter a regex will be set that matches the username in
                // the From or To header. If a full SIP trace is not specified then the user filer will be set.
                if (AdminId != m_topLevelAdminId) {
                    // If this user is not the top level admin there are tight restrictions on what filter can be set.
                    if (Filter.EventFilterDescr == "full") {
                        Filter = new SIPMonitorFilter("event full and regex :" + Username + "@");
                    }
                    else {
                        Filter.Username = Username;
                    }
                }

                //OutStream.Write(new byte[] { 0x1B, 0x5B, 0x34, 0x37, 0x6d, 0x00, 0x1b, 0x5b, 0x34, 0x36, 0x6d, 0x00 });
                //OutStream.Write(new byte[] { 0x1B, 0x5B, 0x34, 0x37, 0x6d, 0x1b, 0x5b, 0x34, 0x36, 0x6d });
                OutStream.Write(new byte[] { 0x1B, 0x5B, 0x34, 0x37, 0x3b, 0x33, 0x34, 0x6d });
                OutStream.Write(Encoding.ASCII.GetBytes(CRLF + "filter: " + Filter.GetFilterDescription()));
                OutStream.Write(new byte[] { 0x1B, 0x5B, 0x30, 0x6d });
                OutStream.Flush();
                OutStream.Write(Encoding.ASCII.GetBytes(CRLF));
            }
            catch (ApplicationException appExcp) {
                //OutStream.Write(new byte[] { 0x1B, 0x5B, 0x34, 0x37, 0x6d, 0x00, 0x1b, 0x5b, 0x34, 0x36, 0x6d, 0x00 });
                //OutStream.Write(Encoding.ASCII.GetBytes("\n" + appExcp.Message));
                //OutStream.Write(new byte[] { 0x1B, 0x5B, 0x30, 0x6d, 0x00 });
                WriteError(appExcp.Message);
                Filter = null;
            }
            catch (Exception excp) {
                logger.Error("Exception SIPMonitorClientConnection ProcessCommand. " + excp.Message);
                Filter = null;
            }
        }

        private void WriteError(string errorMessage) {
            OutStream.Write(new byte[] { 0x1B, 0x5B, 0x33, 0x31, 0x3b, 0x34, 0x37, 0x6d });
            OutStream.Write(Encoding.ASCII.GetBytes(CRLF + errorMessage));
            OutStream.Write(new byte[] { 0x1B, 0x5B, 0x30, 0x6d });
            OutStream.Flush();
            OutStream.Write(Encoding.ASCII.GetBytes(CRLF + FILTER_COMMAND_PROMPT));
        }
    }
}
