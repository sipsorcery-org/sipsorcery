/* Copyright (c) 2011 The WebM project authors. All Rights Reserved. */
/*  */
/* Use of this source code is governed by a BSD-style license */
/* that can be found in the LICENSE file in the root of the source */
/* tree. An additional intellectual property rights grant can be found */
/* in the file PATENTS.  All contributing project authors may */
/* be found in the AUTHORS file in the root of the source tree. */
#include "vpx/vpx_codec.h"
static const char* const cfg = "--disable-static --disable-examples --disable-unit-tests --disable-tools --disable-docs --disable-multithread --disable-spatial-resampling --disable-temporal-denoising --disable-vp9 --disable-optimizations --target=x86_64-win64-vs16 --disable-mmx --disable-webm-io --disable-libyuv --disable-postproc --disable-runtime-cpu-detect --disable-dependency-tracking --disable-decode-perf-tests --disable-encode-perf-tests --disable-better-hw-compatibility --disable-runtime-cpu-detect";
const char *vpx_codec_build_config(void) {return cfg;}
