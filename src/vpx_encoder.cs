//-----------------------------------------------------------------------------
// Filename: vpx_encoder.cs
//
// Description: Port of: 
//  - vpx_encoder.h
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 26 Oct 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;

namespace Vpx.Net
{
    /// <summary>
    /// This structure holds a fractional value.
    /// </summary>
    public struct vpx_rational_t
    {
        public int num;        /**< fraction numerator */
        public int den;        /**< fraction denominator */
    }

    /// <summary>
    /// Multi-pass Encoding Pass.
    /// </summary>
    public enum vpx_enc_pass
    {
        VPX_RC_ONE_PASS,   /**< Single pass mode */
        VPX_RC_FIRST_PASS, /**< First pass of multi-pass mode */
        VPX_RC_LAST_PASS   /**< Final pass of multi-pass mode */
    }

    /// <summary>
    /// Rate control mode.
    /// </summary>
    public enum vpx_rc_mode
    {
        VPX_VBR, /**< Variable Bit Rate (VBR) mode */
        VPX_CBR, /**< Constant Bit Rate (CBR) mode */
        VPX_CQ,  /**< Constrained Quality (CQ)  mode */
        VPX_Q,   /**< Constant Quality (Q) mode */
    };

    /// <summary>
    /// Keyframe placement mode.
    /// This enumeration determines whether keyframes are placed automatically by
    /// the encoder or whether this behavior is disabled.Older releases of this
    /// SDK were implemented such that VPX_KF_FIXED meant keyframes were disabled.
    /// This name is confusing for this behavior, so the new symbols to be used
    /// are VPX_KF_AUTO and VPX_KF_DISABLED.
    /// </summary>
    public enum vpx_kf_mode
    {
        VPX_KF_FIXED,       /**< deprecated, implies VPX_KF_DISABLED */
        VPX_KF_AUTO,        /**< Encoder determines optimal placement automatically */
        VPX_KF_DISABLED = 0 /**< Encoder does not place keyframes. */
    };

    /// <summary>
    /// These flags define which error resilient features to enable in the
    /// encoder. The flags are specified through the vpx_codec_enc_cfg::g_error_resilient 
    /// variable.
    /// </summary>
    public enum vpx_codec_er_flags_t : uint
    {
        /// <summary>
        ///  Improve resiliency against losses of whole frames.
        /// </summary>
        VPX_ERROR_RESILIENT_DEFAULT = 0x1,

        /// <summary>
        /// The frame partitions are independently decodable by the bool decoder,
        /// meaning that partitions can be decoded even though earlier partitions have
        /// been lost. Note that intra prediction is still done over the partition
        /// boundary.
        /// </summary>
        VPX_ERROR_RESILIENT_PARTITIONS = 0x2
    }

    /// <summary>
    /// Generic fixed size buffer structure.
    /// This structure is able to hold a reference to any fixed size buffer.
    /// </summary>
    public unsafe struct vpx_fixed_buf_t
    {
        public void* buf;       /**< Pointer to the data */
        public ulong sz;       /**< Length of the buffer, in chars */
    }

    /// <summary>
    /// This structure contains the encoder settings that have common representations
    /// across all codecs. This doesn't imply that all codecs support all features,
    /// however.
    /// </summary>
    public class vpx_codec_enc_cfg_t
    {
        /// <summary>
        /// Temporal Scalability: Maximum length of the sequence defining frame
        /// layer membership.
        /// </summary>
        public const int VPX_TS_MAX_PERIODICITY = 16;

        /// <summary>
        /// Temporal Scalability: Maximum number of coding layers
        /// </summary>
        public const int VPX_TS_MAX_LAYERS = 5;

        /// <summary>
        /// Temporal+Spatial Scalability: Maximum number of coding layers.
        /// </summary>
        public const int VPX_MAX_LAYERS = 12;   // 3 temporal + 4 spatial layers are allowed.

        /// <summary>
        /// Spatial Scalability: Maximum number of coding layers.
        /// </summary>
        public const int VPX_SS_MAX_LAYERS = 5;

        /// <summary>
        /// Spatial Scalability: Default number of coding layers.
        /// </summary>
        public const int VPX_SS_DEFAULT_LAYERS = 1;

        /*
         * generic settings (g)
         */

        /*!\brief Deprecated: Algorithm specific "usage" value
         *
         * This value must be zero.
         */
        public uint g_usage;

        /*!\brief Maximum number of threads to use
         *
         * For multi-threaded implementations, use no more than this number of
         * threads. The codec may use fewer threads than allowed. The value
         * 0 is equivalent to the value 1.
         */
        public uint g_threads;

