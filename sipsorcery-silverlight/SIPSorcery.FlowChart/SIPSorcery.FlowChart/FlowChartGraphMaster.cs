using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml;
using SIPSorcery.Client.FlowChart.FlowChartDiagrams;

namespace SIPSorcery.Client.FlowChart
{
    public class FlowChartGraphMaster
    {
        public const string CRLF = "\r\n";  // Used when producing the XML.

        //private static ILog logger = log4net.LogManager.GetLogger("flowchart");

        private Dictionary<string, FlowChartDiagram> m_flowchartDiagrams = new Dictionary<string, FlowChartDiagram>();  // Ideally the string indexes will be Guids but strings have been used to allow flowcharts to be created by hand
        private Dictionary<string, ActionDiagram> m_actionDiagrams = new Dictionary<string, ActionDiagram>();           // Contains a subset of the FlowChart diagrams list with only the Action diagrams.
        private Dictionary<string, DecisionDiagram> m_decisionDiagrams = new Dictionary<string, DecisionDiagram>();     // Contains a subset of the FlowChart diagrams list with only the Decision diagrams.

        //Graphics m_graphics;
        Canvas m_flowChartCanvas;

        private Brush m_diagramPen;               // Pen for drawing flowchart diagrams.
        private Brush m_originPointPen;           // Pen for drawing origination connection points.
        private Brush m_termPointPen;             // Pen for drawing termination connection points.
        private Brush m_highlightedDiagramPen;    // Pen for drawing highlighted flowchart diagrams.
        private Brush m_linePen;
        private Color m_diagramLineColor;
        private Brush m_lineOriginationCircleBrush;
        private Brush m_lineTerminationCircleBrush;
        private FontFamily m_diagramTextFont;
        private Brush m_diagramTextBrush;

        private Point m_currOriginPoint;
        private Point m_currMousePoint;

        public FlowChartGraphMaster(Canvas flowChartCanvas)
        {
            m_flowChartCanvas = flowChartCanvas;
            //m_graphics = flowchartPanel.CreateGraphics();

            /*GraphicsPath hPath = new GraphicsPath();
            // Create the outline for our custom end cap.
            hPath.AddLine(new Point(0, 0), new Point(-4, -7));
            hPath.AddLine(new Point(-4, -7), new Point(4, -7));
            hPath.AddLine(new Point(4, -7), new Point(0, 0));
            CustomLineCap arrowCap = new CustomLineCap(null, hPath);

            m_diagramLineColor = Color.Blue;
            m_diagramPen = new Pen(m_diagramLineColor);
            m_lineOriginationCircleBrush = Brushes.OrangeRed;
            m_linePen = new Pen(Color.OrangeRed);
            m_linePen.StartCap = LineCap.RoundAnchor;
            m_linePen.CustomEndCap = arrowCap;
            m_lineTerminationCircleBrush = Brushes.WhiteSmoke;
            m_originPointPen = new Pen(Color.Orange);
            m_termPointPen = new Pen(Color.Black);
            m_highlightedDiagramPen = new Pen(m_diagramLineColor, 3);
            m_diagramTextFont = new Font("Arial", 8);
            m_diagramTextBrush = Brushes.Blue;*/

            m_diagramLineColor = Colors.Blue;
            m_diagramPen = new SolidColorBrush(m_diagramLineColor);
        }

        public void PanelResize(Panel flowchartPanel)
        {
            /*m_graphics.Dispose();
            m_graphics = flowchartPanel.CreateGraphics();
            Paint();*/
        }

