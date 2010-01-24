//-----------------------------------------------------------------------------
// Filename: FastAGIConfiguration.cs
//
// Description: Loads application configuration settings.
//
// Author(s):
// Aaron Clauson
//
// History:
// 24 Jan 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Ltd, London, UK
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
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Xml;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Asterisk.FastAGI
{
    public class FastAGIConfiguration
	{
        private const int QUEUEDREQUESTS_ALERT_DEFAULTTHRESHOLD = 10;
        private const int AGI_DEFAULT_SERVERPORT = 4573;
        private const int AGI_WORKERTHREAD_DEFAULTCOUNT = 3;      
        private const string AGI_SERVER_IPADDRESS = "AGIServerIPAddress";
        private const string AGI_SERVER_PORT = "AGIServerPort";
        private const string AGI_WORKER_THREADCOUNT = "AGIWorkerThreadsCount";
        private const string FASTAGI_QUEUEMETRICS_FILE_PATH = "FastAGIQueueMetricsFilePath";
        private const string FASTAGI_APPMETRICS_FILE_PATH = "FastAGIAppMetricsFilePath";
        private const string FASTAGI_QUEUEDREQUESTS_ALERT_THRESHOLD = "FastAGIQueuedRequestsAlertThreshold";  // Number of requests waitign threshold before sending an email alert.
        private const string ALERT_EMAIL_ADDRESS = "AlertEmailAddress";

		private static ILog logger = AppState.logger;

        public static readonly string AGIServerIPAddress;
        public static readonly int AGIServerPort = AGI_DEFAULT_SERVERPORT;
        public static readonly int AGIWorkerThreadsCount = AGI_WORKERTHREAD_DEFAULTCOUNT;
        public static readonly int FastAGIQueuedRequestsAlertThreshold = QUEUEDREQUESTS_ALERT_DEFAULTTHRESHOLD;
        public static readonly string FastAGIQueueMetricsFilePath;
        public static readonly string FastAGIAppMetricsFilePath;
        public static readonly string AlertEmailAddress;
        
        static FastAGIConfiguration()
		{
			try
			{
                AGIServerIPAddress = ConfigurationManager.AppSettings[AGI_SERVER_IPADDRESS];
                Int32.TryParse(ConfigurationManager.AppSettings[AGI_SERVER_PORT], out AGIServerPort);
                Int32.TryParse(ConfigurationManager.AppSettings[AGI_WORKER_THREADCOUNT], out AGIWorkerThreadsCount);
                Int32.TryParse(ConfigurationManager.AppSettings[FASTAGI_QUEUEDREQUESTS_ALERT_THRESHOLD], out FastAGIQueuedRequestsAlertThreshold);
                FastAGIQueueMetricsFilePath = ConfigurationManager.AppSettings[FASTAGI_QUEUEMETRICS_FILE_PATH];
                FastAGIAppMetricsFilePath = ConfigurationManager.AppSettings[FASTAGI_APPMETRICS_FILE_PATH];
                AlertEmailAddress = ConfigurationManager.AppSettings[ALERT_EMAIL_ADDRESS];
			}
			catch(Exception excp)
			{
				logger.Error("Exception FastAGIConfiguration. "  + excp.Message);
			}
		}
	}
}
