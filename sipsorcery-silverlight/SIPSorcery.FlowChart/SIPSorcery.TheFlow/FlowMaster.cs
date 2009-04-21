using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Xml.Linq;
using System.Xml.Serialization;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;
//using Microsoft.JScript.Runtime;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.TheFlow
{
    public class FlowMaster
    {
        private IFlowChartDiagram m_startDiagram;

        private Dictionary<Guid, IFlowChartDiagram> m_flowDiagrams;

        public int Count
        {
            get { return m_flowDiagrams.Count; }
        }

        public FlowMaster()
        {
            m_flowDiagrams = new Dictionary<Guid, IFlowChartDiagram>();
        }

        public static List<FlowChartDiagramProperties> ParseFromXML(string flowchartXML)
        {
            if (flowchartXML != null && flowchartXML.Trim().Length > 0)
            {
                XDocument flowChartDOM = XDocument.Parse(flowchartXML);

                if (flowChartDOM != null)
                {
                    List<FlowChartDiagramProperties> diagrams = (
                            from e in flowChartDOM.Root.Elements("diagram")
                            select new FlowChartDiagramProperties
                            {
                                DiagramType = (string)e.Attribute("type"),
                                DiagramId = (Guid)e.Attribute("diagramid"),
                                Left = (double)e.Attribute("left"),
                                Top = (double)e.Attribute("top"),
                                ConnectionPoints = (from conn in e.Elements("connection")
                                                 select new FlowChartConnectionPointProperties
                                                { 
                                                    Type = conn.Attribute("type").Value,
                                                    Name = conn.Attribute("name").Value,
                                                    DestinationId = (Guid)conn.Attribute("destinationid"),
                                                    Title = (string)conn.Element("title"),
                                                    Content = (string)conn.Element("content")
                                                }).ToList()
   
                            }).ToList();

                    return diagrams;
                }
                else
                {
                    throw new ApplicationException("The XML could not be parsed, flowchart not loaded.");
                }
            }
            else
            {
                throw new ApplicationException("Cannot load flowchart from empty string.");
            }
        }

        public void AddDiagram(IFlowChartDiagram diagram)
        {
            m_flowDiagrams.Add(diagram.GetDiagramId(), diagram);
        }

        public void Clear()
        {
            m_flowDiagrams.Clear();
            m_startDiagram = null;
        }

        public List<IFlowChartDiagram> GetDiagramsList()
        {
            return m_flowDiagrams.Values.ToList();
        }

        public void Run(ScriptHelper scriptHelper)
        {
            try
            {
                if (m_startDiagram == null)
                {
                    scriptHelper.Log( "No start diagram has been specified. Please drag a connection from the start point to your start diagram.\n");
               }
               else
               {
                    ScriptScope scriptScope = ScriptRuntime.Create().GetEngine("JScript").CreateScope();

                    //ScriptScope scriptScope = IronRuby.CreateRuntime().CreateScope("IronPython");
                    //ScriptScope scriptScope = IronRuby.CreateRuntime().CreateScope("ruby");
                    //ScriptScope scriptScope = IronPython.Runtime.Cre.CreateRuntime().CreateScope("IronPython");

                    scriptHelper.Log("Started at " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + "\n");

                    scriptScope.SetVariable("sys", scriptHelper);

                    IFlowChartDiagram nextDiagram = m_startDiagram;

                    while (nextDiagram != null)
                    {
                        nextDiagram = nextDiagram.Execute(scriptScope);
                    }

                   scriptHelper.Log("Completed at " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + "\n");
                }
            }
            catch (Exception excp)
            {
                scriptHelper.Log("Exception. " + excp + "\n");
            }
        }

        public void SetStart(IFlowChartDiagram startDiagram)
        {
            m_startDiagram = startDiagram;
        }

        public string ToXML()
        {
            XDocument flowChartDOM = new XDocument();
            flowChartDOM.Add(new XElement("flowchart"));

            foreach (IFlowChartDiagram flowDiagram in m_flowDiagrams.Values)
            {
                XElement diagramElement = new XElement("diagram",
                    new XAttribute("type", flowDiagram.GetType().ToString()),
                    new XAttribute("diagramid", flowDiagram.GetDiagramId()),
                    new XAttribute("left", flowDiagram.GetLeft()),
                    new XAttribute("top", flowDiagram.GetTop()));

                    XElement connectionsElement = new XElement("connections");

                    foreach (FlowChartConnectionPointProperties connection in flowDiagram.GetConnectionPoints())
                    {
                        XElement connectionElement = new XElement("connection",
                            new XAttribute("type", connection.Type),
                            new XAttribute("name", (connection.Name != null) ? connection.Name : String.Empty),
                            new XElement("destinationid", connection.DestinationId.ToString()),
                            new XElement("title", (connection.Title != null) ? connection.Title : String.Empty),
                            new XElement("content", (connection.Content != null) ? connection.Content : String.Empty));

                        connectionsElement.Add(connectionElement);
                    }

                    diagramElement.Add(connectionsElement);
                flowChartDOM.Root.Add(diagramElement);
            }

            return flowChartDOM.ToString();
        }

       	#region Unit testing.

		#if UNITTEST

        public class MockFlowDiagram : UserControl, IFlowChartDiagram
        {
            public event FlowChartDiagramClickedDelegate DiagramSelected;
            public event FlowChartConnectionPointClickedDelegate ConnectionSourceSelected;
            public event FlowChartConnectionPointClickedDelegate ConnectionSinkSelectedDown;
            public event FlowChartConnectionPointClickedDelegate ConnectionSinkSelectedUp;

            public Guid DiagramId;
            public double Left;
            public double Top;
            public string Title;

            FlowChartConnectionPoint ConnectionPoint;

            public MockFlowDiagram(FlowChartDiagramProperties properties)
            {
                DiagramId = properties.DiagramId;
                Left = properties.Left;
                Top = properties.Top;
                ConnectionPoint = new FlowChartConnectionPoint(ConnectionPointTypesEnum.Source, null, this, null, null);
                //LoadConnections(properties.Connections);
            }

            public void Load(FlowChartDiagramProperties properties)
            { }

            public void UpdateConnectionLines()
            { }

            public void Edit()
            { }

            public IFlowChartDiagram Execute(ScriptScope scriptScope)
            {
                return null;
            }

            public Guid GetDiagramId()
            {
                return DiagramId;
            }

            public double GetLeft()
            {
                return Left;
            }

            public double GetTop()
            {
                return Top;
            }

            public List<FlowChartConnectionPointProperties> GetConnectionPoints()
            {
                return new List<FlowChartConnectionPointProperties>();
            }

            public void LoadConnectionPoints(List<FlowChartConnectionPointProperties> connections)
            {
                //ConnectionPoint = connections[0];
            }

            public Point GetConnectionPointCenter(UIElement pointElement)
            {
                return new Point(0, 0);
            }
        }

        [TestFixture]
        public class SIPDialPlanUnitTest
        {    
            [TestFixtureSetUp]
            public void Init()
            { }

            [TestFixtureTearDown]
            public void Dispose()
            { }

            [Test]
            public void SampleTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                Assert.IsTrue(true, "True was false.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void FlowMasterSaveAndLoadUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
                /*
                Guid mockDiagramId = Guid.NewGuid();
                List<FlowChartConnectionPoint> connections = new List<FlowChartConnectionPoint>();
                FlowChartConnectionPoint conn1 = new FlowChartConnectionPoint(ConnectionPointTypesEnum.Source, null, "Conn1", "sys.Log('hello world');", Guid.Empty);
                connections.Add(conn1);
                FlowChartConnectionPoint conn2 = new FlowChartConnectionPoint(ConnectionPointTypesEnum.Source, null, "Conn2", "sys.Log('goodbye cruel world');", Guid.Empty);
                connections.Add(conn2);
                FlowChartDiagramProperties mockProperties = new FlowChartConnectionPoint("mock", null, mockDiagramId, 100, 100, connections);
                MockFlowDiagram mockDiagram = new MockFlowDiagram(mockProperties);

                FlowMaster flowMaster = new FlowMaster();
                flowMaster.m_flowDiagrams.Add(mockDiagram.DiagramId, mockDiagram);

                string flowChartXML = flowMaster.ToXML();
                Console.WriteLine(flowChartXML);

                List <FlowChartDiagramProperties> reloadedDiagrams = FlowMaster.ParseFromXML(flowChartXML);

                Assert.IsTrue(reloadedDiagrams.Count == 1, "The flow master has the wrong number of diagrams.");
                //Assert.IsTrue(reloadedFlow.m_flowDiagrams.ContainsKey(mockDiagramId), "The mock diagram was not correctly loaded.");
                //Assert.IsTrue(reloadedFlow.m_flowDiagrams[mockDiagramId].GetConnectionProperties().Count == 2, "The re-loaded mock diagram did not have the correct number of connections.");

                FlowChartDiagramProperties reloadedDiagram1 = reloadedDiagrams[0];
                //List<FlowChartConnectionProperties> reloadedConnections = reloadedDiagram1.GetConnectionProperties();
                */
                /*Assert.IsTrue(reloadedDiagram1.GetLeft() == mockDiagram.Left, "The left property was not correctly re-loaded.");
                Assert.IsTrue(reloadedDiagram1.GetTop() == mockDiagram.Top, "The top property was not correctly re-loaded.");
                Assert.IsTrue(reloadedConnections[0].ConnectionId == conn1.ConnectionId, "The connectionid was not correctly re-loaded.");
                Assert.IsTrue(reloadedConnections[0].DestinationId == conn1.DestinationId, "The destinationid was not correctly re-loaded.");
                Assert.IsTrue(reloadedConnections[0].Title == conn1.Title, "The title was not correctly re-loaded.");
                Assert.IsTrue(reloadedConnections[0].Content == conn1.Content, "The content was not correctly re-loaded.");
                Assert.IsTrue(reloadedConnections[1].ConnectionId == conn2.ConnectionId, "The connectionid was not correctly re-loaded for connection 2.");*/

                Console.WriteLine("---------------------------------");
            }
        }

        #endif

        #endregion
    }
}
