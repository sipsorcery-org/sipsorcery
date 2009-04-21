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
    public class FlowChartConnectionSink
    {
        public Shape ConnectionPointElement;
        public IFlowChartDiagram Diagram;                   // The diagram that owns this connection point.
        public UIElement ConnectionSourceElement;
        public Line Connection;                             // As the source connection point objects of this class control the x1,y1 end of the line.

        private Brush m_originalBrush;

        public FlowChartConnectionSink(Shape connectionPointElement, IFlowChartDiagram diagram, UIElement connectionSourceElement)
        {
            ConnectionPointElement = connectionPointElement;
            Diagram = diagram;
            ConnectionSourceElement = connectionSourceElement;
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
    }

}
