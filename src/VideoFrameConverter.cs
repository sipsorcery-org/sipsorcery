using System;
using System.Drawing;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SIPSorcery.Ffmpeg
{
    public sealed unsafe class VideoFrameConverter : IDisposable
    {
        private static ILogger logger = NullLogger.Instance;

        private readonly IntPtr _convertedFrameBufferPtr;
        private readonly Size _srcSize;
        private readonly Size _dstSize;
        private readonly byte_ptrArray4 _dstData;
        private readonly int_array4 _dstLinesize;
        private readonly SwsContext* _pConvertContext;
        private readonly AVPixelFormat _srcPixelFormat;
        private readonly AVPixelFormat _dstPixelFormat;

        public VideoFrameConverter(Size sourceSize, AVPixelFormat sourcePixelFormat,
            Size destinationSize, AVPixelFormat destinationPixelFormat)
        {
            _srcSize = sourceSize;
            _dstSize = destinationSize;
            _srcPixelFormat = sourcePixelFormat;
            _dstPixelFormat = destinationPixelFormat;

            _pConvertContext = ffmpeg.sws_getContext(sourceSize.Width, sourceSize.Height, sourcePixelFormat,
                destinationSize.Width,
                destinationSize.Height, destinationPixelFormat,
                ffmpeg.SWS_FAST_BILINEAR, null, null, null);
            if (_pConvertContext == null)
            {
                throw new ApplicationException("Could not initialize the conversion context.");
            }

            var convertedFrameBufferSize = ffmpeg.av_image_get_buffer_size(destinationPixelFormat, destinationSize.Width, destinationSize.Height, 1);
            _convertedFrameBufferPtr = Marshal.AllocHGlobal(convertedFrameBufferSize);
            _dstData = new byte_ptrArray4();
            _dstLinesize = new int_array4();

            ffmpeg.av_image_fill_arrays(ref _dstData, ref _dstLinesize, (byte*)_convertedFrameBufferPtr, destinationPixelFormat, destinationSize.Width, destinationSize.Height, 1);

            logger.LogDebug($"Successfully initialised ffmpeg based image converted for {sourceSize}:{sourcePixelFormat}->{_dstSize}:{_dstPixelFormat}.");
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(_convertedFrameBufferPtr);
            ffmpeg.sws_freeContext(_pConvertContext);
        }

        public AVFrame Convert(byte[] srcData)
        {
            int linesz = ffmpeg.av_image_get_linesize(_srcPixelFormat, _dstSize.Width, 0);

            fixed (byte* pSrcData = srcData)
            {
                var srcFrameData = new byte_ptrArray8 { [0] = pSrcData };
                var srcLinesize = new int_array8 { [0] = srcData.Length / _srcSize.Height };

                AVFrame srcFrame = new AVFrame
                {
                    data = srcFrameData,
                    linesize = srcLinesize,
                    width = _srcSize.Width,
                    height = _srcSize.Height
                };

                ffmpeg.sws_scale(_pConvertContext, srcFrame.data, srcFrame.linesize, 0, srcFrame.height, _dstData, _dstLinesize).ThrowExceptionIfError();

                var data = new byte_ptrArray8();
                data.UpdateFrom(_dstData);
                var linesize = new int_array8();
                linesize.UpdateFrom(_dstLinesize);

                return new AVFrame
                {
                    data = data,
                    linesize = linesize,
                    width = _dstSize.Width,
                    height = _dstSize.Height
                };
            }
        }
    }
}
