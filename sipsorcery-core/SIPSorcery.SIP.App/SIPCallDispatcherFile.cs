using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App
{
    public class SIPCallDispatcherFile
    {
        public const int USEALWAYS_APPSERVER_PRIORITY = 100;
        public const int DISABLED_APPSERVER_PRIORITY = 0;
        public const int NEVERUSE_APPSERVER_PRIORITY = -1;

        private static ILog logger = AppState.logger;

        private SIPMonitorLogDelegate SendMonitorEvent_External;

        private string m_appServerEndPointsPath;
        private Dictionary<string, int> m_appServerEndPoints;  // [<SIPEndPoint>, <int>] the integer value represents the relative weighting of the end point.
        private ScriptLoader m_appServerEndPointsLoader;

        public SIPCallDispatcherFile(SIPMonitorLogDelegate sendMonitorEvent, string path)
        {
            SendMonitorEvent_External = sendMonitorEvent;
            m_appServerEndPointsPath = path;
        }

        public void LoadAndWatch()
        {
            if (!m_appServerEndPointsPath.IsNullOrBlank() && File.Exists(m_appServerEndPointsPath))
            {
                m_appServerEndPointsLoader = new ScriptLoader(SendMonitorEvent_External, m_appServerEndPointsPath);
                m_appServerEndPointsLoader.ScriptFileChanged += (s, e) => { m_appServerEndPoints = LoadAppServerEndPoints(m_appServerEndPointsLoader.GetText()); };
                m_appServerEndPoints = LoadAppServerEndPoints(m_appServerEndPointsLoader.GetText());
            }
            else
            {
                throw new ApplicationException("SIPCallDispatcherFile was passed a path to a non-existent file, " + m_appServerEndPointsPath + ".");
            }
        }

        public bool IsAppServerEndPoint(SIPEndPoint remoteEndPoint)
        {
            if (m_appServerEndPoints == null || m_appServerEndPoints.Count == 0)
            {
                return false;
            }
            else
            {
                return m_appServerEndPoints.ContainsKey(remoteEndPoint.ToString());
            }
        }

        public void UpdateAppServerPriority(SIPEndPoint appServerEndPoint, int priority)
        {
            try
            {
                string appServerEndPointsText = null;
                using (StreamReader sr = new StreamReader(m_appServerEndPointsPath))
                {
                    appServerEndPointsText = sr.ReadToEnd();
                }

                Dictionary<string, int> appServerPriorities = LoadAppServerEndPoints(appServerEndPointsText);

                    bool changed = false;

                    if (appServerPriorities.ContainsKey(appServerEndPoint.ToString()))
                    {
                        if (appServerPriorities[appServerEndPoint.ToString()] != NEVERUSE_APPSERVER_PRIORITY)
                        {
                            appServerPriorities[appServerEndPoint.ToString()] = priority;
                            logger.Debug("SIPCallDispatcherFile updated priority on " + appServerEndPoint + " to " + priority + ".");
                            changed = true;
                        }
                        else
                        {
                            logger.Debug("SIPCallDispatcherFile did NOT priority on disabled App Server endpoint " + appServerEndPoint + ".");
                        }
                    }

                    if (changed)
                    {
                        using (StreamWriter sw = new StreamWriter(m_appServerEndPointsPath, false, Encoding.ASCII))
                        {
                            foreach (KeyValuePair<string, int> appServerPriority in appServerPriorities)
                            {
                                sw.WriteLine(appServerPriority.Value + "," + appServerPriority.Key);
                            }
                        }
                    }
            }
            catch (Exception excp)
            {
                logger.Error("Exception UpdateAppServerPriority. " + excp.Message);
            }
        }

        public SIPEndPoint GetAppServer()
        {
            if (m_appServerEndPoints == null || m_appServerEndPoints.Count == 0)
            {
                return null;
            }
            else
            {
                lock (m_appServerEndPoints)
                {
                    foreach (KeyValuePair<string, int> appServerEntry in m_appServerEndPoints)
                    {
                        if (appServerEntry.Value == USEALWAYS_APPSERVER_PRIORITY)
                        {
                            //logger.Debug("GetAppServer returning " + appServerEntry.Key + ".");
                            return SIPEndPoint.ParseSIPEndPoint(appServerEntry.Key);
                        }
                    }
                }

                logger.Warn("No available app server entry could be found by SIPCallDispatcherFile in " + m_appServerEndPointsPath + ".");
                return null;
            }
        }

        private Dictionary<string, int> LoadAppServerEndPoints(string appServerFileText)
        {
            try
            {
                logger.Debug("SIPCallDispatcherFile loading application server endpoints.");

                if (!appServerFileText.IsNullOrBlank())
                {
                    Dictionary<string, int> appServerEndPoints = new Dictionary<string, int>();

                    appServerFileText.Split('\r', '\n').ToList().ForEach((appServerLine) =>
                    {
                        if (!appServerLine.IsNullOrBlank())
                        {
                            appServerLine = appServerLine.Trim();
                            int commaIndex = appServerLine.IndexOf(',');
                            if (commaIndex != -1)
                            {
                                int priority = Convert.ToInt32(appServerLine.Substring(0, commaIndex));
                                SIPEndPoint appServerEndPoint = SIPEndPoint.ParseSIPEndPoint(appServerLine.Substring(commaIndex + 1));
                                appServerEndPoints.Add(appServerEndPoint.ToString(), priority);

                                logger.Debug(" added app server endpoint " + appServerEndPoint + " with a relative priority of " + priority + ".");
                            }
                            else
                            {
                                logger.Warn(" an app server entry in the call dispatcher file was invalid, " + appServerLine + ".");
                            }
                        }
                    });

                    return appServerEndPoints;
                }
                else
                {
                    return null;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception LoadAppServerEndPoints. " + excp.Message);
                return null;
            }
        }
    }
}
