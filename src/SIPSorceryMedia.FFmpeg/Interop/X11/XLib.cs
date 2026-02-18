

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace SIPSorceryMedia.FFmpeg.Interop.X11
{
    using Display = IntPtr;
    using Window = IntPtr;

    internal class XLib
    {
        private const string lib_x11 = "libX11";
        private static readonly object displayLock = new object();

        [DllImport(lib_x11, EntryPoint = "XOpenDisplay")]
        private static extern unsafe Display sys_XOpenDisplay(sbyte* display);
        public static unsafe Display XOpenDisplay(sbyte* display)
        {
            lock (displayLock)
                return sys_XOpenDisplay(display);
        }

        [DllImport(lib_x11, EntryPoint = "XCloseDisplay")]
        public static extern int XCloseDisplay(Display display);

        [DllImport(lib_x11, EntryPoint = "XDefaultRootWindow")]
        public static extern Window XDefaultRootWindow(Display display);

        [DllImport(lib_x11, EntryPoint = "XDisplayWidth")]
        public static extern int XDisplayWidth(Display display, int screenNumber);

        [DllImport(lib_x11, EntryPoint = "XDisplayHeight")]
        public static extern int XDisplayHeight(Display display, int screenNumber);


        public static List<Monitor> GetMonitors()
        {
            List<Monitor> result = new List<Monitor>();
            unsafe
            {
                IntPtr display = XLib.XOpenDisplay(null);
                IntPtr rootWindow = XLib.XDefaultRootWindow(display);
                XRRMonitorInfo* monitors = XRandr.XRRGetMonitors(display, rootWindow, true, out int count);
                for (int i = 0; i < count; i++)
                {
                    XRRMonitorInfo monitor = monitors[i];
                    string nameAddition = monitor.Name == null ? "" : $" ({new string(monitor.Name)})";

                    Monitor m = new Monitor
                    {
                        Name = $"{i}{nameAddition}",
                        Path = ":0",
                        Rect = new Rectangle(monitor.X, monitor.Y, monitor.Width, monitor.Height),
                        Primary = monitor.Primary > 0,
                    };
                    result.Add(m);
                }
            }
            return result;
        }

    }
}
