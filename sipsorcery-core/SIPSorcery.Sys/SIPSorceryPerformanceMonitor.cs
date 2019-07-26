// ============================================================================
// FileName: SIPSorceryPerformanceMonitor.cs
//
// Description:
// Contains helper and configuration functions to allow application wide use of
// Windows performance monitor counters to monitor application metrics.
//
// Author(s):
// Aaron Clauson
//
// History:
// 27 Jul 2010  Aaron Clauson   Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
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
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Sys
{

    public class SIPSorceryPerformanceMonitor
    {
        private const string PERFORMANCE_COUNTER_CATEGORY_NAME = "SIPSorcery";

        public const string PROXY_PREFIX = "Proxy";
        public const string REGISTRAR_PREFIX = "Registrar";
        public const string REGISTRATION_AGENT_PREFIX = "RegistrationAgent";

        public const string SIP_TRANSPORT_STUN_REQUESTS_PER_SECOND_SUFFIX = "STUNRequestsPerSecond";
        public const string SIP_TRANSPORT_SIP_REQUESTS_PER_SECOND_SUFFIX = "SIPRequestsPerSecond";
        public const string SIP_TRANSPORT_SIP_RESPONSES_PER_SECOND_SUFFIX = "SIPResponsesPerSecond";
        public const string SIP_TRANSPORT_SIP_BAD_MESSAGES_PER_SECOND_SUFFIX = "SIPBadMessagesPerSecond";

        public const string PROXY_STUN_REQUESTS_PER_SECOND = PROXY_PREFIX + SIP_TRANSPORT_STUN_REQUESTS_PER_SECOND_SUFFIX;
        public const string PROXY_SIP_REQUESTS_PER_SECOND = PROXY_PREFIX + SIP_TRANSPORT_SIP_REQUESTS_PER_SECOND_SUFFIX;
        public const string PROXY_SIP_RESPONSES_PER_SECOND = PROXY_PREFIX + SIP_TRANSPORT_SIP_RESPONSES_PER_SECOND_SUFFIX;
        public const string PROXY_SIP_BAD_MESSAGES_PER_SECOND = PROXY_PREFIX + SIP_TRANSPORT_SIP_BAD_MESSAGES_PER_SECOND_SUFFIX;

        public const string REGISTRAR_STUN_REQUESTS_PER_SECOND = REGISTRAR_PREFIX + SIP_TRANSPORT_STUN_REQUESTS_PER_SECOND_SUFFIX;
        public const string REGISTRAR_SIP_REQUESTS_PER_SECOND = REGISTRAR_PREFIX + SIP_TRANSPORT_SIP_REQUESTS_PER_SECOND_SUFFIX;
        public const string REGISTRAR_SIP_RESPONSES_PER_SECOND = REGISTRAR_PREFIX + SIP_TRANSPORT_SIP_RESPONSES_PER_SECOND_SUFFIX;
        public const string REGISTRAR_SIP_BAD_MESSAGES_PER_SECOND = REGISTRAR_PREFIX + SIP_TRANSPORT_SIP_BAD_MESSAGES_PER_SECOND_SUFFIX;
        public const string REGISTRAR_REGISTRATION_REQUESTS_PER_SECOND = REGISTRAR_PREFIX + "RegistersReceivedPerSecond";

        public const string REGISTRATION_AGENT_STUN_REQUESTS_PER_SECOND = REGISTRATION_AGENT_PREFIX + SIP_TRANSPORT_STUN_REQUESTS_PER_SECOND_SUFFIX;
        public const string REGISTRATION_AGENT_SIP_REQUESTS_PER_SECOND = REGISTRATION_AGENT_PREFIX + SIP_TRANSPORT_SIP_REQUESTS_PER_SECOND_SUFFIX;
        public const string REGISTRATION_AGENT_SIP_RESPONSES_PER_SECOND = REGISTRATION_AGENT_PREFIX + SIP_TRANSPORT_SIP_RESPONSES_PER_SECOND_SUFFIX;
        public const string REGISTRATION_AGENT_SIP_BAD_MESSAGES_PER_SECOND = REGISTRATION_AGENT_PREFIX + SIP_TRANSPORT_SIP_BAD_MESSAGES_PER_SECOND_SUFFIX;
        public const string REGISTRATION_AGENT_REGISTRATIONS_PER_SECOND = REGISTRATION_AGENT_PREFIX + "RegistrationsPerSecond";

        private static ILog logger = AppState.logger;

        private static IPerformanceMonitor m_performanceMonitor;

        public static void Initialise(IPerformanceMonitor performanceMonitor)
        {
            m_performanceMonitor = performanceMonitor;
        }

        public static void IncrementCounter(string counterName)
        {
            IncrementCounter(counterName, 1);
        }

        public static void IncrementCounter(string counterName, int incrementBy)
        {
            try
            {
                m_performanceMonitor?.IncrementCounter(counterName, incrementBy);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPSorceryPerformanceMonitor IncrementCounter (" + counterName + "). " + excp.Message);
            }
        }
    }
}
