// Copyright (c) The Vignette Authors
// This file is part of SeeShark.
// SeeShark is licensed under the BSD 3-Clause License. See LICENSE for details.

using System;
using System.Runtime.InteropServices;

namespace SIPSorceryMedia.FFmpeg.Interop.X11
{
    using Display = IntPtr;
    using Window = IntPtr;

    internal static class XRandr
    {
        private const string lib_x_randr = "libXrandr";

        [DllImport(lib_x_randr, EntryPoint = "XRRGetMonitors")]
        public static extern unsafe XRRMonitorInfo* XRRGetMonitors(Display dpy, Window window, bool getActive, out int nmonitors);
    }
}
