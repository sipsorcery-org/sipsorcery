//-----------------------------------------------------------------------------
// Filename:decodemv.cs
//
// Description: Port of:
//  - decodemv.c
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 01 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
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

using System.Linq;

using vp8_prob = System.Byte;
using vp8_reader = Vpx.Net.BOOL_DECODER;

namespace Vpx.Net
{
    public static class decodemv
    {
        enum MB_MODES { CNT_INTRA, CNT_NEAREST, CNT_NEAR, CNT_SPLITMV };

        static readonly vp8_prob[][] vp8_sub_mv_ref_prob3 = new vp8_prob[8][] {
          new vp8_prob[blockd.VP8_SUBMVREFS - 1] { 147, 136, 18 }, /* SUBMVREF_NORMAL          */
          new vp8_prob[blockd.VP8_SUBMVREFS - 1] { 223, 1, 34 },   /* SUBMVREF_LEFT_ABOVE_SAME */
          new vp8_prob[blockd.VP8_SUBMVREFS - 1] { 106, 145, 1 },  /* SUBMVREF_LEFT_ZED        */
          new vp8_prob[blockd.VP8_SUBMVREFS - 1] { 208, 1, 1 },    /* SUBMVREF_LEFT_ABOVE_ZED  */
          new vp8_prob[blockd.VP8_SUBMVREFS - 1] { 179, 121, 1 },  /* SUBMVREF_ABOVE_ZED       */
          new vp8_prob[blockd.VP8_SUBMVREFS - 1] { 223, 1, 34 },   /* SUBMVREF_LEFT_ABOVE_SAME */
          new vp8_prob[blockd.VP8_SUBMVREFS - 1] { 179, 121, 1 },  /* SUBMVREF_ABOVE_ZED       */
          new vp8_prob[blockd.VP8_SUBMVREFS - 1] { 208, 1, 1 }     /* SUBMVREF_LEFT_ABOVE_ZED  */
        };

        static readonly byte[] mbsplit_fill_count = { 8, 8, 4, 1 };
        static readonly byte[,] mbsplit_fill_offset = {
          { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 },
          { 0, 1, 4, 5, 8, 9, 12, 13, 2, 3, 6, 7, 10, 11, 14, 15 },
          { 0, 1, 4, 5, 2, 3, 6, 7, 8, 9, 12, 13, 10, 11, 14, 15 },
          { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 }
        };

        unsafe static B_PREDICTION_MODE read_bmode(ref vp8_reader bc, in vp8_prob* p)
        {
            int i = treereader.vp8_treed_read(ref bc, entropymode.vp8_bmode_tree, p);

            return (B_PREDICTION_MODE)i;
        }

        unsafe static MB_PREDICTION_MODE read_ymode(ref vp8_reader bc, in vp8_prob* p)
        {
            int i = treereader.vp8_treed_read(ref bc, entropymode.vp8_ymode_tree, p);

            return (MB_PREDICTION_MODE)i;
        }

        unsafe static MB_PREDICTION_MODE read_kf_ymode(ref vp8_reader bc, in vp8_prob* p)
        {
            int i = treereader.vp8_treed_read(ref bc, entropymode.vp8_kf_ymode_tree, p);

            return (MB_PREDICTION_MODE)i;
        }

        unsafe static MB_PREDICTION_MODE read_uv_mode(ref vp8_reader bc, in vp8_prob* p)
        {
            int i = treereader.vp8_treed_read(ref bc, entropymode.vp8_uv_mode_tree, p);

            return (MB_PREDICTION_MODE)i;
        }

        static int read_mvcomponent(ref vp8_reader r, in byte[] prob)
        {
            //vp8_prob p = (const vp8_prob*)mvc;
            int x = 0;

            if (treereader.vp8_read(ref r, prob[(int)MV_ENUM.mvpis_short]) > 0)
            { /* Large */
                int i = 0;

                do
                {
                    x += treereader.vp8_read(ref r, prob[(int)MV_ENUM.MVPbits + i]) << i;
                } while (++i < 3);

                i = (int)MV_ENUM.mvlong_width - 1; /* Skip bit 3, which is sometimes implicit */

                do
                {
                    x += treereader.vp8_read(ref r, prob[(int)MV_ENUM.MVPbits + i]) << i;
                } while (--i > 3);

                if ((x & 0xFFF0) == 0 || treereader.vp8_read(ref r, prob[(int)MV_ENUM.MVPbits + 3]) > 0) x += 8;
            }
            else
            { /* small */
                x = treereader.vp8_treed_read(ref r, entropymode.vp8_small_mvtree, prob.Skip((int)MV_ENUM.MVPshort).ToArray());
            }

            if (x > 0 && treereader.vp8_read(ref r, prob[(int)MV_ENUM.MVPsign]) > 0) x = -x;

            return x;
        }

