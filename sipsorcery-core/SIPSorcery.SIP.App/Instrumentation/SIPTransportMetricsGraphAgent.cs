// ============================================================================
// FileName: SIPTransportMetricsGraphAgent.cs
//
// Description:
// Graphs data from a the SIPTransport measurements.
//
// Author(s):
// Aaron Clauson
//
// History:
// 22 Feb 2008	Aaron Clauson	Created.
//
// License: 
// Aaron Clauson
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using SIPSorcery.Sys;
using log4net;
using ZedGraph;

namespace SIPSorcery.SIP.App
{
    /// <summary>
    /// Graphs results from a the SIPTransport metrics file. The metrics are stored in a format of:
    /// 
    /// sample time,total packets,sip requests in,sip responses in,sip requests sent,sip responses sent,pending sip transactions,unrecognised packets,bad sip packets in,stun requests,discard packets,total parse time,avg parse time
    /// 
    /// The graph produced is a multi series line graph.
    /// </summary>
    public class SIPTransportMetricsGraphAgent
	{
        private const int GRAPH_WIDTH = 850;
        private const int GRAPH_HEIGHT = 500;
        private const int GRAPH_SAMPLES = 5000;          // Number of response times to record for the graph.
        private const int GRAPH_SPACING = 60000;
        private const int NUMBER_TOPTALKERS_TOPPLOT = 6;

        private static ILog logger = AppState.logger;

        private static readonly string m_trafficMetrics = SIPTransportMetric.PACKET_VOLUMES_KEY;
        private static readonly string m_methodMetrics = SIPTransportMetric.SIPMETHOD_VOLUMES_KEY;
        private static readonly string m_topTalkerMetrics = SIPTransportMetric.TOPTALKERS_VOLUME_KEY;

        private string m_localGraphsDir = null;
        private string m_serverFilename = null;
        private string m_metricsFileName;
        private string m_metricsFileCopyName;
        private int m_graphPeriod = GRAPH_SPACING;

        private RollingPointPairList m_totalSIPPacketsList = new RollingPointPairList(GRAPH_SAMPLES);
        private RollingPointPairList m_sipRequestsInList = new RollingPointPairList(GRAPH_SAMPLES);
        private RollingPointPairList m_sipResponsesInList = new RollingPointPairList(GRAPH_SAMPLES);
        private RollingPointPairList m_sipRequestsOutList = new RollingPointPairList(GRAPH_SAMPLES);
        private RollingPointPairList m_sipResponsesOutList = new RollingPointPairList(GRAPH_SAMPLES);
        private RollingPointPairList m_pendingTransactionsList = new RollingPointPairList(GRAPH_SAMPLES);
        private RollingPointPairList m_discardsList = new RollingPointPairList(GRAPH_SAMPLES);
        private RollingPointPairList m_unrecognisedList = new RollingPointPairList(GRAPH_SAMPLES);
        private RollingPointPairList m_tooLargeList = new RollingPointPairList(GRAPH_SAMPLES);
        private RollingPointPairList m_badSIPList = new RollingPointPairList(GRAPH_SAMPLES);
        private RollingPointPairList m_stunList = new RollingPointPairList(GRAPH_SAMPLES);
        private RollingPointPairList m_totalParseTimeList = new RollingPointPairList(GRAPH_SAMPLES);
        private RollingPointPairList m_avgParseTimeList = new RollingPointPairList(GRAPH_SAMPLES);
        private Dictionary<SIPMethodsEnum, RollingPointPairList> m_sipMethodsLists = new Dictionary<SIPMethodsEnum, RollingPointPairList>();
        private Dictionary<string, RollingPointPairList> m_topTalkersLists = new Dictionary<string, RollingPointPairList>();
        private Dictionary<string, int> m_topTalkersCount = new Dictionary<string, int>();  // Used to allow the top 10 top talkers to be discerned.

        private Dictionary<SIPMethodsEnum, Color> m_methodColours = new Dictionary<SIPMethodsEnum, Color>();     // Lookup table for the colours to use in the SIP Methods graph.
        private Color[] m_topTalkerColours = new Color[10];

