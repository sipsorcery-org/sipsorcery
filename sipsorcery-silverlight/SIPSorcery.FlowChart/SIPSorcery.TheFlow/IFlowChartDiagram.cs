using System;
using System.Collections.Generic;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Shapes;
using Microsoft.Scripting.Hosting;

namespace SIPSorcery.TheFlow
{
    public delegate void FlowChartDiagramClickedDelegate(UIElement diagram, double mouseX, double mouseY);
    public delegate void FlowChartConnectionPointClickedDelegate(FlowChartConnectionPoint connectionPoint, double mouseX, double mouseY);
    public delegate void FlowChartDiagramDoubleClickDelegate(UIElement diagram);

    public enum ConnectionPointTypesEnum
    {
        Source = 1,
        Sink = 2,
    }

    public interface IFlowChartDiagram
    {
        event FlowChartDiagramClickedDelegate DiagramSelected;
        event FlowChartConnectionPointClickedDelegate ConnectionSourceSelected;
        event FlowChartConnectionPointClickedDelegate ConnectionSinkSelectedDown;
        event FlowChartConnectionPointClickedDelegate ConnectionSinkSelectedUp;

        Guid GetDiagramId();
        double GetLeft();
        double GetTop();
        //string GetTitle();
        //double GetSinkCenterX();
        //double GetSinkCenterY();
        Point GetConnectionPointCenter(UIElement pointElement);

        void Load(FlowChartDiagramProperties properties);
        //void SetConnectionLine(
        void UpdateConnectionLines();
        void Edit();
        IFlowChartDiagram Execute(ScriptScope scriptScope);
        List<FlowChartConnectionPointProperties> GetConnectionPoints();
        void LoadConnectionPoints(List<FlowChartConnectionPointProperties> connectionPoints);
    }

    public struct FlowChartConnectionPointProperties
    {
        public string Type;
        public string Title;            // The title that will be displayed on the flow chart for this connection point.
        public string Content;          // The scipt content associated with this connection point.
        public string Name;
        public Guid DestinationId;      // Id of the diagram this connection point is connected to, if any.

        public FlowChartConnectionPointProperties(string type, string title, string content, string name, Guid destinationId)
        {
            Type = type;
            Title = title;
            Content = content;
            Name = name;
            DestinationId = destinationId;
        }
    }

    public struct FlowChartDiagramProperties
    {
        public string DiagramType;
        public Guid DiagramId;
        public double Left;
        public double Top;
        public List<FlowChartConnectionPointProperties> ConnectionPoints;

        public FlowChartDiagramProperties(string diagramType, Guid diagramId, double left, double top, List<FlowChartConnectionPointProperties> connectionPoints)
        {
            DiagramType = diagramType;
            DiagramId = diagramId;
            Left = left;
            Top = top;
            ConnectionPoints = connectionPoints;
        }
    }
}