        static void read_mv(ref vp8_reader r, ref MV mv, in byte[] prob, in byte[] probNext)
        {
            mv.row = (short)(read_mvcomponent(ref r, prob) * 2);
            //mv.col = (short)(read_mvcomponent(ref r, ++mvc) * 2);
            mv.col = (short)(read_mvcomponent(ref r, probNext) * 2);
        }

        static unsafe void read_kf_modes(VP8D_COMP pbi, ArrPtr<MODE_INFO> miPtr)
        {
            MODE_INFO mi = miPtr.get();
            vp8_reader bc = pbi.mbc[8];
            int mis = pbi.common.mode_info_stride;

            mi.mbmi.ref_frame = (int)MV_REFERENCE_FRAME.INTRA_FRAME;

            fixed (byte* p = vp8_entropymodedata.vp8_kf_ymode_prob)
            {
                mi.mbmi.mode = (byte)read_kf_ymode(ref bc, in p).GetHashCode();
            }

            if (mi.mbmi.mode == (int)MB_PREDICTION_MODE.B_PRED)
            {
                int i = 0;
                mi.mbmi.is_4x4 = 1;

                do
                {
                    B_PREDICTION_MODE A = findnearmv.above_block_mode(miPtr, i, mis);
                    B_PREDICTION_MODE L = findnearmv.left_block_mode(miPtr, i);

                    mi = miPtr.get();

                    //mi->bmi[i].as_mode = read_bmode(bc, vp8_kf_bmode_prob[A][L]);

                    fixed (byte* p = vp8_entropymodedata.vp8_kf_bmode_prob)
                    {
                        int step = (int)A * (vp8_entropymodedata.vp8_kf_bmode_prob.GetLength(1) * vp8_entropymodedata.vp8_kf_bmode_prob.GetLength(2)) +
                                    (int)L * (vp8_entropymodedata.vp8_kf_bmode_prob.GetLength(2));
                        mi.bmi[i].as_mode = read_bmode(ref bc, p + step);
                    }
                } while (++i < 16);
            }

            //mi->mbmi.uv_mode = read_uv_mode(bc, vp8_kf_uv_mode_prob);

            fixed (byte* q = vp8_entropymodedata.vp8_kf_uv_mode_prob)
            {
                mi.mbmi.uv_mode = (byte)read_uv_mode(ref bc, q);
            }
        }

        public static void read_mb_features(ref vp8_reader r, ref MB_MODE_INFO mi, MACROBLOCKD x)
        {
            /* Is segmentation enabled */
            if (x.segmentation_enabled > 0 && x.update_mb_segmentation_map > 0)
            {
                /* If so then read the segment id. */
                if (treereader.vp8_read(ref r, x.mb_segment_tree_probs[0]) > 0)
                {
                    mi.segment_id =
                        (byte)(2 + treereader.vp8_read(ref r, x.mb_segment_tree_probs[2]));
                }
                else
                {
                    mi.segment_id =
                        (byte)(treereader.vp8_read(ref r, x.mb_segment_tree_probs[1]));
                }
            }
        }

        unsafe static void read_mvcontexts(ref vp8_reader bc, MV_CONTEXT[] mvc)
        {
            int i = 0;

            do
            {
                vp8_prob[] up = entropymv.vp8_mv_update_probs[i].prob;
                int upIndex = 0;
                //vp8_prob* p = (vp8_prob*)(mvc + i);
                vp8_prob[] p = mvc[i].prob;
                int pIndex = 0;
                int pStop = (int)MV_ENUM.MVPcount;
                //vp8_prob * pstop = p + (int)MV_ENUM.MVPcount;

                do
                {
                    if (treereader.vp8_read(ref bc, up[upIndex++]) > 0)
                    {
                        vp8_prob x = (vp8_prob)treereader.vp8_read_literal(ref bc, 7);

                        p[pIndex] = (byte)(x > 0 ? x << 1 : 1);
                    }
                    //} while (++p < pstop);
                } while (++pIndex < pStop);
            } while (++i < 2);
        }

