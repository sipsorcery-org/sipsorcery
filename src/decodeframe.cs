//-----------------------------------------------------------------------------
// Filename: decodeframe.cs
//
// Description: Port of:
//  - decodeframe.c
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 28 Oct 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

/*
 *  Copyright (c) 2010 The WebM project authors. All Rights Reserved.
 *
 *  Use of this source code is governed by a BSD-style license
 *  that can be found in the LICENSE file in the root of the source
 *  tree. An additional intellectual property rights grant can be found
 *  in the file PATENTS.  All contributing project authors may
 *  be found in the AUTHORS file in the root of the source tree.
 */

using System;

using vp8_prob = System.Byte;
using vp8_reader = Vpx.Net.BOOL_DECODER;
using ptrdiff_t = System.UInt64;

namespace Vpx.Net
{
    public unsafe static class decodeframe
    {
        public static void vp8cx_init_de_quantizer(VP8D_COMP pbi)
        {
            int Q;
            VP8_COMMON pc = pbi.common;

            for (Q = 0; Q < VP8_COMMON.QINDEX_RANGE; ++Q)
            {
                pc.Y1dequant[Q, 0] = (short)quant_common.vp8_dc_quant(Q, pc.y1dc_delta_q);
                pc.Y2dequant[Q, 0] = (short)quant_common.vp8_dc2quant(Q, pc.y2dc_delta_q);
                pc.UVdequant[Q, 0] = (short)quant_common.vp8_dc_uv_quant(Q, pc.uvdc_delta_q);

                pc.Y1dequant[Q, 1] = (short)quant_common.vp8_ac_yquant(Q);
                pc.Y2dequant[Q, 1] = (short)quant_common.vp8_ac2quant(Q, pc.y2ac_delta_q);
                pc.UVdequant[Q, 1] = (short)quant_common.vp8_ac_uv_quant(Q, pc.uvac_delta_q);
            }
        }

        static void vp8_mb_init_dequantizer(VP8D_COMP pbi, MACROBLOCKD xd)
        {
            int i;
            int QIndex;
            MB_MODE_INFO mbmi = xd.mode_info_context.get().mbmi;
            VP8_COMMON pc = pbi.common;

            /* Decide whether to use the default or alternate baseline Q value. */
            if (xd.segmentation_enabled > 0)
            {
                /* Abs Value */
                if (xd.mb_segement_abs_delta == blockd.SEGMENT_ABSDATA)
                {
                    QIndex = xd.segment_feature_data[(int)MB_LVL_FEATURES.MB_LVL_ALT_Q, mbmi.segment_id];

                    /* Delta Value */
                }
                else
                {
                    QIndex = pc.base_qindex +
                             xd.segment_feature_data[(int)MB_LVL_FEATURES.MB_LVL_ALT_Q, mbmi.segment_id];
                }

                QIndex = (QIndex >= 0) ? ((QIndex <= VP8_COMMON.MAXQ) ? QIndex : VP8_COMMON.MAXQ)
                                       : 0; /* Clamp to valid range */
            }
            else
            {
                QIndex = pc.base_qindex;
            }

            /* Set up the macroblock dequant constants */
            xd.dequant_y1_dc[0] = 1;
            xd.dequant_y1[0] = pc.Y1dequant[QIndex, 0];
            xd.dequant_y2[0] = pc.Y2dequant[QIndex, 0];
            xd.dequant_uv[0] = pc.UVdequant[QIndex, 0];

            for (i = 1; i < 16; ++i)
            {
                xd.dequant_y1_dc[i] = xd.dequant_y1[i] = pc.Y1dequant[QIndex, 1];
                xd.dequant_y2[i] = pc.Y2dequant[QIndex, 1];
                xd.dequant_uv[i] = pc.UVdequant[QIndex, 1];
            }
        }

        static int get_delta_q(ref vp8_reader bc, int prev, int* q_update)
        {
            int ret_val = 0;

            if (treereader.vp8_read_bit(ref bc) > 0)
            {
                ret_val = treereader.vp8_read_literal(ref bc, 4);

                if (treereader.vp8_read_bit(ref bc) > 0) ret_val = -ret_val;
            }

            /* Trigger a quantizer update if the delta-q value has changed */
            if (ret_val != prev) *q_update = 1;

            return ret_val;
        }

        public static uint read_partition_size(VP8D_COMP pbi, in byte* cx_size)
        {
            //byte[] temp = new byte[3];
            //if (pbi->decrypt_cb)
            //{
            //    pbi->decrypt_cb(pbi->decrypt_state, cx_size, temp, 3);
            //    cx_size = temp;
            //}
            return (uint)(cx_size[0] + (cx_size[1] << 8) + (cx_size[2] << 16));
        }

        public static int read_is_valid(in byte* start, ulong len, in byte* end)
                  => len != 0 && end > start && len <= (ulong)(end - start) ? 1 : 0;

        public static uint read_available_partition_size(
                VP8D_COMP pbi, in byte* token_part_sizes,
                in byte* fragment_start,
                in byte* first_fragment_end, in byte* fragment_end,
                int i, int num_part)
        {
            VP8_COMMON pc = pbi.common;
            byte* partition_size_ptr = token_part_sizes + i * 3;
            uint partition_size = 0;
            ptrdiff_t bytes_left = (ulong)(fragment_end - fragment_start);
            if (bytes_left < 0)
            {
                vpx_codec.vpx_internal_error(
                    ref pc.error, vpx_codec_err_t.VPX_CODEC_CORRUPT_FRAME,
                    $"Truncated packet or corrupt partition. No bytes left {bytes_left}.");
            }
            /* Calculate the length of this partition. The last partition
             * size is implicit. If the partition size can't be read, then
             * either use the remaining data in the buffer (for EC mode)
             * or throw an error.
             */
            if (i < num_part - 1)
            {
                if (read_is_valid(partition_size_ptr, 3, first_fragment_end) > 0)
                {
                    partition_size = read_partition_size(pbi, partition_size_ptr);
                }
                else if (pbi.ec_active > 0)
                {
                    partition_size = (uint)bytes_left;
                }
                else
                {
                    vpx_codec.vpx_internal_error(ref pc.error, vpx_codec_err_t.VPX_CODEC_CORRUPT_FRAME,
                                       "Truncated partition size data");
                }
            }
            else
            {
                partition_size = (uint)bytes_left;
            }

            /* Validate the calculated partition length. If the buffer
             * described by the partition can't be fully read, then restrict
             * it to the portion that can be (for EC mode) or throw an error.
             */
            if (read_is_valid(fragment_start, partition_size, fragment_end) == 0)
            {
                if (pbi.ec_active > 0)
                {
                    partition_size = (uint)bytes_left;
                }
                else
                {
                    vpx_codec.vpx_internal_error(ref pc.error, vpx_codec_err_t.VPX_CODEC_CORRUPT_FRAME,
                                       $"Truncated packet or corrupt partition {i + 1} length");
                }
            }
            return partition_size;
        }

