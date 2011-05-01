using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SIPSorcery
{
    public partial class AssetListMenuBar : UserControl
    {
        public event Action Add;
        public event Action Refresh;
        public event Action<string> Filter;
        public event Action Help;
        public event Action PageFirst;
        public event Action PagePrevious;
        public event Action PageNext;
        public event Action PageLast;

        public AssetListMenuBar()
        {
            InitializeComponent();
            DisablePagingButtons();
        }

        /// <summary>
        /// Used to enable or disable the Add button on the menu bar.
        /// </summary>
        /// <param name="isEnabled">True to enable the button so it appears on the menu bar, false to disable it.</param>
        public void EnableAdd(bool isEnabled)
        {
            if (isEnabled)
            {
                UIHelper.SetVisibility(m_addButton, Visibility.Visible);
            }
            else
            {
                UIHelper.SetVisibility(m_addButton, Visibility.Collapsed);
            }
        }

        /// <summary>
        /// Used to enable or disable the Refresh button on the menu bar.
        /// </summary>
        /// <param name="isEnabled">True to enable the button so it appears on the menu bar, false to disable it.</param>
        public void EnableRefresh(bool isEnabled)
        {
            if (isEnabled)
            {
                UIHelper.SetVisibility(m_refreshButton, Visibility.Visible);
            }
            else
            {
                UIHelper.SetVisibility(m_refreshButton, Visibility.Collapsed);
            }
        }

        /// <summary>
        /// Used to enable or disable the Filter button and text box on the menu bar.
        /// </summary>
        /// <param name="isEnabled">True to enable the filter controls so they appear on the menu bar, false to disable them.</param>
        public void EnableFilter(bool isEnabled)
        {
            if (isEnabled)
            {
                UIHelper.SetVisibility(m_filterButton, Visibility.Visible);
                UIHelper.SetVisibility(m_filterTextBox, Visibility.Visible);
            }
            else
            {
                UIHelper.SetVisibility(m_filterButton, Visibility.Collapsed);
                UIHelper.SetVisibility(m_filterTextBox, Visibility.Collapsed);
            }
        }

        /// <summary>
        /// Used to enable or disable the Help button on the menu bar.
        /// </summary>
        /// <param name="isEnabled">True to enable the Delete button so it appears on the menu bar, false to disable it.</param>
        public void EnableHelp(bool isEnabled)
        {
            if (isEnabled)
            {
                UIHelper.SetVisibility(m_helpButton, Visibility.Visible);
            }
            else
            {
                UIHelper.SetVisibility(m_helpButton, Visibility.Collapsed);
            }
        }

        public void EnablePaging(bool enabled)
        {
            UIHelper.SetIsEnabled(m_pageFirstButton, enabled);
            UIHelper.SetIsEnabled(m_pagePreviousButton, enabled);
            UIHelper.SetIsEnabled(m_pageNextButton, enabled);
            UIHelper.SetIsEnabled(m_pageLastButton, enabled);
        }

        public void SetTitle(string title)
        {
            UIHelper.SetText(m_assetTitle, title);
        }

        public void SetDisplayedRange(int offset, int count, int total, int displayCount)
        {
            if (total == 0)
            {
                UIHelper.SetText(m_displayedRecordsTextBlock, "No records were found");
                DisablePagingButtons();
            }
            else if (total <= displayCount)
            {
                UIHelper.SetText(m_displayedRecordsTextBlock, "Displaying " + total + " of " + total);
                DisablePagingButtons();
            }
            else
            {
                int start = offset + 1;
                int end = offset + count;
                UIHelper.SetText(m_displayedRecordsTextBlock, start + " to " + end + " of " + total);

                if (offset > 0)
                {
                    UIHelper.SetIsEnabled(m_pageFirstButton, true);
                    UIHelper.SetIsEnabled(m_pagePreviousButton, true);
                }
                else
                {
                    UIHelper.SetIsEnabled(m_pageFirstButton, false);
                    UIHelper.SetIsEnabled(m_pagePreviousButton, false);
                }

                if (offset + count < total)
                {
                    UIHelper.SetIsEnabled(m_pageNextButton, true);
                    UIHelper.SetIsEnabled(m_pageLastButton, true);
                }
                else
                {
                    UIHelper.SetIsEnabled(m_pageNextButton, false);
                    UIHelper.SetIsEnabled(m_pageLastButton, false);
                }
            }
        }

        private void DisablePagingButtons()
        {
            UIHelper.SetIsEnabled(m_pageFirstButton, false);
            UIHelper.SetIsEnabled(m_pagePreviousButton, false);
            UIHelper.SetIsEnabled(m_pageNextButton, false);
            UIHelper.SetIsEnabled(m_pageLastButton, false);
        }

        private void AddButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (Add != null)
            {
                Add();
            }
        }

        private void RefreshButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (Refresh != null)
            {
                Refresh();
            }
        }

        private void FilterButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (Filter != null)
            {
                Filter(m_filterTextBox.Text);
            }
        }

        private void HelpButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (Help != null)
            {
                Help();
            }
        }

        private void PageFirstButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (PageFirst != null)
            {
                PageFirst();
            }
        }

        private void PagePreviousButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (PagePrevious != null)
            {
                PagePrevious();
            }
        }

        private void PageNextButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (PageNext != null)
            {
                PageNext();
            }
        }

        private void PageLastButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (PageLast != null)
            {
                PageLast();
            }
        }
    }
}