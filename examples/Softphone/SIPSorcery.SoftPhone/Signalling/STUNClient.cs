//-----------------------------------------------------------------------------
// Filename: STUNClient.cs
//
// Description: A STUN client whose sole purpose is to determine the public IP address
// of the machine running the softphone. 
// 
// History:
// 27 Mar 2012	Aaron Clauson	Refactored, (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SoftPhone
{
    public class SoftphoneSTUNClient
    {
        private const string STUN_CLIENT_THREAD_NAME = "stunclient";

        private ILog logger = AppState.logger;

        private string m_stunServerHostname = SIPSoftPhoneState.STUNServerHostname;
        
        private ManualResetEvent m_stunClientMRE = new ManualResetEvent(false);     // Used to set the interval on the STUN lookups and also allow the thread to be stopped.
        private bool m_stop;

        public static IPAddress PublicIPAddress;

        public SoftphoneSTUNClient()
        {
            if (!m_stunServerHostname.IsNullOrBlank())
            {
                // If a STUN server hostname has been specified start the STUN client thread.
                ThreadPool.QueueUserWorkItem(delegate { StartSTUNClient(); });
            }
        }

        public void Stop()
        {
            m_stop = true;
            m_stunClientMRE.Set();
        }

        private void StartSTUNClient()
        {
            try
            {
                Thread.CurrentThread.Name = STUN_CLIENT_THREAD_NAME;

                logger.Debug("STUN client started.");

                while (!m_stop)
                {
                    try
                    {
                        IPAddress publicIP = STUNClient.GetPublicIPAddress(m_stunServerHostname);
                        if (publicIP != null)
                        {
                            logger.Debug("The STUN client was able to determine the public IP address as " + publicIP.ToString() + ".");
                            PublicIPAddress = publicIP;
                        }
                        else
                        {
                            logger.Debug("The STUN client could not determine the public IP address.");
                            PublicIPAddress = null;
                        }
                    }
                    catch (Exception getAddrExcp)
                    {
                        logger.Error("Exception StartSTUNClient GetPublicIPAddress. " + getAddrExcp.Message);
                    }

                    m_stunClientMRE.Reset();
                    m_stunClientMRE.WaitOne(60000);
                }

                logger.Warn("STUN client thread stopped.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception StartSTUNClient. " + excp.Message);
            }
        }
    }
}
