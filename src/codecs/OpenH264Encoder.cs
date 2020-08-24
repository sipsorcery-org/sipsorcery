using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace SIPSorceryMedia.Windows.Codecs
{
    public class OpenH264Encoder
    {
        [DllImport("openh264-2.1.1-win64", EntryPoint = "WelsGetCodecVersion")]
        public static extern IntPtr GetCodecVersion();

        private bool IsDisposed;
        public Action<DataFrame> EncodeResult;
        [DllImport("OpenH264Lib", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        static extern void InitEncoder(string dllName, int width, int height, int bps, int fps, int keyframeInterval, EncodeFunc encodeFunc);
        [DllImport("OpenH264Lib", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        static extern void Dispose();

        [DllImport("OpenH264Lib", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern void EncodeFrame(byte_ptrArray8 data, float timeStamp, bool forceKeyFrame);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        unsafe delegate void EncodeFunc(byte* data, int* sizes, int sizeCount, int layerSize, FrameType frameType, uint frameNum);
        unsafe struct EncodeFuncContext
        {
            public IntPtr Pointer;
            public static implicit operator EncodeFuncContext(EncodeFunc func) => new EncodeFuncContext { Pointer = Marshal.GetFunctionPointerForDelegate(func) };
        }

        private EncodeFuncContext encodeFuncContext;

        private List<Delegate> delegateRefs = new List<Delegate>();
        private float FrameNum = 0;
        private uint EncodeNum = 0;
        private int Width;
        private int Height;
        private float KeyframeInterval;
        private int IFrameCount = 0;
        private bool forceKeyFrame = false;
        private ConcurrentQueue<DateTime> dateTimeQueue = new ConcurrentQueue<DateTime>();

        enum FrameType { Invalid, IDR, I, P, Skip, IPMixed };

        public OpenH264Encoder(string dllName, int width, int height, int bps, int fps, int keyframeInterval)
        {
            this.Width = width;
            this.Height = height;
            this.KeyframeInterval = keyframeInterval;
            bps = bps / 2;

            unsafe
            {
                EncodeFunc encodeFunc = (byte* data, int* sizes, int sizeCount, int layerSize, FrameType frameType, uint frameNum) =>
                {
                    var keyFrame = (frameType == FrameType.IDR) || (frameType == FrameType.I);
                    var d = new byte[layerSize];
                    Marshal.Copy((IntPtr)data, d, 0, layerSize);
                    var now = DateTime.Now;
                    if (dateTimeQueue.TryDequeue(out var dt))
                    {
                        now = dt;
                    }
                    var df = new DataFrame()
                    {
                        StartTime = now,
                        Data = d,
                        DataLength = layerSize,
                        FrameNum = EncodeNum++,
                        KeyFrame = keyFrame
                    };
                    EncodeResult?.Invoke(df);
                };

                delegateRefs.Add(encodeFunc);

                this.encodeFuncContext = new EncodeFuncContext { Pointer = Marshal.GetFunctionPointerForDelegate(encodeFunc) };

                InitEncoder(dllName, width, height, bps, fps, keyframeInterval, encodeFunc);
            }
        }

        [HandleProcessCorruptedStateExceptions]
        public bool EncodeData(byte[] frame, DateTime dt)
        {
            try
            {
                dateTimeQueue.Enqueue(dt);

                IFrameCount++;
                if (IFrameCount > KeyframeInterval)
                {
                    forceKeyFrame = true;
                    IFrameCount = 0;
                }

                unsafe
                {
                    fixed (byte* start = frame)
                    {
                        var data = new byte_ptrArray8 { [0] = start };
                        EncodeFrame(data, FrameNum++, forceKeyFrame);
                    }
                }
                forceKeyFrame = false;
                return true;
            }
            catch
            {
                if (!IsDisposed)
                {
                    throw;
                }
                return false;
            }
        }

        //public void EncodeData(byte[] data, float timeStamp)
        //{
        //    EncodeFrame(data, timeStamp);
        //}

        public void Close()
        {
            IsDisposed = true;
            Dispose();
        }
    }

    public struct DataFrame
    {
        public int DataLength { get; set; }
        public byte[] Data { get; set; }
        public uint FrameNum { get; set; }
        public int pDataLength { get; set; }
        public bool KeyFrame { get; set; }
        public DateTime StartTime { get; set; }
    }

    public unsafe struct byte_ptrArray8
    {
        public static readonly int Size = 8;
        byte* _0; byte* _1; byte* _2; byte* _3; byte* _4; byte* _5; byte* _6; byte* _7;

        public byte* this[uint i]
        {
            get { if (i >= Size) throw new ArgumentOutOfRangeException(); fixed (byte** p0 = &_0) { return *(p0 + i); } }
            set { if (i >= Size) throw new ArgumentOutOfRangeException(); fixed (byte** p0 = &_0) { *(p0 + i) = value; } }
        }
        public byte*[] ToArray()
        {
            fixed (byte** p0 = &_0) { var a = new byte*[Size]; for (uint i = 0; i < Size; i++) a[i] = *(p0 + i); return a; }
        }
        public void UpdateFrom(byte*[] array)
        {
            fixed (byte** p0 = &_0) { uint i = 0; foreach (var value in array) { *(p0 + i++) = value; if (i >= Size) return; } }
        }
        public static implicit operator byte*[](byte_ptrArray8 @struct) => @struct.ToArray();
    }
}
