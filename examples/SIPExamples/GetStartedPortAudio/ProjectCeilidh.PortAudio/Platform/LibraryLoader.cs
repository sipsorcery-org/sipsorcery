using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;

namespace ProjectCeilidh.PortAudio.Platform
{
    /// <summary>
    /// Handles the loading of native libraries and their symbols.
    /// </summary>
    internal abstract class LibraryLoader
    {
        public const string PLATFORM_WINDOWS = "WINDOWS";
        public const string PLATFORM_LINUX = "LINUX";
        public const string PLATFORM_OSX = "OSX";

        public static string CurrentPlatform
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return PLATFORM_WINDOWS;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return PLATFORM_LINUX;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return PLATFORM_OSX;

                throw new PlatformNotSupportedException();
            }
        }

        public static LibraryLoader LoaderForPlatform { get; }

        static LibraryLoader()
        {
            switch (CurrentPlatform)
            {
                case PLATFORM_LINUX:
                    LoaderForPlatform = new LinuxLibraryLoader();
                    break;
                case PLATFORM_WINDOWS:
                    LoaderForPlatform = new WindowsLibraryLoader();
                    break;
                case PLATFORM_OSX:
                    LoaderForPlatform = new OsxLibraryLoader();
                    break;
                default:
                    throw new PlatformNotSupportedException();
            }
        }

        /// <summary>
        /// Load a native library, relying on the OS to search the PATH.
        /// </summary>
        /// <param name="libraryName">The name of the library to load.</param>
        /// <param name="version">The version of the library to load. This should be as specific as possible.</param>
        /// <returns>A handle that can be used to refer to the native library.</returns>
        public NativeLibraryHandle LoadNativeLibrary(string libraryName, Version version) =>
            LoadNativeLibrary("", libraryName, version);

        /// <summary>
        /// Load a native library, searching a specific folder.
        /// </summary>
        /// <param name="path">The folder to search for the library.</param>
        /// <param name="libraryName">The name of the library to load.</param>
        /// <param name="version">The version of the library to load. This should be as specific as possible.</param>
        /// <returns>A handle that can be used to refer to the native library.</returns>
        public NativeLibraryHandle LoadNativeLibrary(string path, string libraryName, Version version)
        {
            var handle = GetNativeLibraryNames(libraryName, version).Select(x => LoadNativeLibrary(Path.Combine(path, x)))
                .FirstOrDefault(x => x != null);
            
            if (handle == null) throw new DllNotFoundException($"Could not find library \"{libraryName}.{version.Major}.{version.Minor}.{version.Build}\"");

            return handle;
        }

        protected abstract string[] GetNativeLibraryNames(string libraryName, Version version);
        protected abstract NativeLibraryHandle LoadNativeLibrary(string libraryName);
    }
}
