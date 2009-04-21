using System;
using System.Collections.Generic;
using System.IO.IsolatedStorage;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SIPSorcery.TheFlow;

namespace SIPSorcery.Client.FlowChartDemo
{   
	public partial class Page : UserControl
	{
        private const int CANVAS_DIAGRAM_LEFT_GAP = 5;
        private const int CANVAS_DIAGRAM_TOP_GAP = 10;
        private const int DOUBLECLICK_INTERVAL_MILLISECONDS = 600; // If an item gets clicked twice in this interval it is treated as a double click.

        private double m_mouseStartX;
        private double m_mouseStartY;
        private UIElement m_activeElement = null;

        // Used to detect double clicks on diagrams.
        private DateTime m_clickedTime = DateTime.MinValue;
        private UIElement m_clickedElement = null;
        
        private DiagramContentEditor m_diagramEditor = new DiagramContentEditor();
        private DecisionContentEditor m_decisionBlockEditor = new DecisionContentEditor();

        private FlowChartConnectionPoint m_connectionSource = null;
        private Polyline m_connectionLine = null;
        private Polyline m_startLine = null;
        private FlowChartConnectionPoint m_startConnectionPoint = null;

        private FlowMaster m_flowMaster = new FlowMaster();
        private Popup m_errorPopup = new Popup();
        private AppPopup m_errorPopupContent = new AppPopup();
        
		public Page()
		{
	    	InitializeComponent();  
		}

        private void UserControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            m_diagramEditor.Closed += new ContentEditorClosedDelegate(DiagramEditor_Closed);
            m_diagramEditor.Visibility = Visibility.Collapsed;
            m_diagramEditor.SetValue(Canvas.ZIndexProperty, 5);
            m_flowChartCanvas.Children.Add(m_diagramEditor);

            m_decisionBlockEditor.Closed += new DecisionContentEditorClosedDelegate(DecisionEditor_Closed);
            m_decisionBlockEditor.Visibility = Visibility.Collapsed;
            m_decisionBlockEditor.SetValue(Canvas.ZIndexProperty, 5);
            m_flowChartCanvas.Children.Add(m_decisionBlockEditor);
          
            m_errorPopupContent.Closed += new PopupClosedDelegate(ErrorPopupClosed);
            m_errorPopup.Child = m_errorPopupContent;
        }

