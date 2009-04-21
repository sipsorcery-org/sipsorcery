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
    public class FlowChartFactory
    {
        /// <summary>
        /// ToDo: See if the diagrams can be created using the DiagramType information and reflection rather then using string matching. 
        /// That would make it more flexible and robust for adding new diagram types and if existing diagram types change namespaces.
        /// </summary>
        /// <param name="diagramProperties"></param>
        /// <returns></returns>
        public static IFlowChartDiagram CreateDiagram(FlowChartDiagramProperties diagramProperties)
        {
            switch (diagramProperties.DiagramType)
            {
#if UNITTEST
                case "SIPSorcery.TheFlow.FlowMaster+MockFlowDiagram":
                    FlowMaster.MockFlowDiagram mockDiagram = new FlowMaster.MockFlowDiagram(diagramProperties);
                    return mockDiagram;
#endif
                default:
                    throw new ApplicationException("Diagram type " + diagramProperties.DiagramType + " was not recognised.");

            }
        }
    }
}
