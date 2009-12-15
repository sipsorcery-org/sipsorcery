using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NSsh.Server;
using NSsh.Server.ChannelLayer;
using NSsh.Server.ChannelLayer.Console;

namespace SIPSorcery.SSHServer
{
    public class SIPSorceryChannelConsumer : BaseConsoleChannelConsumer
    {
        protected override IConsole CreateConsole()
        {
            if (base.AuthenticatedIdentity is SIPSorceryIdentity)
            {
                return new SIPSorceryShell((SIPSorceryIdentity)base.AuthenticatedIdentity);
            }
            else
            {
                throw new ApplicationException("The IIdentity object for SIPSorceryChannelConsumer must be a SIPSorceryIdentity instance.");
            }
        }
    }

    public class SIPSorceryShell : IConsole
    {
        public TextWriter StandardInput { get; private set; }
        public TextReader StandardOutput { get; private set; }
        public TextReader StandardError { get; private set; }

        private SIPSorceryVT100Server m_sshVT100Server;

        public event EventHandler Closed;

        public bool HasClosed
        {
            get { return m_sshVT100Server.HasClosed; }
        }

        public SIPSorceryShell(SIPSorceryIdentity sipSorceryIdentity)
        {
            m_sshVT100Server = new SIPSorceryVT100Server(sipSorceryIdentity.Customer);
            m_sshVT100Server.Closed += (sender, args) => { Close(); };
            StandardError = new StreamReader(m_sshVT100Server.ErrorStream);
            StandardOutput = new StreamReader(m_sshVT100Server.OutStream);
            StandardInput = new StreamWriter(m_sshVT100Server.InStream);
        }

        public void Close()
        {
            if (!m_sshVT100Server.HasClosed)
            {
                m_sshVT100Server.Close();
            }

            if (Closed != null)
            {
                Closed(this, new EventArgs());
            }
        }
    }
}
