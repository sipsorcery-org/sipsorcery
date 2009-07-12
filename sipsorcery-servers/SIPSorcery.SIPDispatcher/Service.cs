// ============================================================================
// FileName: Service.cs
//
// Description:
// Allows the SIP Server Agent to be run as a Windows Service.
//
// Author(s):
// Aaron Clauson
//
// History:
// 21 Oct 2006	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2009 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Data;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Threading;

namespace SIPSorcery.SIPDispatcher
{
	public class Service : System.ServiceProcess.ServiceBase
	{
		public const string DEFAULT_STARTUPERRORLOG_PATH = @"c:\temp\sipdispatcher.strarterror.log";
        public const string DEFAULT_SHUTDOWNERRORLOG_PATH = @"c:\temp\sipdispatcher.stoperror.log";

        private SIPDispatcherDaemon m_daemon;
				
		public Service(SIPDispatcherDaemon daemon)
		{
			this.CanShutdown = true;
			this.CanStop = true;

            m_daemon = daemon;
		}

		protected override void Dispose( bool disposing )
		{
			base.Dispose( disposing );
		}

		protected override void OnStart(string[] args)
		{
			try
			{
				Thread daemonThread = new Thread(m_daemon.Start);
				daemonThread.Start();
			}
			catch(Exception excp)
			{
				StreamWriter errorLog = new StreamWriter(DEFAULT_STARTUPERRORLOG_PATH, true);
				errorLog.WriteLine(DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + " Exception Starting Service. " +excp.Message);
				errorLog.Close();

                throw excp;
			}
		}
 
		protected override void OnStop()
		{
			try
			{
                m_daemon.Stop();
			}
			catch(Exception excp)
			{
				StreamWriter errorLog = new StreamWriter(DEFAULT_SHUTDOWNERRORLOG_PATH, true);
				errorLog.WriteLine(DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + " Exception Stopping Service. " + excp.Message);
				errorLog.Close();
			}
		}
	}
}
