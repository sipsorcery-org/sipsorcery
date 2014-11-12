/*
 *  Copyright (c) 2013 The WebM project authors. All Rights Reserved.
 *
 *  Use of this source code is governed by a BSD-style license
 *  that can be found in the LICENSE file in the root of the source
 *  tree. An additional intellectual property rights grant can be found
 *  in the file PATENTS.  All contributing project authors may
 *  be found in the AUTHORS file in the root of the source tree.
 */
#ifndef WEBMENC_H_
#define WEBMENC_H_

#include <stdio.h>
#include <stdlib.h>

#if defined(_MSC_VER)
/* MSVS doesn't define off_t */
typedef __int64 off_t;
#else
#include <stdint.h>
#endif

#include "tools_common.h"
#include "vpx/vpx_encoder.h"

typedef off_t EbmlLoc;

struct cue_entry {
  unsigned int time;
  uint64_t loc;
};

struct EbmlGlobal {
  int debug;

  FILE *stream;
  int64_t last_pts_ms;
  vpx_rational_t framerate;

  /* These pointers are to the start of an element */
  off_t position_reference;
  off_t seek_info_pos;
  off_t segment_info_pos;
  off_t track_pos;
  off_t cue_pos;
  off_t cluster_pos;

  /* This pointer is to a specific element to be serialized */
  off_t track_id_pos;

  /* These pointers are to the size field of the element */
  EbmlLoc startSegment;
  EbmlLoc startCluster;

  uint32_t cluster_timecode;
  int cluster_open;

  struct cue_entry *cue_list;
  unsigned int cues;
};

/* Stereo 3D packed frame format */
typedef enum stereo_format {
  STEREO_FORMAT_MONO = 0,
  STEREO_FORMAT_LEFT_RIGHT = 1,
  STEREO_FORMAT_BOTTOM_TOP = 2,
  STEREO_FORMAT_TOP_BOTTOM = 3,
  STEREO_FORMAT_RIGHT_LEFT = 11
} stereo_format_t;

void write_webm_seek_element(struct EbmlGlobal *ebml,
                             unsigned int id,
                             off_t pos);

void write_webm_file_header(struct EbmlGlobal *glob,
                            const vpx_codec_enc_cfg_t *cfg,
                            const struct vpx_rational *fps,
                            stereo_format_t stereo_fmt,
                            unsigned int fourcc);

void write_webm_block(struct EbmlGlobal *glob,
                      const vpx_codec_enc_cfg_t *cfg,
                      const vpx_codec_cx_pkt_t *pkt);

void write_webm_file_footer(struct EbmlGlobal *glob, int hash);

#endif  // WEBMENC_H_
