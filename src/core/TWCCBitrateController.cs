/*
* Filename: TWCCBitrateController.cs
*
* Description:
* Uses TWCC Reports to adjust bitrates and framerates for video streams.
*
* Author:        Sean Tearney
* Date:          2025 - 03 - 05
*
* License:       BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
* 
* Change Log:
*   2025-03-05  Initial creation.
*/

using System;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.core
{

    public class BitrateUpdateEventArgs : EventArgs
    {
        public int Bitrate { get; set; }
        public int Framerate { get; set; }

        public BitrateUpdateEventArgs(double bitrate, double framerate)
        {
            Bitrate = (int)bitrate;
            Framerate = (int)framerate;
        }
    }
    /// <summary>
    /// A controller that uses TWCC feedback reports
    /// to adjust the encoder bitrate based on measured network conditions
    /// over a rolling window using an exponential moving average (EMA).
    /// </summary>
    public class TWCCBitrateController
    {
        public event EventHandler<BitrateUpdateEventArgs> OnBitrateChange;
        private double _currentBitrate;    // in bits per second
        private readonly double _minBitrate = 60000;
        private double _maxBitrate;

        // EMA for delay (µs) and loss rate (0 to 1).
        private double _rollingAvgDelay;
        private double _rollingLossRate;
        private bool _isFirstFeedback = true;

        // Smoothing factor for EMA. Smaller alpha => slower reaction.
        private const double Alpha = 0.1;

        // Interval (ms) at which we update the encoder bitrate.
        private const int UpdateIntervalMs = 1000; // 1 second

        private DateTime _lastUpdateTime;

        // For accumulating feedback in the current interval.
        private int _accumReceivedCount;
        private int _accumLostCount;
        private double _accumDelaySum;
        private double _maxFramerate;
        private double _minFramerate = 2;
        private double _framerate;
        private bool _inited = false;

        // Thread-safety lock if needed
        private readonly object _lock = new object();


        public TWCCBitrateController()
        {
            _lastUpdateTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Add a new feedback report. We accumulate the stats,
        /// but only update the actual bitrate on a fixed interval.
        /// </summary>
        public void ProcessFeedback(RTCPTWCCFeedback feedback)
        {
            if (feedback == null || !_inited) { return; }

            lock (_lock)
            {
                foreach (var ps in feedback.PacketStatuses)
                {
                    switch (ps.Status)
                    {
                        case TWCCPacketStatusType.NotReceived:
                            _accumLostCount++;
                            break;
                        case TWCCPacketStatusType.ReceivedSmallDelta:
                        case TWCCPacketStatusType.ReceivedLargeDelta:
                            if (ps.Delta.HasValue)
                            {
                                // Add the delay (µs).
                                _accumDelaySum += Math.Abs(ps.Delta.Value);
                                _accumReceivedCount++;
                            }
                            break;
                    }
                }

                var now = DateTime.UtcNow;
                double elapsedMs = (now - _lastUpdateTime).TotalMilliseconds;

                // Check if it's time to update the bitrate.
                if (elapsedMs >= UpdateIntervalMs)
                {
                    // Compute average stats over this interval
                    double intervalAvgDelay = 0.0;
                    double intervalLossRate = 0.0;

                    int totalPackets = _accumReceivedCount + _accumLostCount;
                    if (totalPackets > 0)
                    {
                        intervalAvgDelay = _accumDelaySum / _accumReceivedCount;
                        intervalLossRate = (double)_accumLostCount / totalPackets;
                    }

                    // Reset accumulators for the next interval.
                    _accumDelaySum = 0.0;
                    _accumReceivedCount = 0;
                    _accumLostCount = 0;

                    // Update rolling averages (EMA).
                    if (_isFirstFeedback)
                    {
                        _rollingAvgDelay = intervalAvgDelay;
                        _rollingLossRate = intervalLossRate;
                        _isFirstFeedback = false;
                    }
                    else
                    {
                        _rollingAvgDelay = (1 - Alpha) * _rollingAvgDelay + Alpha * intervalAvgDelay;
                        _rollingLossRate = (1 - Alpha) * _rollingLossRate + Alpha * intervalLossRate;
                    }

                    // Adjust the bitrate with a simple AIMD logic.
                    // Example thresholds (tune to your environment!):
                    const double highDelayThreshold = 100000.0;   // 100 ms in microseconds
                    const double superHighDelayThreshold = 200000.0; // 200 ms in microseconds
                    const double lossThreshold = 0.05;              // 5% loss
                    const double superHighLossThreshold = 0.15; // 15% loss

                    if (_rollingAvgDelay > highDelayThreshold || _rollingLossRate > lossThreshold)
                    {
                        if (_rollingAvgDelay > superHighDelayThreshold || _rollingLossRate > superHighLossThreshold)
                        {
                            // Super high delay: more drastic reduction.
                            _currentBitrate *= 0.5;
                            _framerate *= 0.5;
                        }
                        else
                        {
                            // Congestion: decrease bitrate by 15%.
                            _currentBitrate *= 0.85;
                            _framerate *= 0.85;
                        }
                    }
                    else
                    {
                        // Good network: increase by 20 kbps.
                        _currentBitrate += 20000;
                        _framerate *= 1.1;
                    }

                    // Clamp
                    _currentBitrate = Math.Max(_minBitrate, Math.Min(_currentBitrate, _maxBitrate));
                    _framerate = Math.Max(_minFramerate, Math.Min(_framerate, _maxFramerate));

                    // Update the encoder bitrate
                    OnBitrateChange?.Invoke(this, new BitrateUpdateEventArgs(_currentBitrate, _framerate));

                    // Log for debugging
                    //Debug.WriteLine(
                    //   $"[TWCCBitrateController] IntervalAvgDelay: {intervalAvgDelay:F1}µs, IntervalLossRate: {intervalLossRate:P1}, " +
                    //   $"RollingAvgDelay: {_rollingAvgDelay:F1}µs, RollingLossRate: {_rollingLossRate:P1}, MaxBitRate: {_maxBitrate:F0}, NewBitrate: {_currentBitrate:F0}bps");

                    _lastUpdateTime = now;
                }
            }
        }

        public void CalculateMaxBitrate(int width, int height, int framerate, VideoCodecsEnum codec)
        {
            // Base bitrates in kbps at 1fps
            int baseBitratePerFrame;

            if (codec == VideoCodecsEnum.H264)
            {
                if (width * height <= 352 * 288)
                {
                    // CIF or smaller
                    baseBitratePerFrame = 13;
                }
                else if (width * height <= 640 * 480)
                {
                    // VGA
                    baseBitratePerFrame = 35;
                }
                else if (width * height <= 1280 * 720)
                {
                    // 720p
                    baseBitratePerFrame = 87;
                }
                else if (width * height <= 1920 * 1080)
                {
                    // 1080p
                    baseBitratePerFrame = 173;
                }
                else                                    
                {
                    // 4K and above
                    baseBitratePerFrame = 347;
                }
            }
            else
            {
                if (width * height <= 352 * 288)
                {
                    // CIF or smaller
                    baseBitratePerFrame = 10;
                }
                else if (width * height <= 640 * 480)
                {
                    // VGA
                    baseBitratePerFrame = 27;
                }
                else if (width * height <= 1280 * 720)
                {
                    // 720p
                    baseBitratePerFrame = 67;
                }
                else if (width * height <= 1920 * 1080)
                {
                    // 1080p
                    baseBitratePerFrame = 133;
                }
                else
                {
                    // 4K and above
                    baseBitratePerFrame = 267;
                }
            }

            _maxBitrate = baseBitratePerFrame * framerate * 1000; // Convert to bps

            if (!_inited)
            {
                _currentBitrate = _maxBitrate/4;
                _maxFramerate = _framerate = framerate;
                _inited = true;
            }
        }
    }
}
