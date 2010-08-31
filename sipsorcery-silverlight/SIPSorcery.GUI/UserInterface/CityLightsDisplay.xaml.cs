using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery
{
	public partial class CityLightsDisplay : UserControl
	{
        private const int HIT_POINT_RADIUS = 5;
        private const int HIT_POINT_DURATION = 60;

        private double m_mapWidth;
        private double m_mapHeight;

        private delegate void AddHitPointDelegate(double row, double column);

        private class HeatMapHitPoint
        {
            private static Duration m_hitPointDuration = new Duration(TimeSpan.FromSeconds(HIT_POINT_DURATION));

            public int GeoId;
            public string CityName;
            public double Latitude;
            public double Longitude;
            public Ellipse HitPointShape;
            public DoubleAnimation HitPointAnimation;
            public Storyboard HitPointStoryboard;

            private Canvas m_canvas;

            public HeatMapHitPoint(Canvas map, double latitude, double longitude)
            {
                m_canvas = map;
                Latitude = latitude;
                Longitude = longitude;

                HitPointShape = new Ellipse();
                HitPointShape.Width = HIT_POINT_RADIUS;
                HitPointShape.Height = HIT_POINT_RADIUS;
                HitPointShape.SetValue(Canvas.TopProperty, latitude);
                HitPointShape.SetValue(Canvas.LeftProperty, longitude);
                HitPointShape.Fill = new SolidColorBrush(Color.FromArgb(0xff, 0x93, 0xFF, 0x00));
                HitPointShape.Stroke = new SolidColorBrush(Color.FromArgb(0xff, 0x93, 0xFF, 0x00));

                HitPointAnimation = new DoubleAnimation();
                Storyboard.SetTarget(HitPointAnimation, HitPointShape);
                Storyboard.SetTargetProperty(HitPointAnimation, new PropertyPath("(UIElement.Opacity)"));
                HitPointAnimation.To = 0;

                HitPointStoryboard = new Storyboard();
                HitPointStoryboard.Children.Add(HitPointAnimation);
                //HitPointStoryboard.Completed += new EventHandler(HitPointStoryboard_Completed);
            }

            /*private void HitPointStoryboard_Completed(object sender, EventArgs e)
            {
                m_canvas.Children.Remove(HitPointShape);
                HitPointStoryboard.Completed -= new EventHandler(HitPointStoryboard_Completed);
                HitPointStoryboard = null;
                HitPointAnimation = null;
                HitPointShape = null; 
            }*/
        }

        public CityLightsDisplay()
		{
			InitializeComponent();

            m_mapWidth = m_heatPointMap.Width;
            m_mapHeight = m_heatPointMap.Height;
		}

        public void WriteMonitorMessage(string monitorMessage)
        {
            UIHelper.SetText(m_monitorEventTextBox, monitorMessage);
        }

        public void PlotHeatMapEvent(SIPMonitorMachineEventTypesEnum eventType, IPAddress remoteAddress)
        {
            /*byte[] ipNetworkBytes = remoteAddress.GetAddressBytes();
            long ipNetwork = ipNetworkBytes[0] * 2048 + ipNetworkBytes[1] * 8 + ipNetworkBytes[2];
            int row = (int)ipNetwork / MONITOR_HEATMAP_COLUMNS;
            int column = (int)ipNetwork % MONITOR_HEATMAP_COLUMNS;
            AddHitPoint(row, column);
            UIHelper.AppendText(m_monitorEventTextBox, " " + row + "," + column + ".");*/
        }

        public void RunHitPointSimulation(object state)
        {
            while(true)
            {
                double latitude = Crypto.GetRandomInt(0, (int)m_mapHeight);
                double longitude = Crypto.GetRandomInt(0, (int)m_mapWidth);
                AddHitPoint(latitude, longitude);

                Thread.Sleep(Crypto.GetRandomInt(10, 100));
            }
        }

        public void AddHitPoint(double latitude, double longitude)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new AddHitPointDelegate(AddHitPoint), latitude, longitude);
            }
            else
            {
                HeatMapHitPoint hitPoint = new HeatMapHitPoint(m_heatPointMap, latitude, longitude);
                m_heatPointMap.Children.Add(hitPoint.HitPointShape);
                hitPoint.HitPointStoryboard.Begin();
            }
        }
    }
}