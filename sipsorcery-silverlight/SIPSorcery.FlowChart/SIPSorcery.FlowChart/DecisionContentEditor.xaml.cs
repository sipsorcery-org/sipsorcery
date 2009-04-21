using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SIPSorcery.Client.FlowChartDemo
{
    public delegate void DecisionContentEditorClosedDelegate(UIElement diagram, string leftContent, string bottomContent, string rightContent);

	public partial class DecisionContentEditor : UserControl
	{
        private UIElement m_diagram;
        private string m_originalLeftConditionContent;
        private string m_originalBottomConditionContent;
        private string m_originalRightConditionContent;

        public event DecisionContentEditorClosedDelegate Closed;

		public DecisionContentEditor()
		{
			// Required to initialize variables
			InitializeComponent();
		}

        public void Display(UIElement diagram, string title, string leftContent, string bottomContent, string rightContent)
        {
            m_diagram = diagram;
            m_originalLeftConditionContent = leftContent;
            m_originalBottomConditionContent = bottomContent;
            m_originalRightConditionContent = rightContent;
            m_diagramTitle.Text = (title != null) ? title : "Decision Diagram Editor";
            m_leftConditionContent.Text = (leftContent != null) ? leftContent : String.Empty;
            m_bottomConditionContent.Text = (bottomContent != null) ? bottomContent : String.Empty;
            m_rightConditionContent.Text = (rightContent != null) ? rightContent : String.Empty;
            this.Visibility = Visibility.Visible;
        }

        private void CancelButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
            if (Closed != null)
            {
                Closed(m_diagram, m_originalLeftConditionContent, m_originalBottomConditionContent, m_originalRightConditionContent);
            }
        }

        private void UpdateButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
           this.Visibility = Visibility.Collapsed;
            if (Closed != null)
            {
                Closed(m_diagram, m_leftConditionContent.Text, m_bottomConditionContent.Text, m_rightConditionContent.Text);
            }
        }
	}
}