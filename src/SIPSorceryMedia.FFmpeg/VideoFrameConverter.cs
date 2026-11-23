using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;

namespace SIPSorceryMedia.FFmpeg
{
    public sealed unsafe class VideoFrameConverter : IDisposable
    {
        private static ILogger logger = SIPSorcery.LogFactory.CreateLogger<VideoFrameConverter>();

        private IntPtr _convertedFrameBufferPtr;
        private readonly int _srcWidth;
        private readonly int _srcHeight;
        private readonly int _dstWidth;
        private readonly int _dstHeight;
        private readonly byte_ptrArray4 _dstData;
        private readonly int_array4 _dstLinesize;
        private readonly SwsContext* _pConvertContext;
        private readonly AVPixelFormat _srcPixelFormat;
        private readonly AVPixelFormat _dstPixelFormat;

        private readonly AVFrame *_dstFrame;

        // The sws_scale binding takes arrays, and passing it the fixed size structs converts each of them to a new
        // array on every call. Convert(byte*), which runs for every raw frame encoded from a non-I420 format, passes
        // these instead: the destination ones never change, the source ones are refilled for each frame.
        private readonly byte*[] _srcDataArray = new byte*[4];
        private readonly int[] _srcStrideArray = new int[4];
        private readonly byte*[] _dstDataArray;
        private readonly int[] _dstLinesizeArray;

        public int SourceWidth => _srcWidth;
        public int SourceHeight => _srcHeight;
        public AVPixelFormat SourcePixelFormat => _srcPixelFormat;
        public int DestinationWidth => _dstWidth;
        public int DestinationHeight => _dstHeight;
        public AVPixelFormat DestinationPixelFormat => _dstPixelFormat;

        public VideoFrameConverter(
           int srcWidth, 
           int srcHeight, 
           AVPixelFormat sourcePixelFormat,
           int dstWidth, 
           int dstHeight, 
           AVPixelFormat destinationPixelFormat)
        {
            logger.LogDebug("Attempting to initialise video frame converter for {srcWidth}:{srcHeight}:{sourcePixelFormat}->{dstWidth}:{dstHeight}:{destinationPixelFormat}.", 
                srcWidth, srcHeight, sourcePixelFormat, dstWidth, dstHeight, destinationPixelFormat);

            _srcWidth = srcWidth;
            _srcHeight = srcHeight;
            _dstWidth = dstWidth;
            _dstHeight = dstHeight;
            _srcPixelFormat = sourcePixelFormat;
            _dstPixelFormat = destinationPixelFormat;

            _pConvertContext = ffmpeg.sws_getContext(srcWidth, srcHeight, sourcePixelFormat,
                dstWidth, dstHeight, destinationPixelFormat,
                0, null, null, null);
            if (_pConvertContext == null)
            {
                throw new ApplicationException("Could not initialize the conversion context.");
            }

            var convertedFrameBufferSize = ffmpeg.av_image_get_buffer_size(destinationPixelFormat, dstWidth, dstHeight, 1).ThrowExceptionIfError() 
                + (int)ffmpeg.av_cpu_max_align();

            _convertedFrameBufferPtr = Marshal.AllocHGlobal(convertedFrameBufferSize);
            _dstData = new byte_ptrArray4();
            _dstLinesize = new int_array4();

            ffmpeg.av_image_fill_arrays(ref _dstData, ref _dstLinesize, (byte*)_convertedFrameBufferPtr, destinationPixelFormat, dstWidth, dstHeight, 1)
                .ThrowExceptionIfError();

            _dstDataArray = _dstData.ToArray();
            _dstLinesizeArray = _dstLinesize.ToArray();

            _dstFrame = ffmpeg.av_frame_alloc();
            _dstFrame->width = _dstWidth;
            _dstFrame->height = _dstHeight;
            _dstFrame->data.UpdateFrom(_dstData);
            _dstFrame->linesize.UpdateFrom(_dstLinesize);
            _dstFrame->format = (int)_dstPixelFormat;

            logger.LogDebug("Successfully initialised ffmpeg based image converter for {srcWidth}:{srcHeight}:{sourcePixelFormat}->{dstWidth}:{dstHeight}:{_dstPixelFormat}.",
                srcWidth, srcHeight, sourcePixelFormat, dstWidth, dstHeight, destinationPixelFormat);
        }

        private bool IsDisposed => _convertedFrameBufferPtr == IntPtr.Zero;

        private void EnsureNotDisposed()
        {
            if (IsDisposed) { throw new ObjectDisposedException(nameof(VideoFrameConverter)); }
        }

        public void Dispose()
        {
            if (IsDisposed) { return; }

            Marshal.FreeHGlobal(_convertedFrameBufferPtr);
            _convertedFrameBufferPtr = IntPtr.Zero;
            ffmpeg.sws_freeContext(_pConvertContext);
        }

        public AVFrame Convert(IntPtr srcData)
        {
            return Convert((byte*)srcData);
        }

        public AVFrame Convert(byte[] srcData)
        {
            AVFrame result;
            fixed (byte* pSrcData = srcData)
            {
                result = Convert(pSrcData);
            }
            return result;
        }

