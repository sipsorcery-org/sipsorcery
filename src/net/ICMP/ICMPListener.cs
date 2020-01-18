﻿//-----------------------------------------------------------------------------
// Filename: ICMPListener.cs
//
// Description: Raw socket to receive ICMP packets. Mainly used as a means to
// detect when transmissions to dead UDP sockets are attempted.
//
// Author(s):
// Aaron Clauson
//  
// History:
// ??           Aaron Clauson	Created.
// 07 Feb 2012  Aaron Clauson   The listener never worked up until now. The trick was
//                              adding the IOControl socket directive. Also to allow all
//                              ICMP packets in a firewall rule had to be set.
//
// Notes:
// netsh advfirewall firewall add rule name="All ICMP v4" dir=in action=allow protocol=icmpv4:any,any
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class ICMPListener
    {
        private readonly static ILogger logger = Log.Logger;

        private Socket m_icmpListener;
        private bool m_stop;

        public event Action<ICMP, IPEndPoint> Receive;

        public ICMPListener(IPAddress listenAddress)
        {
            m_icmpListener = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Icmp);
            m_icmpListener.Bind(new IPEndPoint(listenAddress, 0));
            m_icmpListener.IOControl(IOControlCode.ReceiveAll, new byte[] { 1, 0, 0, 0 }, new byte[] { 1, 0, 0, 0 });  //SIO_RCVALL of Winsock
        }

        public void Start()
        {
            try
            {
                byte[] buffer = new byte[4096];
                EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

                logger.LogDebug("ICMPListener receive thread starting.");

                while (!m_stop)
                {
                    int bytesRead = m_icmpListener.ReceiveFrom(buffer, ref remoteEndPoint);
                    logger.LogDebug("ICMPListener received " + bytesRead + " from " + remoteEndPoint.ToString());

                    if (Receive != null)
                    {
                        Receive(new ICMP(buffer, bytesRead), remoteEndPoint as IPEndPoint);
                    }

                    //m_icmpListener.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, this.ReceiveRawPacket, buffer);
                }

                logger.LogDebug("ICMPListener receive thread stopped.");
            }
            catch (Exception excp)
            {
                logger.LogError("Exception ICMPListener Start. " + excp.Message);
                throw;
            }
        }

        private void ReceiveRawPacket(IAsyncResult ar)
        {
            try
            {
                byte[] buffer = (byte[])ar.AsyncState;
                int bytesRead = m_icmpListener.EndReceive(ar);
                logger.LogDebug("ICMPListener received " + bytesRead + ".");
            }
            catch (Exception excp)
            {
                logger.LogError("Exception ReceiveRawPacket. " + excp.Message);
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
                logger.LogError("Exception ICMPListener Stop. " + excp.Message);
            }
        }
    }
}
