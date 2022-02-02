using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace SIPSorceryMedia.FFmpeg.Interop.Win32
{
    internal class User32
    {
        const uint MONITORINFOF_PRIMARY = 1;

        [StructLayout(LayoutKind.Sequential)]
        struct Rect
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MonitorInfo
        {
            public uint size;
            public Rect monitor;
            public Rect work;
            public uint flags;
        }

        delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr hmon, ref MonitorInfo mi);


    #region PUBLIC

        public static List<Monitor> GetMonitors()
        {
            List<Monitor> result = new List<Monitor> ();
            int index = 0;
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                    delegate (IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData)
                    {
                        MonitorInfo mi = new MonitorInfo();
                        mi.size = (uint)Marshal.SizeOf(mi);
                        bool success = GetMonitorInfo(hMonitor, ref mi);

                        Monitor monitor = new Monitor
                        {
                            Name = $"{index++}",
                            Path = "desktop",
                            Rect = new Rectangle(mi.monitor.left, mi.monitor.top, mi.monitor.right - mi.monitor.left, mi.monitor.bottom - mi.monitor.top),
                            Primary = mi.flags == MONITORINFOF_PRIMARY,
                        };
                        result.Add(monitor);
                        return true;
                    }, 
                    IntPtr.Zero);
            return result;
        }

    #endregion PUBLIC

    }
}
