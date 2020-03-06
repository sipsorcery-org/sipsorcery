//-----------------------------------------------------------------------------
// Filename: MediaManager.cs
//
// Description: This class manages different media channels that can be included 
// in a call, e.g. audio and video.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 27 Nov 2014	Aaron Clauson	Refactored, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using SIPSorceryMedia;

namespace SIPSorcery.SoftPhone
{
    public class MediaManager
    {
        private static ILogger logger = Log.Logger;

        private AudioChannel _audioChannel;
        private VpxEncoder _vpxDecoder;
        private ImageConvert _imageConverter;

        private Task _localVideoSamplingTask;
        private CancellationTokenSource _localVideoSamplingCancelTokenSource;
        private bool _stop;
        private int _encodingSample = 1;
        private bool _useVideo;
        private Dispatcher _dispatcher;
        private bool _isAudioStarted;

        /// <summary>
        /// Fires when an audio sample is available from the local input device (microphone).
        /// [sample].
        /// </summary>
        public event Action<byte[]> OnLocalAudioSampleReady;

        /// <summary>
        /// Fires when a local video sample has been received from a capture device (webcam) and 
        /// is ready for display in the local UI.
        /// [sample, width, height] 
        /// </summary>
        public event Action<byte[], int, int> OnLocalVideoSampleReady;

        /// <summary>
        /// Fires when a local video sample has been encoded and is ready for transmission
        /// over an RTP channel.
        /// </summary>
        public event Action<byte[]> OnLocalVideoEncodedSampleReady;

        /// <summary>
        /// Fires when a remote video sample has been decoded and is ready for display in the local
        /// UI.
        /// [sample, width, height] 
        /// </summary>
        public event Action<byte[], int, int> OnRemoteVideoSampleReady;

        /// <summary>
        /// Fires when there is an error communicating with the local webcam.
        /// </summary>
        public event Action<string> OnLocalVideoError = delegate { };

        /// <summary>
        /// This class manages different media renderer's that can be included in a call, e.g. audio and video.
        /// </summary>
        /// <param name="dispatcher">Need a UI dispatcher so tasks can be executed on the UI thread. For example this object
        /// gets created when a button is clicked on and is therefore owned by the UI thread. When a call transfer completes the
        /// resources need to be closed without any UI interaction. In that case need to marshal back to the UI thread.</param>
        /// <param name="useVideo">Set to true if the current call is going to be using video.</param>
        public MediaManager(Dispatcher dispatcher, bool useVideo = false)
        {
            _dispatcher = dispatcher;
            _useVideo = useVideo;

            if (_useVideo)
            {
                _vpxDecoder = new VpxEncoder();
                _vpxDecoder.InitDecoder();

                _imageConverter = new ImageConvert();
            }
        }

        public List<VideoMode> GetVideoDevices()
        {
            List<VideoMode> videoDevices = null;

            var videoSampler = new MediaSource();
            videoSampler.GetVideoDevices(ref videoDevices);

            return videoDevices;
        }

        public void StartAudio()
        {
            if (!_isAudioStarted)
            {
                _isAudioStarted = true;

                _audioChannel = new AudioChannel();
                if (_audioChannel != null)
                {
                    _audioChannel.StartRecording();
                    _audioChannel.SampleReady += sample => OnLocalAudioSampleReady?.Invoke(sample);
                }
            }
        }

        public void StopAudio()
        {
            if (_audioChannel != null)
            {
                _dispatcher.DoOnUIThread(() =>
                {
                    _audioChannel.Close();
                    _audioChannel = null;
                });
            }
        }

