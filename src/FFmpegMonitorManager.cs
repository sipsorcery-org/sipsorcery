using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Drawing;

using SIPSorceryMedia.FFmpeg.Interop.X11;
using SIPSorceryMedia.FFmpeg.Interop.Win32;
using static SIPSorceryMedia.FFmpeg.Interop.Win32.User32;

namespace SIPSorceryMedia.FFmpeg
{
    public unsafe class FFmpegMonitorManager
    {
        static public List<Monitor>? GetMonitorDevices()
        {
            List<Monitor>? result = null ; 

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                result = User32.GetMonitors(); 
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                result = XLib.GetMonitors();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // TODO
            }
            else
            {
                throw new NotSupportedException($"Cannot find adequate input format - OSArchitecture:[{RuntimeInformation.OSArchitecture}] - OSDescription:[{RuntimeInformation.OSDescription}]");
            }

            return result;
        }
    }

    public class Monitor
    {
        public String Name { get; set; }

        public String Path { get; set; }

        public Rectangle Rect { get; set; }

        public Boolean Primary { get; set; }
    }

}
