using System;
using System.Runtime.InteropServices;

namespace PortAudio.Net
{
    public class PaBuffer : IDisposable
    {
        /// <summary>
        /// Indicates whether this object owns memory at Pointer which has been allocated from the unmanaged heap
        /// </summary>
        private bool owning = false;

        /// <summary>
        /// This handle will have a value if Pointer refers to a pinned object
        /// </summary>
        private GCHandle? handle = null;
        
        /// <summary>
        /// Set true on disposal to protect from accidentally trying to free resources multiple times
        /// </summary>
        private bool disposed = false;

        /// <summary>
        /// An object used as a lock to ensure thread safety during object disposal
        /// </summary>
        private object lockObject = new object();

        /// <summary>
        /// A pointer to the contents of the buffer, either supplied externally, allocated on the unmanaged heap, or
        /// from a pinned array.
        /// </summary>
        public IntPtr Pointer { get; }

        /// <summary>
        /// The number of sample frames in the buffer.
        /// </summary>
        public int Frames { get; }

        /// <summary>
        /// The number of channels of sound data in the buffer.
        /// </summary>
        public int Channels { get; }

        /// <summary>
        /// Initializes the Channels and Frames of a new PaBuffer
        /// </summary>
        /// <param name="channels">The number of logical data channels</param>
        /// <param name="frames">The number of logical data frames</param>
        private PaBuffer(int channels, int frames)
        {
            Channels = channels;
            Frames = frames;
        }

        /// <summary>
        /// Initializes an instance of <see cref="PaBuffer"/> backed by memory allocated on the unmanaged heap for the
        /// lifetime of the buffer until it is disposed.
        /// </summary>
        /// <param name="size">The physical size in bytes to allocate for the buffer</param>
        /// <param name="channels">The number of logical data channels</param>
        /// <param name="frames">The number of logical data frames</param>
        public PaBuffer(int size, int channels, int frames) : this(channels, frames)
        {
            Pointer = Marshal.AllocHGlobal(size);
            owning = true;
        }

        /// <summary>
        /// Initializes an instance of <see cref="PaBuffer"/> backed by an array object which is pinned for the lifetime
        /// of the buffer until it is disposed. Modifying data in the buffer alters the data in the underlying array.
        /// </summary>
        /// <remarks>
        /// Pinning large objects can severely affect the efficiency of the runtime's garbage collector. Objects
        /// constructed by this method should be disposed as soon as possible.
        /// </remarks>
        /// <param name="array">
        /// The array used to back the buffer containing exactly <c>Frames * Channels</c> elements
        /// </param>
        /// <param name="channels">The number of logical data channels</param>
        /// <param name="frames">The number of logical data frames</param>
        public PaBuffer(Array array, int channels, int frames) : this(channels, frames)
        {
            CheckArrayDimensions(array);
            handle = GCHandle.Alloc(array, GCHandleType.Pinned);
            Pointer = handle.Value.AddrOfPinnedObject();
        }

        /// <summary>
        /// Initializes an instance of <see cref="PaBuffer"/> backed by arbitrary memory. No action is taken upon
        /// disposal.
        /// </summary>
        /// <param name="pointer">A pointer to the memory used by the buffer</param>
        /// <param name="channels">The number of logical data channels</param>
        /// <param name="frames">The number of logical data frames</param>
        public unsafe PaBuffer(IntPtr pointer, int channels, int frames) : this(channels, frames)
        {
            Pointer = pointer;
        }

        protected void CheckArrayDimensions(Array array)
        {
            if (array.Length != Channels * Frames)
                throw new ArgumentException(
                    "The array is expected to contain exactly as many elements as the buffer, "+
                    "i.e. array.Length == channels * frames.",
                    nameof(array));
        }

        protected virtual void Dispose(bool disposing)
        {
            lock (lockObject)
            {
                if (disposed)
                    return;
                disposed = true;
            }
            if (owning)
                Marshal.FreeHGlobal(Pointer);
            else if (handle.HasValue)
                handle.Value.Free();
        }

        /// </inheritdoc>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~PaBuffer()
        {
            Dispose(false);
        }
    }

    public class PaBuffer<T>: PaBuffer where T: unmanaged
    {

        #if NETCOREAPP