        unsafe static void mb_mode_mv_init(VP8D_COMP pbi)
        {
            ref vp8_reader bc = ref pbi.mbc[8];
            //MV_CONTEXT mvc = pbi.common.fc.mvc;

            /* Read the mb_no_coeff_skip flag */
            pbi.common.mb_no_coeff_skip = treereader.vp8_read_bit(ref bc);

            pbi.prob_skip_false = 0;
            if (pbi.common.mb_no_coeff_skip > 0)
            {
                pbi.prob_skip_false = (vp8_prob)treereader.vp8_read_literal(ref bc, 8);
            }

            if (pbi.common.frame_type != FRAME_TYPE.KEY_FRAME)
            {
                pbi.prob_intra = (vp8_prob)treereader.vp8_read_literal(ref bc, 8);
                pbi.prob_last = (vp8_prob)treereader.vp8_read_literal(ref bc, 8);
                pbi.prob_gf = (vp8_prob)treereader.vp8_read_literal(ref bc, 8);

                if (treereader.vp8_read_bit(ref bc) > 0)
                {
                    int i = 0;

                    do
                    {
                        pbi.common.fc.ymode_prob[i] = (vp8_prob)treereader.vp8_read_literal(ref bc, 8);
                    } while (++i < 4);
                }

                if (treereader.vp8_read_bit(ref bc) > 0)
                {
                    int i = 0;

                    do
                    {
                        pbi.common.fc.uv_mode_prob[i] = (vp8_prob)treereader.vp8_read_literal(ref bc, 8);
                    } while (++i < 3);
                }

                read_mvcontexts(ref bc, pbi.common.fc.mvc);
            }
        }

        public unsafe static void decode_mb_mode_mvs(VP8D_COMP pbi, ArrPtr<MODE_INFO> mi)
        {
            /* Read the Macroblock segmentation map if it is being updated explicitly
             * this frame (reset to 0 above by default)
             * By default on a key frame reset all MBs to segment 0
             */
            if (pbi.mb.update_mb_segmentation_map > 0)
            {
                read_mb_features(ref pbi.mbc[8], ref mi.get().mbmi, pbi.mb);
            }
            else if (pbi.common.frame_type == FRAME_TYPE.KEY_FRAME)
            {
                mi.get().mbmi.segment_id = 0;
            }

            /* Read the macroblock coeff skip flag if this feature is in use,
             * else default to 0 */
            if (pbi.common.mb_no_coeff_skip > 0)
            {
                mi.get().mbmi.mb_skip_coeff = (byte)treereader.vp8_read(ref pbi.mbc[8], pbi.prob_skip_false);
            }
            else
            {
                mi.get().mbmi.mb_skip_coeff = 0;
            }

            mi.get().mbmi.is_4x4 = 0;
            if (pbi.common.frame_type == FRAME_TYPE.KEY_FRAME)
            {
                read_kf_modes(pbi, mi);
            }
            else
            {
                read_mb_modes_mv(pbi, mi, ref mi.get().mbmi);
            }
        }

