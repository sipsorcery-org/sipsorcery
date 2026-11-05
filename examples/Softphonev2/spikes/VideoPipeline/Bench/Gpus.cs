using System.Runtime.InteropServices;

namespace VideoSpike.Bench;

/// <summary>
/// Maps the adapter LUIDs in the "GPU Engine" counter names to adapter names, via DXGI, so the
/// results say which GPU did the work on a machine with two.
/// </summary>
public static unsafe class Gpus
{
    private static Dictionary<string, string>? _names;

    public static Dictionary<string, string> Names() => _names ??= Enumerate();

    public static string Short(string luid)
    {
        if (!Names().TryGetValue(luid, out var name)) return luid;
        foreach (var vendor in new[] { "NVIDIA", "Intel", "AMD", "Microsoft" })
            if (name.Contains(vendor, StringComparison.OrdinalIgnoreCase)) return vendor;
        return name;
    }

    private static Dictionary<string, string> Enumerate()
    {
        var result = new Dictionary<string, string>();
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");   // IDXGIFactory1
        if (CreateDXGIFactory1(ref iid, out var factory) < 0) return result;
        try
        {
            var vtbl = *(void***)factory;
            var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)vtbl[12];
            for (uint i = 0; ; i++)
            {
                IntPtr adapter;
                if (enumAdapters1(factory, i, &adapter) < 0) break;
                var avtbl = *(void***)adapter;
                var getDesc1 = (delegate* unmanaged[Stdcall]<IntPtr, DXGI_ADAPTER_DESC1*, int>)avtbl[10];
                DXGI_ADAPTER_DESC1 desc;
                if (getDesc1(adapter, &desc) >= 0)
                {
                    var name = new string(desc.Description).TrimEnd('\0');
                    result[$"0x{desc.LuidHigh:X8}_0x{desc.LuidLow:X8}"] = name;
                }
                Marshal.Release(adapter);
            }
        }
        finally { Marshal.Release(factory); }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_ADAPTER_DESC1
    {
        public fixed char Description[128];
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);
}