        public void Paint()
        {
            // Draw the diagrams.
            /*foreach (FlowChartDiagram flowchartDiagram in m_flowchartDiagrams.Values)
            {
                Pen diagramPen = (flowchartDiagram.Highlighted) ? m_highlightedDiagramPen : m_diagramPen;
                flowchartDiagram.DrawDiagram(m_graphics, diagramPen, m_originPointPen, m_termPointPen, m_diagramTextFont, m_diagramTextBrush);

                foreach (OriginConnectionPoint originPoint in flowchartDiagram.OriginConnectionPoints)
                {
                    if (originPoint.ConnectedDiagramId != null)
                    {
                        if (m_flowchartDiagrams.ContainsKey(originPoint.ConnectedDiagramId))
                        {
                            FlowChartDiagram dstDiagram = m_flowchartDiagrams[originPoint.ConnectedDiagramId];
                            m_graphics.DrawLine(m_linePen, originPoint.CenterPoint, dstDiagram.TerminationPoint.CenterPoint);

                            if (originPoint.DiagramText != null && originPoint.DiagramText.Trim().Length > 0)
                            {
                                if (originPoint.PositionDescription == OriginConnectionPoint.LEFT_POSITION_DESCRIPTION)
                                {
                                    Point upperLeftTextPoint = new Point(originPoint.CenterPoint.X - 50, originPoint.CenterPoint.Y + 10);
                                    m_graphics.DrawString(originPoint.DiagramText, m_diagramTextFont, m_diagramTextBrush, upperLeftTextPoint);
                                }
                                else if (originPoint.PositionDescription == OriginConnectionPoint.BOTTOM_POSITION_DESCRIPTION)
                                {
                                    Point upperLeftTextPoint = new Point(originPoint.CenterPoint.X - 25, originPoint.CenterPoint.Y + 10);
                                    m_graphics.DrawString(originPoint.DiagramText, m_diagramTextFont, m_diagramTextBrush, upperLeftTextPoint);
                                }
                                else if (originPoint.PositionDescription == OriginConnectionPoint.RIGHT_POSITION_DESCRIPTION)
                                {
                                    Point upperLeftTextPoint = new Point(originPoint.CenterPoint.X + 5, originPoint.CenterPoint.Y + 10);
                                    m_graphics.DrawString(originPoint.DiagramText, m_diagramTextFont, m_diagramTextBrush, upperLeftTextPoint);
                                }
                            }
                        }
                        else
                        {
                            // The remote diagram has been removed therefore there is no longer anywhere to terminate the line, remove the link.
                            originPoint.ConnectedDiagramId = null;
                        }
                    }
                }
            }

            // If there is a line draw in progress draw it.
            if (m_currOriginPoint != Point.Empty && m_currMousePoint != Point.Empty)
            {
                DrawInProgressConnLine(m_graphics, m_currOriginPoint, m_currMousePoint);
            }*/
        }

        public string AddFlowchartDiagram(FlowItemsEnum diagramType, Point diagramCenterPoint)
        {
            FlowChartDiagram flowChartDiagram = null;

            if (diagramType == FlowItemsEnum.Action)
            {
                ActionDiagram actionDiagram = new ActionDiagram(Guid.NewGuid().ToString(), diagramCenterPoint);
                m_actionDiagrams.Add(actionDiagram.DiagramId, actionDiagram);

                flowChartDiagram = actionDiagram;
            }
            else if (diagramType == FlowItemsEnum.Decision)
            {
                DecisionDiagram decisionDiagram = new DecisionDiagram(Guid.NewGuid().ToString(), diagramCenterPoint);
                m_decisionDiagrams.Add(decisionDiagram.DiagramId, decisionDiagram);

                flowChartDiagram = decisionDiagram;
            }

            m_flowchartDiagrams.Add(flowChartDiagram.DiagramId, flowChartDiagram);
            flowChartDiagram.DrawDiagram(m_flowChartCanvas, m_diagramPen, m_originPointPen, m_termPointPen, m_diagramTextFont, m_diagramTextBrush);

            return flowChartDiagram.DiagramId;
        }

        public void RemoveDiagram(string diagramId)
        {
            if (m_flowchartDiagrams.ContainsKey(diagramId))
            {
                FlowChartDiagram diagram = m_flowchartDiagrams[diagramId];

                if(diagram.DiagramType == FlowItemsEnum.Action && m_actionDiagrams.ContainsKey(diagramId))
                {
                    m_actionDiagrams.Remove(diagramId);
                }
                else if(diagram.DiagramType == FlowItemsEnum.Decision && m_decisionDiagrams.ContainsKey(diagramId))
                {
                    m_decisionDiagrams.Remove(diagramId);
                }
                
                m_flowchartDiagrams.Remove(diagramId);
            }
        }

