using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ProjectCeilidh.PortAudio.Native;

namespace ProjectCeilidh.PortAudio
{
    internal static class PortAudioLifetimeRegistry
    {
        private static readonly object SyncObject = new object();
        private static readonly HashSet<int> RegisteredHandles = new HashSet<int>();

        public static void Register(object target)
        {
            lock (SyncObject)
            {
                if (RegisteredHandles.Count == 0)
                {
                    Debug.WriteLine("Initializing PortAudio...");

                    var err = Native.PortAudio.Pa_Initialize();
                    if (err < PaErrorCode.NoError) throw PortAudioException.GetException(err);
                }

                RegisteredHandles.Add(RuntimeHelpers.GetHashCode(target));

                Debug.WriteLine($"Registered a new object, now up to {RegisteredHandles.Count}");
            }
        }

        public static void UnRegister(object target)
        {
            lock (SyncObject)
            {
                if (!RegisteredHandles.Remove(RuntimeHelpers.GetHashCode(target))) return;

                Debug.WriteLine($"Unregistered an object, now down to {RegisteredHandles.Count}");

                if (RegisteredHandles.Count != 0) return;

                Debug.WriteLine("Terminating PortAudio...");

                PortAudioInstanceCache.ClearCache();

                var err = Native.PortAudio.Pa_Terminate();
                if (err < PaErrorCode.NoError) throw PortAudioException.GetException(err);
            }
        }
    }
}
