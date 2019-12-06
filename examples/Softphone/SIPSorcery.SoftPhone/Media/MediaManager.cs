//-----------------------------------------------------------------------------
// Filename: MediaManager.cs
//
// Description: This class manages different media channels that can be included in a call, e.g.
// aduio and video. It also controls the RTP transmission and reception.
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
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SIPSorceryMedia;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SoftPhone
{
    public class MediaManager
    {
        private ILog logger = AppState.logger;

        private RTPManager _rtpManager;
        private AudioChannel _audioChannel;
        private VPXEncoder _vpxDecoder;
        private ImageConvert _imageConverter;

        private Task _localVideoSamplingTask;
        private Task _localAudioSamplingTask;
        private CancellationTokenSource _localVideoSamplingCancelTokenSource;
        private bool _stop = false;
        private int _encodingSample = 1;
        private bool _useVideo = false;

        // Audio and Video events.
        public event Action<byte[], int, int> OnLocalVideoSampleReady;      // [sample, width, height] Fires when a local video sample is ready for display.
        public event Action<byte[], int, int> OnRemoteVideoSampleReady;     // [sample, width, height] Fires when a remote video sample is ready for display.
        public event Action<string> OnLocalVideoError = delegate { };       // Fires when there is an error communicating with the local webcam.
        //public event Action<string> OnRemoteVideoError;

        public MediaManager(bool useVideo = false)
        {
            _useVideo = useVideo;

            if (_useVideo == true)
            {
                _vpxDecoder = new VPXEncoder();
                _vpxDecoder.InitDecoder();

                _imageConverter = new ImageConvert();
            }
        }

        public List<VideoMode> GetVideoDevices()
        {
            List<VideoMode> videoDevices = null;

            var videoSampler = new MFVideoSampler();
            videoSampler.GetVideoDevices(ref videoDevices);

            return videoDevices;
        }

        public void NewCall()
        {
            _audioChannel = new AudioChannel();
            _audioChannel.SampleReady += AudioChannelSampleReady;

            _rtpManager = new RTPManager(true, _useVideo);
            _rtpManager.OnRemoteVideoSampleReady += EncodedVideoSampleReceived;
            _rtpManager.OnRemoteAudioSampleReady += RemoteAudioSampleReceived;

            if (_audioChannel != null)
            {
                _audioChannel.StartRecording();
            }
        }

        public void EndCall()
        {
            if (_audioChannel != null)
            {
                _audioChannel.SampleReady -= AudioChannelSampleReady;
                _audioChannel.Close();
                _audioChannel = null;
            }

            _rtpManager.OnRemoteVideoSampleReady -= EncodedVideoSampleReceived;
            _rtpManager.OnRemoteAudioSampleReady -= RemoteAudioSampleReceived;
            _rtpManager.Close();
        }

        public SDP GetSDP(IPAddress callDstAddress)
        {
            return _rtpManager.GetSDP(callDstAddress);
        }

        public void SetRemoteSDP(SDP remoteSDP)
        {
            _rtpManager.SetRemoteSDP(remoteSDP);
        }

        /// <summary>
        /// Event handler for processing audio samples from the audio channel.
        /// </summary>
        /// <param name="sample">The audio sample ready for transmission.</param>
        private void AudioChannelSampleReady(byte[] sample)
        {
            if (sample != null && _rtpManager != null)
            {
                _rtpManager.AudioChannelSampleReady(sample);
            }
        }

        public void StartLocalVideo(VideoMode videoMode)
        {
            if (_localVideoSamplingTask != null && !_localVideoSamplingTask.IsCompleted && _localVideoSamplingCancelTokenSource != null)
            {
                _localVideoSamplingCancelTokenSource.Cancel();
            }

            var videoSampler = new MFVideoSampler();
            videoSampler.Init(videoMode.DeviceIndex, VideoSubTypesEnum.RGB24, videoMode.Width, videoMode.Height);
            //videoSampler.InitFromFile();
            //_audioChannel = new AudioChannel();

            _localVideoSamplingCancelTokenSource = new CancellationTokenSource();
            var cancellationToken = _localVideoSamplingCancelTokenSource.Token;

            _localVideoSamplingTask = Task.Run(() => SampleWebCam(videoSampler, videoMode, _localVideoSamplingCancelTokenSource));

            _localAudioSamplingTask = Task.Factory.StartNew(() =>
            {
                Thread.CurrentThread.Name = "audsampler_" + videoMode.DeviceIndex;

                while (!_stop && !cancellationToken.IsCancellationRequested)
                {
                    byte[] audioSample = null;
                    int result = videoSampler.GetAudioSample(ref audioSample);

                    if (result == NAudio.MediaFoundation.MediaFoundationErrors.MF_E_HW_MFT_FAILED_START_STREAMING)
                    {
                        logger.Warn("An audio sample could not be acquired from the local source. Check that it is not already in use.");
                        //OnLocalVideoError("A sample could not be acquired from the local webcam. Check that it is not already in use.");
                        break;
                    }
                    else if (result != 0)
                    {
                        logger.Warn("An audio sample could not be acquired from the local source. Check that it is not already in use. Error code: " + result);
                        //OnLocalVideoError("A sample could not be acquired from the local webcam. Check that it is not already in use. Error code: " + result);
                        break;
                    }
                    else if (audioSample != null)
                    {
                        if (_audioChannel != null)
                        {
                            _audioChannel.AudioSampleReceived(audioSample, 0);
                        }
                    }
                }
            }, cancellationToken);
        }

        private void SampleWebCam(MFVideoSampler videoSampler, VideoMode videoMode, CancellationTokenSource cts)
        {
            try
            {
                Thread.CurrentThread.Name = "vidsampler_" + videoMode.DeviceIndex + "_" + videoMode.Width + "_" + videoMode.Height;

                var vpxEncoder = new VPXEncoder();
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
                    if (sample?.HasVideoSample == true)
                    {
                        // This event sends the raw bitmap to the WPF UI.
                        OnLocalVideoSampleReady?.Invoke(videoSample, videoSampler.Width, videoSampler.Height);

                        // This event encodes the sample and forwards it to the RTP manager for network transmission.
                        if (_rtpManager != null)
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

                            //if(encodedBuffer )
                            _rtpManager.LocalVideoSampleReady(encodedBuffer);
                        }
                    }
                }

                videoSampler.Stop();
                vpxEncoder.Dispose();
            }
            catch (Exception excp)
            {
                logger.Error($"Exception SampleWebCam. {excp.Message}");
            }
        }

        public void StopLocalVideo()
        {
            if (_localVideoSamplingTask != null && !_localVideoSamplingTask.IsCompleted && _localVideoSamplingCancelTokenSource != null)
            {
                _localVideoSamplingCancelTokenSource.Cancel();
            }
        }

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

                unsafe
                {
                    _imageConverter.ConvertYUVToRGB((byte*)decodedSamplePtr, VideoSubTypesEnum.I420, Convert.ToInt32(decodedImgWidth), Convert.ToInt32(decodedImgHeight), VideoSubTypesEnum.RGB24, ref bmp);
                }

                Marshal.FreeHGlobal(decodedSamplePtr);

                OnRemoteVideoSampleReady?.Invoke(bmp, Convert.ToInt32(decodedImgWidth), Convert.ToInt32(decodedImgHeight));
            }
        }

        public void RemoteAudioSampleReceived(byte[] sample, int length)
        {
            _audioChannel?.AudioSampleReceived(sample, 0);
        }

        private void LocalVideoEncodedSampleReady(byte[] sample)
        {
            _rtpManager?.LocalVideoSampleReady(sample);
        }

        /// <summary>
        /// Called when the media channels are no longer required, such as when the VoIP call using it has terminated, and all resources can be shutdown
        /// and closed.
        /// </summary>
        public void Close()
        {
            try
            {
                logger.Debug("Media Manager closing.");

                _stop = true;

                if (_audioChannel != null)
                {
                    _audioChannel.SampleReady -= AudioChannelSampleReady;
                    _audioChannel.Close();
                }

                if (_rtpManager != null)
                {
                    _rtpManager.OnRemoteVideoSampleReady -= EncodedVideoSampleReceived;
                    _rtpManager.OnRemoteAudioSampleReady -= RemoteAudioSampleReceived;
                    _rtpManager.Close();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception Media Manager Close. " + excp);
            }
        }

        /// <summary>
        /// This method gets the media manager to pass local media samples to the RTP channel and then 
        /// receive them back as the remote video stream. This tests that the codec and RTP packetisation
        /// are working.
        /// </summary>
        public void RunLoopbackTest()
        {
            _rtpManager = new RTPManager(false, true);
            _rtpManager.OnRemoteVideoSampleReady += EncodedVideoSampleReceived;

            var sdp = _rtpManager.GetSDP(IPAddress.Loopback);
            _rtpManager.SetRemoteSDP(sdp);
        }
    }
}