        public static void setup_token_decoder(VP8D_COMP pbi, in byte* token_part_sizes)
        {
            int bool_decoder_idx = 0;
            vp8_reader bool_decoder = pbi.mbc[bool_decoder_idx];
            uint partition_idx;
            uint fragment_idx;
            uint num_token_partitions;
            byte* first_fragment_end =
                pbi.fragments.ptrs[0] + pbi.fragments.sizes[0];

            TOKEN_PARTITION multi_token_partition =
                (TOKEN_PARTITION)treereader.vp8_read_literal(ref pbi.mbc[8], 2);
            if (dboolhuff.vp8dx_bool_error(ref pbi.mbc[8]) == 0)
            {
                pbi.common.multi_token_partition = multi_token_partition;
            }
            num_token_partitions = (uint)(1 << (int)pbi.common.multi_token_partition);

            /* Check for partitions within the fragments and unpack the fragments
             * so that each fragment pointer points to its corresponding partition. */
            for (fragment_idx = 0; fragment_idx < pbi.fragments.count; ++fragment_idx)
            {
                uint fragment_size = pbi.fragments.sizes[fragment_idx];
                byte* fragment_end =
                    pbi.fragments.ptrs[fragment_idx] + fragment_size;
                /* Special case for handling the first partition since we have already
                 * read its size. */
                if (fragment_idx == 0)
                {
                    /* Size of first partition + token partition sizes element */
                    ptrdiff_t ext_first_part_size = (ulong)(token_part_sizes -
                                                    pbi.fragments.ptrs[0] +
                                                    3 * (num_token_partitions - 1));
                    if (fragment_size < (uint)ext_first_part_size)
                    {
                        vpx_codec.vpx_internal_error(ref pbi.common.error, vpx_codec_err_t.VPX_CODEC_CORRUPT_FRAME,
                            $"Corrupted fragment size {fragment_size}");
                    }
                    fragment_size -= (uint)ext_first_part_size;
                    if (fragment_size > 0)
                    {
                        pbi.fragments.sizes[0] = (uint)ext_first_part_size;
                        /* The fragment contains an additional partition. Move to
                         * next. */
                        fragment_idx++;
                        pbi.fragments.ptrs[fragment_idx] = pbi.fragments.ptrs[0] + pbi.fragments.sizes[0];
                    }
                }
                /* Split the chunk into partitions read from the bitstream */
                while (fragment_size > 0)
                {
                    ptrdiff_t partition_size = read_available_partition_size(
                        pbi, token_part_sizes, pbi.fragments.ptrs[fragment_idx],
                        first_fragment_end, fragment_end, (int)(fragment_idx - 1),
                        (int)num_token_partitions);
                    pbi.fragments.sizes[fragment_idx] = (uint)partition_size;
                    if (fragment_size < (uint)partition_size)
                    {
                        vpx_codec.vpx_internal_error(ref pbi.common.error, vpx_codec_err_t.VPX_CODEC_CORRUPT_FRAME,
                                           $"Corrupted fragment size {fragment_size}");
                    }
                    fragment_size -= (uint)partition_size;
                    //assert(fragment_idx <= num_token_partitions);
                    if (fragment_size > 0)
                    {
                        /* The fragment contains an additional partition.
                         * Move to next. */
                        fragment_idx++;
                        pbi.fragments.ptrs[fragment_idx] = pbi.fragments.ptrs[fragment_idx - 1] + partition_size;
                    }
                }
            }

            pbi.fragments.count = num_token_partitions + 1;

            for (partition_idx = 1; partition_idx < pbi.fragments.count;
                 ++partition_idx)
            {
                if (dboolhuff.vp8dx_start_decode(ref bool_decoder, pbi.fragments.ptrs[partition_idx],
                                       pbi.fragments.sizes[partition_idx], null, null) > 0)
                {
                    vpx_codec.vpx_internal_error(ref pbi.common.error, vpx_codec_err_t.VPX_CODEC_MEM_ERROR,
                                       $"Failed to allocate bool decoder {partition_idx}");
                }

                //bool_decoder++;
                bool_decoder_idx++;
                bool_decoder = pbi.mbc[bool_decoder_idx];
            }
        }

        public static void init_frame(VP8D_COMP pbi)
        {
            VP8_COMMON pc = pbi.common;
            MACROBLOCKD xd = pbi.mb;

            if (pc.frame_type == FRAME_TYPE.KEY_FRAME)
            {
                /* Various keyframe initializations */
                //memcpy(pc->fc.mvc, vp8_default_mv_context, sizeof(vp8_default_mv_context));
                for (int i = 0; i < entropymv.vp8_default_mv_context.Length; i++)
                {
                    pc.fc.mvc[i] = new MV_CONTEXT();

                    for (int j = 0; j < entropymv.vp8_default_mv_context[i].prob.Length; j++)
                    {
                        pc.fc.mvc[i].prob[j] = entropymv.vp8_default_mv_context[i].prob[j];
                    }
                }

                entropymode.vp8_init_mbmode_probs(pc);

                entropy.vp8_default_coef_probs(pc);

                /* reset the segment feature data to 0 with delta coding (Default state). */
                // Port AC: array elements of xd.segment_feature_data will be initialised to 0 by default.
                //memset(xd->segment_feature_data, 0, sizeof(xd->segment_feature_data));

                xd.mb_segement_abs_delta = blockd.SEGMENT_DELTADATA;

                /* reset the mode ref deltasa for loop filter */
                // Port AC: theses array elements will be initialised to 0 by default.
                //memset(xd->ref_lf_deltas, 0, sizeof(xd->ref_lf_deltas));
                //memset(xd->mode_lf_deltas, 0, sizeof(xd->mode_lf_deltas));

                /* All buffers are implicitly updated on key frames. */
                pc.refresh_golden_frame = 1;
                pc.refresh_alt_ref_frame = 1;
                pc.copy_buffer_to_gf = 0;
                pc.copy_buffer_to_arf = 0;

                ///* Note that Golden and Altref modes cannot be used on a key frame so
                // * ref_frame_sign_bias[] is undefined and meaningless
                // */
                pc.ref_frame_sign_bias[(int)MV_REFERENCE_FRAME.GOLDEN_FRAME] = 0;
                pc.ref_frame_sign_bias[(int)MV_REFERENCE_FRAME.ALTREF_FRAME] = 0;
            }
            else
            {
                /* To enable choice of different interploation filters */
                if (pc.use_bilinear_mc_filter == 0)
                {
                    xd.subpixel_predict = vp8_rtcd.vp8_sixtap_predict4x4;
                    xd.subpixel_predict8x4 = vp8_rtcd.vp8_sixtap_predict8x4;
                    xd.subpixel_predict8x8 = vp8_rtcd.vp8_sixtap_predict8x8;
                    xd.subpixel_predict16x16 = vp8_rtcd.vp8_sixtap_predict16x16;
                }
                else
                {
                    xd.subpixel_predict = vp8_rtcd.vp8_bilinear_predict4x4;
                    xd.subpixel_predict8x4 = vp8_rtcd.vp8_bilinear_predict8x4;
                    xd.subpixel_predict8x8 = vp8_rtcd.vp8_bilinear_predict8x8;
                    xd.subpixel_predict16x16 = vp8_rtcd.vp8_bilinear_predict16x16;
                }

                if (pbi.decoded_key_frame > 0 && pbi.ec_enabled > 0 && pbi.ec_active == 0)
                {
                    pbi.ec_active = 1;
                }
            }

            xd.left_context = pc.left_context;
            xd.mode_info_context = pc.mi;
            xd.frame_type = pc.frame_type;
            xd.mode_info_context.get().mbmi.mode = (byte)MB_PREDICTION_MODE.DC_PRED;
            xd.mode_info_stride = pc.mode_info_stride;
            xd.corrupted = 0; /* init without corruption */

            xd.fullpixel_mask = -1; // 0xffffffff;
            if (pc.full_pixel > 0) xd.fullpixel_mask = -8;// 0xfffffff8;
        }

