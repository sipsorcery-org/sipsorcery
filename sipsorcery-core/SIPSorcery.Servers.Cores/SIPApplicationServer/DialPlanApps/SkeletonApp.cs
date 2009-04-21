//-----------------------------------------------------------------------------
// Filename: SkeletonApp.cs
//
// Description: The framework for creating a new dial plan application.
// 
// History:
// 18 Nov 2007	    Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2007 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
//-----------------------------------------------------------------------------

using System;
using System.Data;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{
    public struct SkeletonStruct
    {
        public static SkeletonStruct Empty = new SkeletonStruct(null);

        public string Data;

        public SkeletonStruct(string data)
        {
            Data = data;
        }

        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }

        public static bool operator ==(SkeletonStruct x, SkeletonStruct y)
        {
            if (x.Data == y.Data)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static bool operator !=(SkeletonStruct x, SkeletonStruct y)
        {
            return !(x == y);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }

    public class SkeletonApp
    {
        private static ILog logger = AppState.GetLogger("sipproxy");

        private event SIPMonitorLogDelegate m_statefulProxyLogEvent;
        private SIPMonitorEventWriter m_monitorEventWriter;

        private string m_clientUsername = null;             // If the UAC is authenticated holds the username of the client.
        private UASInviteTransaction m_clientTransaction;   // Proxy transaction established with a client making a call out through the switch.
        public SIPTransaction ClientTransaction
        {
            get { return m_clientTransaction; }
        }
        
        public string Owner
        {
            get { return m_clientUsername; }
        }

        private SkeletonStruct m_skeletonStruct;            

        public SkeletonApp(
            SIPMonitorLogDelegate statefulProxyLogEvent,
            SIPMonitorEventWriter monitorEventWriter,
            UASInviteTransaction clientTransaction,
            string username)
        {
            m_statefulProxyLogEvent = statefulProxyLogEvent;
            m_monitorEventWriter = monitorEventWriter;

            m_clientTransaction = clientTransaction;
            //m_clientTransaction.TransactionCancelled += new SIPTransactionCancelledDelegate(CallCancelled);

            m_clientUsername = username;
        }

        public void Start(string commandData)
        {
            try
            {
                logger.Debug("SkeletonApp Start.");

                m_skeletonStruct = new SkeletonStruct(commandData);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SkeletonApp Start. " + excp.Message);
            }
        }
    }
}