        public void StartVideo(VideoMode videoMode)
        {
            if (_localVideoSamplingTask != null && !_localVideoSamplingTask.IsCompleted && _localVideoSamplingCancelTokenSource != null)
            {
                _localVideoSamplingCancelTokenSource.Cancel();
            }

            var videoSampler = new MediaSource();
            videoSampler.Init(0, videoMode.DeviceIndex, VideoSubTypesEnum.RGB24, videoMode.Width, videoMode.Height);
            //videoSampler.InitFromFile();
            //_audioChannel = new AudioChannel();

            _localVideoSamplingCancelTokenSource = new CancellationTokenSource();
            var cancellationToken = _localVideoSamplingCancelTokenSource.Token;

            _localVideoSamplingTask = Task.Run(() => SampleWebCam(videoSampler, videoMode, _localVideoSamplingCancelTokenSource));

            //_localAudioSamplingTask = Task.Factory.StartNew(() =>
            //{
            //    Thread.CurrentThread.Name = "audsampler_" + videoMode.DeviceIndex;

            //    while (!_stop && !cancellationToken.IsCancellationRequested)
            //    {
            //        byte[] audioSample = null;
            //        int result = videoSampler.GetAudioSample(ref audioSample);

            //        if (result == NAudio.MediaFoundation.MediaFoundationErrors.MF_E_HW_MFT_FAILED_START_STREAMING)
            //        {
            //            logger.Warn("An audio sample could not be acquired from the local source. Check that it is not already in use.");
            //        //OnLocalVideoError("A sample could not be acquired from the local webcam. Check that it is not already in use.");
            //        break;
            //        }
            //        else if (result != 0)
            //        {
            //            logger.Warn("An audio sample could not be acquired from the local source. Check that it is not already in use. Error code: " + result);
            //        //OnLocalVideoError("A sample could not be acquired from the local webcam. Check that it is not already in use. Error code: " + result);
            //        break;
            //        }
            //        else if (audioSample != null)
            //        {
            //            if (_audioChannel != null)
            //            {
            //                _audioChannel.AudioSampleReceived(audioSample, 0);
            //            }
            //        }
            //    }
            //}, cancellationToken);
        }

        private void SampleWebCam(MediaSource videoSampler, VideoMode videoMode, CancellationTokenSource cts)
        {
            try
            {
                Thread.CurrentThread.Name = "vidsampler_" + videoMode.DeviceIndex + "_" + videoMode.Width + "_" + videoMode.Height;

                var vpxEncoder = new VpxEncoder();
                // TODO: The last parameter passed to the vpx encoder init needs to be the frame stride not the width.
                vpxEncoder.InitEncoder(Convert.ToUInt32(videoMode.Width), Convert.ToUInt32(videoMode.Height), Convert.ToUInt32(videoMode.Width));

                // var videoSampler = new MFVideoSampler();
                //videoSampler.Init(videoMode.DeviceIndex, videoMode.Width, videoMode.Height);
                // videoSampler.InitFromFile();

                while (!_stop && !cts.IsCancellationRequested)
                {
                    byte[] videoSample = null;
                    var sample = videoSampler.GetSample(ref videoSample);

                    //if (result == NAudio.MediaFoundation.MediaFoundationErrors.MF_E_HW_MFT_FAILED_START_STREAMING)
                    //{
                    //    logger.Warn("A sample could not be acquired from the local webcam. Check that it is not already in use.");
                    //    OnLocalVideoError("A sample could not be acquired from the local webcam. Check that it is not already in use.");
                    //    break;
                    //}
                    //else if (result != 0)
                    //{
                    //    logger.Warn("A sample could not be acquired from the local webcam. Check that it is not already in use. Error code: " + result);
                    //    OnLocalVideoError("A sample could not be acquired from the local webcam. Check that it is not already in use. Error code: " + result);
                    //    break;
                    //}
                    //else 
                    if (sample != null && sample.HasVideoSample)
                    {
                        // This event sends the raw bitmap to the WPF UI.
                        if (sample.Stride < 0)
                        {
                            OnLocalVideoSampleReady?.Invoke(videoSample.Reverse().ToArray(), videoSampler.Width, videoSampler.Height);
                        }
                        else
                        {
                            OnLocalVideoSampleReady?.Invoke(videoSample, videoSampler.Width, videoSampler.Height);
                        }

                        // This event encodes the sample and forwards it to the RTP manager for network transmission.
                        if (OnLocalVideoEncodedSampleReady != null)
                        {
                            IntPtr rawSamplePtr = Marshal.AllocHGlobal(videoSample.Length);
                            Marshal.Copy(videoSample, 0, rawSamplePtr, videoSample.Length);

                            byte[] yuv = null;

                            unsafe
                            {
                                // TODO: using width instead of stride.
                                _imageConverter.ConvertRGBtoYUV((byte*)rawSamplePtr, VideoSubTypesEnum.RGB24, Convert.ToInt32(videoMode.Width), Convert.ToInt32(videoMode.Height), Convert.ToInt32(videoMode.Width), VideoSubTypesEnum.I420, ref yuv);
                                //_imageConverter.ConvertToI420((byte*)rawSamplePtr, VideoSubTypesEnum.RGB24, Convert.ToInt32(videoMode.Width), Convert.ToInt32(videoMode.Height), ref yuv);
                            }

                            Marshal.FreeHGlobal(rawSamplePtr);

                            IntPtr yuvPtr = Marshal.AllocHGlobal(yuv.Length);
                            Marshal.Copy(yuv, 0, yuvPtr, yuv.Length);

                            byte[] encodedBuffer = null;

                            unsafe
                            {
                                vpxEncoder.Encode((byte*)yuvPtr, yuv.Length, _encodingSample++, ref encodedBuffer);
                            }

                            Marshal.FreeHGlobal(yuvPtr);

                            OnLocalVideoEncodedSampleReady(encodedBuffer);
                        }
                    }
                }

                videoSampler.Shutdown();
                vpxEncoder.Dispose();
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception SampleWebCam. {excp.Message}");
            }
        }

