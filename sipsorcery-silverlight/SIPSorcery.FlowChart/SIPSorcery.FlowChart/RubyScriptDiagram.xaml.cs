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
    public delegate void EditBlockDiagramConentDelegate(RubyScriptDiagram blockDiagram);

    public partial class RubyScriptDiagram : UserControl, IFlowChartDiagram
    {
        public event FlowChartDiagramClickedDelegate DiagramSelected;
        public event FlowChartConnectionPointClickedDelegate ConnectionSourceSelected;
        public event FlowChartConnectionPointClickedDelegate ConnectionSinkSelectedDown;
        public event FlowChartConnectionPointClickedDelegate ConnectionSinkSelectedUp;
        public event EditBlockDiagramConentDelegate EditContent;

        private SolidColorBrush m_highlightBrush;
        private SolidColorBrush m_normalBrush;

        private Guid m_diagramId;
        public bool Highlighted { get; private set; }
        private string m_title;             // The title of the flowchart diagram, will be displayed on the flowchart.
        //public string ScriptContent;        // The contents of the flowchart diagram. Can be edited by double clicking on the diagram.

        public FlowChartConnectionPoint ConnectionPointSource;
        public FlowChartConnectionPoint ConnectionPointSink;

        public string ScriptContent
        {
            get { return ConnectionPointSource.Content; }
            set { ConnectionPointSource.Content = value; }
        }

        public RubyScriptDiagram()
        {
            InitializeComponent();

            m_diagramId = Guid.NewGuid();
            ConnectionPointSource = new FlowChartConnectionPoint(ConnectionPointTypesEnum.Source, null, this, m_connectionSource, null);
            ConnectionPointSink = new FlowChartConnectionPoint(ConnectionPointTypesEnum.Sink, null, this, m_connectionSink, null);
            
            Highlighted = false;
            m_highlightBrush = new SolidColorBrush(Colors.Purple);
            m_normalBrush = (SolidColorBrush)m_diagramCanvas.Background;
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
            if (ConnectionPointSource.Connection != null)
            {
                ConnectionPointSource.Connection.Points = FlowChartGraphics.GetArrowLine(GetConnectionPointCenter(m_connectionSource), ConnectionPointSource.DestinationConnectionPoint.GetCenterPoint());
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
                ConnectionSourceSelected(ConnectionPointSource, e.GetPosition((Canvas)this.Parent).X, e.GetPosition((Canvas)this.Parent).Y);
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
            m_diagramCanvas.Background = m_highlightBrush;
        }

        public void Unhighlight()
        {
            Highlighted = false;
            m_diagramCanvas.Background = m_normalBrush;
        }

        public void Edit()
        {
            EditContent(this);
        }

        public IFlowChartDiagram Execute(ScriptScope scriptScope)
        {
            if (ScriptContent != null)
            {
                scriptScope.Execute(ScriptContent);
            }

            if (ConnectionPointSource.DestinationConnectionPoint != null)
            {
                return (IFlowChartDiagram)ConnectionPointSource.DestinationConnectionPoint.ParentDiagram;
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
            Guid destinationConnId = (ConnectionPointSource.DestinationConnectionPoint != null) ? ConnectionPointSource.DestinationConnectionPoint.ConnectionPointId : Guid.Empty;
            FlowChartConnectionPointProperties connProp = new FlowChartConnectionPointProperties("Source", ConnectionPointSource.Title, ConnectionPointSource.Content, null, destinationConnId);
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