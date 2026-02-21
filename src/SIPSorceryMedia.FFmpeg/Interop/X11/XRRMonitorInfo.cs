// Copyright (c) The Vignette Authors
// This file is part of SeeShark.
// SeeShark is licensed under the BSD 3-Clause License. See LICENSE for details.

using System;
using System.Runtime.InteropServices;

namespace SIPSorceryMedia.FFmpeg.Interop.X11
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XRRMonitorInfo
    {
        public sbyte* Name;
        public int Primary;
        public int Automatic;
        public int NOutput;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int MWidth;
        public int MHeight;
        public IntPtr Outputs;
    }
}
