using System;
using System.Runtime.InteropServices;

namespace ProjectCeilidh.PortAudio.Platform
{
    internal class WindowsLibraryLoader : LibraryLoader
    {
        private const string KERNEL32 = "kernel32";

        protected override string[] GetNativeLibraryNames(string libraryName, Version version)
        {
            var archDescription = IntPtr.Size == 4 ? "x86" : "x64";

            return new[]
            {
                $"{libraryName}-{version.Major}.dll",
                $"lib{libraryName}-{version.Major}.dll",
                $"{libraryName}-{version.Major}_{archDescription}.dll",
                $"lib{libraryName}-{version.Major}_{archDescription}.dll",
                $"{libraryName}.dll",
                $"lib{libraryName}.dll",
                $"{libraryName}_{archDescription}.dll",
                $"lib{libraryName}_{archDescription}.dll"
            };
        }

        protected override NativeLibraryHandle LoadNativeLibrary(string libraryName)
        {
            var handle = LoadLibrary(libraryName);
            return handle == IntPtr.Zero ? null : new WindowsNativeLibraryHandle(handle);
        }

        [DllImport(KERNEL32, CharSet = CharSet.Ansi, BestFitMapping = false)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport(KERNEL32, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string dllToLoad);

        [DllImport(KERNEL32, SetLastError = true)]
        private static extern IntPtr FreeLibrary(IntPtr hModule);

        private class WindowsNativeLibraryHandle : NativeLibraryHandle
        {
            private readonly IntPtr _hModule;

            public WindowsNativeLibraryHandle(IntPtr hModule) => _hModule = hModule;

            protected override IntPtr GetSymbolAddress(string name) => GetProcAddress(_hModule, name);

            public override void Dispose() => FreeLibrary(_hModule);
        }
    }
}
