using System;
using System.Runtime.InteropServices;

namespace ProjectCeilidh.PortAudio.Platform
{
    internal class LinuxLibraryLoader : LibraryLoader
    {
        public const int RTLD_NOW = 0x002;

        private const string LIBDL = "libdl.so.2";

        protected override string[] GetNativeLibraryNames(string libraryName, Version version)
        {
            return new[]
            {
                $"lib{libraryName}.so.{version.Major}.{version.Minor}.{version.Build}",
                $"lib{libraryName}.so.{version.Major}.{version.Minor}",
                $"lib{libraryName}.so.{version.Major}"
            };
        }

        protected override NativeLibraryHandle LoadNativeLibrary(string libraryName)
        {
            var handle = dlopen(libraryName, RTLD_NOW);
            return handle == IntPtr.Zero ? null : new LinuxNativeLibraryHandle(handle);
        }

        [DllImport(LIBDL)]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);

        [DllImport(LIBDL)]
        private static extern IntPtr dlopen(string filename, int flag);

        [DllImport(LIBDL)]
        private static extern int dlclose(IntPtr handle);

        private class LinuxNativeLibraryHandle : NativeLibraryHandle
        {
            private readonly IntPtr _handle;

            public LinuxNativeLibraryHandle(IntPtr handle) => _handle = handle;

            protected override IntPtr GetSymbolAddress(string name) => dlsym(_handle, name);

            public override void Dispose() => dlclose(_handle);
        }
    }
}
