﻿//-----------------------------------------------------------------------------
// Filename: STUNClient.cs
//
// Description: A STUN client whose sole purpose is to determine the public IP address
// of the machine running the softphone. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//  
// History:
// 27 Mar 2012	Aaron Clauson	Refactored, Hobart, Australia.
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
        private ILog logger = AppState.logger;

        private Timer updateTimer;

        private readonly TimeSpan updateIntervalNormal = TimeSpan.FromMinutes(1);

        private readonly TimeSpan updateIntervalShort = TimeSpan.FromSeconds(5);

        private readonly string m_stunServerHostname;
        
        private volatile bool m_stop;

        public event Action<IPAddress> PublicIPAddressDetected;

        public SoftphoneSTUNClient(string stunServerHostname)
        {
            m_stunServerHostname = stunServerHostname;
        }

        public void Run()
        {
            m_stop = false;
            updateTimer = new Timer(e =>
            {
                if (!m_stop)
                {
                    var publicIPAddress = GetPublicIPAddress();
                    if (publicIPAddress != null)
                    {
                        PublicIPAddressDetected?.Invoke(publicIPAddress);
                    }

                    var timerInterval = (publicIPAddress == null) ? updateIntervalShort : updateIntervalNormal;
                    updateTimer.Change(timerInterval, timerInterval);
                }
            }, null, TimeSpan.Zero, updateIntervalNormal);

            logger.Debug("STUN client started.");
        }

        public void Stop()
        {
            m_stop = true;
            updateTimer.Change(Timeout.Infinite, Timeout.Infinite);

            logger.Warn("STUN client stopped.");
        }

        private IPAddress GetPublicIPAddress()
        {
            try
            {
                var publicIP = STUNClient.GetPublicIPAddress(m_stunServerHostname);
                if (publicIP != null)
                {
                    logger.Debug($"The STUN client was able to determine the public IP address as {publicIP}");
                }
                else
                {
                    logger.Debug("The STUN client could not determine the public IP address.");
                }

                return publicIP;
            }
            catch (Exception getAddrExcp)
            {
                logger.Error("Exception GetPublicIPAddress. " + getAddrExcp.Message);
                return null;
            }
        }
    }
}