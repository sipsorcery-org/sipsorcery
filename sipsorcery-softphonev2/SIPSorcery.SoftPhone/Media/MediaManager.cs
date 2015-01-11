//-----------------------------------------------------------------------------
// Filename: MediaManager.cs
//
// Description: This class manages different media channels that can be included in a call, e.g.
// aduio and video. It also controls the RTP transmission and reception.
// 
// History:
// 27 Nov 2014	Aaron Clauson	Refactored.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2014 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIPSorcery Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SIPSorceryMedia;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using SIPSorcery.Sys.Net;
using log4net;
using NAudio.MediaFoundation;

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
        private CancellationTokenSource _localVideoSamplingCancelTokenSource;
        private bool _stop = false;
        private int _encodingSample = 1;

        // Audio and Video events.
        public event Action<byte[]> OnLocalVideoSampleReady;                // Fires when a local video sample is ready for display.
        public event Action<byte[], int, int> OnRemoteVideoSampleReady;     // [sample, width, height] Fires when a remote video sample is ready for display.
        public event Action<string> OnLocalVideoError = delegate { };       // Fires when there is an error communicating with the local webcam.
        public event Action<string> OnRemoteVideoError;

        public MediaManager()
        {
            _vpxDecoder = new VPXEncoder();
            _vpxDecoder.InitDecoder();

            _imageConverter = new ImageConvert();
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

            _rtpManager = new RTPManager(true, true);
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

        public SDP GetSDP(bool usePublicIP)
        {
            return _rtpManager.GetSDP(usePublicIP);
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

            _localVideoSamplingCancelTokenSource = new CancellationTokenSource();
            var cancellationToken = _localVideoSamplingCancelTokenSource.Token;

            _localVideoSamplingTask = Task.Factory.StartNew(() =>
            {
                Thread.CurrentThread.Name = "vidsampler_" + videoMode.DeviceIndex + "_" + videoMode.Width + "_" + videoMode.Height;

                var vpxEncoder = new VPXEncoder();
                vpxEncoder.InitEncoder(Convert.ToUInt32(videoMode.Width), Convert.ToUInt32(videoMode.Height));

                var videoSampler = new MFVideoSampler();
                videoSampler.Init(videoMode.DeviceIndex, videoMode.Width, videoMode.Height);

                while (!_stop && !cancellationToken.IsCancellationRequested)
                {
                    byte[] videoSample = null;
                    int result = videoSampler.GetSample(ref videoSample);

                    if (result == NAudio.MediaFoundation.MediaFoundationErrors.MF_E_HW_MFT_FAILED_START_STREAMING)
                    {
                        logger.Warn("A sample could not be acquired from the local webcam. Check that it is not already in use.");
                        OnLocalVideoError("A sample could not be acquired from the local webcam. Check that it is not already in use.");
                        break;
                    }
                    else if (result != 0)
                    {
                        logger.Warn("A sample could not be acquired from the local webcam. Check that it is not already in use. Error code: " + result);
                        OnLocalVideoError("A sample could not be acquired from the local webcam. Check that it is not already in use. Error code: " + result);
                        break;
                    }
                    else if (videoSample != null)
                    {
                        // This event sends the raw bitmap to the WPF UI.
                        if (OnLocalVideoSampleReady != null)
                        {
                            OnLocalVideoSampleReady(videoSample);
                        }

                        // This event encodes the sample and forwards it to the RTP manager for network transmission.
                        if (_rtpManager != null)
                        {
                            IntPtr rawSamplePtr = Marshal.AllocHGlobal(videoSample.Length);
                            Marshal.Copy(videoSample, 0, rawSamplePtr, videoSample.Length);

                            byte[] yuv = null;

                            unsafe
                            {
                                _imageConverter.ConvertRGBtoYUV((byte*)rawSamplePtr, Convert.ToInt32(videoMode.Width), Convert.ToInt32(videoMode.Height), ref yuv);
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

                            _rtpManager.LocalVideoSampleReady(encodedBuffer);
                        }
                    }
                }

                videoSampler.Stop();
                vpxEncoder.Dispose();
            }, cancellationToken);
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
                    _imageConverter.ConvertYUVToRGB((byte*)decodedSamplePtr, Convert.ToInt32(decodedImgWidth), Convert.ToInt32(decodedImgHeight), ref bmp);
                }

                Marshal.FreeHGlobal(decodedSamplePtr);

                if (OnRemoteVideoSampleReady != null)
                {
                    OnRemoteVideoSampleReady(bmp, Convert.ToInt32(decodedImgWidth), Convert.ToInt32(decodedImgHeight));
                }
            }
        }

        public void RemoteAudioSampleReceived(byte[] sample, int length)
        {
            if (_audioChannel != null)
            {
                _audioChannel.AudioSampleReceived(sample, 0);
            }
        }

        private void LocalVideoEncodedSampleReady(byte[] sample)
        {
            if (_rtpManager != null)
            {
                _rtpManager.LocalVideoSampleReady(sample);
            }
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
        /// is working.
        /// </summary>
        public void RunLoopbackTest()
        {
            _rtpManager = new RTPManager(false, true);
            _rtpManager.OnRemoteVideoSampleReady += EncodedVideoSampleReceived;
            _rtpManager.OnRemoteAudioSampleReady += RemoteAudioSampleReceived;

            var sdp = _rtpManager.GetSDP(false);
            _rtpManager.SetRemoteSDP(sdp);
        }
    }
}