        /*!\brief Bitstream profile to use
         *
         * Some codecs support a notion of multiple bitstream profiles. Typically
         * this maps to a set of features that are turned on or off. Often the
         * profile to use is determined by the features of the intended decoder.
         * Consult the documentation for the codec to determine the valid values
         * for this parameter, or set to zero for a sane default.
         */
        public uint g_profile; /**< profile of bitstream to use */

        /*!\brief Width of the frame
         *
         * This value identifies the presentation resolution of the frame,
         * in pixels. Note that the frames passed as input to the encoder must
         * have this resolution. Frames will be presented by the decoder in this
         * resolution, independent of any spatial resampling the encoder may do.
         */
        public uint g_w;

        /*!\brief Height of the frame
         *
         * This value identifies the presentation resolution of the frame,
         * in pixels. Note that the frames passed as input to the encoder must
         * have this resolution. Frames will be presented by the decoder in this
         * resolution, independent of any spatial resampling the encoder may do.
         */
        public uint g_h;

        /*!\brief Bit-depth of the codec
         *
         * This value identifies the bit_depth of the codec,
         * Only certain bit-depths are supported as identified in the
         * vpx_bit_depth_t enum.
         */
        public vpx_bit_depth_t g_bit_depth;

        /*!\brief Bit-depth of the input frames
         *
         * This value identifies the bit_depth of the input frames in bits.
         * Note that the frames passed as input to the encoder must have
         * this bit-depth.
         */
        public uint g_input_bit_depth;

        /*!\brief Stream timebase units
         *
         * Indicates the smallest interval of time, in seconds, used by the stream.
         * For fixed frame rate material, or variable frame rate material where
         * frames are timed at a multiple of a given clock (ex: video capture),
         * the \ref RECOMMENDED method is to set the timebase to the reciprocal
         * of the frame rate (ex: 1001/30000 for 29.970 Hz NTSC). This allows the
         * pts to correspond to the frame number, which can be handy. For
         * re-encoding video from containers with absolute time timestamps, the
         * \ref RECOMMENDED method is to set the timebase to that of the parent
         * container or multimedia framework (ex: 1/1000 for ms, as in FLV).
         */
        public vpx_rational_t g_timebase;

        /*!\brief Enable error resilient modes.
         *
         * The error resilient bitfield indicates to the encoder which features
         * it should enable to take measures for streaming over lossy or noisy
         * links.
         */
        public vpx_codec_er_flags_t g_error_resilient;

        /*!\brief Multi-pass Encoding Mode
         *
         * This value should be set to the current phase for multi-pass encoding.
         * For single pass, set to #VPX_RC_ONE_PASS.
         */
        public vpx_enc_pass g_pass;

        /*!\brief Allow lagged encoding
         *
         * If set, this value allows the encoder to consume a number of input
         * frames before producing output frames. This allows the encoder to
         * base decisions for the current frame on future frames. This does
         * increase the latency of the encoding pipeline, so it is not appropriate
         * in all situations (ex: realtime encoding).
         *
         * Note that this is a maximum value -- the encoder may produce frames
         * sooner than the given limit. Set this value to 0 to disable this
         * feature.
         */
        public uint g_lag_in_frames;

        /*
         * rate control settings (rc)
         */

        /*!\brief Temporal resampling configuration, if supported by the codec.
         *
         * Temporal resampling allows the codec to "drop" frames as a strategy to
         * meet its target data rate. This can cause temporal discontinuities in
         * the encoded video, which may appear as stuttering during playback. This
         * trade-off is often acceptable, but for many applications is not. It can
         * be disabled in these cases.
         *
         * This threshold is described as a percentage of the target data buffer.
         * When the data buffer falls below this percentage of fullness, a
         * dropped frame is indicated. Set the threshold to zero (0) to disable
         * this feature.
         */
        public uint rc_dropframe_thresh;

        /*!\brief Enable/disable spatial resampling, if supported by the codec.
         *
         * Spatial resampling allows the codec to compress a lower resolution
         * version of the frame, which is then upscaled by the encoder to the
         * correct presentation resolution. This increases visual quality at
         * low data rates, at the expense of CPU time on the encoder/decoder.
         */
        public uint rc_resize_allowed;

        /*!\brief Internal coded frame width.
         *
         * If spatial resampling is enabled this specifies the width of the
         * encoded frame.
         */
        public uint rc_scaled_width;

        /*!\brief Internal coded frame height.
         *
         * If spatial resampling is enabled this specifies the height of the
         * encoded frame.
         */
        public uint rc_scaled_height;

        /*!\brief Spatial resampling up watermark.
         *
         * This threshold is described as a percentage of the target data buffer.
         * When the data buffer rises above this percentage of fullness, the
         * encoder will step up to a higher resolution version of the frame.
         */
        public uint rc_resize_up_thresh;