        unsafe static void read_mb_modes_mv(VP8D_COMP pbi, ArrPtr<MODE_INFO> mi, ref MB_MODE_INFO mbmi)
        {
            vp8_reader bc = pbi.mbc[8];
            //mbmi.ref_frame = (MV_REFERENCE_FRAME)treereader.vp8_read(ref bc, pbi.prob_intra);
            mbmi.ref_frame = (byte)treereader.vp8_read(ref bc, pbi.prob_intra);
            if (mbmi.ref_frame > 0)
            { /* inter MB */

                int* cnt = stackalloc int[4];
                int* cntx = cnt;
                var near_mvs = stackalloc int_mv[4];
                int_mv* nmv = near_mvs;
                int mis = pbi.mb.mode_info_stride;

                // Port AC: These pointers reference items in the "mi" array. The problem is the 
                // "mi" array is managed (no easy way to create a non-managed array with managed class
                // elements). Rather than using pining all over the place a new ArrPtr class has
                // been created.
                //MODE_INFO* above = mi - mis;
                //MODE_INFO* left = mi - 1;
                //MODE_INFO* aboveleft = above - 1;
                ArrPtr<MODE_INFO> above = mi - mis;
                ArrPtr<MODE_INFO> left = mi - 1;
                ArrPtr<MODE_INFO> aboveleft = above - 1;

                int[] ref_frame_sign_bias = pbi.common.ref_frame_sign_bias;

                mbmi.need_to_clamp_mvs = 0;

                if (treereader.vp8_read(ref bc, pbi.prob_last) > 0)
                {
                    //mbmi->ref_frame =
                    // (MV_REFERENCE_FRAME)((int)(2 + vp8_read(bc, pbi->prob_gf)));
                    mbmi.ref_frame = (byte)(2 + treereader.vp8_read(ref bc, pbi.prob_gf));
                }

                /* Zero accumulators */
                nmv[0].as_int = nmv[1].as_int = nmv[2].as_int = 0;
                cnt[0] = cnt[1] = cnt[2] = cnt[3] = 0;

                /* Process above */
                if (above.get().mbmi.ref_frame != (byte)MV_REFERENCE_FRAME.INTRA_FRAME)
                {
                    if (above.get().mbmi.mv.as_int != 0)
                    {
                        (++nmv)->as_int = above.get().mbmi.mv.as_int;
                        findnearmv.mv_bias(ref_frame_sign_bias[above.get().mbmi.ref_frame], mbmi.ref_frame,
                                ref *nmv, ref_frame_sign_bias);
                        ++cntx;
                    }

                    *cntx += 2;
                }

                /* Process left */
                if (left.get().mbmi.ref_frame != (byte)MV_REFERENCE_FRAME.INTRA_FRAME)
                {
                    if (left.get().mbmi.mv.as_int != 0)
                    {
                        int_mv this_mv = new int_mv();

                        this_mv.as_int = left.get().mbmi.mv.as_int;
                        findnearmv.mv_bias(ref_frame_sign_bias[left.get().mbmi.ref_frame], mbmi.ref_frame,
                                ref this_mv, ref_frame_sign_bias);

                        if (this_mv.as_int != nmv->as_int)
                        {
                            (++nmv)->as_int = this_mv.as_int;
                            ++cntx;
                        }

                        *cntx += 2;
                    }
                    else
                    {
                        cnt[(int)MB_MODES.CNT_INTRA] += 2;
                    }
                }

                /* Process above left */
                if (aboveleft.get().mbmi.ref_frame != (byte)MV_REFERENCE_FRAME.INTRA_FRAME)
                {
                    if (aboveleft.get().mbmi.mv.as_int != 0)
                    {
                        int_mv this_mv = new int_mv();

                        this_mv.as_int = aboveleft.get().mbmi.mv.as_int;
                        findnearmv.mv_bias(ref_frame_sign_bias[aboveleft.get().mbmi.ref_frame], mbmi.ref_frame,
                                ref this_mv, ref_frame_sign_bias);

                        if (this_mv.as_int != nmv->as_int)
                        {
                            (++nmv)->as_int = this_mv.as_int;
                            ++cntx;
                        }

                        *cntx += 1;
                    }
                    else
                    {
                        cnt[(int)MB_MODES.CNT_INTRA] += 1;
                    }
                }

                if (treereader.vp8_read(ref bc, modecont.vp8_mode_contexts[cnt[(int)MB_MODES.CNT_INTRA], 0]) > 0)
                {
                    /* If we have three distinct MV's ... */
                    /* See if above-left MV can be merged with NEAREST */
                    cnt[(int)MB_MODES.CNT_NEAREST] += ((cnt[(int)MB_MODES.CNT_SPLITMV] > 0) &
                                         (nmv->as_int == near_mvs[(int)MB_MODES.CNT_NEAREST].as_int)) ? 1 : 0;

                    /* Swap near and nearest if necessary */
                    if (cnt[(int)MB_MODES.CNT_NEAR] > cnt[(int)MB_MODES.CNT_NEAREST])
                    {
                        int tmp;
                        tmp = cnt[(int)MB_MODES.CNT_NEAREST];
                        cnt[(int)MB_MODES.CNT_NEAREST] = cnt[(int)MB_MODES.CNT_NEAR];
                        cnt[(int)MB_MODES.CNT_NEAR] = tmp;
                        tmp = (int)near_mvs[(int)MB_MODES.CNT_NEAREST].as_int;
                        near_mvs[(int)MB_MODES.CNT_NEAREST].as_int = near_mvs[(int)MB_MODES.CNT_NEAR].as_int;
                        near_mvs[(int)MB_MODES.CNT_NEAR].as_int = (uint)tmp;
                    }

                    if (treereader.vp8_read(ref bc, modecont.vp8_mode_contexts[cnt[(int)MB_MODES.CNT_NEAREST], 1]) > 0)
                    {
                        if (treereader.vp8_read(ref bc, modecont.vp8_mode_contexts[cnt[(int)MB_MODES.CNT_NEAR], 2]) > 0)
                        {
                            int mb_to_top_edge;
                            int mb_to_bottom_edge;
                            int mb_to_left_edge;
                            int mb_to_right_edge;
                            MV_CONTEXT[] mvc = pbi.common.fc.mvc;
                            int near_index;

                            mb_to_top_edge = pbi.mb.mb_to_top_edge;
                            mb_to_bottom_edge = pbi.mb.mb_to_bottom_edge;
                            mb_to_top_edge -= findnearmv.LEFT_TOP_MARGIN;
                            mb_to_bottom_edge += findnearmv.RIGHT_BOTTOM_MARGIN;
                            mb_to_right_edge = pbi.mb.mb_to_right_edge;
                            mb_to_right_edge += findnearmv.RIGHT_BOTTOM_MARGIN;
                            mb_to_left_edge = pbi.mb.mb_to_left_edge;
                            mb_to_left_edge -= findnearmv.LEFT_TOP_MARGIN;

                            /* Use near_mvs[0] to store the "best" MV */
                            near_index = (int)MB_MODES.CNT_INTRA + (cnt[(int)MB_MODES.CNT_NEAREST] >= cnt[(int)MB_MODES.CNT_INTRA] ? 1 : 0);

                            findnearmv.vp8_clamp_mv2(ref near_mvs[near_index], in pbi.mb);

                            cnt[(int)MB_MODES.CNT_SPLITMV] =
                                (((above.get().mbmi.mode == (int)MB_PREDICTION_MODE.SPLITMV) ? 1 : 0)
                                + ((left.get().mbmi.mode == (int)MB_PREDICTION_MODE.SPLITMV) ? 1 : 0)) * 2
                                + ((aboveleft.get().mbmi.mode == (int)MB_PREDICTION_MODE.SPLITMV) ? 1 : 0);

                            if (treereader.vp8_read(ref bc, modecont.vp8_mode_contexts[cnt[(int)MB_MODES.CNT_SPLITMV], 3]) > 0)
                            {
                                decode_split_mv(ref bc, mi, left, above, ref mbmi, near_mvs[near_index],
                                                mvc, mb_to_left_edge, mb_to_right_edge,
                                                mb_to_top_edge, mb_to_bottom_edge);
                                mbmi.mv.as_int = mi.get().bmi[15].mv.as_int;
                                mbmi.mode = (int)MB_PREDICTION_MODE.SPLITMV;
                                mbmi.is_4x4 = 1;
                            }
                            else
                            {
                                //int_mv * mbmi_mv = mbmi.mv;
                                read_mv(ref bc, ref mbmi.mv.as_mv, in mvc[0].prob, in mvc[1].prob);
                                mbmi.mv.as_mv.row += near_mvs[near_index].as_mv.row;
                                mbmi.mv.as_mv.col += near_mvs[near_index].as_mv.col;

                                /* Don't need to check this on NEARMV and NEARESTMV
                                 * modes since those modes clamp the MV. The NEWMV mode
                                 * does not, so signal to the prediction stage whether
                                 * special handling may be required.
                                 */
                                mbmi.need_to_clamp_mvs =
                                    (byte)findnearmv.vp8_check_mv_bounds(mbmi.mv, mb_to_left_edge, mb_to_right_edge,
                                                        mb_to_top_edge, mb_to_bottom_edge);
                                mbmi.mode = (int)MB_PREDICTION_MODE.NEWMV;
                            }
                        }
                        else
                        {
                            mbmi.mode = (byte)MB_PREDICTION_MODE.NEARMV;
                            mbmi.mv.as_int = near_mvs[(int)MB_MODES.CNT_NEAR].as_int;
                            findnearmv.vp8_clamp_mv2(ref mbmi.mv, in pbi.mb);
                        }
                    }
                    else
                    {
                        mbmi.mode = (byte)MB_PREDICTION_MODE.NEARESTMV;
                        mbmi.mv.as_int = near_mvs[(int)MB_MODES.CNT_NEAREST].as_int;
                        findnearmv.vp8_clamp_mv2(ref mbmi.mv, in pbi.mb);
                    }
                }
                else
                {
                    mbmi.mode = (byte)MB_PREDICTION_MODE.ZEROMV;
                    mbmi.mv.as_int = 0;
                }
            }
            else
            {
                /* required for left and above block mv */
                mbmi.mv.as_int = 0;

                /* MB is intra coded */
                fixed (byte* pymode_prob = pbi.common.fc.ymode_prob)
                {
                    mbmi.mode = (byte)read_ymode(ref bc, pymode_prob);
                }

                if (mbmi.mode == (byte)MB_PREDICTION_MODE.B_PRED)
                {
                    int j = 0;
                    mbmi.is_4x4 = 1;
                    fixed (byte* pbmode_prob = pbi.common.fc.bmode_prob)
                    {
                        do
                        {
                            mi.get().bmi[j].as_mode = read_bmode(ref bc, pbmode_prob);
                        } while (++j < 16);
                    }
                }

                fixed (byte* puv_mode_prob = pbi.common.fc.uv_mode_prob)
                {
                    mbmi.uv_mode = (byte)read_uv_mode(ref bc, puv_mode_prob);
                }
            }
        }