        private ZedGraphControl m_graphControl;
        private Graphics m_g;

        private bool m_stop = false;
        private ManualResetEvent m_graphingWaitEvent = new ManualResetEvent(false); 

        public SIPTransportMetricsGraphAgent(string localGraphsDir, string metricsFileName, string metricsFileCopy)
        {
            m_localGraphsDir = localGraphsDir;
            //m_serverFilename = serverFileName;
            m_metricsFileName = metricsFileName;
            m_metricsFileCopyName = metricsFileCopy;

            m_graphControl = new ZedGraphControl();
            m_g = m_graphControl.CreateGraphics();

            m_methodColours.Add(SIPMethodsEnum.REGISTER , Color.Red );
            m_methodColours.Add(SIPMethodsEnum.INVITE, Color.Blue);
            m_methodColours.Add(SIPMethodsEnum.BYE, Color.Blue);
            m_methodColours.Add(SIPMethodsEnum.CANCEL, Color.Blue);
            m_methodColours.Add(SIPMethodsEnum.ACK, Color.Blue);
            m_methodColours.Add(SIPMethodsEnum.OPTIONS, Color.Purple);
            m_methodColours.Add(SIPMethodsEnum.UNKNOWN, Color.Orange);
            m_methodColours.Add(SIPMethodsEnum.SUBSCRIBE, Color.Green);
            m_methodColours.Add(SIPMethodsEnum.PING, Color.Green);
            m_methodColours.Add(SIPMethodsEnum.INFO, Color.Green);
            m_methodColours.Add(SIPMethodsEnum.PUBLISH, Color.Green);
            m_methodColours.Add(SIPMethodsEnum.NOTIFY, Color.Aquamarine);

            m_topTalkerColours[0] = Color.FromArgb(252, 2, 4);      // Red.
            m_topTalkerColours[1] = Color.FromArgb(4, 154, 252);    // Navy'ish.
            m_topTalkerColours[2] = Color.FromArgb(4, 154, 156);    // Green'ish.
            m_topTalkerColours[3] = Color.FromArgb(4, 254, 252);    // Aqua.
            m_topTalkerColours[4] = Color.FromArgb(4, 178, 4);      // Flat Green.
            m_topTalkerColours[5] = Color.FromArgb(252, 174, 172);  // Pink'ish.
            m_topTalkerColours[6] = Color.FromArgb(4, 2, 252);      // Blue.
            m_topTalkerColours[7] = Color.FromArgb(252, 202, 4);    // Orange-Yellow.
            m_topTalkerColours[8] = Color.FromArgb(180, 2, 180);    // Purple.
            m_topTalkerColours[9] = Color.Black;
        }

        public void Start(int graphingPeriod)
        {
            if (graphingPeriod != 0)
            {
                m_graphPeriod = graphingPeriod * 1000;
            }

            Thread metricsThread = new Thread(new ThreadStart(StartGraphing));
            metricsThread.Name = "sipmetrics";
            metricsThread.Start();
        }

		public void StartGraphing()
		{
			try
			{
				logger.Debug("SIPTransport Metrics Graph Agent starting");

                while (!m_stop)
                {
                    UpdateGraph();
                    m_graphingWaitEvent.WaitOne(m_graphPeriod, false);
                }
			}
			catch(Exception excp)
			{
                logger.Error("Exception SIPTransportMetricsGraphAgent Start. " + excp.Message);
			}
		}

        public void Stop()
        {
            m_stop = true;
            m_graphingWaitEvent.Set();
        }

