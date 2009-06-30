// ============================================================================
// FileName: DialPlanExecutingScript.cs
//
// Description:
// Represents an executing instance of a script dial plan.
//
// Author(s):
// Aaron Clauson
//
// History:
// 15 Jun 2009  Aaron Clauson   Refwctored from DialPlanEngine
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;
using Microsoft.Scripting.Hosting;

namespace SIPSorcery.AppServer.DialPlan
{
    public class DialPlanExecutingScript
    {
        public const int MAX_SCRIPTPROCESSING_SECONDS = 10;           // The maximum amount of time a script will be able to execute for without completing or executing a Dial command.

        private static int ScriptCounter;

        private static ILog logger = AppState.GetLogger("dialplanengine");

        public Guid Id;
        public Thread DialPlanScriptThread;
        public ScriptScope DialPlanScriptScope;
        public bool InUse;
        public bool Complete;
        public DialPlanContext ExecutingDialPlanContext;
        public string Owner;
        public DateTime StartTime;
        public DateTime EndTime;
        public SIPMonitorLogDelegate LogDelegate;
        public int ScriptNumber;
        public string ExecutionError;               // Set if there was an exeception attempting to execute the script.
        public SIPResponseStatusCodesEnum LastFailureStatus;
        public string LastFailureReason;

        public DialPlanExecutingScript(ScriptScope scriptScope, SIPMonitorLogDelegate logDelegate)
        {
            ScriptNumber = ++ScriptCounter % Int32.MaxValue;
            Id = Guid.NewGuid();
            DialPlanScriptScope = scriptScope;
            LogDelegate = logDelegate;
        }

        public void Initialise(DialPlanContext dialPlanContext)
        {
            ExecutingDialPlanContext = dialPlanContext;
            Owner = ExecutingDialPlanContext.Owner;
            StartTime = DateTime.Now;
            EndTime = StartTime.AddSeconds(MAX_SCRIPTPROCESSING_SECONDS);
            InUse = true;
            Complete = false;
            ExecutionError = null;
            LastFailureStatus = SIPResponseStatusCodesEnum.None;
            LastFailureReason = null;
            //DialPlanScriptScope.ClearVariables();
        }

        /// <remarks>
        /// I have not found a way to externally halt a DLR script from executing the approach used here is to
        /// put the thread the script is executing on to sleep adnd let the monitor thread abort it. The thread
        /// should only ever be asleep for approx 500ms (or whatever check period the monitor thread is running with).
        /// </remarks>
        public void StopExecution()
        {
            try
            {
                Complete = true;
                DialPlanScriptThread.Suspend();
            }
            catch (Exception excp)
            {
                logger.Warn("Exception DialPlanExecutingScript StopExecution. " + excp.Message);
            }
        }

        public void Clear()
        {
            //logger.Debug("Clearing DialPlanExecutingScript.");
            ExecutingDialPlanContext = null;
            Owner = null;
            InUse = false;
            Complete = true;
            ExecutionError = null;
            LastFailureStatus = SIPResponseStatusCodesEnum.None;
            LastFailureReason = null;
        }
    }
}
