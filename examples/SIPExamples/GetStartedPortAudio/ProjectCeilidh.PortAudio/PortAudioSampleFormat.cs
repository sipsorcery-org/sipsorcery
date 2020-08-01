using System;
using ProjectCeilidh.PortAudio.Native;

namespace ProjectCeilidh.PortAudio
{
    public struct PortAudioSampleFormat
    {
        public enum PortAudioNumberFormat
        {
            Unsigned,
            Signed,
            FloatingPoint
        }

        internal PaSampleFormats SampleFormat { get; }

        /// <summary>
        /// Get the width of the sample format in bytes.
        /// </summary>
        public int FormatSize { get; }

        public PortAudioNumberFormat NumberFormat { get; }

        public PortAudioSampleFormat(PortAudioNumberFormat format, int size)
        {
            NumberFormat = format;
            FormatSize = size;

            switch (size)
            {
                case 1 when format == PortAudioNumberFormat.Signed:
                    SampleFormat = PaSampleFormats.Int8;
                    break;
                case 1 when format == PortAudioNumberFormat.Unsigned:
                    SampleFormat = PaSampleFormats.UInt8;
                    break;
                case 2 when format == PortAudioNumberFormat.Signed:
                    SampleFormat = PaSampleFormats.Int16;
                    break;
                case 3 when format == PortAudioNumberFormat.Signed:
                    SampleFormat = PaSampleFormats.Int24;
                    break;
                case 4 when format == PortAudioNumberFormat.Signed:
                    SampleFormat = PaSampleFormats.Int32;
                    break;
                case 4 when format == PortAudioNumberFormat.FloatingPoint:
                    SampleFormat = PaSampleFormats.Float32;
                    break;
                default:
                    throw new NotSupportedException();
            }
        }

        public override string ToString() => $"{FormatSize*8}-bit, {NumberFormat}";
    }
}
