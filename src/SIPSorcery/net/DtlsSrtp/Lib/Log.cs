// SharpSRTP
// Copyright (C) 2025 Lukas Volf
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE 
// SOFTWARE.

using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SIPSorcery.Net.SharpSRTP
{
    public static class Log
    {
        public static bool WarnEnabled { get; set; } = true;
        public static void Warn(string message, Exception ex = null)
        {
            SinkWarn(message, ex);
        }

        public static bool ErrorEnabled { get; set; } = true;
        public static void Error(string message, Exception ex = null)
        {
            SinkError(message, ex);
        }

        public static bool TraceEnabled { get; set; } = true;
        public static void Trace(string message, Exception ex = null)
        {
            SinkTrace(message, ex);
        }

        public static bool DebugEnabled { get; set; }
#if DEBUG
            = true;
#endif

        public static void Debug(string message, Exception ex = null)
        {
            SinkDebug(message, ex);
        }

        public static void Debug(string messageTemplate, params object[] args)
        {
            SinkDebug(FormatTemplate(messageTemplate, args), null);
        }

        public static bool InfoEnabled { get; set; }
#if DEBUG
            = true;
#endif
        public static void Info(string message, Exception ex = null)
        {
            SinkInfo(message, ex);
        }

        public static Action<string, Exception> SinkWarn = new Action<string, Exception>((m, ex) => { System.Diagnostics.Debug.WriteLine(m); });
        public static Action<string, Exception> SinkError = new Action<string, Exception>((m, ex) => { System.Diagnostics.Debug.WriteLine(m); });
        public static Action<string, Exception> SinkTrace = new Action<string, Exception>((m, ex) => { System.Diagnostics.Debug.WriteLine(m); });
        public static Action<string, Exception> SinkDebug = new Action<string, Exception>((m, ex) => { System.Diagnostics.Debug.WriteLine(m); });
        public static Action<string, Exception> SinkInfo = new Action<string, Exception>((m, ex) => { System.Diagnostics.Debug.WriteLine(m); });

        private static readonly Regex MessageTemplateRegex = new Regex(@"(?<!\{)\{[^{}]+\}(?!\})", RegexOptions.Compiled);

        private static string FormatTemplate(string messageTemplate, object[] args)
        {
            if (string.IsNullOrEmpty(messageTemplate) || args == null || args.Length == 0)
            {
                return messageTemplate;
            }

            int index = 0;
            string compositeFormat = MessageTemplateRegex.Replace(messageTemplate, _ => $"{{{index++}}}");
            return string.Format(CultureInfo.InvariantCulture, compositeFormat, args);
        }
    }
}
