using System;
using System.Collections.Generic;
using System.Text;

namespace SIPSorcery.SoftPhone.Signalling
{
    /// <summary>Event args for realtime text message</summary>
    public class TextEventArgs : EventArgs
    {
        /// <summary>Timestamp of text</summary>
        /// <remarks>Will be set to the time of creation of this instance, if no custom value was provided</remarks>
        public DateTime Timestamp { get; set; }

        /// <summary>Text data</summary>
        public string Text { get; set; }

        /// <summary>Source name</summary>
        public string Source { get; set; }

        /// <inheritdoc cref="TextEventArgs"/>
        public TextEventArgs(string source, string text, DateTime? time = null)
        {
            Source = source;
            Text = text;
            Timestamp = time ?? DateTime.Now;
        }
    }
}
