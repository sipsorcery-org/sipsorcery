//-----------------------------------------------------------------------------
// Filename: STUNClient.cs
//
// Description: A STUN client whose sole purpose is to determine the public IP address
// of the machine running the softphone. 
// 
// History:
// 27 Mar 2012	Aaron Clauson	Refactored.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2012 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIPSorcery Ltd. 
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
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SoftPhone
{
    public class SoftphoneSTUNClient
    {
        private const string STUN_CLIENT_THREAD_NAME = "sipproxy-stunclient";

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
