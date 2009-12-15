using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Net;
using System.Threading;
using System.Windows;
using System.Windows.Browser;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SIPSorcery.Persistence;

namespace SIPSorcery.UIControls
{
    public delegate void GetAssetListDelegate(int start, int count);

    public class AssetViewPanel : StackPanel 
    {
        public const int DEFAULT_DISPLAY_COUNT = 25;

        private AssetListMenuBar m_menuBar;
        private Border m_detailsBorder;
        private DataGrid m_dataGrid;
        private double m_heightAdjustment = 0;

        private int m_listOffset;
        private int m_listCount;    // The actual number of items being displayed. Normally the same as the display count except for the last block.

        public GetAssetListDelegate GetAssetList;
        public event MenuButtonClickedDelegate Add;
        public event MenuButtonClickedDelegate Delete;

        private int m_displayCount = DEFAULT_DISPLAY_COUNT;
        public int DisplayCount
        {
            get { return m_displayCount; }
            set { m_displayCount = value; }
        }

        private int m_listTotal;
        public int AssetListTotal
        {
            get { return m_listTotal; }
            set { m_listTotal = value; }
        }

        public AssetViewPanel()
        {
            m_menuBar = new AssetListMenuBar();
            m_detailsBorder = new Border();
            this.Children.Add(m_menuBar);
            m_menuBar.Add += new MenuButtonClickedDelegate(MenuBar_Add);
            m_menuBar.Delete += new MenuButtonClickedDelegate(MenuBar_Delete);
            this.Loaded += new RoutedEventHandler(AssetViewPanel_Loaded);
            m_detailsBorder.SizeChanged += new SizeChangedEventHandler(DetailsBorder_SizeChanged);

            m_menuBar.Refresh += new MenuButtonClickedDelegate(() => { RefreshAsync(); });
            m_menuBar.PageFirst += new MenuButtonClickedDelegate(MenuBar_PageFirst);
            m_menuBar.PagePrevious += new MenuButtonClickedDelegate(MenuBar_PagePrevious);
            m_menuBar.PageNext += new MenuButtonClickedDelegate(MenuBar_PageNext);
            m_menuBar.PageLast += new MenuButtonClickedDelegate(MenuBar_PageLast);
        }

        private void AssetViewPanel_Loaded(object sender, RoutedEventArgs e)
        {
            foreach (UIElement element in this.Children)
            {
                if (element.GetType() == typeof(Border))
                {
                    m_dataGrid = (DataGrid)((Border)element).Child;
                }
            }

            this.Children.Add(m_detailsBorder);
        }

        public void SetAssetListSource(IEnumerable list)
        {
            // Set the datagrid source.
            UIHelper.SetDataGridSource(m_dataGrid, list);

            // Set the list count so that the range can be displayed.
            m_listCount = 0;
            IEnumerator listEnumerator = list.GetEnumerator();
            while (listEnumerator.MoveNext())
            {
                m_listCount++;
            }
            m_menuBar.SetDisplayedRange(m_listOffset, m_listCount, m_listTotal, m_displayCount);
        }

        private void MenuBar_PageFirst()
        {
            m_listOffset = 0;
            RefreshAsync();
        }

        private void MenuBar_PagePrevious()
        {
            m_listOffset -= DisplayCount;
            if (m_listOffset < 0)
            {
                m_listOffset = 0;
            }
            RefreshAsync();
        }

        private void MenuBar_PageNext()
        {
            m_listOffset += DisplayCount;
            if (m_listOffset > m_listTotal)
            {
                m_listOffset = DisplayCount * (m_listTotal / DisplayCount);
            }
            RefreshAsync();
        }

        private void MenuBar_PageLast()
        {
            m_listOffset = DisplayCount * (m_listTotal / DisplayCount);
            if (m_listOffset >= m_listTotal)
            {
                m_listOffset -= DisplayCount;
            }
            RefreshAsync();
        }

        private void MenuBar_Add()
        {
            if (Add != null)
            {
                Add();
            }
        }

        private void MenuBar_Delete()
        {
            if (Delete != null)
            {
                Delete();
            }
        }

        public void RefreshAsync()
        {
            GetAssetList(m_listOffset, DisplayCount);
        }

        public void SetDetailsElement(UIElement detailsElement)
        {
            if (detailsElement == null)
            {
                throw new ArgumentNullException("detailsElement", "You must provide a UIElement when setting the details control.");
            }

            m_detailsBorder.Child = detailsElement;

            //heightAdjustment += (double)detailsElement.GetValue(Canvas.HeightProperty);
            //m_heightAdjustment += (double)detailsElement.GetValue(Canvas.ActualHeightProperty);
            //m_heightAdjustment += detailsElement.RenderSize.Height;
            //UIHelper.AdjustPluginHeight(m_heightAdjustment);
        }

        private void DetailsBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            m_heightAdjustment = e.NewSize.Height - e.PreviousSize.Height;
            //UIHelper.AdjustPluginHeight(m_heightAdjustment);
        }

        public void CloseDetailsPane()
        {
            //UIHelper.AdjustPluginHeight(m_heightAdjustment * -1);
            m_detailsBorder.Child = null;
            //m_heightAdjustment = 0;
        }

        public void MenuEnableAdd(bool isEnabled)
        {
            m_menuBar.EnableAdd(isEnabled);
        }

        public void MenuEnableRefresh(bool isEnabled)
        {
            m_menuBar.EnableRefresh(isEnabled);
        }

        public void MenuEnableFilter(bool isEnabled)
        {
            m_menuBar.EnableFilter(isEnabled);
        }

        public void MenuEnableDelete(bool isEnabled)
        {
            m_menuBar.EnableDelete(isEnabled);
        }

        public void SetTitle(string title)
        {
            m_menuBar.SetTitle(title);
        }

        /// <summary>
        /// Lets the panel know the asset manager created a new asset and that the total and list display
        /// count should be incremented.
        /// </summary>
        public void AssetAdded()
        {
            m_listCount++;
            m_listTotal++;
            m_menuBar.SetDisplayedRange(m_listOffset, m_listCount, m_listTotal, m_displayCount);
        }

        /// <summary>
        /// The opposite of adding an asset. Lets the panel know the asset manager deleted an asset and that 
        /// the total and list display count should be decremented.
        /// </summary>
        public void AssetDeleted()
        {
            m_listCount--;
            m_listTotal--;

            if ((m_listOffset >= m_listTotal && m_listTotal > 0) || (m_listCount == 0 && m_listTotal > 0))
            {
                // The last item being displayed on a list has been deleted need to do a refresh to get back to the previous screen.
                MenuBar_PagePrevious();
            }
            else
            {
                m_menuBar.SetDisplayedRange(m_listOffset, m_listCount, m_listTotal, m_displayCount);
            }
        }
    }
}