        /*!\brief Spatial resampling down watermark.
         *
         * This threshold is described as a percentage of the target data buffer.
         * When the data buffer falls below this percentage of fullness, the
         * encoder will step down to a lower resolution version of the frame.
         */
        public uint rc_resize_down_thresh;

        /*!\brief Rate control algorithm to use.
         *
         * Indicates whether the end usage of this stream is to be streamed over
         * a bandwidth constrained link, indicating that Constant Bit Rate (CBR)
         * mode should be used, or whether it will be played back on a high
         * bandwidth link, as from a local disk, where higher variations in
         * bitrate are acceptable.
         */
        public vpx_rc_mode rc_end_usage;

        /*!\brief Two-pass stats buffer.
        *
        * A buffer containing all of the stats packets produced in the first
        * pass, concatenated.
        */
        public vpx_fixed_buf_t rc_twopass_stats_in;

        /*!\brief first pass mb stats buffer.
         *
         * A buffer containing all of the first pass mb stats packets produced
         * in the first pass, concatenated.
         */
        public vpx_fixed_buf_t rc_firstpass_mb_stats_in;

        /*!\brief Target data rate
         *
         * Target bandwidth to use for this stream, in kilobits per second.
         */
        public uint rc_target_bitrate;

        /*
         * quantizer settings
         */

        /*!\brief Minimum (Best Quality) Quantizer
         *
         * The quantizer is the most direct control over the quality of the
         * encoded image. The range of valid values for the quantizer is codec
         * specific. Consult the documentation for the codec to determine the
         * values to use.
         */
        public uint rc_min_quantizer;

        /*!\brief Maximum (Worst Quality) Quantizer
         *
         * The quantizer is the most direct control over the quality of the
         * encoded image. The range of valid values for the quantizer is codec
         * specific. Consult the documentation for the codec to determine the
         * values to use.
         */
        public uint rc_max_quantizer;

        /*
         * bitrate tolerance
         */

        /*!\brief Rate control adaptation undershoot control
         *
         * VP8: Expressed as a percentage of the target bitrate,
         * controls the maximum allowed adaptation speed of the codec.
         * This factor controls the maximum amount of bits that can
         * be subtracted from the target bitrate in order to compensate
         * for prior overshoot.
         * VP9: Expressed as a percentage of the target bitrate, a threshold
         * undershoot level (current rate vs target) beyond which more aggressive
         * corrective measures are taken.
         *   *
         * Valid values in the range VP8:0-1000 VP9: 0-100.
         */
        public uint rc_undershoot_pct;

        /*!\brief Rate control adaptation overshoot control
         *
         * VP8: Expressed as a percentage of the target bitrate,
         * controls the maximum allowed adaptation speed of the codec.
         * This factor controls the maximum amount of bits that can
         * be added to the target bitrate in order to compensate for
         * prior undershoot.
         * VP9: Expressed as a percentage of the target bitrate, a threshold
         * overshoot level (current rate vs target) beyond which more aggressive
         * corrective measures are taken.
         *
         * Valid values in the range VP8:0-1000 VP9: 0-100.
         */
        public uint rc_overshoot_pct;

        /*
         * decoder buffer model parameters
         */

        /*!\brief Decoder Buffer Size
         *
         * This value indicates the amount of data that may be buffered by the
         * decoding application. Note that this value is expressed in units of
         * time (milliseconds). For example, a value of 5000 indicates that the
         * client will buffer (at least) 5000ms worth of encoded data. Use the
         * target bitrate (#rc_target_bitrate) to convert to bits/bytes, if
         * necessary.
         */
        public uint rc_buf_sz;

        /*!\brief Decoder Buffer Initial Size
         *
         * This value indicates the amount of data that will be buffered by the
         * decoding application prior to beginning playback. This value is
         * expressed in units of time (milliseconds). Use the target bitrate
         * (#rc_target_bitrate) to convert to bits/bytes, if necessary.
         */
        public uint rc_buf_initial_sz;

        /*!\brief Decoder Buffer Optimal Size
         *
         * This value indicates the amount of data that the encoder should try
         * to maintain in the decoder's buffer. This value is expressed in units
         * of time (milliseconds). Use the target bitrate (#rc_target_bitrate)
         * to convert to bits/bytes, if necessary.
         */
        public uint rc_buf_optimal_sz;

        /*
         * 2 pass rate control parameters
         */

        /*!\brief Two-pass mode CBR/VBR bias
         *
         * Bias, expressed on a scale of 0 to 100, for determining target size
         * for the current frame. The value 0 indicates the optimal CBR mode
         * value should be used. The value 100 indicates the optimal VBR mode
         * value should be used. Values in between indicate which way the
         * encoder should "lean."
         */
        public uint rc_2pass_vbr_bias_pct;

