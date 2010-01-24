// ============================================================================
// FileName: JobQueueDaemon.cs
//
// Description:
// Queues objects, typically network connections of some kind, and uses a pool
// of threads to process them on a FIFO basis. Alerts and metrics are also
// provided.
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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using log4net;

namespace SIPSorcery.Asterisk.FastAGI
{
    public delegate AppTimeMetric ProcessQueuedJobDelegate(Object job);
    public delegate void QueueSizeAlertDelegate(int queueSize, int threshold);
    
    public struct AppTimeMetric
    {
        public DateTime MeasurementTime;
        public string AppName;
        public double ProcessingTime;

        public AppTimeMetric(DateTime measurementTime, string appName, double processingTime)
        {
            MeasurementTime = measurementTime;
            AppName = appName;
            ProcessingTime = processingTime;
        }
    }
    
    public struct QueuesSizeMetric
    {
        public DateTime MeasurementTime;
        public int QueueSize;

        public QueuesSizeMetric(DateTime measurementTime, int queueSize)
        {
            MeasurementTime = measurementTime;
            QueueSize = queueSize;
        }
    }
    
    public class JobQueueDameon
	{
		private const int MAX_THREAD_WAITTIME = 30000;	// 5 minutes.
        private const int ALERT_PERIOD_MINUTES = 5;
        private const int MAX_METRIC_MEASUREMENTS = 5000;
        private const int METRICS_TOFILE_PERIOD = 15;   // Dump the metrics queue to file every 15s.

        public Queue<Object> m_jobQueue = new Queue<Object>();
        private AutoResetEvent m_workerThreadARE = new AutoResetEvent(false);    // When a new request is ready this will be fired to activate a worker thread.
		
        private ILog logger = LogManager.GetLogger("jqdaemon");

        private int m_jobQueueAlertThreshold;
        private DateTime m_lastMetricsWriteTime = DateTime.Now;
        private StreamWriter m_queueMetricsStreamWriter = null;
        private StreamWriter m_appMetricsStreamWriter = null;
        private Queue<QueuesSizeMetric> m_queueSizeMetrics = new Queue<QueuesSizeMetric>();
        private Queue<AppTimeMetric> m_appTimeMetrics = new Queue<AppTimeMetric>();
        private bool m_useMetrics = false;  // if the metrics file streams are available this will be set to true.

        public event ProcessQueuedJobDelegate ProcessJobEvent;
        public event QueueSizeAlertDelegate QueueSizeAlertEvent;

        private bool m_close = false;
        private int m_numberThreads = 0;
        private int m_closedThreads = 0;
        private ManualResetEvent m_allThreadsClosedMRE = new ManualResetEvent(false);   // Used to make sure all threads are closed before returning.
        
        public JobQueueDameon(int queueAlertThreshold, string queueSizeMetricsPath, string appTimesMetrisPath)
		{
            try
            {
                m_jobQueueAlertThreshold = queueAlertThreshold;

                if (appTimesMetrisPath != null && appTimesMetrisPath.Trim().Length > 0)
                {
                    m_appMetricsStreamWriter = new StreamWriter(appTimesMetrisPath);
                }

                if (queueSizeMetricsPath != null && queueSizeMetricsPath.Trim().Length > 0)
                {
                    m_queueMetricsStreamWriter = new StreamWriter(queueSizeMetricsPath);
                }

                //m_npgsqlStats = new NpgsqlConnectorStats(m_servicesDBConnStr);
            }
            catch (Exception excp)
            {
                logger.Error("Exception JobQueueDameon Initialising Metrics Files. " + excp.Message);
            }

            if (m_appMetricsStreamWriter != null && m_queueMetricsStreamWriter != null)
            {
                logger.Debug("Metrics enabled for " + queueSizeMetricsPath + " and " + appTimesMetrisPath + ".");
                m_useMetrics = true;
            }
            else
            {
                logger.Debug("Metrics disabled.");
            }
		}
		
