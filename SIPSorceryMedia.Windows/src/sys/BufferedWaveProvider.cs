﻿using NAudio.Wave;
using System;

namespace SIPSorceryMedia.Windows.Sys
{
    /// <summary>
    /// Provides a buffered store of samples
    /// Read method will return queued samples or fill buffer with zeroes
    /// Now backed by a circular buffer
    /// </summary>
    internal sealed class BufferedWaveProvider : IWaveProvider
    {
        private CircularBuffer? circularBuffer;
        private readonly WaveFormat waveFormat;

        /// <summary>
        /// Creates a new buffered WaveProvider
        /// </summary>
        /// <param name="waveFormat">WaveFormat</param>
        public BufferedWaveProvider(WaveFormat waveFormat)
        {
            this.waveFormat = waveFormat;
            BufferLength = waveFormat.AverageBytesPerSecond * 5;
            ReadFully = true;
        }

        /// <summary>
        /// If true, always read the amount of data requested, padding with zeroes if necessary
        /// By default is set to true
        /// </summary>
        public bool ReadFully { get; set; }

        /// <summary>
        /// Buffer length in bytes
        /// </summary>
        public int BufferLength { get; set; }

        /// <summary>
        /// Buffer duration
        /// </summary>
        public TimeSpan BufferDuration
        {
            get
            {
                return TimeSpan.FromSeconds((double)BufferLength / WaveFormat.AverageBytesPerSecond);
            }
            set
            {
                BufferLength = (int)(value.TotalSeconds * WaveFormat.AverageBytesPerSecond);
            }
        }

        /// <summary>
        /// If true, when the buffer is full, start throwing away data
        /// if false, AddSamples will throw an exception when buffer is full
        /// </summary>
        public bool DiscardOnBufferOverflow { get; set; }

        /// <summary>
        /// The number of buffered bytes
        /// </summary>
        public int BufferedBytes
        {
            get
            {
                return circularBuffer == null ? 0 : circularBuffer.Count;
            }
        }

        /// <summary>
        /// Buffered Duration
        /// </summary>
        public TimeSpan BufferedDuration
        {
            get { return TimeSpan.FromSeconds((double)BufferedBytes / WaveFormat.AverageBytesPerSecond); }
        }

        /// <summary>
        /// Gets the WaveFormat
        /// </summary>
        public WaveFormat WaveFormat
        {
            get { return waveFormat; }
        }

        /// <summary>
        /// Adds samples. Takes a copy of buffer, so that buffer can be reused if necessary
        /// </summary>
        public void AddSamples(ReadOnlySpan<byte> buffer)
        {
            // create buffer here to allow user to customise buffer length
            if (circularBuffer == null)
            {
                circularBuffer = new CircularBuffer(BufferLength);
            }

            var written = circularBuffer.Write(buffer);
            if (written < buffer.Length && !DiscardOnBufferOverflow)
            {
                throw new InvalidOperationException("Buffer full");
            }
        }

        /// <summary>
        /// Adds samples. Takes a copy of buffer, so that buffer can be reused if necessary
        /// </summary>
        public void AddSamples(byte[] buffer, int offset, int count)
        {
            AddSamples(buffer.AsSpan(offset, count));
        }

        /// <summary>
        /// Reads from this WaveProvider
        /// Will always return count bytes, since we will zero-fill the buffer if not enough available
        /// </summary>
        public int Read(byte[] buffer, int offset, int count)
        {
            int read = 0;
            if (circularBuffer != null) // not yet created
            {
                read = circularBuffer.Read(buffer, offset, count);
            }
            if (ReadFully && read < count)
            {
                // zero the end of the buffer
                Array.Clear(buffer, offset + read, count - read);
                read = count;
            }
            return read;
        }

        /// <summary>
        /// Discards all audio from the buffer
        /// </summary>
        public void ClearBuffer()
        {
            if (circularBuffer != null)
            {
                circularBuffer.Reset();
            }
        }
    }
}
