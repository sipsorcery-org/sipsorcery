using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.Scripting.Hosting;
using SIPSorcery.TheFlow;

namespace SIPSorcery.Client.FlowChartDemo
{
    public delegate void EditDecisionDiagramConentDelegate(RubyDecisionDiagram decisionDiagram);

    public partial class RubyDecisionDiagram : UserControl, IFlowChartDiagram 
	{
        private FlowChartConnectionPoint ConnectionPointSourceBottom;
        private FlowChartConnectionPoint ConnectionPointSourceLeft;
        private FlowChartConnectionPoint ConnectionPointSourceRight;
        private FlowChartConnectionPoint ConnectionPointSink;

        public event FlowChartDiagramClickedDelegate DiagramSelected;
        public event FlowChartConnectionPointClickedDelegate ConnectionSourceSelected;
        public event FlowChartConnectionPointClickedDelegate ConnectionSinkSelectedDown;
        public event FlowChartConnectionPointClickedDelegate ConnectionSinkSelectedUp;
        public event EditDecisionDiagramConentDelegate EditContent;

        public string LeftConditionContent
        {
            get { return ConnectionPointSourceLeft.Content; }
            set { ConnectionPointSourceLeft.Content = value; }
        }

        public string BottomConditionContent
        {
            get { return ConnectionPointSourceBottom.Content; }
            set { ConnectionPointSourceBottom.Content = value; }
        }

        public string RightConditionContent
        {
            get { return ConnectionPointSourceRight.Content; }
            set { ConnectionPointSourceRight.Content = value; }
        }

        private SolidColorBrush m_highlightBrush;
        private SolidColorBrush m_normalBrush;

        private Guid m_diagramId;
        public bool Highlighted { get; private set; }
        private string m_title;

        public RubyDecisionDiagram()
		{
			InitializeComponent();

            m_diagramId = Guid.NewGuid();

            ConnectionPointSourceBottom = new FlowChartConnectionPoint(ConnectionPointTypesEnum.Source, "bottom", this, m_connectionSourceBottom, null);
            ConnectionPointSourceLeft = new FlowChartConnectionPoint(ConnectionPointTypesEnum.Source, "left", this, m_connectionSourceLeft, null);
            ConnectionPointSourceRight = new FlowChartConnectionPoint(ConnectionPointTypesEnum.Source, "right", this, m_connectionSourceRight, null);
            ConnectionPointSink = new FlowChartConnectionPoint(ConnectionPointTypesEnum.Sink, "sink", this, m_connectionSink, null);

            Highlighted = false;
            m_highlightBrush = new SolidColorBrush(Colors.Purple);
            m_normalBrush = (SolidColorBrush)m_decisionDiagramCanvas.Background;
		}

        public void Load(FlowChartDiagramProperties diagramProperties)
        {

        }

        /// <summary>
        /// For diagrams that have connection lines this function will update the end points of the line(s).
        /// This function will be called when the diagram is moved around within the flow chart.
        /// </summary>
        public void UpdateConnectionLines()
        {
            if (ConnectionPointSourceBottom.Connection != null)
            {
                ConnectionPointSourceBottom.Connection.Points = FlowChartGraphics.GetArrowLine(GetConnectionPointCenter(m_connectionSourceBottom), ConnectionPointSourceBottom.DestinationConnectionPoint.GetCenterPoint());
            }

            if (ConnectionPointSourceLeft.Connection != null)
            {
                ConnectionPointSourceLeft.Connection.Points = FlowChartGraphics.GetArrowLine(GetConnectionPointCenter(m_connectionSourceLeft), ConnectionPointSourceLeft.DestinationConnectionPoint.GetCenterPoint());
            }

            if (ConnectionPointSourceRight.Connection != null)
            {
                ConnectionPointSourceRight.Connection.Points = FlowChartGraphics.GetArrowLine(GetConnectionPointCenter(m_connectionSourceRight), ConnectionPointSourceRight.DestinationConnectionPoint.GetCenterPoint());
            }

            if (ConnectionPointSink.Connection != null)
            {
                Point startPoint = ConnectionPointSink.Connection.Points[0];
                ConnectionPointSink.Connection.Points = FlowChartGraphics.GetArrowLine(startPoint, GetConnectionPointCenter(m_connectionSink));
            }
        }