        public void UpdateGraph()
        {
            try
            {
                try
                {
                    DateTime startTime = DateTime.Now;
                    
                    // Take a copy of the metrics file.
                    if (File.Exists(m_metricsFileCopyName))
                    {
                        File.Delete(m_metricsFileCopyName);
                    }

                    logger.Debug("Copying " + m_metricsFileName + " to " + m_metricsFileCopyName);
                    File.Copy(m_metricsFileName, m_metricsFileCopyName);

                    StreamReader metricsReader = new StreamReader(m_metricsFileCopyName);
                    m_totalSIPPacketsList.Clear();
                    m_sipRequestsInList.Clear();
                    m_sipResponsesInList.Clear();
                    m_sipRequestsOutList.Clear();
                    m_sipResponsesOutList.Clear();
                    m_pendingTransactionsList.Clear();
                    m_discardsList.Clear();
                    m_unrecognisedList.Clear();
                    m_tooLargeList.Clear();
                    m_badSIPList.Clear();
                    m_stunList.Clear();
                    m_totalParseTimeList.Clear();
                    m_avgParseTimeList.Clear();
                    m_sipMethodsLists = new Dictionary<SIPMethodsEnum, RollingPointPairList>();
                    m_topTalkersLists.Clear();
                    m_topTalkersCount.Clear();

                    string metricsLine = metricsReader.ReadLine();
                    int sampleCount = 0;
                    while (metricsLine != null)
                    {
                        #region Process metrics line.

                        if (metricsLine.Trim().Length != 0 && Regex.Match(metricsLine, ",").Success)
                        {
                            string[] fields = metricsLine.Split(',');
                            XDate sampleDate = new XDate(DateTime.Parse(fields[1]));
                            int samplePeriod = Convert.ToInt32(fields[2]);              // Sample period in seconds.
                            if (samplePeriod == 0)
                            {
                                throw new ApplicationException("The sample period for a measurement was 0 in SIPTransportMetricsGraphAgent.");
                            }
                            
                            if (metricsLine.StartsWith(m_trafficMetrics))
                            {
                                try
                                {
                                    m_totalSIPPacketsList.Add(sampleDate, Convert.ToDouble(fields[3]) / samplePeriod);
                                    m_sipRequestsInList.Add(sampleDate, Convert.ToDouble(fields[4]) / samplePeriod);
                                    m_sipResponsesInList.Add(sampleDate, Convert.ToDouble(fields[5]) / samplePeriod);
                                    m_sipRequestsOutList.Add(sampleDate, Convert.ToDouble(fields[6]) / samplePeriod);
                                    m_sipResponsesOutList.Add(sampleDate, Convert.ToDouble(fields[7]) / samplePeriod);
                                    m_pendingTransactionsList.Add(sampleDate, Convert.ToDouble(fields[8]));
                                    m_unrecognisedList.Add(sampleDate, Convert.ToDouble(fields[9]) / samplePeriod);
                                    m_badSIPList.Add(sampleDate, Convert.ToDouble(fields[10]) / samplePeriod);
                                    m_stunList.Add(sampleDate, Convert.ToDouble(fields[11]) / samplePeriod);
                                    m_discardsList.Add(sampleDate, Convert.ToDouble(fields[12]) / samplePeriod);
                                    m_tooLargeList.Add(sampleDate, Convert.ToDouble(fields[13]) / samplePeriod);
                                    m_totalParseTimeList.Add(sampleDate, Convert.ToDouble(fields[14]) / samplePeriod);
                                    m_avgParseTimeList.Add(sampleDate, Convert.ToDouble(fields[15]));
                                    sampleCount++;
                                }
                                catch (Exception sampleExcp)
                                {
                                    logger.Warn("Could not process metrics sample: " + metricsLine + ". " + sampleExcp.Message);
                                }
                            }
                            else if (metricsLine.StartsWith(m_methodMetrics))
                            {
                                for (int index = 3; index < fields.Length; index++)
                                {
                                    string[] methodSplit = fields[index].Split('=');
                                    SIPMethodsEnum method = SIPMethods.GetMethod(methodSplit[0]);
                                    int methodPackets = Convert.ToInt32(methodSplit[1]) / samplePeriod;

                                    if(!m_sipMethodsLists.ContainsKey(method))
                                    {
                                        m_sipMethodsLists.Add(method, new RollingPointPairList(GRAPH_SAMPLES));
                                    }

                                    m_sipMethodsLists[method].Add(sampleDate, methodPackets);
                                }
                            }
                            else if (metricsLine.StartsWith(m_topTalkerMetrics))
                            {
                                for (int index = 3; index < fields.Length; index++)
                                {
                                    string[] talkersSplit = fields[index].Split('=');
                                    string topTalkerSocket = talkersSplit[0];
                                    int topTalkerPackets = Convert.ToInt32(talkersSplit[1]) / samplePeriod;

                                    if (!m_topTalkersLists.ContainsKey(topTalkerSocket))
                                    {
                                        m_topTalkersLists.Add(topTalkerSocket, new RollingPointPairList(GRAPH_SAMPLES));
                                        m_topTalkersCount.Add(topTalkerSocket, 0);
                                    }

                                    //logger.Debug("Adding point for " + topTalkerSocket + " and " + topTalkerPackets + ".");
                                    m_topTalkersLists[topTalkerSocket].Add(sampleDate, topTalkerPackets);
                                    m_topTalkersCount[topTalkerSocket] = m_topTalkersCount[topTalkerSocket] + topTalkerPackets;
                                }
                            }
                        }

                        #endregion

                        metricsLine = metricsReader.ReadLine();
                    }
                    metricsReader.Close();

                    #region Create the traffic graphs.

                    GraphPane totalSIPPacketsGraphPane = new GraphPane(new Rectangle(0, 0, GRAPH_WIDTH, GRAPH_HEIGHT), "Total SIP Packets per Second", "Time", "Packets/s");
                    GraphPane pendingSIPTransactionsGraphPane = new GraphPane(new Rectangle(0, 0, GRAPH_WIDTH, GRAPH_HEIGHT), "Pending SIP Transactions", "Time", "Total");
                    GraphPane breakdownGraphPane = new GraphPane(new Rectangle(0, 0, GRAPH_WIDTH, GRAPH_HEIGHT), "SIP Request and Responses per Second", "Time", "Packets/s");
                    GraphPane anomaliesGraphPane = new GraphPane(new Rectangle(0, 0, GRAPH_WIDTH, GRAPH_HEIGHT), "Anomalous Packets per Second", "Time", "Packets/s");
                    GraphPane totalParseTimesGraphPane = new GraphPane(new Rectangle(0, 0, GRAPH_WIDTH, GRAPH_HEIGHT), "SIP Packet Parse Time per Second", "Time", "Total Parse Tme (ms)/s");
                    GraphPane averageParseTimesGraphPane = new GraphPane(new Rectangle(0, 0, GRAPH_WIDTH, GRAPH_HEIGHT), "Average SIP Packet Parse Time", "Time", "Average Parse Tme (ms)");

                    totalSIPPacketsGraphPane.Legend.IsVisible = false;
                    totalSIPPacketsGraphPane.XAxis.Type = AxisType.Date;
                    totalSIPPacketsGraphPane.XAxis.Scale.Format = "HH:mm:ss";

                    pendingSIPTransactionsGraphPane.Legend.IsVisible = false;
                    pendingSIPTransactionsGraphPane.XAxis.Type = AxisType.Date;
                    pendingSIPTransactionsGraphPane.XAxis.Scale.Format = "HH:mm:ss";

                    breakdownGraphPane.Legend.Location.AlignH = AlignH.Right;
                    breakdownGraphPane.XAxis.Type = AxisType.Date;
                    breakdownGraphPane.XAxis.Scale.Format = "HH:mm:ss";

                    anomaliesGraphPane.XAxis.Type = AxisType.Date;
                    anomaliesGraphPane.XAxis.Scale.Format = "HH:mm:ss";

                    totalParseTimesGraphPane.XAxis.Type = AxisType.Date;
                    totalParseTimesGraphPane.Legend.IsVisible = false;
                    totalParseTimesGraphPane.XAxis.Scale.Format = "HH:mm:ss";

                    averageParseTimesGraphPane.XAxis.Type = AxisType.Date;
                    averageParseTimesGraphPane.Legend.IsVisible = false;
                    averageParseTimesGraphPane.XAxis.Scale.Format = "HH:mm:ss";

                    LineItem totalSIPPacketsCurve = totalSIPPacketsGraphPane.AddCurve("Total SIP Packets", m_totalSIPPacketsList, Color.Black, SymbolType.None);
                    LineItem pendingTransactionsCurve = pendingSIPTransactionsGraphPane.AddCurve("Pending SIP Transactions", m_pendingTransactionsList, Color.Black, SymbolType.None);
                    LineItem sipRequestsInCurve = breakdownGraphPane.AddCurve("Requests In", m_sipRequestsInList, Color.Blue, SymbolType.None);
                    LineItem sipResponsesInCurve = breakdownGraphPane.AddCurve("Responses In", m_sipResponsesInList, Color.DarkGreen, SymbolType.None);
                    LineItem sipRequestsOutCurve = breakdownGraphPane.AddCurve("Requests Out", m_sipRequestsOutList, Color.BlueViolet, SymbolType.None);
                    LineItem sipResponsesOutCurve = breakdownGraphPane.AddCurve("Responses Out", m_sipResponsesOutList, Color.DarkKhaki, SymbolType.None);
                    LineItem discardsCurve = anomaliesGraphPane.AddCurve("Discards", m_discardsList, Color.Red, SymbolType.None);
                    LineItem badSIPCurve = anomaliesGraphPane.AddCurve("Bad SIP", m_badSIPList, Color.Purple, SymbolType.None);
                    LineItem unrecognisedCurve = anomaliesGraphPane.AddCurve("Unrecognised", m_unrecognisedList, Color.Green, SymbolType.None);
                    LineItem tooLargeCurve = anomaliesGraphPane.AddCurve("Too Large", m_tooLargeList, Color.Coral, SymbolType.None);
                    LineItem stunCurve = anomaliesGraphPane.AddCurve("STUN", m_stunList, Color.Blue, SymbolType.None);
                    LineItem totalParseTimeCurve = totalParseTimesGraphPane.AddCurve("Total Parse Time", m_totalParseTimeList, Color.Black, SymbolType.None);
                    LineItem averageParseTimeCurve = averageParseTimesGraphPane.AddCurve("Average Parse Time", m_avgParseTimeList, Color.Black, SymbolType.None);

                    totalSIPPacketsGraphPane.AxisChange(m_g);
                    pendingSIPTransactionsGraphPane.AxisChange(m_g);
                    breakdownGraphPane.AxisChange(m_g);
                    anomaliesGraphPane.AxisChange(m_g);
                    totalParseTimesGraphPane.AxisChange(m_g);
                    averageParseTimesGraphPane.AxisChange(m_g);

                    Bitmap totalsGraphBitmap = totalSIPPacketsGraphPane.GetImage();
                    totalsGraphBitmap.Save(m_localGraphsDir + "siptotals.png", ImageFormat.Png);

                    Bitmap pendingTransactionsGraphBitmap = pendingSIPTransactionsGraphPane.GetImage();
                    pendingTransactionsGraphBitmap.Save(m_localGraphsDir + "siptransactions.png", ImageFormat.Png);

                    Bitmap breakdownGraphBitmap = breakdownGraphPane.GetImage();
                    breakdownGraphBitmap.Save(m_localGraphsDir + "sipmessagetypes.png", ImageFormat.Png);

                    Bitmap anomaliesGraphBitmap = anomaliesGraphPane.GetImage();
                    anomaliesGraphBitmap.Save(m_localGraphsDir + "anomalies.png", ImageFormat.Png);

                    Bitmap totalParseTimeGraphBitmap = totalParseTimesGraphPane.GetImage();
                    totalParseTimeGraphBitmap.Save(m_localGraphsDir + "siptotalparse.png", ImageFormat.Png);

                    Bitmap averageParseTimeGraphBitmap = averageParseTimesGraphPane.GetImage();
                    averageParseTimeGraphBitmap.Save(m_localGraphsDir + "sipaverageparse.png", ImageFormat.Png);

                    #endregion

                    #region Create SIP methods graph.

                    GraphPane methodsGraphPane = new GraphPane(new Rectangle(0, 0, GRAPH_WIDTH, GRAPH_HEIGHT), "SIP Packets for Method per Second", "Time", "SIP Packets/s");
                    methodsGraphPane.XAxis.Type = AxisType.Date;
                    methodsGraphPane.XAxis.Scale.Format = "HH:mm:ss";

                    foreach (KeyValuePair<SIPMethodsEnum, RollingPointPairList> entry in m_sipMethodsLists)
                    {
                        Color methodColor = (m_methodColours.ContainsKey(entry.Key)) ? m_methodColours[entry.Key] : Color.Black;
                        LineItem methodCurve = methodsGraphPane.AddCurve(entry.Key.ToString(), entry.Value, methodColor, SymbolType.None);
                    }

                    methodsGraphPane.AxisChange(m_g);
                    Bitmap methodsGraphBitmap = methodsGraphPane.GetImage();
                    methodsGraphBitmap.Save(m_localGraphsDir + "sipmethods.png", ImageFormat.Png);

                    #endregion

                    #region Create top talkers graph.

                    // Get the top 10 talkers.
                    if (m_topTalkersCount.Count > 0)
                    {
                        string[] topTalkerSockets = new string[m_topTalkersCount.Count];
                        int[] topTalkerValues = new int[m_topTalkersCount.Count];
                        m_topTalkersCount.Keys.CopyTo(topTalkerSockets, 0);
                        m_topTalkersCount.Values.CopyTo(topTalkerValues, 0);

                        Array.Sort<int, string>(topTalkerValues, topTalkerSockets);

                        GraphPane toptalkersGraphPane = new GraphPane(new Rectangle(0, 0, GRAPH_WIDTH, GRAPH_HEIGHT), "SIP Top Talkers", "Time", "SIP Packets/s");
                        toptalkersGraphPane.XAxis.Type = AxisType.Date;
                        toptalkersGraphPane.XAxis.Scale.Format = "HH:mm:ss";

                        //foreach (KeyValuePair<string, RollingPointPairList> entry in m_topTalkersLists)
                        for (int index = topTalkerSockets.Length - 1; (index >= topTalkerSockets.Length - NUMBER_TOPTALKERS_TOPPLOT && index >= 0); index--)
                        {
                            string socket = topTalkerSockets[index];
                            RollingPointPairList topTalkerPoints = m_topTalkersLists[socket];
                            Color topTalkerColor = m_topTalkerColours[topTalkerSockets.Length - 1 - index];
                            //logger.Debug("Adding curve for " + socket + " (count=" + topTalkerValues[index] + ").");
                            LineItem topTalkersCurve = toptalkersGraphPane.AddCurve(socket, topTalkerPoints, topTalkerColor, SymbolType.None);
                            //break;
                        }

                        toptalkersGraphPane.AxisChange(m_g);
                        Bitmap topTalkersGraphBitmap = toptalkersGraphPane.GetImage();
                        topTalkersGraphBitmap.Save(m_localGraphsDir + "siptoptalkers.png", ImageFormat.Png);
                    }

                    #endregion

                    logger.Debug("Metrics graph for " + m_metricsFileCopyName + " completed in " + DateTime.Now.Subtract(startTime).TotalMilliseconds.ToString("0.##") + "ms, " + sampleCount + " samples.");

                    #region Uplodad file to server.

                    /*if (m_serverFilename != null && m_serverFilename.Trim().Length > 0)
                    {
                        Uri target = new Uri(m_serverFilename);
                        FtpWebRequest request = (FtpWebRequest)WebRequest.Create(target);
                        request.Method = WebRequestMethods.Ftp.UploadFile;
                        request.Credentials = new NetworkCredential("anonymous", "svcmon@blueface.ie");

                        FileStream localStream = File.OpenRead(m_totalsGraphFilename);
                        Stream ftpStream = request.GetRequestStream();
                        byte[] buffer = new byte[localStream.Length];
                        localStream.Read(buffer, 0, buffer.Length);
                        localStream.Close();
                        ftpStream.Write(buffer, 0, buffer.Length);
                        ftpStream.Close();

                        FtpWebResponse response = (FtpWebResponse)request.GetResponse();
                        response.Close();
                        //logger.Debug("Result of ftp upload to " + m_serverFilename + " is " + response.StatusDescription + ".");
                    }*/

                    #endregion
                }
                catch (Exception graphExcp)
                {
                    logger.Error("Exception Saving Graph. " + graphExcp.Message);
                }
            }
            catch (Exception excp)
            {
                logger.Debug("Exception UpdateGraph. " + excp.Message);
            }
        }
	}
}