        /// <summary>
        /// Gets a span for the underlying memory backing the buffer.
        /// </summary>
        /// <remarks>
        /// Due to its safety and efficiency, <see cref="Span<T>"/> is the preferred means of accessing the memory.
        /// However, this property is not available in .NET Framework, only .NET Core.
        /// </remarks>
        public Span<T> Span
        {
            get
            {
                unsafe
                {
                    return new Span<T>(Pointer.ToPointer(), Channels * Frames);
                }
            }
        }

        #endif

        private unsafe int Size => Channels * Frames * sizeof(T);

        /// <summary>
        /// Initializes a new instance of <see cref="PaBuffer<T>"/> backed by memory allocated on the unmanaged heap for
        /// the lifetime of the buffer until it is disposed.
        /// </summary>
        /// <param name="channels">The number of logical data channels</param>
        /// <param name="frames">The number of logical data frames</param>
        public PaBuffer(int channels, int frames) :
            base(channels * frames * Marshal.SizeOf(typeof(T)), channels, frames) {}

        /// <summary>
        /// Initializes an instance of <see cref="PaBuffer<T>"/> backed by arbitrary memory. No action is taken upon
        /// disposal.
        /// </summary>
        /// <param name="pointer">A pointer to the memory used by the buffer</param>
        /// <param name="channels">The number of logical data channels</param>
        /// <param name="frames">The number of logical data frames</param>
        public unsafe PaBuffer(IntPtr pointer, int channels, int frames) : base(pointer, channels, frames) {}

        /// <summary>
        /// Initializes an instance of <see cref="PaBuffer<T>"/> backed by an array object which is pinned for the
        /// lifetime of the buffer until it is disposed. Modifying data in the buffer alters the data in the underlying
        /// array.
        /// </summary>
        /// <remarks>
        /// Pinning large objects can severely affect the efficiency of the runtime's garbage collector. Objects
        /// constructed by this method should be disposed as soon as possible.
        /// </remarks>
        /// <param name="array">
        /// The array used to back the buffer containing exactly <c>Frames * Channels</c> elements.
        /// </param>
        /// <param name="channels">The number of logical data channels</param>
        /// <param name="frames">The number of logical data frames</param>
        public PaBuffer(T[] array, int channels, int frames) : base(array, channels, frames) {}

        /// <summary>
        /// Initializes an instance of <see cref="PaBuffer<T>"/> backed by an array object which is pinned for the
        /// lifetime of the buffer until it is disposed. Modifying data in the buffer alters the data in the underlying
        /// array.
        /// </summary>
        /// <remarks>
        /// Pinning large objects can severely affect the efficiency of the runtime's garbage collector. Objects
        /// constructed by this method should be disposed as soon as possible.
        /// </remarks>
        /// <param name="array">
        /// The array used to back the buffer containing exactly <c>Frames</c> elements in the first dimension, and
        /// <c>Channels</c> elements in the second.
        /// </param>
        /// <param name="channels">The number of logical data channels</param>
        /// <param name="frames">The number of logical data frames</param>
        public PaBuffer(T[,] array, int channels, int frames) : base(array, channels, frames)
        {
            try
            {
                CheckArrayDimensions(array);
            }
            catch
            {
                // unfortunately the base constructor will have already pinned the array, which must be freed
                Dispose();
            }
        }

        private void CheckArrayDimensions(T[,] array)
        {
            if (array.GetLength(0) != Frames)
                throw new ArgumentException(
                    "The length of the array's 0th dimension is expected to match the number of frames, " +
                    "i.e. array.GetLength(0) == frames.",
                    nameof(array));
            if (array.GetLength(1) != Channels)
                throw new ArgumentException(
                    "The length of the array's 1st dimension is expected to match the number of channels, " +
                    "i.e. array.GetLength(1) == channels.",
                    nameof(array));
        }

        /// <summary>
        /// Copies the data from the buffer to a memory location specified.
        /// </summary>
        /// <param name="pointer">A pointer to the memory location the data will be copied to</param>
        private unsafe void GetData(T* pointer) => Buffer.MemoryCopy(Pointer.ToPointer(), (void*)pointer, Size, Size);