        /*!\brief Two-pass mode per-GOP minimum bitrate
         *
         * This value, expressed as a percentage of the target bitrate, indicates
         * the minimum bitrate to be used for a single GOP (aka "section")
         */
        public uint rc_2pass_vbr_minsection_pct;

        /*!\brief Two-pass mode per-GOP maximum bitrate
         *
         * This value, expressed as a percentage of the target bitrate, indicates
         * the maximum bitrate to be used for a single GOP (aka "section")
         */
        public uint rc_2pass_vbr_maxsection_pct;

        /*!\brief Two-pass corpus vbr mode complexity control
         * Used only in VP9: A value representing the corpus midpoint complexity
         * for corpus vbr mode. This value defaults to 0 which disables corpus vbr
         * mode in favour of normal vbr mode.
         */
        public uint rc_2pass_vbr_corpus_complexity;

        /*
         * keyframing settings (kf)
         */

        /*!\brief Keyframe placement mode
         *
         * This value indicates whether the encoder should place keyframes at a
         * fixed interval, or determine the optimal placement automatically
         * (as governed by the #kf_min_dist and #kf_max_dist parameters)
         */
        public vpx_kf_mode kf_mode;

        /*!\brief Keyframe minimum interval
        *
        * This value, expressed as a number of frames, prevents the encoder from
        * placing a keyframe nearer than kf_min_dist to the previous keyframe. At
        * least kf_min_dist frames non-keyframes will be coded before the next
        * keyframe. Set kf_min_dist equal to kf_max_dist for a fixed interval.
        */
        public uint kf_min_dist;

        /*!\brief Keyframe maximum interval
         *
         * This value, expressed as a number of frames, forces the encoder to code
         * a keyframe if one has not been coded in the last kf_max_dist frames.
         * A value of 0 implies all frames will be keyframes. Set kf_min_dist
         * equal to kf_max_dist for a fixed interval.
         */
        public uint kf_max_dist;

        /*
         * Spatial scalability settings (ss)
         */

        /*!\brief Number of spatial coding layers.
         *
         * This value specifies the number of spatial coding layers to be used.
         */
        public uint ss_number_layers;

        /*!\brief Enable auto alt reference flags for each spatial layer.
         *
         * These values specify if auto alt reference frame is enabled for each
         * spatial layer.
         */
        public int[] ss_enable_auto_alt_ref = new int[VPX_SS_MAX_LAYERS];

        /*!\brief Target bitrate for each spatial layer.
         *
         * These values specify the target coding bitrate to be used for each
         * spatial layer. (in kbps)
         */
        public uint[] ss_target_bitrate = new uint[VPX_SS_MAX_LAYERS];

        /*!\brief Number of temporal coding layers.
         *
         * This value specifies the number of temporal layers to be used.
         */
        public uint ts_number_layers;

        /*!\brief Target bitrate for each temporal layer.
         *
         * These values specify the target coding bitrate to be used for each
         * temporal layer. (in kbps)
         */
        public uint[] ts_target_bitrate= new uint[VPX_TS_MAX_LAYERS];

        /*!\brief Frame rate decimation factor for each temporal layer.
         *
         * These values specify the frame rate decimation factors to apply
         * to each temporal layer.
         */
        public uint[] ts_rate_decimator = new uint[VPX_TS_MAX_LAYERS];

        /*!\brief Length of the sequence defining frame temporal layer membership.
         *
         * This value specifies the length of the sequence that defines the
         * membership of frames to temporal layers. For example, if the
         * ts_periodicity = 8, then the frames are assigned to coding layers with a
         * repeated sequence of length 8.
         */
        public uint ts_periodicity;

        /*!\brief Template defining the membership of frames to temporal layers.
         *
         * This array defines the membership of frames to temporal coding layers.
         * For a 2-layer encoding that assigns even numbered frames to one temporal
         * layer (0) and odd numbered frames to a second temporal layer (1) with
         * ts_periodicity=8, then ts_layer_id = (0,1,0,1,0,1,0,1).
         */
        public uint[] ts_layer_id = new uint[VPX_TS_MAX_PERIODICITY];

        /*!\brief Target bitrate for each spatial/temporal layer.
         *
         * These values specify the target coding bitrate to be used for each
         * spatial/temporal layer. (in kbps)
         *
         */
        public uint[] layer_target_bitrate = new uint[VPX_MAX_LAYERS];

        /*!\brief Temporal layering mode indicating which temporal layering scheme to
         * use.
         *
         * The value (refer to VP9E_TEMPORAL_LAYERING_MODE) specifies the
         * temporal layering mode to use.
         *
         */
        public int temporal_layering_mode;
    }

    public class vpx_encoder
    {
    }
}