        /// <summary>
        /// When the Start element is clicked on in the flowchart it means the user wants to set the first diagram in the flowchart. The user will drag
        /// a line from the start button to a connection point sink on the flowchart diagram that they wish to set as the initial one.
        /// If the start diagram has not already been set this method handler needs to create a new in progress line originating from the Start element.
        /// </summary>
        private void StartPoint_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (m_startConnectionPoint == null)
            {
                Point startPoint = new Point((double)m_startPoint.GetValue(Canvas.LeftProperty) + m_startPoint.Width / 2, (double)m_startPoint.GetValue(Canvas.TopProperty) + m_startPoint.Height / 2);
                Point endPoint = new Point(e.GetPosition(m_flowChartCanvas).X, e.GetPosition(m_flowChartCanvas).Y);
                m_startLine = GetConnectionLine(startPoint, endPoint);
                m_flowChartCanvas.Children.Add(m_startLine);
            }
        }

        /// <summary>
        /// A mouse up event on a connection sink point when there is an in progress line and the sink does not already have a 
        /// connection means the user wishes to create a new connection.
        /// </summary>
        private void ConnectionSinkSelectedUp(FlowChartConnectionPoint connectionSink, double mouseX, double mouseY)
        {
            if (m_startLine != null && m_startConnectionPoint == null)
            {
                // The start line has been dropped onto the initial flowchart diagram.
                m_startConnectionPoint = connectionSink;
                connectionSink.Connection = m_startLine;
                m_startLine.SetValue(Canvas.ZIndexProperty, 5);
                m_flowMaster.SetStart((IFlowChartDiagram)connectionSink.ParentDiagram);
            }
            else if (m_connectionSource != null)
            {
                IFlowChartDiagram sinkDiagram = (IFlowChartDiagram)connectionSink.ParentDiagram;

                double x1 = m_connectionSource.CenterX();
                double y1 = m_connectionSource.CenterY();
                Point sinkCenter = sinkDiagram.GetConnectionPointCenter((UIElement)connectionSink.ConnectionPointElement);

                Polyline connectionLine = GetConnectionLine(new Point(x1, y1), sinkCenter);
                m_connectionSource.Connection = connectionLine;
                connectionSink.Connection = connectionLine;
                m_connectionSource.DestinationConnectionPoint = connectionSink;
                connectionSink.DestinationConnectionPoint = m_connectionSource;
                connectionLine.SetValue(Canvas.ZIndexProperty, 5);
                m_flowChartCanvas.Children.Add(connectionLine);
            }

            ClearFlowChartSelections();
        }

        /// <summary>
        /// When a source connection point is clicked on it is to initiate the creation of a new flowchart connection line between diagrams.
        /// This handler needs to highlight the connection point and create a new in progress connection line.
        /// </summary>
        private void ConnectionSourceSelected(FlowChartConnectionPoint connectionSource, double mouseX, double mouseY)
        {
            connectionSource.Highlight();
            m_connectionSource = connectionSource;
        }

        /// <summary>
        /// When a connection sink is clicked on and there is a connection on the point it means the user would like to move
        /// the connection. This handler removes and connection on the point and creates an in progress line.
        /// </summary>
        private void ConnectionSinkSelectedDown(FlowChartConnectionPoint connectionPoint, double mouseX, double mouseY)
        {
            if (connectionPoint == m_startConnectionPoint)
            {
                m_flowMaster.SetStart(null);
                m_startConnectionPoint = null;
                m_flowChartCanvas.Children.Remove(connectionPoint.Connection);
                connectionPoint.Connection = null;

                Point startPoint = new Point((double)m_startPoint.GetValue(Canvas.LeftProperty) + m_startPoint.Width / 2, (double)m_startPoint.GetValue(Canvas.TopProperty) + m_startPoint.Height / 2);
                Point endPoint = new Point(mouseX, mouseY);
                m_startLine = GetConnectionLine(startPoint, endPoint);
                m_flowChartCanvas.Children.Add(m_startLine); 
            }
            else if (connectionPoint.Connection != null)
            {
                connectionPoint.DestinationConnectionPoint.Highlight();
                m_connectionSource = connectionPoint.DestinationConnectionPoint;

                m_flowChartCanvas.Children.Remove(connectionPoint.Connection);
                connectionPoint.Connection = null;
                connectionPoint.DestinationConnectionPoint = null;
            }
        }

        private void FlowChartCanvas_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (m_startLine != null && m_startConnectionPoint == null)
            {
                // Start line has been left orphaned (not attached to a diagram) so remove.
                m_flowChartCanvas.Children.Remove(m_startLine);
                m_startLine = null;
            }
           
            ClearFlowChartSelections();
        }

        private void SetDiagramActive(UIElement diagram, double mouseX, double mouseY)
        {
            if (m_clickedElement != null && diagram == m_clickedElement && DateTime.Now.Subtract(m_clickedTime).TotalMilliseconds < DOUBLECLICK_INTERVAL_MILLISECONDS)
            {
                //RubyScriptDiagram flowDiagram = (RubyScriptDiagram)m_clickedElement;
                //flowDiagram.Highlight();
                //m_diagramEditor.Display(diagram, flowDiagram.Title, flowDiagram.ScriptContent);
                IFlowChartDiagram dblClickedDiagram = (IFlowChartDiagram)m_clickedElement;
                dblClickedDiagram.Edit();

                m_clickedElement = null;
            }
            else
            {
                m_mouseStartX = mouseX;
                m_mouseStartY = mouseY;
                m_activeElement = diagram;
                m_clickedElement = diagram;
                m_clickedTime = DateTime.Now;
            }
        }

        private void FlowChartCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (m_activeElement != null)
            {
                // If the diagram gets moved then prevent double click.
                m_clickedElement = null;

                #region Mouse movement is for a flowchart diagram being dragged around.

                double xShift = e.GetPosition(m_flowChartCanvas).X - m_mouseStartX;
                double yShift = e.GetPosition(m_flowChartCanvas).Y - m_mouseStartY;

                double newXPosn = (double)m_activeElement.GetValue(Canvas.LeftProperty) + xShift;
                double newYPosn = (double)m_activeElement.GetValue(Canvas.TopProperty) + yShift;

                #region If the move would put the diagram outside the Canvas only allow it up to the edge.

                if (newXPosn < CANVAS_DIAGRAM_LEFT_GAP)
                {
                    newXPosn = CANVAS_DIAGRAM_LEFT_GAP;
                }
                else if (newXPosn > m_flowChartCanvas.Width - CANVAS_DIAGRAM_LEFT_GAP - (double)m_activeElement.GetValue(Canvas.WidthProperty))
                {
                    newXPosn = m_flowChartCanvas.Width - CANVAS_DIAGRAM_LEFT_GAP - (double)m_activeElement.GetValue(Canvas.WidthProperty);
                }

                if (newYPosn < CANVAS_DIAGRAM_TOP_GAP)
                {
                    newYPosn = CANVAS_DIAGRAM_TOP_GAP;
                }
                else if (newYPosn > m_flowChartCanvas.Height - CANVAS_DIAGRAM_TOP_GAP - (double)m_activeElement.GetValue(Canvas.HeightProperty))
                {
                    newYPosn = m_flowChartCanvas.Height - CANVAS_DIAGRAM_TOP_GAP - (double)m_activeElement.GetValue(Canvas.HeightProperty);
                }

                // Update any connection lines on this diagram.
                ((IFlowChartDiagram)m_activeElement).UpdateConnectionLines();

                // Record the current mouse position so that the shift can be determined for the next move.
                m_mouseStartX = e.GetPosition(m_flowChartCanvas).X;
                m_mouseStartY = e.GetPosition(m_flowChartCanvas).Y;

                m_activeElement.SetValue(Canvas.LeftProperty, newXPosn);
                m_activeElement.SetValue(Canvas.TopProperty, newYPosn);

                #endregion

                #region Autoscroll the viewer if the item being dragged is about to go off the screen.

                /*
                double itemHeight = (double)m_activeElement.GetValue(Canvas.HeightProperty);

                if (newYPosn + itemHeight > m_flowChartScrollView.ViewportHeight)
                {
                    m_flowChartScrollView.ScrollToVerticalOffset(m_flowChartScrollView.VerticalOffset + yShift);
                }

                m_yScroll.Text = m_flowChartScrollView.VerticalOffset.ToString();
                */
                  
                #endregion

                #endregion
            }
            else if (m_startLine != null && m_startConnectionPoint == null)
            {
                MoveConnectionLine(m_startLine, new Point(e.GetPosition(m_flowChartCanvas).X, e.GetPosition(m_flowChartCanvas).Y));
            }
            else if (m_connectionSource != null)
            {
                #region Mouse movement is for a connection line being dragged around.

                if (m_connectionLine == null)
                {
                    Point startPoint = new Point(m_connectionSource.CenterX(), m_connectionSource.CenterY());
                    Point endPoint = new Point(e.GetPosition(m_flowChartCanvas).X, e.GetPosition(m_flowChartCanvas).Y);
                    m_connectionLine = GetConnectionLine(startPoint, endPoint);
                    m_flowChartCanvas.Children.Add(m_connectionLine);
                }
                else
                {
                    MoveConnectionLine(m_connectionLine, new Point(e.GetPosition(m_flowChartCanvas).X, e.GetPosition(m_flowChartCanvas).Y));
                }

                #endregion
            }
        }

        private void FlowChartCanvas_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            ClearFlowChartSelections();
        }

        private void ClearFlowChartSelections()
        {
            m_activeElement = null;

            if (m_startLine != null && m_startConnectionPoint == null)
            {
                m_flowChartCanvas.Children.Remove(m_startLine);
            }

            if (m_connectionSource != null)
            {
                m_connectionSource.Unhighlight();
            }

            if (m_connectionLine != null)
            {
                m_flowChartCanvas.Children.Remove(m_connectionLine);
            }

            m_startLine = null;
            m_connectionSource = null;
            m_connectionLine = null;
        }

        private void RunScript(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                m_activityTextBox.Text = String.Empty;
                ScriptHelper scriptHelper = new ScriptHelper(WriteDebugMessage);
                m_flowMaster.Run(scriptHelper);
            }
            catch (Exception excp)
            {
                m_activityTextBox.Text += "Exception. " + excp;
            }
        }

        private void WriteDebugMessage(string message)
        {
            m_activityTextBox.Text += message;
        }

        private void BlockButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            AddBlockDiagram(new RubyScriptDiagram());
        }

        private void AddBlockDiagram(RubyScriptDiagram blockDiagram)
        {
            m_flowChartCanvas.Children.Add(blockDiagram);
            m_flowMaster.AddDiagram(blockDiagram);
            blockDiagram.SetValue(Canvas.ZIndexProperty, 3);
            blockDiagram.DiagramSelected += new FlowChartDiagramClickedDelegate(SetDiagramActive);
            blockDiagram.ConnectionSourceSelected += new FlowChartConnectionPointClickedDelegate(ConnectionSourceSelected);
            blockDiagram.ConnectionSinkSelectedDown += new FlowChartConnectionPointClickedDelegate(ConnectionSinkSelectedDown);
            blockDiagram.ConnectionSinkSelectedUp += new FlowChartConnectionPointClickedDelegate(ConnectionSinkSelectedUp);
            blockDiagram.EditContent += new EditBlockDiagramConentDelegate(BlockDiagramEditContent);
        }

        private void DecisionButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            AddDecisionDiagram(new RubyDecisionDiagram());
        }

        private void AddDecisionDiagram(RubyDecisionDiagram decisionDiagram)
        {
            m_flowChartCanvas.Children.Add(decisionDiagram);
            m_flowMaster.AddDiagram(decisionDiagram);
            decisionDiagram.SetValue(Canvas.ZIndexProperty, 3);
            decisionDiagram.DiagramSelected += new FlowChartDiagramClickedDelegate(SetDiagramActive);
            decisionDiagram.ConnectionSourceSelected += new FlowChartConnectionPointClickedDelegate(ConnectionSourceSelected);
            decisionDiagram.ConnectionSinkSelectedDown += new FlowChartConnectionPointClickedDelegate(ConnectionSinkSelectedDown);
            decisionDiagram.ConnectionSinkSelectedUp += new FlowChartConnectionPointClickedDelegate(ConnectionSinkSelectedUp);
            decisionDiagram.EditContent += new EditDecisionDiagramConentDelegate(DecisionDiagramEditContent);
        }

        private void BlockDiagramEditContent(RubyScriptDiagram blockDiagram)
        {
            m_diagramEditor.Display(blockDiagram, "Title", blockDiagram.ScriptContent);
        }

        private void DecisionDiagramEditContent(RubyDecisionDiagram decisionDiagram)
        {
            m_decisionBlockEditor.Display(decisionDiagram, "Title", decisionDiagram.LeftConditionContent, decisionDiagram.BottomConditionContent, decisionDiagram.RightConditionContent);
        }

        private void Save(object sender, System.Windows.RoutedEventArgs e)
        {
            if (m_flowMaster.Count > 0)
            {
                IsolatedStorageFile file = IsolatedStorageFile.GetUserStoreForApplication();
                IsolatedStorageFileStream fs = file.CreateFile("sipsorcery.xml");
                byte[] buffer = Encoding.UTF8.GetBytes(m_flowMaster.ToXML());
                fs.Write(buffer, 0, buffer.Length);
                fs.Close();
            }
            else
            {
                DisplayErrorPopup("Error Saving Flow Chart", "The flow chart does not contain any diagrams to save.");
            }
        }

        private void Load(object sender, System.Windows.RoutedEventArgs e)
        {
             IsolatedStorageFile file = IsolatedStorageFile.GetUserStoreForApplication();
             IsolatedStorageFileStream fs = file.OpenFile("sipsorcery.xml", System.IO.FileMode.Open);
             byte[] buffer = new byte[fs.Length];
             fs.Read(buffer, 0, Convert.ToInt32(fs.Length));
             fs.Close();
              WriteDebugMessage( Encoding.UTF8.GetString(buffer, 0, buffer.Length));

            // Remove all the current flow chart diagrams.
             foreach (UIElement oldDiagram in m_flowMaster.GetDiagramsList())
             {
                 m_flowChartCanvas.Children.Remove(oldDiagram);
             }

             List<FlowChartDiagramProperties> reloadedDiagrams = FlowMaster.ParseFromXML(Encoding.UTF8.GetString(buffer, 0, buffer.Length));

             foreach (FlowChartDiagramProperties reloadDiagramProps in reloadedDiagrams)
             {
                 IFlowChartDiagram diagram = CreateDiagram(reloadDiagramProps);

                 // Position the diagram in its old spot.
                 ((UIElement)diagram).SetValue(Canvas.ZIndexProperty, 3);
                 ((UIElement)diagram).SetValue(Canvas.TopProperty, reloadDiagramProps.Top);
                 ((UIElement)diagram).SetValue(Canvas.LeftProperty, reloadDiagramProps.Left);
             }
        }

        private void DisplayErrorPopup(string title, string errorMessage)
        {
            //m_errorPopup.Visibility = Visibility.Visible;
            m_errorPopup.IsOpen = true;
        }

        private void ErrorPopupClosed()
        {
            //m_errorPopup.Visibility = Visibility.Collapsed;
            m_errorPopup.IsOpen = false;
        }

        private void DiagramEditor_Closed(UIElement diagram, string content)
        {
            RubyScriptDiagram flowDiagram = (RubyScriptDiagram)diagram;
            flowDiagram.ScriptContent = content;
            flowDiagram.Unhighlight();
        }

        private void DecisionEditor_Closed(UIElement diagram, string leftContent, string bottomContent, string rightContent)
        {
            RubyDecisionDiagram decisionDiagram = (RubyDecisionDiagram)diagram;
            decisionDiagram.LeftConditionContent = leftContent;
            decisionDiagram.BottomConditionContent = bottomContent;
            decisionDiagram.RightConditionContent = rightContent;
            //flowDiagram.Unhighlight();
        }

        private IFlowChartDiagram CreateDiagram(FlowChartDiagramProperties diagramProperties)
        {
            switch (diagramProperties.DiagramType)
            {
                case "SIPSorcery.Client.FlowChartDemo.RubyScriptDiagram":
                    RubyScriptDiagram scriptDiagam = new RubyScriptDiagram();
                    AddBlockDiagram(scriptDiagam);
                    return scriptDiagam;
                    
                case "SIPSorcery.Client.FlowChartDemo.RubyDecisionDiagram":
                    RubyDecisionDiagram decisionDiagram = new RubyDecisionDiagram();
                    AddDecisionDiagram(decisionDiagram);
                    return decisionDiagram;

                default:
                    throw new ApplicationException("Diagram type " + diagramProperties.DiagramType + " was not recognised.");

            }
        }

        private Polyline GetConnectionLine(Point start, Point end)
        {
            Polyline connectionLine = new Polyline();
            connectionLine.Points = FlowChartGraphics.GetArrowLine(start, end);
            connectionLine.Stroke = new SolidColorBrush(Colors.Purple);
            connectionLine.StrokeThickness = 2;
            return connectionLine;
        }

        private void MoveConnectionLine(Polyline connectionLine, Point end)
        {
            connectionLine.Points = FlowChartGraphics.GetArrowLine(connectionLine.Points[0], end);
        }
	}
}