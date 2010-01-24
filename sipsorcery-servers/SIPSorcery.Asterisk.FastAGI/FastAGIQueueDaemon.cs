// ============================================================================
// FileName: FastAGIQueueDaemon.cs
//
// Description:
// Queues new FastAGI connections.
//
// Author(s):
// Aaron Clauson
//
// History:
// 03 May 2006	Aaron Clauson	Created.
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
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Asterisk.FastAGI
{
    public class FastAGIQueueDaemon : JobQueueDameon
    {
        private const int ALERT_PERIOD_MINUTES = 5;     // Spacing between alert notification emails.

        private ILog logger = AppState.GetLogger("fastagi");

        private string m_localIPAddress = FastAGIConfiguration.AGIServerIPAddress;
        private string m_alertEmailAddress;
        private DateTime m_lastQueueSizeEmailAlertTime = DateTime.MinValue;
        private DateTime m_lastExceptionEmailAlertTime = DateTime.MinValue;

        public FastAGIQueueDaemon(int queueAlertThreshold, string queueMetricsPath, string appMetricsPath, string alertEmailAddress)
            : base(queueAlertThreshold, queueMetricsPath, appMetricsPath)
        {
            //logger.Debug("FastAGIQueueDaemon threshold=" + queueAlertThreshold + ", queuemetrics=" + queueMetricsPath + ", appmetrics=" + appMetricsPath + ".");

            m_alertEmailAddress = alertEmailAddress;
            base.QueueSizeAlertEvent += new QueueSizeAlertDelegate(QueueSizeAlert);
            base.ProcessJobEvent += new ProcessQueuedJobDelegate(ProcessJob);
        }

        private AppTimeMetric ProcessJob(object job)
        {
            Socket fastAGISocket = null;

            try
            {
                fastAGISocket = (Socket)job;
                fastAGISocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, true);

                logger.Debug("fastagi connection from " + IPSocket.GetSocketString((IPEndPoint)fastAGISocket.RemoteEndPoint) + "(" + Thread.CurrentThread.Name + " " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + ")");

                byte[] buffer = new byte[2048];
                int bytesRead = 1;

                // Caution - it could take the Asterisk server more than one socket send to get the all the request parameters sent.
                StringBuilder request = new StringBuilder();
                while (bytesRead > 0)
                {
                    bytesRead = fastAGISocket.Receive(buffer, 0, 2048, SocketFlags.None);
                    request.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));
                    logger.Debug(Encoding.ASCII.GetString(buffer, 0, bytesRead));

                    if (request.ToString() != null && (Regex.Match(request.ToString(), @"\n\n", RegexOptions.Singleline).Success || Regex.Match(request.ToString(), @"\r\n\r\n", RegexOptions.Singleline).Success))
                    {
                        break;
                    }
                }

                FastAGIRequest fastAGIRequest = new FastAGIRequest();
                return fastAGIRequest.Run(fastAGISocket, request.ToString());
            }
            catch (Exception excp)
            {
                logger.Error("Exception FastAGIWorker ProcessJobEvent. " + excp.Message);
                ExceptionAlert(excp.Message);
                return new AppTimeMetric();
            }
            finally
            {
                if (fastAGISocket != null)
                {
                    try
                    {
                        logger.Debug("connection closed.");
                        fastAGISocket.Close();
                    }
                    catch(Exception sockCkoseExcp)
                    {
                        logger.Error("Exception FastAGIQueueDaemon ProceesJob (closing AGI socket). " + sockCkoseExcp);
                    }
                }
            }
        }

        private void QueueSizeAlert(int queueSize, int threshold)
        {
            try
            {
                if (DateTime.Now.Subtract(m_lastQueueSizeEmailAlertTime).TotalMinutes > ALERT_PERIOD_MINUTES)
                {
                    m_lastQueueSizeEmailAlertTime = DateTime.Now;
                    //Email.SendEmail(m_alertEmailAddress, m_alertEmailAddress, "FastAGI Queued Request Threshold Exceeded on " + m_localIPAddress + " at " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss"), "Current queue size=" + queueSize + ", threshold=" + threshold + ".");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FastAGIWorker QueueSizeAlertEvent. " + excp.Message);
            }
        }

        private void ExceptionAlert(string exceptionMessage)
        {
            try
            {
                if (DateTime.Now.Subtract(m_lastExceptionEmailAlertTime).TotalMinutes > ALERT_PERIOD_MINUTES)
                {
                    m_lastExceptionEmailAlertTime = DateTime.Now;
                    //Email.SendEmail(m_alertEmailAddress, m_alertEmailAddress, "FastAGI Exception " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + " from " + m_localIPAddress, exceptionMessage);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FastAGIQueueDaemon ExceptionAlert. " + excp);
            }
        }
    }
}