		public void AddNewConnection(Object job)
		{
            try
            {
                lock (m_jobQueue)
                {
                    m_jobQueue.Enqueue(job);
                }

                if (m_jobQueue.Count > m_jobQueueAlertThreshold)
                {
                    logger.Warn("JobQueueDaemon Queued Request Threshold Exceeded, current queue size=" + m_jobQueue.Count + ">" + m_jobQueueAlertThreshold + ".");

                    if (QueueSizeAlertEvent != null)
                    {
                        QueueSizeAlertEvent(m_jobQueue.Count, m_jobQueueAlertThreshold);
                    }
                    else
                    {
                        logger.Warn("QueueSizeAlert event not fired as no event handler in place.");
                    }
                }

                m_workerThreadARE.Set();

                #region Queue Size and Application Processing Time metrics.

                if (m_useMetrics)
                {
                    bool exclusive = Monitor.TryEnter(m_queueSizeMetrics, 0);
                    if (exclusive)
                    {
                        try
                        {
                            //logger.Debug("ProcessQueue taking queue measurement.");

                            while (m_queueSizeMetrics.Count > MAX_METRIC_MEASUREMENTS)
                            {
                                m_queueSizeMetrics.Dequeue();
                            }
                            m_queueSizeMetrics.Enqueue(new QueuesSizeMetric(DateTime.Now, m_jobQueue.Count));

                            if (DateTime.Now.Subtract(m_lastMetricsWriteTime).TotalSeconds > METRICS_TOFILE_PERIOD)
                            {
                                // Dump queue metric measurements to file.
                                m_queueMetricsStreamWriter.BaseStream.SetLength(0);

                                QueuesSizeMetric[] measurements = m_queueSizeMetrics.ToArray();
                                foreach (QueuesSizeMetric measurement in measurements)
                                {
                                    m_queueMetricsStreamWriter.WriteLine(measurement.MeasurementTime.Ticks + "," + measurement.QueueSize);
                                }
                                m_queueMetricsStreamWriter.Flush();

                                // Dump app metric measurements to file.
                                m_appMetricsStreamWriter.BaseStream.SetLength(0);
                                AppTimeMetric[] appMeasurements = null;
                                if (Monitor.TryEnter(m_appTimeMetrics, 0))
                                {
                                    appMeasurements = m_appTimeMetrics.ToArray();
                                    Monitor.Exit(m_appTimeMetrics);
                                }

                                if (appMeasurements != null)
                                {
                                    foreach (AppTimeMetric appMeasurement in appMeasurements)
                                    {
                                        m_appMetricsStreamWriter.WriteLine(appMeasurement.MeasurementTime.Ticks + "," + appMeasurement.AppName + "," + appMeasurement.ProcessingTime.ToString("0.##"));
                                    }
                                }
                                m_appMetricsStreamWriter.Flush();

                                m_lastMetricsWriteTime = DateTime.Now;
                            }
                        }
                        catch (Exception metricExcp)
                        {
                            logger.Error("Exception JobQueueDaemon Metrics. " + metricExcp.Message);
                        }
                        finally
                        {
                            Monitor.Exit(m_queueSizeMetrics);
                        }
                    }
                    else
                    {
                        logger.Debug("Queue metrics busy, measurement not taken.");
                    }
                }

                #endregion
            }
            catch (Exception excp)
            {
                logger.Debug("Exception JobQueueDameon AddNewConnection. " + excp.Message);
            }
		}

		public void Start(int numberThreads, string threadName)
		{
			try
			{
                m_numberThreads = numberThreads;
                
                for(int index=1; index<=numberThreads; index++)
				{
					Thread workerThread = new Thread(new ThreadStart(ProcessQueue));
					workerThread.Name = "jqd-" + threadName.Trim() + "-" + index.ToString();
					workerThread.Start();
				}
			}
			catch(Exception excp)
			{
				logger.Error("Exception JobQueueDaemon Start. " + excp.Message);
				throw excp;
			}
		}

		public void ProcessQueue()
		{
            try
            {
                logger.Debug("JobQueueDaemon Thread " + Thread.CurrentThread.Name + " started.");

                while (!m_close)
                {
                    while (m_jobQueue.Count > 0 && !m_close)
                    {
                        #region Dequeue waiting job and process.

                        try
                        {
                            Object job = null;
                            
                            lock (m_jobQueue)
                            {
                                job = m_jobQueue.Dequeue();
                            }

                            if (job != null && ProcessJobEvent != null)
                            {
                                AppTimeMetric appMetric = ProcessJobEvent(job);

                                logger.Debug(appMetric.AppName + " took " + appMetric.ProcessingTime.ToString("0.##") + "ms.");

                                if (m_useMetrics)
                                {
                                    while (m_appTimeMetrics.Count > MAX_METRIC_MEASUREMENTS)
                                    {
                                        m_appTimeMetrics.Dequeue();
                                    }
                                    m_appTimeMetrics.Enqueue(appMetric);
                                }
                            }
                            else
                            {
                                logger.Warn("Queued job was discarded as there was no event handler allocated to process jobs.");
                            }
                        }
                        catch (Exception workerExcp)
                        {
                            logger.Error("Exception JobQueueDaemon processing job. " + workerExcp.Message);
                        }

                        #endregion
                    }

                    // No more requests outstanding, put thread to sleep until a new request is received.
                    if (!m_close)
                    {
                        m_workerThreadARE.WaitOne();
                        logger.Debug("Thread " + Thread.CurrentThread.Name + " signalled.");
                    }
                }

                logger.Debug("Closing thread " + Thread.CurrentThread.Name + ".");
            }
            catch (Exception excp)
            {
                logger.Error("Exception JobQueueDaemon ProcessQueue. " + excp.Message);
            }
            finally
            {
                m_closedThreads++;

                if (m_closedThreads == m_numberThreads)
                {
                    logger.Debug("All threads " + m_numberThreads + " closed.");
                    m_allThreadsClosedMRE.Set();
                }
                else
                {
                    logger.Debug(m_closedThreads + " of " + m_numberThreads + " closed.");

                    if (m_close)
                    {
                        // Signal other threads time to close.
                        m_workerThreadARE.Set();
                    }
                }
            }
		}

        public void Stop()
        {
            try
            {
                logger.Debug("JobQueueDeamon Stop.");

                m_close = true;

                m_allThreadsClosedMRE.Reset();

                m_workerThreadARE.Set();

                // Wait for all threads to close or 2s.
                m_allThreadsClosedMRE.WaitOne(2000, false);

                logger.Debug("JobQueueDeamon stopped.");
            }
			catch(Exception excp)
			{
                logger.Error("Exception JobQueueDeamon Stop. " + excp.Message);
			}
        }
	}
}
