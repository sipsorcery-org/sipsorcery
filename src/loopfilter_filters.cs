//-----------------------------------------------------------------------------
// Filename: loopfilter_filters.cs
//
// Description: Port of:
//  - loopfilter_filters.c
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 06 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
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

using System.Runtime.CompilerServices;
using uc = System.Byte;

namespace Vpx.Net
{
    public unsafe static class loopfilter_filters
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int vp8_signed_char_clamp(int t)
        {
            if ((sbyte)t == t) return t;
            return 127 ^ (t >> 31);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int fast_abs(int x)
        {
            int sign = x >> 31;
            return (x + sign) ^ sign;
        }

        /* should we apply any filter at all ( 11111111 yes, 00000000 no) */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static sbyte vp8_filter_mask(uc limit, uc blimit, uc p3, uc p2, uc p1,
                                           uc p0, uc q0, uc q1, uc q2, uc q3)
        {
            if (fast_abs(p3 - p2) > limit) return 0;
            if (fast_abs(p2 - p1) > limit) return 0;
            if (fast_abs(p1 - p0) > limit) return 0;
            if (fast_abs(q1 - q0) > limit) return 0;
            if (fast_abs(q2 - q1) > limit) return 0;
            if (fast_abs(q3 - q2) > limit) return 0;
            if (fast_abs(p0 - q0) * 2 + fast_abs(p1 - q1) / 2 > blimit) return 0;
            return -1;
        }

        /* is there high variance internal edge ( 11111111 yes, 00000000 no) */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static sbyte vp8_hevmask(uc thresh, uc p1, uc p0, uc q0, uc q1)
        {
            if (fast_abs(p1 - p0) > thresh || fast_abs(q1 - q0) > thresh)
            {
                return -1;
            }
            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void vp8_filter(sbyte mask, uc hev, uc* op1, uc* op0, uc* oq0,
                       uc* oq1)
        {
            // Port AC: Arithemtic overflow occurs.
            //checked
            //{
            int ps0, qs0;
            int ps1, qs1;
            int filter_value, Filter1, Filter2;
            int u;

            ps1 = *(sbyte*)op1 ^ -128;
            ps0 = *(sbyte*)op0 ^ -128;
            qs0 = *(sbyte*)oq0 ^ -128;
            qs1 = *(sbyte*)oq1 ^ -128;

            /* add outer taps if we have high edge variance */
            filter_value = vp8_signed_char_clamp(ps1 - qs1);
            filter_value &= (sbyte)hev;

            /* inner taps */
            filter_value = vp8_signed_char_clamp(filter_value + 3 * (qs0 - ps0));
            filter_value &= mask;

            /* save bottom 3 bits so that we round one side +4 and the other +3
             * if it equals 4 we'll set it to adjust by -1 to account for the fact
             * we'd round it by 3 the other way
             */
            Filter1 = vp8_signed_char_clamp(filter_value + 4);
            Filter2 = vp8_signed_char_clamp(filter_value + 3);
            Filter1 >>= 3;
            Filter2 >>= 3;
            u = vp8_signed_char_clamp(qs0 - Filter1);
            *oq0 = (byte)(u ^ 0x80);
            u = vp8_signed_char_clamp(ps0 + Filter2);
            *op0 = (byte)(u ^ 0x80);
            filter_value = Filter1;

            /* outer tap adjustments */
            filter_value += 1;
            filter_value >>= 1;
            filter_value &= (sbyte)~hev;

            u = vp8_signed_char_clamp(qs1 - filter_value);
            *oq1 = (byte)(u ^ 0x80);
            u = vp8_signed_char_clamp(ps1 + filter_value);
            *op1 = (byte)(u ^ 0x80);
            //}
        }

        static void loop_filter_horizontal_edge_c(byte* s, int p, /* pitch */
                                          in byte* blimit,
                                          in byte* limit,
                                          in byte* thresh,
                                          int count)
        {
            int hev = 0; /* high edge variance */
            sbyte mask = 0;
            int i = 0;

            /* loop filter designed to work using chars so that we can make maximum use
             * of 8 bit simd instructions.
             */
            do
            {
                mask = vp8_filter_mask(limit[0], blimit[0], s[-4 * p], s[-3 * p], s[-2 * p],
                                       s[-1 * p], s[0 * p], s[1 * p], s[2 * p], s[3 * p]);

                hev = vp8_hevmask(thresh[0], s[-2 * p], s[-1 * p], s[0 * p], s[1 * p]);

                vp8_filter(mask, (uc)hev, s - 2 * p, s - 1 * p, s, s + 1 * p);

                ++s;
            } while (++i < count * 8);
        }

        static void loop_filter_vertical_edge_c(byte* s, int p,
                                                in byte* blimit,
                                                in byte* limit,
                                                in byte* thresh,
                                                int count)
        {
            int hev = 0; /* high edge variance */
            sbyte mask = 0;
            int i = 0;

            /* loop filter designed to work using chars so that we can make maximum use
             * of 8 bit simd instructions.
             */
            do
            {
                mask = vp8_filter_mask(limit[0], blimit[0], s[-4], s[-3], s[-2], s[-1],
                                       s[0], s[1], s[2], s[3]);

                hev = vp8_hevmask(thresh[0], s[-2], s[-1], s[0], s[1]);

                vp8_filter(mask, (uc)hev, s - 2, s - 1, s, s + 1);

                s += p;
            } while (++i < count * 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void vp8_mbfilter(sbyte mask, uc hev, uc* op2, uc* op1, uc* op0,
                         uc* oq0, uc* oq1, uc* oq2)
        {
            // Port AC: Arithmetic overflow.
            //checked
            //{
            int s, u;
            int filter_value, Filter1, Filter2;
            int ps2 = *(sbyte*)op2 ^ -128;
            int ps1 = *(sbyte*)op1 ^ -128;
            int ps0 = *(sbyte*)op0 ^ -128;
            int qs0 = *(sbyte*)oq0 ^ -128;
            int qs1 = *(sbyte*)oq1 ^ -128;
            int qs2 = *(sbyte*)oq2 ^ -128;

            /* add outer taps if we have high edge variance */
            filter_value = vp8_signed_char_clamp(ps1 - qs1);
            filter_value = vp8_signed_char_clamp(filter_value + 3 * (qs0 - ps0));
            filter_value &= mask;

            Filter2 = filter_value;
            Filter2 &= (sbyte)hev;

            /* save bottom 3 bits so that we round one side +4 and the other +3 */
            Filter1 = vp8_signed_char_clamp(Filter2 + 4);
            Filter2 = vp8_signed_char_clamp(Filter2 + 3);
            Filter1 >>= 3;
            Filter2 >>= 3;
            qs0 = vp8_signed_char_clamp(qs0 - Filter1);
            ps0 = vp8_signed_char_clamp(ps0 + Filter2);

            /* only apply wider filter if not high edge variance */
            filter_value &= (sbyte)~hev;
            Filter2 = filter_value;

            /* roughly 3/7th difference across boundary */
            u = vp8_signed_char_clamp((63 + Filter2 * 27) >> 7);
            s = vp8_signed_char_clamp(qs0 - u);
            *oq0 = (byte)(s ^ 0x80);
            s = vp8_signed_char_clamp(ps0 + u);
            *op0 = (byte)(s ^ 0x80);

            /* roughly 2/7th difference across boundary */
            u = vp8_signed_char_clamp((63 + Filter2 * 18) >> 7);
            s = vp8_signed_char_clamp(qs1 - u);
            *oq1 = (byte)(s ^ 0x80);
            s = vp8_signed_char_clamp(ps1 + u);
            *op1 = (byte)(s ^ 0x80);

            /* roughly 1/7th difference across boundary */
            u = vp8_signed_char_clamp((63 + Filter2 * 9) >> 7);
            s = vp8_signed_char_clamp(qs2 - u);
            *oq2 = (byte)(s ^ 0x80);
            s = vp8_signed_char_clamp(ps2 + u);
            *op2 = (byte)(s ^ 0x80);
            //}
        }

        static void mbloop_filter_horizontal_edge_c(byte* s, int p,
                                            in byte* blimit,
                                            in byte* limit,
                                            in byte* thresh,
                                            int count)
        {
            sbyte hev = 0; /* high edge variance */
            sbyte mask = 0;
            int i = 0;

            /* loop filter designed to work using chars so that we can make maximum use
             * of 8 bit simd instructions.
             */
            do
            {
                mask = vp8_filter_mask(limit[0], blimit[0], s[-4 * p], s[-3 * p], s[-2 * p],
                                       s[-1 * p], s[0 * p], s[1 * p], s[2 * p], s[3 * p]);

                hev = vp8_hevmask(thresh[0], s[-2 * p], s[-1 * p], s[0 * p], s[1 * p]);

                vp8_mbfilter(mask, (uc)hev, s - 3 * p, s - 2 * p, s - 1 * p, s, s + 1 * p,
                             s + 2 * p);

                ++s;
            } while (++i < count * 8);
        }

        public static void vp8_loop_filter_simple_horizontal_edge_c(byte* y_ptr,
                                              int y_stride,
                                              in byte* blimit)
        {
            sbyte mask = 0;
            int i = 0;

            do
            {
                mask = vp8_simple_filter_mask(blimit[0], y_ptr[-2 * y_stride],
                                              y_ptr[-1 * y_stride], y_ptr[0 * y_stride],
                                              y_ptr[1 * y_stride]);
                vp8_simple_filter(mask, y_ptr - 2 * y_stride, y_ptr - 1 * y_stride, y_ptr,
                                  y_ptr + 1 * y_stride);
                ++y_ptr;
            } while (++i < 16);
        }

        public static void vp8_loop_filter_simple_vertical_edge_c(byte* y_ptr, int y_stride,
                                                    in byte* blimit)
        {
            sbyte mask = 0;
            int i = 0;

            do
            {
                mask = vp8_simple_filter_mask(blimit[0], y_ptr[-2], y_ptr[-1], y_ptr[0],
                                              y_ptr[1]);
                vp8_simple_filter(mask, y_ptr - 2, y_ptr - 1, y_ptr, y_ptr + 1);
                y_ptr += y_stride;
            } while (++i < 16);
        }

        public static void mbloop_filter_vertical_edge_c(byte* s, int p,
                                                          byte* blimit,
                                                          byte* limit,
                                                          byte* thresh,
                                                          int count)
        {
            sbyte hev = 0; /* high edge variance */
            sbyte mask = 0;
            int i = 0;

            do
            {
                mask = vp8_filter_mask(limit[0], blimit[0], s[-4], s[-3], s[-2], s[-1],
                                       s[0], s[1], s[2], s[3]);

                hev = vp8_hevmask(thresh[0], s[-2], s[-1], s[0], s[1]);

                vp8_mbfilter(mask, (uc)hev, s - 3, s - 2, s - 1, s, s + 1, s + 2);

                s += p;
            } while (++i < count * 8);
        }

        /* should we apply any filter at all ( 11111111 yes, 00000000 no) */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static sbyte vp8_simple_filter_mask(uc blimit, uc p1, uc p0, uc q0, uc q1)
        {
            /* Why does this cause problems for win32?
             * error C2143: syntax error : missing ';' before 'type'
             *  (void) limit;
             */
            if (fast_abs(p0 - q0) * 2 + fast_abs(p1 - q1) / 2 <= blimit)
            {
                return -1;
            }
            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void vp8_simple_filter(sbyte mask, uc* op1, uc* op0, uc* oq0, uc* oq1)
        {
            int filter_value, Filter1, Filter2;
            int p1 = *(sbyte*)op1 ^ -128;
            int p0 = *(sbyte*)op0 ^ -128;
            int q0 = *(sbyte*)oq0 ^ -128;
            int q1 = *(sbyte*)oq1 ^ -128;
            int u;

            filter_value = vp8_signed_char_clamp(p1 - q1);
            filter_value = vp8_signed_char_clamp(filter_value + 3 * (q0 - p0));
            filter_value &= mask;

            /* save bottom 3 bits so that we round one side +4 and the other +3 */
            Filter1 = vp8_signed_char_clamp(filter_value + 4);
            Filter1 >>= 3;
            u = vp8_signed_char_clamp(q0 - Filter1);
            *oq0 = (byte)(u ^ 0x80);

            Filter2 = vp8_signed_char_clamp(filter_value + 3);
            Filter2 >>= 3;
            u = vp8_signed_char_clamp(p0 + Filter2);
            *op0 = (byte)(u ^ 0x80);
        }

        /* Horizontal MB filtering */
        public static void vp8_loop_filter_mbh_c(byte* y_ptr, byte* u_ptr,
                                   byte* v_ptr, int y_stride, int uv_stride,
                                   loop_filter_info lfi)
        {
            mbloop_filter_horizontal_edge_c(y_ptr, y_stride, lfi.mblim, lfi.lim, lfi.hev_thr, 2);

            if (u_ptr != null)
            {
                mbloop_filter_horizontal_edge_c(u_ptr, uv_stride, lfi.mblim, lfi.lim, lfi.hev_thr, 1);
            }

            if (v_ptr != null)
            {
                mbloop_filter_horizontal_edge_c(v_ptr, uv_stride, lfi.mblim, lfi.lim, lfi.hev_thr, 1);
            }
        }

        /* Horizontal B Filtering */
        public static void vp8_loop_filter_bh_c(byte* y_ptr, byte* u_ptr,
                                  byte* v_ptr, int y_stride, int uv_stride,
                                  loop_filter_info lfi)
        {
            loop_filter_horizontal_edge_c(y_ptr + 4 * y_stride, y_stride, lfi.blim,
                                          lfi.lim, lfi.hev_thr, 2);
            loop_filter_horizontal_edge_c(y_ptr + 8 * y_stride, y_stride, lfi.blim,
                                          lfi.lim, lfi.hev_thr, 2);
            loop_filter_horizontal_edge_c(y_ptr + 12 * y_stride, y_stride, lfi.blim,
                                          lfi.lim, lfi.hev_thr, 2);

            if (u_ptr != null)
            {
                loop_filter_horizontal_edge_c(u_ptr + 4 * uv_stride, uv_stride, lfi.blim,
                                              lfi.lim, lfi.hev_thr, 1);
            }

            if (v_ptr != null)
            {
                loop_filter_horizontal_edge_c(v_ptr + 4 * uv_stride, uv_stride, lfi.blim,
                                              lfi.lim, lfi.hev_thr, 1);
            }
        }

        /* Vertical MB Filtering */
        public static void vp8_loop_filter_mbv_c(byte* y_ptr, byte* u_ptr,
                                       byte* v_ptr, int y_stride, int uv_stride,
                                       loop_filter_info lfi)
        {
            mbloop_filter_vertical_edge_c(y_ptr, y_stride, lfi.mblim, lfi.lim, lfi.hev_thr, 2);

            if (u_ptr != null)
            {
                mbloop_filter_vertical_edge_c(u_ptr, uv_stride, lfi.mblim, lfi.lim, lfi.hev_thr, 1);
            }

            if (v_ptr != null)
            {
                mbloop_filter_vertical_edge_c(v_ptr, uv_stride, lfi.mblim, lfi.lim, lfi.hev_thr, 1);
            }
        }

        public static void vp8_loop_filter_bhs_c(byte* y_ptr, int y_stride, in byte* blimit)
        {
            vp8_loop_filter_simple_horizontal_edge_c(y_ptr + 4 * y_stride, y_stride,
                                                     blimit);
            vp8_loop_filter_simple_horizontal_edge_c(y_ptr + 8 * y_stride, y_stride,
                                                     blimit);
            vp8_loop_filter_simple_horizontal_edge_c(y_ptr + 12 * y_stride, y_stride,
                                                     blimit);
        }

        /* Vertical B Filtering */
        public static void vp8_loop_filter_bv_c(byte* y_ptr, byte* u_ptr,
                                      byte* v_ptr, int y_stride, int uv_stride,
                                      loop_filter_info lfi)
        {
            loop_filter_vertical_edge_c(y_ptr + 4, y_stride, lfi.blim, lfi.lim,
                                        lfi.hev_thr, 2);
            loop_filter_vertical_edge_c(y_ptr + 8, y_stride, lfi.blim, lfi.lim,
                                        lfi.hev_thr, 2);
            loop_filter_vertical_edge_c(y_ptr + 12, y_stride, lfi.blim, lfi.lim,
                                        lfi.hev_thr, 2);

            if (u_ptr != null)
            {
                loop_filter_vertical_edge_c(u_ptr + 4, uv_stride, lfi.blim, lfi.lim,
                                            lfi.hev_thr, 1);
            }

            if (v_ptr != null)
            {
                loop_filter_vertical_edge_c(v_ptr + 4, uv_stride, lfi.blim, lfi.lim,
                                            lfi.hev_thr, 1);
            }
        }

        public static void vp8_loop_filter_bvs_c(byte* y_ptr, int y_stride, in byte* blimit)
        {
            vp8_loop_filter_simple_vertical_edge_c(y_ptr + 4, y_stride, blimit);
            vp8_loop_filter_simple_vertical_edge_c(y_ptr + 8, y_stride, blimit);
            vp8_loop_filter_simple_vertical_edge_c(y_ptr + 12, y_stride, blimit);
        }
    }
}
