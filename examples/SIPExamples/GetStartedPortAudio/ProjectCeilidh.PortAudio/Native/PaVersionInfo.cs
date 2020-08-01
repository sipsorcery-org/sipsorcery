using System;
using System.Runtime.InteropServices;

namespace ProjectCeilidh.PortAudio.Native
{
    internal readonly struct PaVersionInfo
    {
        public string VersionControlRevision => _versionControlRevision == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(_versionControlRevision);

        public string VersionText => _versionText == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(_versionText);

        public Version Version => new Version(VersionMajor, VersionMinor, VersionSubMinor);

        public int VersionMajor { get; }
        public int VersionMinor { get;  }
        public int VersionSubMinor { get; }
        private IntPtr _versionControlRevision { get; }
        private IntPtr _versionText { get; }
    }
}