        public string GetHitPointDiagram(Point hitPoint)
        {
            string hitDiagramId = null;
            foreach(FlowChartDiagram flowChartDiagram in m_flowchartDiagrams.Values)
            {
                if (flowChartDiagram.IsHit(hitPoint))
                {
                    hitDiagramId = flowChartDiagram.DiagramId;
                    break;
                }
            }

            return hitDiagramId;
        }

        /// <summary>
        /// Attempts to find a hit for the mouse cursor and a origin connection point. If there is a hit the connection point is
        /// returned otherwise null.
        /// </summary>
        public OriginConnectionPoint GetOriginHitPoint(Point hitPoint)
        {
            foreach (FlowChartDiagram flowChartDiagram in m_flowchartDiagrams.Values)
            {
                OriginConnectionPoint connPoint = flowChartDiagram.IsOriginHitPoint(hitPoint);
                if (connPoint != null)
                {
                    return connPoint;
                }
            }

            return null;
        }

        /// <summary>
        /// Attempts to find a hit for the mouse cursor and a termination connection point. If there is a hit the connection point is
        /// returned otherwise null.
        /// </summary>
        public TerminationConnectionPoint GetTerminationHitPoint(Point hitPoint)
        {
            foreach (FlowChartDiagram flowChartDiagram in m_flowchartDiagrams.Values)
            {
                TerminationConnectionPoint connPoint = flowChartDiagram.IsTerminationHitPoint(hitPoint);
                if (connPoint != null)
                {
                    return connPoint;
                }
            }

            return null;
        }

        public void MoveDiagram(string diagramId, Point newCenterPoint)
        {
            if (m_flowchartDiagrams.ContainsKey(diagramId))
            {
                m_flowchartDiagrams[diagramId].DrawDiagram(m_flowChartCanvas, newCenterPoint, m_diagramPen, m_originPointPen, m_termPointPen, m_diagramTextFont, m_diagramTextBrush);
            }
        }

        public void SetInProgressLineEndPoints(Point origin, Point destination)
        {
            m_currOriginPoint = origin;
            m_currMousePoint = destination;
        }

        public void AddConnection(OriginConnectionPoint originPoint, string destinationDiagramId)
        {
            originPoint.ConnectedDiagramId = destinationDiagramId;
        }

        public void RemoveConnection(OriginConnectionPoint originPoint)
        {
            originPoint.ConnectedDiagramId = null;
        }

        /// <summary>
        /// Removes the connection based on the destination diagram id. The origin connection point is returned so it can be used
        /// in constructing an in porgress line when the cursor is dragged off a connected termination point.
        /// </summary>
        public OriginConnectionPoint RemoveConnection(string destinationDiagramId)
        {
            foreach (FlowChartDiagram diagram in m_flowchartDiagrams.Values)
            {
                foreach (OriginConnectionPoint originPoint in diagram.OriginConnectionPoints)
                {
                    if (originPoint.ConnectedDiagramId == destinationDiagramId)
                    {
                        originPoint.ConnectedDiagramId = null;
                        return originPoint;
                    }
                }
            }

            return null;
        }

        public List<FlowChartDiagram> GetFlowChartDiagrams()
        {
            List<FlowChartDiagram> flowChartDiagramsList = new List<FlowChartDiagram>();
            foreach (FlowChartDiagram flowChartDiagram in m_flowchartDiagrams.Values)
            {
                flowChartDiagramsList.Add(flowChartDiagram);
            }

            return flowChartDiagramsList;
        }

        public List<ActionDiagram> GetActionDiagrams()
        {
            List<ActionDiagram> actionDiagramsList = new List<ActionDiagram>();
            foreach (ActionDiagram actionDiagram in m_actionDiagrams.Values)
            {
                actionDiagramsList.Add(actionDiagram);
            }

            return actionDiagramsList;
        }