        public AVFrame Convert(byte * pSrcData)
        {
            EnsureNotDisposed();

            byte_ptrArray4 src = new byte_ptrArray4();
            int_array4 srcStride = new int_array4();

            ffmpeg.av_image_fill_arrays(ref src, ref srcStride, pSrcData, _srcPixelFormat, _srcWidth, _srcHeight, 1).ThrowExceptionIfError();

            for (uint i = 0; i < 4; i++)
            {
                _srcDataArray[i] = src[i];
                _srcStrideArray[i] = srcStride[i];
            }

            ffmpeg.sws_scale(_pConvertContext, _srcDataArray, _srcStrideArray, 0, _srcHeight, _dstDataArray, _dstLinesizeArray).ThrowExceptionIfError();

            // Element by element: UpdateFrom takes an array, so passing it the fixed size structs would allocate.
            var data = new byte_ptrArray8();
            var linesize = new int_array8();
            for (uint i = 0; i < 4; i++)
            {
                data[i] = _dstData[i];
                linesize[i] = _dstLinesize[i];
            }

            return new AVFrame
            {
                data = data,
                linesize = linesize,
                width = _dstWidth,
                height = _dstHeight,
                format = (int)_dstPixelFormat
            };
        }

        // to convert Bitmap to Frame
        public byte[] ConvertToBuffer(byte[] srcData)
        {
            EnsureNotDisposed();

            //int linesz0 = ffmpeg.av_image_get_linesize(_srcPixelFormat, _dstSize.Width, 0);
            //int linesz1 = ffmpeg.av_image_get_linesize(_srcPixelFormat, _dstSize.Width, 1);
            //int linesz2 = ffmpeg.av_image_get_linesize(_srcPixelFormat, _dstSize.Width, 2);

            byte_ptrArray4 src = new byte_ptrArray4();
            int_array4 srcStride = new int_array4();

            fixed (byte* pSrcData = srcData)
            {
                ffmpeg.av_image_fill_arrays(ref src, ref srcStride, pSrcData, _srcPixelFormat, _srcWidth, _srcHeight, 1).ThrowExceptionIfError();
            }

            ffmpeg.sws_scale(_pConvertContext, src, srcStride, 0, _srcHeight, _dstData, _dstLinesize).ThrowExceptionIfError();

            int outputBufferSize = ffmpeg.av_image_get_buffer_size(_dstPixelFormat, _dstWidth, _dstHeight, 1);
            byte[] outputBuffer = new byte[outputBufferSize];

            fixed (byte* pOutData = outputBuffer)
            {
                ffmpeg.av_image_copy_to_buffer(pOutData, outputBufferSize, _dstData, _dstLinesize, _dstPixelFormat, _dstWidth, _dstHeight, 1)
                    .ThrowExceptionIfError();
            }

            return outputBuffer;
        }

        public AVFrame Convert(AVFrame frame)
            => *Convert(&frame);

        public AVFrame* Convert(AVFrame* frame)
        {
            EnsureNotDisposed();

            try
            {
                ffmpeg.av_frame_copy_props(_dstFrame, frame).ThrowExceptionIfError();

                ffmpeg.sws_scale(_pConvertContext,
                                frame->data, frame->linesize, 0, frame->height,
                                _dstFrame->data, _dstFrame->linesize)
                    .ThrowExceptionIfError();
            }
            catch (Exception e)
            {
                logger.LogError("{Log}", e.Message);
            }

            return _dstFrame;
        }

        public byte[] ConvertFrame(ref AVFrame frame)
        {
            EnsureNotDisposed();

            //int linesz0 = ffmpeg.av_image_get_linesize(_srcPixelFormat, _dstSize.Width, 0);
            //int linesz1 = ffmpeg.av_image_get_linesize(_srcPixelFormat, _dstSize.Width, 1);
            //int linesz2 = ffmpeg.av_image_get_linesize(_srcPixelFormat, _dstSize.Width, 2);

            //byte_ptrArray4 src = new byte_ptrArray4();
            //int_array4 srcStride = new int_array4();

            //fixed (byte* pSrcData = srcData)
            //{
            //    ffmpeg.av_image_fill_arrays(ref src, ref srcStride, pSrcData, _srcPixelFormat, _srcWidth, _srcHeight, 1).ThrowExceptionIfError();
            //}

            ffmpeg.sws_scale(_pConvertContext, frame.data, frame.linesize, 0, frame.height, _dstData, _dstLinesize).ThrowExceptionIfError();

            int outputBufferSize = ffmpeg.av_image_get_buffer_size(_dstPixelFormat, _dstWidth, _dstHeight, 1);
            byte[] outputBuffer = new byte[outputBufferSize];

            fixed (byte* pOutData = outputBuffer)
            {
                ffmpeg.av_image_copy_to_buffer(pOutData, outputBufferSize, _dstData, _dstLinesize, _dstPixelFormat, _dstWidth, _dstHeight, 1)
                    .ThrowExceptionIfError();
            }

            return outputBuffer;
        }
    }
}
