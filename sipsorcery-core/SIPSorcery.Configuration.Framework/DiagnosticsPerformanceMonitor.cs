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
using System.Diagnostics;
using log4net;

namespace SIPSorcery.Sys
{
    public class DiagnosticsPerformanceMonitor : IPerformanceMonitor
    {
        private readonly ILog logger;
        private readonly string categoryName;

        private readonly Dictionary<string, PerformanceCounterType> m_counterNames = new Dictionary<string, PerformanceCounterType>();
        private readonly Dictionary<string, PerformanceCounter> m_counters = new Dictionary<string, PerformanceCounter>();

        private bool m_sipsorceryCategoryReady;

        public DiagnosticsPerformanceMonitor(ILog logger, string categoryName, IEnumerable<string> counterNames)
        {
            this.logger = logger;
            this.categoryName = categoryName;

            foreach (var counter in counterNames)
                m_counterNames.Add(counter, PerformanceCounterType.RateOfCountsPerSecond32);

            m_sipsorceryCategoryReady = CheckCounters();
        }

        public void IncrementCounter(string counterName, int incrementBy)
        {
            if (m_sipsorceryCategoryReady)
            {
                if (m_counters.ContainsKey(counterName))
                {
                    m_counters[counterName].IncrementBy(incrementBy);
                }
                else
                {
                    var counter = new PerformanceCounter(categoryName, counterName, false);
                    m_counters.Add(counterName, counter);
                    counter.IncrementBy(incrementBy);
                }
            }
        }

        private bool CheckCounters()
        {
            try
            {
                if (!PerformanceCounterCategory.Exists(categoryName))
                {
                    CreateCategory();
                }
                else
                {
                    foreach (var counter in m_counterNames)
                    {
                        if (!PerformanceCounterCategory.CounterExists(counter.Key, categoryName))
                        {
                            CreateCategory();
                            break;
                        }
                    }
                }

                return true;
            }
            catch (Exception excp)
            {
                logger.Error("Exception DiagnosticsPerformanceMonitor CheckCounters. " + excp.Message);
            }

            return false;
        }

        private void CreateCategory()
        {
            try
            {
                logger.Debug($"DiagnosticsPerformanceMonitor creating {categoryName} category.");

                if (PerformanceCounterCategory.Exists(categoryName))
                {
                    PerformanceCounterCategory.Delete(categoryName);
                }

                var ccdc = new CounterCreationDataCollection();

                foreach (var counter in m_counterNames)
                {
                    ccdc.Add(new CounterCreationData
                    {
                        CounterType = counter.Value,
                        CounterName = counter.Key
                    });

                    logger.Debug("DiagnosticsPerformanceMonitor added counter " + counter.Key + ".");
                }

                PerformanceCounterCategory.Create(categoryName, "SIP Sorcery performance counters", PerformanceCounterCategoryType.SingleInstance, ccdc);
            }
            catch (Exception excp)
            {
                logger.Error("Exception DiagnosticsPerformanceMonitor CreateCategory. " + excp.Message);
                throw;
            }
        }
    }
}