        public List<DecisionDiagram> GetDecisionDiagrams()
        {
            List<DecisionDiagram> decisionDiagramsList = new List<DecisionDiagram>();
            foreach (DecisionDiagram decisionDiagram in m_decisionDiagrams.Values)
            {
                decisionDiagramsList.Add(decisionDiagram);
            }

            return decisionDiagramsList;
        }

        public void HighlightDiagram(string diagramId)
        {
            if (diagramId != null && m_flowchartDiagrams.ContainsKey(diagramId))
            {
                m_flowchartDiagrams[diagramId].Highlighted = true;
                //m_flowchartDiagrams[diagramId].DrawDiagram(m_graphics, m_highlightedDiagramPen, m_originPointPen, m_termPointPen);
            }
        }

        public void UnhighlightDiagram(string diagramId)
        {
            if (m_flowchartDiagrams.ContainsKey(diagramId))
            {
                m_flowchartDiagrams[diagramId].Highlighted = false;
                //m_flowchartDiagrams[diagramId].DrawDiagram(m_graphics, m_diagramPen, m_originPointPen, m_termPointPen);
            }
        }

        public string ToXML()
        {
            string flowChartXML = "<flowchart>" + CRLF;

            foreach(FlowChartDiagram flowDiagram in m_flowchartDiagrams.Values)
            {
                string itemCenterPoint = flowDiagram.CenterPoint.X.ToString() + "," + flowDiagram.CenterPoint.Y.ToString();
                flowChartXML += " <flowitem type='" + flowDiagram.DiagramType.ToString() + "' flowitemid='" + flowDiagram.DiagramId + "' centerpoint='" + itemCenterPoint + "'>" + CRLF;
                flowChartXML += "  <contents><![CDATA[" + flowDiagram.Contents + "]]></contents>" + CRLF;
                flowChartXML += "  <diagramtext><![CDATA[" + flowDiagram.DiagramText + "]]></diagramtext>" + CRLF;
                flowChartXML += "  <flowconnections>" + CRLF;

                foreach (OriginConnectionPoint originPoint in flowDiagram.OriginConnectionPoints)
                {
                    if (originPoint.ConnectedDiagramId != null)
                    {
                        flowChartXML += "   <flowconnection targetflowitemid='" + originPoint.ConnectedDiagramId + "' position='" + originPoint.PositionDescription + "'>" +
                                        "    <condition><![CDATA[" + originPoint.Condition + "]]></condition>" + CRLF +
                                        "    <diagramtext><![CDATA[" + originPoint.DiagramText + "]]></diagramtext>" + CRLF +
                                        "   </flowconnection>" + CRLF;
                    }
                }

                flowChartXML += "  </flowconnections>" + CRLF;
                flowChartXML += " </flowitem>" + CRLF;
            }

            flowChartXML += "</flowchart>" + CRLF;

            return flowChartXML;
        }

        /// <summary>
        /// Clears all diagrams from the flowchart, wipes the slate.
        /// </summary>
        public void Clear()
        {
            m_flowchartDiagrams.Clear();
            m_actionDiagrams.Clear();
            m_decisionDiagrams.Clear();
        }

        public FlowChartDiagram GetFlowChartDiagram(string diagramId)
        {
            if (m_flowchartDiagrams.ContainsKey(diagramId))
            {
                return m_flowchartDiagrams[diagramId];
            }

            return null;
        }

        public ActionDiagram GetActionDiagram(string diagramId)
        {
            if (m_actionDiagrams.ContainsKey(diagramId))
            {
                return m_actionDiagrams[diagramId];
            }

            return null;
        }

        public DecisionDiagram GetDecisionDiagram(string diagramId)
        {
            if (m_decisionDiagrams.ContainsKey(diagramId))
            {
                return m_decisionDiagrams[diagramId];
            }

            return null;
        }

        /// <summary>
        /// When the user has clicked on a line origination point and is dragging the mouse around this method takes care of drawing the line.
        /// </summary>
        private void DrawInProgressConnLine(Point originPoint, Point mousePoint)
        {
            //graphics.DrawLine(m_linePen, originPoint, mousePoint);
        }
    }
}