        public static int vp8_decode_frame(VP8D_COMP pbi)
        {
            vp8_reader bc = pbi.mbc[8];
            VP8_COMMON pc = pbi.common;
            MACROBLOCKD xd = pbi.mb;
            byte* data = pbi.fragments.ptrs[0];
            uint data_sz = pbi.fragments.sizes[0];
            byte* data_end = data + data_sz;
            ptrdiff_t first_partition_length_in_bytes;

            int i, j, k, l;
            int[] mb_feature_data_bits = entropy.vp8_mb_feature_data_bits;
            int corrupt_tokens = 0;
            int prev_independent_partitions = pbi.independent_partitions;

            ref YV12_BUFFER_CONFIG yv12_fb_new = ref pbi.dec_fb_ref[(int)MV_REFERENCE_FRAME.INTRA_FRAME];

            /* start with no corruption of current frame */
            xd.corrupted = 0;
            yv12_fb_new.corrupted = 0;

            if (data_end - data < 3)
            {
                if (pbi.ec_active == 0)
                {
                    vpx_codec.vpx_internal_error(ref pc.error, vpx_codec_err_t.VPX_CODEC_CORRUPT_FRAME,
                                       "Truncated packet");
                }

                /* Declare the missing frame as an inter frame since it will
                   be handled as an inter frame when we have estimated its
                   motion vectors. */
                pc.frame_type = FRAME_TYPE.INTER_FRAME;
                pc.version = 0;
                pc.show_frame = 1;
                first_partition_length_in_bytes = 0;
            }
            else
            {
                //byte[] clear_buffer = new byte[10];
                byte* clear = data;
                //if (pbi.decrypt_cb)
                //{
                //    int n = (int)VPXMIN(sizeof(clear_buffer), data_sz);
                //    pbi->decrypt_cb(pbi->decrypt_state, data, clear_buffer, n);
                //    clear = clear_buffer;
                //}

                pc.frame_type = (FRAME_TYPE)(clear[0] & 1);
                pc.version = (clear[0] >> 1) & 7;
                pc.show_frame = (clear[0] >> 4) & 1;
                first_partition_length_in_bytes = (ptrdiff_t)
                    (clear[0] | (clear[1] << 8) | (clear[2] << 16)) >> 5;

                if (pbi.ec_active == 0 && (data + first_partition_length_in_bytes > data_end ||
                                        data + first_partition_length_in_bytes < data))
                {
                    vpx_codec.vpx_internal_error(ref pc.error, vpx_codec_err_t.VPX_CODEC_CORRUPT_FRAME,
                                       "Truncated packet or corrupt partition 0 length");
                }

                data += 3;
                clear += 3;

                alloccommon.vp8_setup_version(pc);

                if (pc.frame_type == FRAME_TYPE.KEY_FRAME)
                {
                    /* vet via sync code */
                    /* When error concealment is enabled we should only check the sync
                     * code if we have enough bits available
                     */
                    if (data + 3 < data_end)
                    {
                        if (clear[0] != 0x9d || clear[1] != 0x01 || clear[2] != 0x2a)
                        {
                            vpx_codec.vpx_internal_error(ref pc.error, vpx_codec_err_t.VPX_CODEC_UNSUP_BITSTREAM,
                                               "Invalid frame sync code");
                        }
                    }

                    /* If error concealment is enabled we should only parse the new size
                     * if we have enough data. Otherwise we will end up with the wrong
                     * size.
                     */
                    if (data + 6 < data_end)
                    {
                        pc.Width = (clear[3] | (clear[4] << 8)) & 0x3fff;
                        pc.horiz_scale = clear[4] >> 6;
                        pc.Height = (clear[5] | (clear[6] << 8)) & 0x3fff;
                        pc.vert_scale = clear[6] >> 6;
                        data += 7;
                    }
                    else if (pbi.ec_active == 0)
                    {
                        vpx_codec.vpx_internal_error(ref pc.error, vpx_codec_err_t.VPX_CODEC_CORRUPT_FRAME,
                                           "Truncated key frame header");
                    }
                    else
                    {
                        /* Error concealment is active, clear the frame. */
                        data = data_end;
                    }
                }
                else
                {
                    // Port AC: Struct value copy assignment being used to acheive the same result as memcpy.
                    //memcpy(xd.pre, yv12_fb_new, sizeof(YV12_BUFFER_CONFIG));
                    //memcpy(xd.dst, yv12_fb_new, sizeof(YV12_BUFFER_CONFIG));

                    xd.pre = yv12_fb_new;
                    xd.dst = yv12_fb_new;
                }
            }
            if (pbi.decoded_key_frame == 0 && pc.frame_type != FRAME_TYPE.KEY_FRAME)
            {
                return -1;
            }

            init_frame(pbi);

            //if (dboolhuff.vp8dx_start_decode(bc, data, (uint)(data_end - data),
            //            pbi.decrypt_cb, pbi.decrypt_state))
            if (dboolhuff.vp8dx_start_decode(ref bc, data, (uint)(data_end - data), null, null) > 0)
            {
                vpx_codec.vpx_internal_error(ref pc.error, vpx_codec_err_t.VPX_CODEC_MEM_ERROR,
                                   "Failed to allocate bool decoder 0");
            }
            if (pc.frame_type == FRAME_TYPE.KEY_FRAME)
            {
                // (void)vp8_read_bit(bc);  // colorspace
                treereader.vp8_read_bit(ref bc);  // colorspace.
                pc.clamp_type = (CLAMP_TYPE)treereader.vp8_read_bit(ref bc);
            }

            /* Is segmentation enabled */
            xd.segmentation_enabled = (byte)treereader.vp8_read_bit(ref bc);

            if (xd.segmentation_enabled > 0)
            {
                /* Signal whether or not the segmentation map is being explicitly updated
                 * this frame. */
                xd.update_mb_segmentation_map = (byte)treereader.vp8_read_bit(ref bc);
                xd.update_mb_segmentation_data = (byte)treereader.vp8_read_bit(ref bc);

                if (xd.update_mb_segmentation_data > 0)
                {
                    xd.mb_segement_abs_delta = (byte)treereader.vp8_read_bit(ref bc);

                    //memset(xd.segment_feature_data, 0, sizeof(xd.segment_feature_data));
                    for (int ii = 0; ii < xd.segment_feature_data.GetLength(0); ii++)
                    {
                        for (int jj = 0; jj < xd.segment_feature_data.GetLength(1); jj++)
                        {
                            xd.segment_feature_data[ii, jj] = 0;
                        }
                    }

                    /* For each segmentation feature (Quant and loop filter level) */
                    for (i = 0; i < (int)MB_LVL_FEATURES.MB_LVL_MAX; ++i)
                    {
                        for (j = 0; j < blockd.MAX_MB_SEGMENTS; ++j)
                        {
                            /* Frame level data */
                            if (treereader.vp8_read_bit(ref bc) > 0)
                            {
                                xd.segment_feature_data[i, j] =
                                    (sbyte)treereader.vp8_read_literal(ref bc, mb_feature_data_bits[i]);

                                if (treereader.vp8_read_bit(ref bc) > 0)
                                {
                                    xd.segment_feature_data[i, j] = (sbyte)-xd.segment_feature_data[i, j];
                                }
                            }
                            else
                            {
                                xd.segment_feature_data[i, j] = 0;
                            }
                        }
                    }
                }

                if (xd.update_mb_segmentation_map > 0)
                {
                    /* Which macro block level features are enabled */
                    //memset(xd.mb_segment_tree_probs, 255, sizeof(xd.mb_segment_tree_probs));
                    for (int ii = 0; ii < xd.mb_segment_tree_probs.Length; ii++)
                    {
                        xd.mb_segment_tree_probs[ii] = 255;
                    }

                    /* Read the probs used to decode the segment id for each macro block. */
                    for (i = 0; i < blockd.MB_FEATURE_TREE_PROBS; ++i)
                    {
                        /* If not explicitly set value is defaulted to 255 by memset above */
                        if (treereader.vp8_read_bit(ref bc) > 0)
                        {
                            xd.mb_segment_tree_probs[i] = (vp8_prob)treereader.vp8_read_literal(ref bc, 8);
                        }
                    }
                }
            }
            else
            {
                /* No segmentation updates on this frame */
                xd.update_mb_segmentation_map = 0;
                xd.update_mb_segmentation_data = 0;
            }

            /* Read the loop filter level and type */
            pc.filter_type = (LOOPFILTERTYPE)treereader.vp8_read_bit(ref bc);
            pc.filter_level = treereader.vp8_read_literal(ref bc, 6);
            pc.sharpness_level = treereader.vp8_read_literal(ref bc, 3);

            /* Read in loop filter deltas applied at the MB level based on mode or ref
             * frame. */
            xd.mode_ref_lf_delta_update = 0;
            xd.mode_ref_lf_delta_enabled = (byte)treereader.vp8_read_bit(ref bc);

            if (xd.mode_ref_lf_delta_enabled > 0)
            {
                /* Do the deltas need to be updated */
                xd.mode_ref_lf_delta_update = (byte)treereader.vp8_read_bit(ref bc);

                if (xd.mode_ref_lf_delta_update > 0)
                {
                    /* Send update */
                    for (i = 0; i < blockd.MAX_REF_LF_DELTAS; ++i)
                    {
                        if (treereader.vp8_read_bit(ref bc) > 0)
                        {
                            /*sign = vp8_read_bit( bc );*/
                            xd.ref_lf_deltas[i] = (sbyte)treereader.vp8_read_literal(ref bc, 6);

                            if (treereader.vp8_read_bit(ref bc) > 0)
                            { /* Apply sign */
                                xd.ref_lf_deltas[i] = (sbyte)(xd.ref_lf_deltas[i] * -1);
                            }
                        }
                    }

                    /* Send update */
                    for (i = 0; i < blockd.MAX_MODE_LF_DELTAS; ++i)
                    {
                        if (treereader.vp8_read_bit(ref bc) > 0)
                        {
                            /*sign = vp8_read_bit( bc );*/
                            xd.mode_lf_deltas[i] = (sbyte)treereader.vp8_read_literal(ref bc, 6);

                            if (treereader.vp8_read_bit(ref bc) > 0)
                            { /* Apply sign */
                                xd.mode_lf_deltas[i] = (sbyte)(xd.mode_lf_deltas[i] * -1);
                            }
                        }
                    }
                }
            }

            setup_token_decoder(pbi, data + first_partition_length_in_bytes);

            xd.current_bc = pbi.mbc[0];

            /* Read the default quantizers. */
            {
                int Q, q_update;

                Q = treereader.vp8_read_literal(ref bc, 7); /* AC 1st order Q = default */
                pc.base_qindex = Q;
                q_update = 0;
                pc.y1dc_delta_q = get_delta_q(ref bc, pc.y1dc_delta_q, &q_update);
                pc.y2dc_delta_q = get_delta_q(ref bc, pc.y2dc_delta_q, &q_update);
                pc.y2ac_delta_q = get_delta_q(ref bc, pc.y2ac_delta_q, &q_update);
                pc.uvdc_delta_q = get_delta_q(ref bc, pc.uvdc_delta_q, &q_update);
                pc.uvac_delta_q = get_delta_q(ref bc, pc.uvac_delta_q, &q_update);

                if (q_update > 0) vp8cx_init_de_quantizer(pbi);

                /* MB level dequantizer setup */
                vp8_mb_init_dequantizer(pbi, pbi.mb);
            }

            /* Determine if the golden frame or ARF buffer should be updated and how.
             * For all non key frames the GF and ARF refresh flags and sign bias
             * flags must be set explicitly.
             */
            if (pc.frame_type != FRAME_TYPE.KEY_FRAME)
            {
                /* Should the GF or ARF be updated from the current frame */
                pc.refresh_golden_frame = treereader.vp8_read_bit(ref bc);

                pc.refresh_alt_ref_frame = treereader.vp8_read_bit(ref bc);

                /* Buffer to buffer copy flags. */
                pc.copy_buffer_to_gf = 0;

                if (pc.refresh_golden_frame == 0)
                {
                    pc.copy_buffer_to_gf = treereader.vp8_read_literal(ref bc, 2);
                }

                pc.copy_buffer_to_arf = 0;

                if (pc.refresh_alt_ref_frame == 0)
                {
                    pc.copy_buffer_to_arf = treereader.vp8_read_literal(ref bc, 2);
                }

                pc.ref_frame_sign_bias[(int)MV_REFERENCE_FRAME.GOLDEN_FRAME] = treereader.vp8_read_bit(ref bc);
                pc.ref_frame_sign_bias[(int)MV_REFERENCE_FRAME.ALTREF_FRAME] = treereader.vp8_read_bit(ref bc);
            }

            pc.refresh_entropy_probs = treereader.vp8_read_bit(ref bc);

            if (pc.refresh_entropy_probs == 0)
            {
                //memcpy(pc.lfc, pc.fc, sizeof(pc.fc));
                pc.lfc = pc.fc.CopyOf();
            }

            pc.refresh_last_frame = pc.frame_type == FRAME_TYPE.KEY_FRAME || (treereader.vp8_read_bit(ref bc) > 0) ? 1 : 0;

            {
                pbi.independent_partitions = 1;

                /* read coef probability tree */
                for (i = 0; i < entropy.BLOCK_TYPES; ++i)
                {
                    for (j = 0; j < entropy.COEF_BANDS; ++j)
                    {
                        for (k = 0; k < entropy.PREV_COEF_CONTEXTS; ++k)
                        {
                            for (l = 0; l < entropy.ENTROPY_NODES; ++l)
                            {
                                fixed (vp8_prob* probsPtr = &pc.fc.coef_probs[i,j,k,0])
                                {
                                    vp8_prob* p = probsPtr + l;

                                    if (treereader.vp8_read(ref bc, coefupdateprobs.vp8_coef_update_probs[i, j, k, l]) > 0)
                                    {
                                        *p = (vp8_prob)treereader.vp8_read_literal(ref bc, 8);
                                    }
                                    if (k > 0 && *p != pc.fc.coef_probs[i,j,k - 1,l])
                                    {
                                        pbi.independent_partitions = 0;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            /* clear out the coeff buffer */
            //memset(xd.qcoeff, 0, sizeof(xd.qcoeff));
            for (int ii = 0; ii < xd.qcoeff.Length; ii++)
            {
                xd.qcoeff[ii] = 0;
            }

            decodemv.vp8_decode_mode_mvs(pbi);

            //DebugProbe.DumpMotionVectors(pbi.common.mip, pbi.common.mb_cols, pbi.common.mb_rows);

            //memset(pc.above_context, 0, sizeof(ENTROPY_CONTEXT_PLANES) * pc.mb_cols);
            for (int ii = 0; ii < pc.above_context.Length; ii++)
            {
                pc.above_context[ii].Clear();
            }

            pbi.frame_corrupt_residual = 0;

            {
                decode_mb_rows(pbi);
                corrupt_tokens |= xd.corrupted;
            }

            /* Collect information about decoder corruption. */
            /* 1. Check first boolean decoder for errors. */
            yv12_fb_new.corrupted = dboolhuff.vp8dx_bool_error(ref bc);
            /* 2. Check the macroblock information */
            yv12_fb_new.corrupted |= corrupt_tokens;

            if (pbi.decoded_key_frame == 0)
            {
                if (pc.frame_type == FRAME_TYPE.KEY_FRAME && yv12_fb_new.corrupted == 0)
                {
                    pbi.decoded_key_frame = 1;
                }
                else
                {
                    vpx_codec.vpx_internal_error(ref pbi.common.error, vpx_codec_err_t.VPX_CODEC_CORRUPT_FRAME,
                                       "A stream must start with a complete key frame");
                }
            }

            /* vpx_log("Decoder: Frame Decoded, Size Roughly:%d bytes
             * \n",bc->pos+pbi->bc2.pos); */

            if (pc.refresh_entropy_probs == 0)
            {
                //memcpy(pc.fc, pc.lfc, sizeof(pc.fc));
                pc.lfc = pc.fc.CopyOf();
                pbi.independent_partitions = prev_independent_partitions;
            }

            return 0;
        }

        static void yv12_extend_frame_top_c(YV12_BUFFER_CONFIG ybf)
        {
            int i;
            byte* src_ptr1;
            byte* dest_ptr1;

            uint Border;
            int plane_stride;

            /***********/
            /* Y Plane */
            /***********/
            Border = (uint)ybf.border;
            plane_stride = ybf.y_stride;
            src_ptr1 = ybf.y_buffer - Border;
            dest_ptr1 = src_ptr1 - (Border * plane_stride);

            for (i = 0; i < (int)Border; ++i)
            {
                //memcpy(dest_ptr1, src_ptr1, plane_stride);
                Mem.memcpy(dest_ptr1, src_ptr1, plane_stride);
                dest_ptr1 += plane_stride;
            }

            /***********/
            /* U Plane */
            /***********/
            plane_stride = ybf.uv_stride;
            Border /= 2;
            src_ptr1 = ybf.u_buffer - Border;
            dest_ptr1 = src_ptr1 - (Border * plane_stride);

            for (i = 0; i < (int)(Border); ++i)
            {
                //memcpy(dest_ptr1, src_ptr1, plane_stride);
                Mem.memcpy(dest_ptr1, src_ptr1, plane_stride);
                dest_ptr1 += plane_stride;
            }

            /***********/
            /* V Plane */
            /***********/

            src_ptr1 = ybf.v_buffer - Border;
            dest_ptr1 = src_ptr1 - (Border * plane_stride);

            for (i = 0; i < (int)(Border); ++i)
            {
                //memcpy(dest_ptr1, src_ptr1, plane_stride);
                Mem.memcpy(dest_ptr1, src_ptr1, plane_stride);
                dest_ptr1 += plane_stride;
            }
        }

        public static void yv12_extend_frame_bottom_c(YV12_BUFFER_CONFIG ybf)
        {
            int i;
            byte* src_ptr1, src_ptr2;
            byte* dest_ptr2;

            uint Border;
            int plane_stride;
            int plane_height;

            /***********/
            /* Y Plane */
            /***********/
            Border = (uint)ybf.border;
            plane_stride = ybf.y_stride;
            plane_height = ybf.y_height;

            src_ptr1 = ybf.y_buffer - Border;
            src_ptr2 = src_ptr1 + (plane_height * plane_stride) - plane_stride;
            dest_ptr2 = src_ptr2 + plane_stride;

            for (i = 0; i < (int)Border; ++i)
            {
                //memcpy(dest_ptr2, src_ptr2, plane_stride);
                Mem.memcpy(dest_ptr2, src_ptr2, plane_stride);
                dest_ptr2 += plane_stride;
            }

            /***********/
            /* U Plane */
            /***********/
            plane_stride = ybf.uv_stride;
            plane_height = ybf.uv_height;
            Border /= 2;

            src_ptr1 = ybf.u_buffer - Border;
            src_ptr2 = src_ptr1 + (plane_height * plane_stride) - plane_stride;
            dest_ptr2 = src_ptr2 + plane_stride;

            for (i = 0; i < (int)(Border); ++i)
            {
                //memcpy(dest_ptr2, src_ptr2, plane_stride);
                Mem.memcpy(dest_ptr2, src_ptr2, plane_stride);
                dest_ptr2 += plane_stride;
            }

            /***********/
            /* V Plane */
            /***********/

            src_ptr1 = ybf.v_buffer - Border;
            src_ptr2 = src_ptr1 + (plane_height * plane_stride) - plane_stride;
            dest_ptr2 = src_ptr2 + plane_stride;

            for (i = 0; i < (int)(Border); ++i)
            {
                //memcpy(dest_ptr2, src_ptr2, plane_stride);
                Mem.memcpy(dest_ptr2, src_ptr2, plane_stride);
                dest_ptr2 += plane_stride;
            }
        }

        static void yv12_extend_frame_left_right_c(YV12_BUFFER_CONFIG ybf,
                                           byte* y_src,
                                           byte* u_src,
                                           byte* v_src)
        {
            int i;
            byte* src_ptr1, src_ptr2;
            byte* dest_ptr1, dest_ptr2;

            uint Border;
            int plane_stride;
            int plane_height;
            int plane_width;

            /***********/
            /* Y Plane */
            /***********/
            Border = (uint)ybf.border;
            plane_stride = ybf.y_stride;
            plane_height = 16;
            plane_width = ybf.y_width;

            /* copy the left and right most columns out */
            src_ptr1 = y_src;
            src_ptr2 = src_ptr1 + plane_width - 1;
            dest_ptr1 = src_ptr1 - Border;
            dest_ptr2 = src_ptr2 + 1;

            for (i = 0; i < plane_height; ++i)
            {
                //memset(dest_ptr1, src_ptr1[0], Border);
                Mem.memset(dest_ptr1, src_ptr1[0], (int)Border);
                //memset(dest_ptr2, src_ptr2[0], Border);
                Mem.memset(dest_ptr2, src_ptr2[0], (int)Border);
                src_ptr1 += plane_stride;
                src_ptr2 += plane_stride;
                dest_ptr1 += plane_stride;
                dest_ptr2 += plane_stride;
            }

            /***********/
            /* U Plane */
            /***********/
            plane_stride = ybf.uv_stride;
            plane_height = 8;
            plane_width = ybf.uv_width;
            Border /= 2;

            /* copy the left and right most columns out */
            src_ptr1 = u_src;
            src_ptr2 = src_ptr1 + plane_width - 1;
            dest_ptr1 = src_ptr1 - Border;
            dest_ptr2 = src_ptr2 + 1;

            for (i = 0; i < plane_height; ++i)
            {
                //memset(dest_ptr1, src_ptr1[0], Border);
                Mem.memset(dest_ptr1, src_ptr1[0], (int)Border);
                //memset(dest_ptr2, src_ptr2[0], Border);
                Mem.memset(dest_ptr2, src_ptr2[0], (int)Border);
                src_ptr1 += plane_stride;
                src_ptr2 += plane_stride;
                dest_ptr1 += plane_stride;
                dest_ptr2 += plane_stride;
            }

            /***********/
            /* V Plane */
            /***********/

            /* copy the left and right most columns out */
            src_ptr1 = v_src;
            src_ptr2 = src_ptr1 + plane_width - 1;
            dest_ptr1 = src_ptr1 - Border;
            dest_ptr2 = src_ptr2 + 1;

            for (i = 0; i < plane_height; ++i)
            {
                //memset(dest_ptr1, src_ptr1[0], Border);
                Mem.memset(dest_ptr1, src_ptr1[0], (int)Border);
                //memset(dest_ptr2, src_ptr2[0], Border);
                Mem.memset(dest_ptr2, src_ptr2[0], (int)Border);
                src_ptr1 += plane_stride;
                src_ptr2 += plane_stride;
                dest_ptr1 += plane_stride;
                dest_ptr2 += plane_stride;
            }
        }

        public unsafe static void decode_mb_rows(VP8D_COMP pbi)
        {
            VP8_COMMON pc = pbi.common;
            MACROBLOCKD xd = pbi.mb;

            ArrPtr<MODE_INFO> lf_mic = xd.mode_info_context;

            int ibc = 0;
            int num_part = 1 << (int)pc.multi_token_partition;

            int recon_yoffset, recon_uvoffset;
            int mb_row, mb_col;
            int mb_idx = 0;

            ref YV12_BUFFER_CONFIG yv12_fb_new = ref pbi.dec_fb_ref[(int)MV_REFERENCE_FRAME.INTRA_FRAME];

            int recon_y_stride = yv12_fb_new.y_stride;
            int recon_uv_stride = yv12_fb_new.uv_stride;

            byte*[,] ref_buffer = new byte*[(int)MV_REFERENCE_FRAME.MAX_REF_FRAMES, 3];
            byte*[] dst_buffer = new byte*[3];
            byte*[] lf_dst = new byte*[3];
            byte*[] eb_dst = new byte*[3];
            int i;
            int[] ref_fb_corrupted = new int[(int)MV_REFERENCE_FRAME.MAX_REF_FRAMES];

            ref_fb_corrupted[(int)MV_REFERENCE_FRAME.INTRA_FRAME] = 0;

            for (i = 1; i < (int)MV_REFERENCE_FRAME.MAX_REF_FRAMES; ++i)
            {
                ref YV12_BUFFER_CONFIG this_fb = ref pbi.dec_fb_ref[i];

                ref_buffer[i, 0] = this_fb.y_buffer;
                ref_buffer[i, 1] = this_fb.u_buffer;
                ref_buffer[i, 2] = this_fb.v_buffer;

                ref_fb_corrupted[i] = this_fb.corrupted;
            }

            /* Set up the buffer pointers */
            eb_dst[0] = lf_dst[0] = dst_buffer[0] = yv12_fb_new.y_buffer;
            eb_dst[1] = lf_dst[1] = dst_buffer[1] = yv12_fb_new.u_buffer;
            eb_dst[2] = lf_dst[2] = dst_buffer[2] = yv12_fb_new.v_buffer;

            xd.up_available = 0;

            /* Initialize the loop filter for this frame. */
            if (pc.filter_level > 0) vp8_loopfilter.vp8_loop_filter_frame_init(pc, xd, pc.filter_level);

            setupintrarecon.vp8_setup_intra_recon_top_line(yv12_fb_new);

            /* Decode the individual macro block */
            for (mb_row = 0; mb_row < pc.mb_rows; ++mb_row)
            {
                if (num_part > 1)
                {
                    xd.current_bc = pbi.mbc[ibc];
                    ibc++;

                    if (ibc == num_part) ibc = 0;
                }

                recon_yoffset = mb_row * recon_y_stride * 16;
                recon_uvoffset = mb_row * recon_uv_stride * 8;

                /* reset contexts */
                xd.above_context = new ArrPtr<ENTROPY_CONTEXT_PLANES>(pc.above_context);
                //memset(xd->left_context, 0, sizeof(ENTROPY_CONTEXT_PLANES));
                xd.left_context.Clear();

                xd.left_available = 0;

                xd.mb_to_top_edge = -((mb_row * 16) << 3);
                xd.mb_to_bottom_edge = ((pc.mb_rows - 1 - mb_row) * 16) << 3;

                xd.recon_above[0] = dst_buffer[0] + recon_yoffset;
                xd.recon_above[1] = dst_buffer[1] + recon_uvoffset;
                xd.recon_above[2] = dst_buffer[2] + recon_uvoffset;

                xd.recon_left[0] = xd.recon_above[0] - 1;
                xd.recon_left[1] = xd.recon_above[1] - 1;
                xd.recon_left[2] = xd.recon_above[2] - 1;

                xd.recon_above[0] -= xd.dst.y_stride;
                xd.recon_above[1] -= xd.dst.uv_stride;
                xd.recon_above[2] -= xd.dst.uv_stride;

                /* TODO: move to outside row loop */
                xd.recon_left_stride[0] = xd.dst.y_stride;
                xd.recon_left_stride[1] = xd.dst.uv_stride;

                setupintrarecon.setup_intra_recon_left(xd.recon_left[0], xd.recon_left[1],
                                       xd.recon_left[2], xd.dst.y_stride,
                                       xd.dst.uv_stride);

                for (mb_col = 0; mb_col < pc.mb_cols; ++mb_col)
                {
                    /* Distance of Mb to the various image edges.
                     * These are specified to 8th pel as they are always compared to values
                     * that are in 1/8th pel units
                     */
                    xd.mb_to_left_edge = -((mb_col * 16) << 3);
                    xd.mb_to_right_edge = ((pc.mb_cols - 1 - mb_col) * 16) << 3;

                    xd.dst.y_buffer = dst_buffer[0] + recon_yoffset;
                    xd.dst.u_buffer = dst_buffer[1] + recon_uvoffset;
                    xd.dst.v_buffer = dst_buffer[2] + recon_uvoffset;

                    if (xd.mode_info_context.get().mbmi.ref_frame >= (int)MV_REFERENCE_FRAME.LAST_FRAME)
                    {
                        MV_REFERENCE_FRAME mvref = (MV_REFERENCE_FRAME)xd.mode_info_context.get().mbmi.ref_frame;
                        xd.pre.y_buffer = ref_buffer[(int)mvref, 0] + recon_yoffset;
                        xd.pre.u_buffer = ref_buffer[(int)mvref, 1] + recon_uvoffset;
                        xd.pre.v_buffer = ref_buffer[(int)mvref, 2] + recon_uvoffset;
                    }
                    else
                    {
                        // ref_frame is INTRA_FRAME, pre buffer should not be used.
                        xd.pre.y_buffer = null;
                        xd.pre.u_buffer = null;
                        xd.pre.v_buffer = null;
                    }

                    /* propagate errors from reference frames */
                    xd.corrupted |= ref_fb_corrupted[xd.mode_info_context.get().mbmi.ref_frame];

                    decode_macroblock(pbi, xd, (uint)mb_idx);

                    //DebugProbe.DumpMacroBlock(xd, mb_idx);

                    mb_idx++;
                    xd.left_available = 1;

                    /* check if the boolean decoder has suffered an error */
                    xd.corrupted |= dboolhuff.vp8dx_bool_error(ref xd.current_bc);

                    xd.recon_above[0] += 16;
                    xd.recon_above[1] += 8;
                    xd.recon_above[2] += 8;
                    xd.recon_left[0] += 16;
                    xd.recon_left[1] += 8;
                    xd.recon_left[2] += 8;

                    recon_yoffset += 16;
                    recon_uvoffset += 8;

                    ++xd.mode_info_context; /* next mb */

                    xd.above_context++;
                }

                /* adjust to the next row of mbs */
                extend.vp8_extend_mb_row(yv12_fb_new, xd.dst.y_buffer + 16, xd.dst.u_buffer + 8,
                                  xd.dst.v_buffer + 8);

                ++xd.mode_info_context; /* skip prediction column */
                xd.up_available = 1;

                if (pc.filter_level > 0)
                {
                    if (mb_row > 0)
                    {
                        if (pc.filter_type == LOOPFILTERTYPE.NORMAL_LOOPFILTER)
                        {
                            vp8_loopfilter.vp8_loop_filter_row_normal(pc, lf_mic, mb_row - 1, recon_y_stride,
                                                       recon_uv_stride, lf_dst[0], lf_dst[1],
                                                       lf_dst[2]);
                        }
                        else
                        {
                            vp8_loopfilter.vp8_loop_filter_row_simple(pc, lf_mic, mb_row - 1, recon_y_stride,
                                                       lf_dst[0]);
                        }
                        if (mb_row > 1)
                        {
                            yv12_extend_frame_left_right_c(yv12_fb_new, eb_dst[0], eb_dst[1],
                                                           eb_dst[2]);

                            eb_dst[0] += recon_y_stride * 16;
                            eb_dst[1] += recon_uv_stride * 8;
                            eb_dst[2] += recon_uv_stride * 8;
                        }

                        lf_dst[0] += recon_y_stride * 16;
                        lf_dst[1] += recon_uv_stride * 8;
                        lf_dst[2] += recon_uv_stride * 8;
                        lf_mic += pc.mb_cols;
                        lf_mic++; /* Skip border mb */
                    }
                }
                else
                {
                    if (mb_row > 0)
                    {
                        /**/
                        yv12_extend_frame_left_right_c(yv12_fb_new, eb_dst[0], eb_dst[1],
                                                       eb_dst[2]);
                        eb_dst[0] += recon_y_stride * 16;
                        eb_dst[1] += recon_uv_stride * 8;
                        eb_dst[2] += recon_uv_stride * 8;
                    }
                }
            }

            if (pc.filter_level > 0)
            {
                if (pc.filter_type == LOOPFILTERTYPE.NORMAL_LOOPFILTER)
                {
                    vp8_loopfilter.vp8_loop_filter_row_normal(pc, lf_mic, mb_row - 1, recon_y_stride,
                                               recon_uv_stride, lf_dst[0], lf_dst[1],
                                               lf_dst[2]);
                }
                else
                {
                    vp8_loopfilter.vp8_loop_filter_row_simple(pc, lf_mic, mb_row - 1, recon_y_stride,
                                               lf_dst[0]);
                }

                yv12_extend_frame_left_right_c(yv12_fb_new, eb_dst[0], eb_dst[1],
                                               eb_dst[2]);
                eb_dst[0] += recon_y_stride * 16;
                eb_dst[1] += recon_uv_stride * 8;
                eb_dst[2] += recon_uv_stride * 8;
            }
            yv12_extend_frame_left_right_c(yv12_fb_new, eb_dst[0], eb_dst[1], eb_dst[2]);
            yv12_extend_frame_top_c(yv12_fb_new);
            yv12_extend_frame_bottom_c(yv12_fb_new);
        }

        static void decode_macroblock(VP8D_COMP pbi, MACROBLOCKD xd, uint mb_idx)
        {
            MB_PREDICTION_MODE mode;
            int i;

            if (xd.mode_info_context.get().mbmi.mb_skip_coeff > 0)
            {
                detokenize.vp8_reset_mb_tokens_context(xd);
            }
            else if (dboolhuff.vp8dx_bool_error(ref xd.current_bc) == 0)
            {
                int eobtotal;
                eobtotal = detokenize.vp8_decode_mb_tokens(pbi, xd);

                /* Special case:  Force the loopfilter to skip when eobtotal is zero */
                xd.mode_info_context.get().mbmi.mb_skip_coeff = (byte)(eobtotal == 0 ? 1 : 0);
            }

            //DebugProbe.DumpSubBlockCoefficients(xd);

            mode = (MB_PREDICTION_MODE)xd.mode_info_context.get().mbmi.mode;

            if (xd.segmentation_enabled > 0) vp8_mb_init_dequantizer(pbi, xd);

            /* do prediction */
            if (xd.mode_info_context.get().mbmi.ref_frame == (int)MV_REFERENCE_FRAME.INTRA_FRAME)
            {
                reconintra.vp8_build_intra_predictors_mbuv_s(
                    xd, xd.recon_above[1], xd.recon_above[2], xd.recon_left[1],
                    xd.recon_left[2], xd.recon_left_stride[1], xd.dst.u_buffer,
                    xd.dst.v_buffer, xd.dst.uv_stride);

                if (mode != MB_PREDICTION_MODE.B_PRED)
                {
                    reconintra.vp8_build_intra_predictors_mby_s(
                        xd, xd.recon_above[0], xd.recon_left[0], xd.recon_left_stride[0],
                        xd.dst.y_buffer, xd.dst.y_stride);
                }
                else
                {
                    //short* DQC = xd.dequant_y1;
                    fixed (short* DQC = xd.dequant_y1)
                    {
                        int dst_stride = xd.dst.y_stride;

                        /* clear out residual eob info */
                        if (xd.mode_info_context.get().mbmi.mb_skip_coeff > 0)
                        {
                            //memset(xd.eobs, 0, 25);
                            Mem.memset<sbyte>(xd.eobs, 0, 25);
                        }

                        reconintra4x4.intra_prediction_down_copy(xd, xd.recon_above[0] + 16);

                        for (i = 0; i < 16; ++i)
                        {
                            BLOCKD b = xd.block[i];
                            byte* dst = xd.dst.y_buffer + b.offset;
                            B_PREDICTION_MODE b_mode = xd.mode_info_context.get().bmi[i].as_mode;
                            byte* Above = dst - dst_stride;
                            byte* yleft = dst - 1;
                            int left_stride = dst_stride;
                            byte top_left = Above[-1];

                            reconintra4x4.vp8_intra4x4_predict(Above, yleft, left_stride, b_mode, dst, dst_stride,
                                                 top_left);

                            //DebugProbe.DumpYSubBlock(i, dst, dst_stride);

                            if (xd.eobs[i] > 0)
                            {
                                if (xd.eobs[i] > 1)
                                {
                                    //vp8_rtcd.vp8_dequant_idct_add(b.qcoeff, DQC, dst, dst_stride);

                                    fixed (short* pQcoeff = b.qcoeff.src())
                                    {
                                        vp8_rtcd.vp8_dequant_idct_add(pQcoeff + b.qcoeff.Index, DQC, dst, dst_stride);
                                    }
                                }
                                else
                                {
                                    vp8_rtcd.vp8_dc_only_idct_add((short)(b.qcoeff.get() * DQC[0]), dst, dst_stride, dst, dst_stride);
                                    //memset(b.qcoeff, 0, 2 * sizeof(b.qcoeff[0]));
                                    Mem.memset<short>(b.qcoeff.src(), 0, 2);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                reconinter.vp8_build_inter_predictors_mb(xd);
            }

            if (xd.mode_info_context.get().mbmi.mb_skip_coeff == 0)
            {
                /* dequantization and idct */
                if (mode != MB_PREDICTION_MODE.B_PRED)
                {
                    //short* DQC = xd.dequant_y1;
                    fixed (short* pDQC = xd.dequant_y1, pY1DQC = xd.dequant_y1_dc)
                    {
                        short* DQC = pDQC;

                        if (mode != MB_PREDICTION_MODE.SPLITMV)
                        {
                            ref BLOCKD b = ref xd.block[24];

                            /* do 2nd order transform on the dc block */
                            if (xd.eobs[24] > 1)
                            {
                                //DebugProbe.DumpSubBlockCoefficients(xd);

                                fixed (short* pDequantY2 = xd.dequant_y2)
                                {
                                     vp8_rtcd.vp8_dequantize_b(b, pDequantY2);
                                }

                                //DebugProbe.DumpSubBlockCoefficients(xd);

                                //vp8_rtcd.vp8_short_inv_walsh4x4(b.dqcoeff[0], xd.qcoeff);
                                fixed (short* dqCoeff = b.dqcoeff.src(), qCoeff = xd.qcoeff)
                                {
                                    // Point to the dqcoeff array for the 24th block.
                                    short * bDqCoeff = dqCoeff + b.dqcoeff.Index;
                                    vp8_rtcd.vp8_short_inv_walsh4x4(bDqCoeff, qCoeff);
                                }

                                //memset(b.qcoeff, 0, 16 * sizeof(b.qcoeff[0]));
                                b.qcoeff.setMultiple(0, 16);
                            }
                            else
                            {
                                //b->dqcoeff[0] = (short)(b->qcoeff[0] * xd->dequant_y2[0]);
                                b.dqcoeff.set((short)(b.qcoeff.get() * xd.dequant_y2[0]));

                                //vp8_rtcd.vp8_short_inv_walsh4x4_1(b.dqcoeff[0], xd.qcoeff);
                                fixed (short* dqCoeff = b.dqcoeff.src(), qCoeff = xd.qcoeff)
                                {
                                    // Point to the dqcoeff array for the 24th block.
                                    short* bDqCoeff = dqCoeff + b.dqcoeff.Index;
                                    vp8_rtcd.vp8_short_inv_walsh4x4_1(bDqCoeff, qCoeff);
                                }

                                //memset(b.qcoeff, 0, 2 * sizeof(b.qcoeff[0]));
                                b.qcoeff.set(0);
                            }

                            /* override the dc dequant constant in order to preserve the
                             * dc components
                             */
                            //DQC = xd.dequant_y1_dc;
                            DQC = pY1DQC;
                        }

                        fixed (short* xdQCoeff = xd.qcoeff)
                        {
                            fixed (sbyte* eobs = xd.eobs)
                            {
                                vp8_rtcd.vp8_dequant_idct_add_y_block(xdQCoeff, DQC, xd.dst.y_buffer,
                                                             xd.dst.y_stride, eobs);
                            }
                        }
                    }
                }

                fixed (short* xdQCoeff = xd.qcoeff, pDequantUV = xd.dequant_uv)
                {
                    short* xdQCoeff16x16 = xdQCoeff + 16 * 16;

                    fixed (sbyte* eobs = xd.eobs)
                    {
                        sbyte* peobs = eobs + 16;
                        vp8_rtcd.vp8_dequant_idct_add_uv_block(xdQCoeff16x16, pDequantUV,
                                              xd.dst.u_buffer, xd.dst.v_buffer,
                                              xd.dst.uv_stride, peobs);
                    }
                }
            }
        }
    }
}