        public static void vp8_decode_mode_mvs(VP8D_COMP pbi)
        {
            ArrPtr<MODE_INFO> mi = pbi.common.mi;
            int mb_row = -1;
            int mb_to_right_edge_start;

            mb_mode_mv_init(pbi);

            pbi.mb.mb_to_top_edge = 0;
            pbi.mb.mb_to_bottom_edge = ((pbi.common.mb_rows - 1) * 16) << 3;
            mb_to_right_edge_start = ((pbi.common.mb_cols - 1) * 16) << 3;

            while (++mb_row < pbi.common.mb_rows)
            {
                int mb_col = -1;

                pbi.mb.mb_to_left_edge = 0;
                pbi.mb.mb_to_right_edge = mb_to_right_edge_start;

                while (++mb_col < pbi.common.mb_cols)
                {
                    decode_mb_mode_mvs(pbi, mi);

                    pbi.mb.mb_to_left_edge -= (16 << 3);
                    pbi.mb.mb_to_right_edge -= (16 << 3);
                    mi++; /* next macroblock */
                }
                pbi.mb.mb_to_top_edge -= (16 << 3);
                pbi.mb.mb_to_bottom_edge -= (16 << 3);

                mi++; /* skip left predictor each row */
            }
        }

        static int get_sub_mv_ref_prob(int left, int above)
        {
            int lez = (left == 0) ? 1 : 0;
            int aez = (above == 0) ? 1 : 0;
            int lea = (left == above) ? 1 : 0;
            //const vp8_prob* prob;

            //prob = vp8_sub_mv_ref_prob3[(aez << 2) | (lez << 1) | (lea)];

            //return prob;

            return (aez << 2) | (lez << 1) | (lea);
        }