        /// <summary>
        /// Copies data from a memory location specified into the entire buffer.
        /// </summary>
        /// <param name="pointer">A pointer to the memory location the data will be copied from</param>
        private unsafe void SetData(T* pointer) => Buffer.MemoryCopy((void*)pointer, Pointer.ToPointer(), Size, Size);

        public void GetArrayData(T[] array)
        {
            CheckArrayDimensions(array);
            unsafe
            {
                fixed (T* ptr = array)
                {
                    GetData(ptr);
                }
            }
        }

        public void GetArrayData(T[,] array)
        {
            CheckArrayDimensions(array);
            unsafe
            {
                fixed (T* ptr = array)
                {
                    GetData(ptr);
                }
            }
        }

        public void SetArrayData(T[] array)
        {
            CheckArrayDimensions(array);
            unsafe
            {
                fixed (T* ptr = array)
                {
                    SetData(ptr);
                }
            }
        }

        public void SetArrayData(T[,] array)
        {
            CheckArrayDimensions(array);
            unsafe
            {
                fixed (T* ptr = array)
                {
                    SetData(ptr);
                }
            }
        }

        public T[] GetArray1D()
        {
            T[] array = new T[Channels * Frames];
            GetArrayData(array);
            return array;
        }

        public T[,] GetArray2D()
        {
            T[,] array = new T[Frames, Channels];
            GetArrayData(array);
            return array;
        }
    }

    public class PaNonInterleavedBuffer<T> : PaBuffer where T: unmanaged
    {
        private PaBuffer<T>[] channelBuffers;

        public PaNonInterleavedBuffer(int channels, int frames) :
            base(channels * Marshal.SizeOf(typeof(IntPtr)), channels, frames)
        {
            channelBuffers = new PaBuffer<T>[Channels];
            unsafe
            {
                IntPtr* ptr = (IntPtr*)Pointer.ToPointer();
                for (int channelIndex = 0; channelIndex < Channels; channelIndex++)
                {
                    var channelBuffer = new PaBuffer<T>(1, Frames);
                    channelBuffers[channelIndex] = channelBuffer;
                    ptr[channelIndex] = channelBuffer.Pointer;
                }
            }
        }

        public unsafe PaNonInterleavedBuffer(IntPtr pointer, int channels, int frames) : base(pointer, channels, frames)
        {
            channelBuffers = new PaBuffer<T>[Channels];
            IntPtr* ptr = (IntPtr*)Pointer.ToPointer();
            for (int channelIndex = 0; channelIndex < Channels; channelIndex++)
                channelBuffers[channelIndex] = new PaBuffer<T>(ptr[channelIndex], channels, frames);
        }

        public PaNonInterleavedBuffer(T[][] array, int channels, int frames) :
            base(channels * Marshal.SizeOf(typeof(IntPtr)), channels, frames)
        {
            try
            {
                if (array.Length != channels)
                    throw new ArgumentException(
                        "The length of the jagged array is expected to match the number of channels, " +
                        "i.e. array.Length == channels.",
                        nameof(array));
                for (int channelIndex = 0; channelIndex < Channels; channelIndex++)
                    if (array[channelIndex].Length != frames)
                        throw new ArgumentException(
                            "The length of each array in the jagged array is expected to match the number of frames, " +
                            "i.e. array[channel].Length == frames, for all channel = 1 ... channel = Channels - 1.",
                            nameof(array));
            }
            catch
            {
                // unfortunately the base constructor will already have allocated memory, which must be freed
                Dispose();
            }
            channelBuffers = new PaBuffer<T>[Channels];
            unsafe
            {
                IntPtr* ptr = (IntPtr*)Pointer.ToPointer();
                for (int channelIndex = 0; channelIndex < Channels; channelIndex++)
                {
                    var channelBuffer = new PaBuffer<T>(array[channelIndex], 1, frames);
                    channelBuffers[channelIndex] = channelBuffer;
                    ptr[channelIndex] = channelBuffer.Pointer;
                }
            }
        }

        public PaBuffer<T> GetChannel(int channelIndex)
        {
            if (channelIndex < 0 || channelIndex >= Channels)
                throw new ArgumentException(
                    "Channel indices must be between 0 and Channels - 1 inclusive.",
                    nameof(channelIndex));
            return channelBuffers[channelIndex];
        }
    }
}