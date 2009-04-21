using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Net
{
    public class ICMPListener
    {
        private readonly static ILog logger = AppState.logger;

        private Socket m_icmpListener;
        private bool m_stop;

        public ICMPListener()
        {
            m_icmpListener = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Icmp);
            //m_icmpListener.Blocking = true;
        }

        public void Start(object state)
        {
            try
            {
                //m_icmpListener.Bind(new IPEndPoint(IPAddress.Any, 0));

                byte[] buffer = new byte[4096];
                EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

                logger.Debug("ICMPListener receive thread starting.");

                while (!m_stop)
                {
                    int bytesRead = m_icmpListener.ReceiveFrom(buffer, ref remoteEndPoint);
                    logger.Debug("ICMPListener received " + bytesRead + " from " + remoteEndPoint.ToString());
                    //m_icmpListener.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, this.ReceiveRawPacket, buffer);
                }

                logger.Debug("ICMPListener receive thread stopped.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception ICMPListener Start. " + excp.Message);
                throw;
            }
        }

        private void ReceiveRawPacket(IAsyncResult ar)
        {
            try
            {
                byte[] buffer = (byte[])ar.AsyncState;
                int bytesRead = m_icmpListener.EndReceive(ar);
                logger.Debug("ICMPListener received " + bytesRead + ".");
            }
            catch (Exception excp)
            {
                logger.Error("Exception ReceiveRawPacket. " + excp.Message);
            }
        }

        public void Stop()
        {
            try
            {
                m_stop = true;
                m_icmpListener.Shutdown(SocketShutdown.Receive);
            }
            catch (Exception excp)
            {
                logger.Error("Exception ICMPListener Stop. " + excp.Message);
            }
        }
    }
}
