using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ProjectCeilidh.PortAudio.Platform
{
    internal abstract class NativeLibraryHandle : IDisposable
    {
        private readonly Dictionary<string, IntPtr> _symbolTable = new Dictionary<string, IntPtr>();

        public T GetSymbolDelegate<T>(string name, bool throwOnError = true) where T : Delegate
        {
            if (!_symbolTable.TryGetValue(name, out var addr)) _symbolTable[name] = addr = GetSymbolAddress(name);

            if (addr == IntPtr.Zero)
            {
                if (throwOnError) throw new KeyNotFoundException($"Could not find symbol \"{name}\"");
                return default;
            }

            try
            {
                return (T) Marshal.GetDelegateForFunctionPointer(addr, typeof(T));
            }
            catch (MarshalDirectiveException)
            {
                if (throwOnError) throw;
                return default;
            }
        }

        // This here would work in cases where there are exported variables, but PortAudio doesn't use any.
        /*public unsafe ref T GetSymbolReference<T>(string name) where T : struct 
        {
            if (!_symbolTable.TryGetValue(name, out var addr)) _symbolTable[name] = addr = GetSymbolAddress(name);

            if (addr == IntPtr.Zero) throw new KeyNotFoundException($"Could not find symbol \"{name}\"");

            return ref Unsafe.AsRef<T>(addr.ToPointer());
        }*/

        protected abstract IntPtr GetSymbolAddress(string name);

        public abstract void Dispose();
    }
}