        private void ConnectionSource_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ConnectionSourceSelected != null)
            {
                ConnectionSourceSelected(GetConnectionPointSource((Shape)sender), e.GetPosition((Canvas)this.Parent).X, e.GetPosition((Canvas)this.Parent).Y);
                e.Handled = true;
            }
        }

        private void ConnectionSink_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ConnectionSinkSelectedDown != null)
            {
                ConnectionSinkSelectedDown(ConnectionPointSink, e.GetPosition((Canvas)this.Parent).X, e.GetPosition((Canvas)this.Parent).Y);
                e.Handled = true;
            }
        }

        private void ConnectionSink_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (ConnectionSinkSelectedUp != null)
            {
                ConnectionSinkSelectedUp(ConnectionPointSink, e.GetPosition((Canvas)this.Parent).X, e.GetPosition((Canvas)this.Parent).Y);
                e.Handled = true;
            }
        }

        private void Diagram_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!e.Handled)
            {
                if (DiagramSelected != null)
                {
                    DiagramSelected(this, e.GetPosition((Canvas)this.Parent).X, e.GetPosition((Canvas)this.Parent).Y);
                }
            }
        }

        public void Highlight()
        {
            Highlighted = true;
            m_decisionDiagramCanvas.Background = m_highlightBrush;
        }

        public void Unhighlight()
        {
            Highlighted = false;
            m_decisionDiagramCanvas.Background = m_normalBrush;
        }

        private FlowChartConnectionPoint GetConnectionPointSource(Shape connectionPointElement)
        {
            if (connectionPointElement == ConnectionPointSourceBottom.ConnectionPointElement)
            {
                return ConnectionPointSourceBottom;
            }
            else if (connectionPointElement == ConnectionPointSourceLeft.ConnectionPointElement)
            {
                return ConnectionPointSourceLeft;
            }
            else
            {
                return ConnectionPointSourceRight;
            }
        }

        public void Edit()
        {
            EditContent(this);
        }

        public IFlowChartDiagram Execute(ScriptScope scriptScope)
        {
            if ((bool)scriptScope.Execute(ConnectionPointSourceLeft.Content))
            {
                return (IFlowChartDiagram)ConnectionPointSourceLeft.DestinationConnectionPoint.ParentDiagram;
            }
            else if ((bool)scriptScope.Execute(ConnectionPointSourceBottom.Content))
            {
                return (IFlowChartDiagram)ConnectionPointSourceBottom.DestinationConnectionPoint.ParentDiagram;
            }
            else if ((bool)scriptScope.Execute(ConnectionPointSourceRight.Content))
            {
                return (IFlowChartDiagram)ConnectionPointSourceRight.DestinationConnectionPoint.ParentDiagram;
            }
            else
            {
                return null;
            }
        }

        public Guid GetDiagramId()
        {
            return m_diagramId;
        }

        public double GetLeft()
        {
            return (double)GetValue(Canvas.LeftProperty);
        }

        public double GetTop()
        {
            return (double)GetValue(Canvas.TopProperty);
        }

        public List<FlowChartConnectionPointProperties> GetConnectionPoints()
        {
            List<FlowChartConnectionPointProperties> connectionsList = new List<FlowChartConnectionPointProperties>();
            Guid destinationConnId = (ConnectionPointSourceBottom.DestinationConnectionPoint != null) ? ConnectionPointSourceBottom.DestinationConnectionPoint.ConnectionPointId : Guid.Empty;
            FlowChartConnectionPointProperties connProp = new FlowChartConnectionPointProperties("SourceBottom", ConnectionPointSourceBottom.Title, ConnectionPointSourceBottom.Content, null, destinationConnId);
            connectionsList.Add(connProp);
            return connectionsList;
        }

        public void LoadConnectionPoints(List<FlowChartConnectionPointProperties> connections)
        {

        }

        public Point GetConnectionPointCenter(UIElement pointElement)
        {
            double x = (double)this.GetValue(Canvas.LeftProperty) + (double)pointElement.GetValue(Canvas.LeftProperty) + ((double)pointElement.GetValue(Canvas.WidthProperty) / 2);
            double y = (double)this.GetValue(Canvas.TopProperty) + (double)pointElement.GetValue(Canvas.TopProperty) + ((double)pointElement.GetValue(Canvas.HeightProperty) / 2);
            return new Point(x, y);
        }
	}
}