        unsafe static void decode_split_mv(ref vp8_reader bc, ArrPtr<MODE_INFO> mi,
                                in ArrPtr<MODE_INFO> left_mb, in ArrPtr<MODE_INFO> above_mb,
                                ref MB_MODE_INFO mbmi, int_mv best_mv,
                                in MV_CONTEXT[] mvc, int mb_to_left_edge,
                                int mb_to_right_edge, int mb_to_top_edge,
                                int mb_to_bottom_edge)
        {
            int s; /* split configuration (16x8, 8x16, 8x8, 4x4) */
            /* number of partitions in the split configuration (see vp8_mbsplit_count) */
            int num_p;
            int j = 0;

            s = 3;
            num_p = 16;
            if (treereader.vp8_read(ref bc, 110) > 0)
            {
                s = 2;
                num_p = 4;
                if (treereader.vp8_read(ref bc, 111) > 0)
                {
                    s = treereader.vp8_read(ref bc, 150);
                    num_p = 2;
                }
            }

            do /* for each subset j */
            {
                int_mv leftmv, abovemv;
                int_mv blockmv = new int_mv();
                int k; /* first block in subset j */

                //vp8_prob* prob;
                k = findnearmv.vp8_mbsplit_offset[s, j];

                if ((k & 3) == 0)
                {
                    /* On L edge, get from MB to left of us */
                    if (left_mb.get().mbmi.mode != (int)MB_PREDICTION_MODE.SPLITMV)
                    {
                        leftmv.as_int = left_mb.get().mbmi.mv.as_int;
                    }
                    else
                    {
                        //leftmv.as_int = (left_mb.bmi + k + 4 - 1)->mv.as_int;
                        leftmv.as_int = left_mb.get().bmi[k + 4 - 1].mv.as_int;
                    }
                }
                else
                {
                    //leftmv.as_int = (mi.bmi + k - 1)->mv.as_int;
                    leftmv.as_int = mi.get().bmi[k - 1].mv.as_int;
                }

                if ((k >> 2) == 0)
                {
                    /* On top edge, get from MB above us */
                    if (above_mb.get().mbmi.mode != (int)MB_PREDICTION_MODE.SPLITMV)
                    {
                        abovemv.as_int = above_mb.get().mbmi.mv.as_int;
                    }
                    else
                    {
                        //abovemv.as_int = (above_mb.bmi + k + 16 - 4)->mv.as_int;
                        abovemv.as_int = above_mb.get().bmi[k + 16 - 4].mv.as_int;
                    }
                }
                else
                {
                    //abovemv.as_int = (mi.bmi + k - 4)->mv.as_int;
                    abovemv.as_int = mi.get().bmi[k - 4].mv.as_int;
                }

                //vp8_prob* prob = get_sub_mv_ref_prob((int)leftmv.as_int, (int)abovemv.as_int);
                int probIndex = get_sub_mv_ref_prob((int)leftmv.as_int, (int)abovemv.as_int);
                var prob = vp8_sub_mv_ref_prob3[probIndex];

                if (treereader.vp8_read(ref bc, prob[0]) > 0)
                {
                    if (treereader.vp8_read(ref bc, prob[1]) > 0)
                    {
                        blockmv.as_int = 0;
                        if (treereader.vp8_read(ref bc, prob[2]) > 0)
                        {
                            //blockmv.as_mv.row = read_mvcomponent(bc, &mvc[0]) * 2;
                            blockmv.as_mv.row = (short)(read_mvcomponent(ref bc, mvc[0].prob) * 2);
                            blockmv.as_mv.row += best_mv.as_mv.row;
                            //blockmv.as_mv.col = read_mvcomponent(bc, &mvc[1]) * 2;
                            blockmv.as_mv.col = (short)(read_mvcomponent(ref bc, mvc[1].prob) * 2);
                            blockmv.as_mv.col += best_mv.as_mv.col;
                        }
                    }
                    else
                    {
                        blockmv.as_int = abovemv.as_int;
                    }
                }
                else
                {
                    blockmv.as_int = leftmv.as_int;
                }

                mbmi.need_to_clamp_mvs |= (byte)
                    findnearmv.vp8_check_mv_bounds(blockmv, mb_to_left_edge, mb_to_right_edge,
                                        mb_to_top_edge, mb_to_bottom_edge);

                {
                    /* Fill (uniform) modes, mvs of jth subset.
                     Must do it here because ensuing subsets can
                     refer back to us via "left" or "above". */
                    //byte* fill_offset;
                    uint fill_count = mbsplit_fill_count[s];

                    //fill_offset = &mbsplit_fill_offset[s,(byte)j * mbsplit_fill_count[s]];
                    int fill_offset = (byte)j * mbsplit_fill_count[s];

                    do
                    {
                        mi.get().bmi[mbsplit_fill_offset[s, fill_offset]].mv.as_int = blockmv.as_int;
                        fill_offset++;
                    } while (--fill_count > 0);
                }

            } while (++j < num_p);

            mbmi.partitioning = (byte)s;
        }
    }
}
