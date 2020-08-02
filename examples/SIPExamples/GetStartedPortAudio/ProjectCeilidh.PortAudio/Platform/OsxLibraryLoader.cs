using System;
using System.Runtime.InteropServices;

namespace ProjectCeilidh.PortAudio.Platform
{
    internal class OsxLibraryLoader : LibraryLoader
    {
        private const int RTLD_NOW = 0x002;
        private const string LIBDL = "libdl";

        protected override string[] GetNativeLibraryNames(string libraryName, Version version)
        {
            return new[]
            {
                $"lib{libraryName}.{version.Major}.{version.Minor}.{version.Build}.dylib",
                $"lib{libraryName}.{version.Major}.{version.Minor}.dylib",
                $"lib{libraryName}.{version.Major}.dylib",
            };
        }

        protected override NativeLibraryHandle LoadNativeLibrary(string libraryName)
        {
            var handle = dlopen(libraryName, RTLD_NOW);
            return handle == IntPtr.Zero ? null : new OsxNativeLibraryHandle(handle);
        }

        [DllImport(LIBDL)]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);

        [DllImport(LIBDL)]
        private static extern IntPtr dlopen(string filename, int flag);

        [DllImport(LIBDL)]
        private static extern int dlclose(IntPtr handle);

        private class OsxNativeLibraryHandle : NativeLibraryHandle
        {
            private readonly IntPtr _handle;

            public OsxNativeLibraryHandle(IntPtr handle) => _handle = handle;

            protected override IntPtr GetSymbolAddress(string name) => dlsym(_handle, name);

            public override void Dispose() => dlclose(_handle);
        }
    }
}
