using System;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SIPSorcery.TheFlow
{
    public class FlowChartGraphics
    {
        private const double ARROW_WIDTH = 4;
        private const double ARROW_HEIGHT = 6;

        /// <summary>
        /// Returns a series of points that connect the start point to the end point and also includes 
        /// an arrow head at the end point.
        /// </summary>
        public static PointCollection GetArrowLine(Point start, Point end)
        {
            // Calculate the tips of the arrow head.
            double theta = Math.Atan2(start.Y - end.Y, start.X - end.X) + Math.PI / 2;
            double cosy = Math.Cos(theta);
            double siny = Math.Sin(theta);
            double x1 = (-1 * ARROW_WIDTH * cosy + ARROW_HEIGHT * siny) + end.X;
            double y1 = (-1 * ARROW_WIDTH * siny - ARROW_HEIGHT * cosy) + end.Y;
            double x2 = (ARROW_WIDTH * cosy + ARROW_HEIGHT * siny) + end.X;
            double y2 = (ARROW_WIDTH * siny - ARROW_HEIGHT * cosy) + end.Y;

            // Draw the line between the two connection points and place the arrow point on the end.
            PointCollection linePoints = new PointCollection();
            linePoints.Clear();
            linePoints.Add(start);
            linePoints.Add(end);
            linePoints.Add(new Point(x1, y1));
            linePoints.Add(new Point(x2, y2));
            linePoints.Add(end);

            return linePoints;
        }
    }
}
