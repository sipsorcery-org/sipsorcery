using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SIPSorcery.SoftPhone.UI
{
    /// <summary>
    /// Interaction logic for ParticipantMessage.xaml
    /// </summary>
    public partial class ParticipantMessage : UserControl
    {
        private DateTime updateTime;

        public string MessageText => m_messageContent.Text;

        /// <summary>First timestamp of this message</summary>
        public DateTime CreationTime { get; set; }

        /// <summary>Last timestamp of this message</summary>
        public DateTime UpdateTime
        {
            get => updateTime;
            set
            {
                updateTime = value;
                m_messageContent.ToolTip = updateTime.ToString("yyyy-MM-dd hh:mm:ss");
            }
        }

        /// <summary>Original t140 block ended with a new line, which means this is the end of the corresponding rtt message</summary>
        public bool IsOpen { get; set; } = true;

        /// <summary>Removes new line characters from incoming text</summary>
        /// <remarks>
        /// May be used to automatically remove any new line characters from <see cref="MessageText"/>, e. g. when
        /// <see cref="IsOpen"/> is used to create new <see cref="ParticipantMessage"/> instances after each new line.
        /// </remarks>
        public bool RemoveNewLines { get; set; }

        public bool IsRemoteMessage { get; }

        public ParticipantMessage(DateTime receivedTime, string senderName, string message, bool isRemoteMessage, bool removeNewLines = true)
        {
            InitializeComponent();

            m_displayName.Text = senderName;
            IsRemoteMessage = isRemoteMessage;
            RemoveNewLines = removeNewLines;
            CreationTime = receivedTime;

            SetFormatting();
            AddText(receivedTime, message);
        }

        /// <summary>Adds text to <see cref="MessageText"/></summary>
        /// <remarks>
        /// This may include modifiying the existing content of <see cref="MessageText"/>, if <paramref name="text"/> starts
        /// with backspace characters. All additional content of <paramref name="text"/> is appended to <see cref="MessageText"/>.<br/>
        /// If <paramref name="text"/> ends with a new line character <see cref="IsOpen"/> is set to <see langword="false"/>.
        /// </remarks>
        /// <returns>Itself (for convenience reasons only)</returns>
        public ParticipantMessage AddText(DateTime time, string text)
        {
            string newText = text;
            newText = HandleBackspace(newText);
            newText = HandleNewLine(newText);

            m_messageContent.Text += newText;
            UpdateTime = time;

            return this;
        }

        private string HandleBackspace(string incomingText)
        {
            string text = incomingText;

            // delete existing text for incoming backspace characters
            while (text.StartsWith("\b", StringComparison.Ordinal))
            {
                if (MessageText.Length > 0)
                {
                    m_messageContent.Text = MessageText.Substring(0, MessageText.Length - 1);
                }
                text = text.Substring(1);
            }

            if (text.Contains("\b"))
            {
                // process backspaces within incoming string
                // c != '\b'                                    => filters out backspace characters itself
                // i + 1 < text.Length && text[i + 1] != '\b'   => filters out character, if following character is backspace
                // i == text.Length - 1                         => allow last character that always fails the previous check
                text = text
                    .Where((c, i) => c != '\b' && (i + 1 < text.Length && text[i + 1] != '\b' || i == text.Length - 1))
                    .Aggregate(string.Empty, (result, c) => result += c);
            }

            return text;
        }

        private string HandleNewLine(string incomingText)
        {
            string sanitizedText;
            if (incomingText.EndsWith("\r") || incomingText.EndsWith("\n"))
            {
                sanitizedText = RemoveNewLines ? incomingText.TrimEnd('\r', '\n') : incomingText;
                IsOpen = false;
            }
            else
            {
                sanitizedText = incomingText;
            }

            return sanitizedText;
        }

        private void SetFormatting()
        {
            if (!IsRemoteMessage)
            {
                m_displayName.FontStyle = FontStyles.Oblique;
                m_displayName.ToolTip = "This is you";
            }
        }
    }
}
