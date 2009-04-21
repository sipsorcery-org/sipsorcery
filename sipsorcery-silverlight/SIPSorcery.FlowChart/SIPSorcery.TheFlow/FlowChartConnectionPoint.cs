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
    public class FlowChartConnectionPoint
    {
        public UIElement ParentDiagram;
        public Shape ConnectionPointElement;
        public FlowChartConnectionPoint DestinationConnectionPoint;
        public Polyline Connection;   
        public ConnectionPointTypesEnum ConnectionPointType;    
        public string Name;                                     // Optional setting that can be used by owner diagrams to allow them to distinguish between connection points of the same type.
        public string Content;                                  // Script content, not always relevant, for example is used by decision diagram but not by block diagram.
        public string Title;                                    // Title to display on diagram, like the content is not always relevant.
        public Guid ConnectionPointId;

        private Brush m_originalBrush;

        public FlowChartConnectionPoint(ConnectionPointTypesEnum connectionType, string name, UIElement connectionParent, Shape connectionPointElement, FlowChartConnectionPoint destinationConnectionPoint)
        {
            ConnectionPointId = Guid.NewGuid();
            ConnectionPointType = connectionType;
            Name = name;
            ParentDiagram = connectionParent;
            ConnectionPointElement = connectionPointElement;
            DestinationConnectionPoint = destinationConnectionPoint;
        }

        public void Highlight()
        {
            m_originalBrush = ((Shape)ConnectionPointElement).Fill;
            ((Shape)ConnectionPointElement).Fill = new SolidColorBrush(Colors.Orange);
        }

        public void Unhighlight()
        {
            ((Shape)ConnectionPointElement).Fill = m_originalBrush;
        }

        public Point GetCenterPoint()
        {
            double x = (double)ParentDiagram.GetValue(Canvas.LeftProperty) + (double)ConnectionPointElement.GetValue(Canvas.LeftProperty) + (ConnectionPointElement.Width / 2);
            double y = (double)ParentDiagram.GetValue(Canvas.TopProperty) + (double)ConnectionPointElement.GetValue(Canvas.TopProperty) + (ConnectionPointElement.Height / 2);
            return new Point(x, y);
        }

        public double CenterX()
        {
            return (double)ParentDiagram.GetValue(Canvas.LeftProperty) + (double)ConnectionPointElement.GetValue(Canvas.LeftProperty) + (ConnectionPointElement.Width / 2);
        }

        public double CenterY()
        {
            return (double)ParentDiagram.GetValue(Canvas.TopProperty) + (double)ConnectionPointElement.GetValue(Canvas.TopProperty) + (ConnectionPointElement.Height / 2);
        }
    }

}