        public void StopVideo()
        {
            if (_localVideoSamplingTask != null && !_localVideoSamplingTask.IsCompleted && _localVideoSamplingCancelTokenSource != null)
            {
                _localVideoSamplingCancelTokenSource.Cancel();
            }
        }

        /// <summary>
        /// This method gets called when an encoded video sample has been received from the remote call party.
        /// The sample needs to be decoded and then handed off to the UI for display.
        /// </summary>
        /// <param name="sample">The encoded video sample.</param>
        public void EncodedVideoSampleReceived(byte[] sample, int length)
        {
            IntPtr encodedBufferPtr = Marshal.AllocHGlobal(length);
            Marshal.Copy(sample, 0, encodedBufferPtr, length);

            byte[] decodedBuffer = null;
            uint decodedImgWidth = 0;
            uint decodedImgHeight = 0;

            unsafe
            {
                _vpxDecoder.Decode((byte*)encodedBufferPtr, length, ref decodedBuffer, ref decodedImgWidth, ref decodedImgHeight);
            }

            Marshal.FreeHGlobal(encodedBufferPtr);

            if (decodedBuffer != null && decodedBuffer.Length > 0)
            {
                IntPtr decodedSamplePtr = Marshal.AllocHGlobal(decodedBuffer.Length);
                Marshal.Copy(decodedBuffer, 0, decodedSamplePtr, decodedBuffer.Length);

                byte[] bmp = null;
                int stride = 0;

                unsafe
                {
                    _imageConverter.ConvertYUVToRGB((byte*)decodedSamplePtr, VideoSubTypesEnum.I420, Convert.ToInt32(decodedImgWidth), Convert.ToInt32(decodedImgHeight), VideoSubTypesEnum.RGB24, ref bmp, ref stride);
                }

                Marshal.FreeHGlobal(decodedSamplePtr);

                OnRemoteVideoSampleReady?.Invoke(bmp, Convert.ToInt32(decodedImgWidth), Convert.ToInt32(decodedImgHeight));
            }
        }

        /// <summary>
        /// This method gets called when an encoded audio sample has been received from the remote call party.
        /// The sample need to be decoded and then submitted to the local audio output device for playback.
        /// </summary>
        /// <param name="sample">The encoded audio sample.</param>
        public void EncodedAudioSampleReceived(byte[] sample)
        {
            _audioChannel?.AudioSampleReceived(sample, 0);
        }

        /// <summary>
        /// Called when the media channels are no longer required, such as when the VoIP call using it has terminated, and all resources can be shutdown
        /// and closed.
        /// </summary>
        public void Close()
        {
            try
            {
                logger.LogDebug("Media Manager closing.");

                _stop = true;

                StopAudio();
                StopVideo();
            }
            catch (Exception excp)
            {
                logger.LogError("Exception Media Manager Close. " + excp);
            }
        }

        /// <summary>
        /// This method gets the media manager to pass local media samples to the RTP channel and then 
        /// receive them back as the remote video stream. This tests that the codec and RTP packetisation
        /// are working.
        /// </summary>
        public void RunLoopbackTest()
        {
            throw new NotImplementedException("TODO");
        }
    }
}
