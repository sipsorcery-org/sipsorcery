/*
 *  Copyright (c) 2013 The WebM project authors. All Rights Reserved.
 *
 *  Use of this source code is governed by a BSD-style license
 *  that can be found in the LICENSE file in the root of the source
 *  tree. An additional intellectual property rights grant can be found
 *  in the file PATENTS.  All contributing project authors may
 *  be found in the AUTHORS file in the root of the source tree.
 */
#include "webmenc.h"

#include <limits.h>
#include <string.h>

#include "third_party/libmkv/EbmlWriter.h"
#include "third_party/libmkv/EbmlIDs.h"

void Ebml_Write(struct EbmlGlobal *glob,
                const void *buffer_in,
                unsigned long len) {
  (void) fwrite(buffer_in, 1, len, glob->stream);
}

#define WRITE_BUFFER(s) \
for (i = len - 1; i >= 0; i--) { \
  x = (char)(*(const s *)buffer_in >> (i * CHAR_BIT)); \
  Ebml_Write(glob, &x, 1); \
}

void Ebml_Serialize(struct EbmlGlobal *glob,
                    const void *buffer_in,
                    int buffer_size,
                    unsigned long len) {
  char x;
  int i;

  /* buffer_size:
   * 1 - int8_t;
   * 2 - int16_t;
   * 3 - int32_t;
   * 4 - int64_t;
   */
  switch (buffer_size) {
    case 1:
      WRITE_BUFFER(int8_t)
      break;
    case 2:
      WRITE_BUFFER(int16_t)
      break;
    case 4:
      WRITE_BUFFER(int32_t)
      break;
    case 8:
      WRITE_BUFFER(int64_t)
      break;
    default:
      break;
  }
}
#undef WRITE_BUFFER

/* Need a fixed size serializer for the track ID. libmkv provides a 64 bit
 * one, but not a 32 bit one.
 */
static void Ebml_SerializeUnsigned32(struct EbmlGlobal *glob,
                                     unsigned int class_id,
                                     uint64_t ui) {
  const unsigned char sizeSerialized = 4 | 0x80;
  Ebml_WriteID(glob, class_id);
  Ebml_Serialize(glob, &sizeSerialized, sizeof(sizeSerialized), 1);
  Ebml_Serialize(glob, &ui, sizeof(ui), 4);
}

static void Ebml_StartSubElement(struct EbmlGlobal *glob,
                                 EbmlLoc *ebmlLoc,
                                 unsigned int class_id) {
  const uint64_t kEbmlUnknownLength = LITERALU64(0x01FFFFFF, 0xFFFFFFFF);
  Ebml_WriteID(glob, class_id);
  *ebmlLoc = ftello(glob->stream);
  Ebml_Serialize(glob, &kEbmlUnknownLength, sizeof(kEbmlUnknownLength), 8);
}

static void Ebml_EndSubElement(struct EbmlGlobal *glob, EbmlLoc *ebmlLoc) {
  off_t pos;
  uint64_t size;

  /* Save the current stream pointer. */
  pos = ftello(glob->stream);

  /* Calculate the size of this element. */
  size = pos - *ebmlLoc - 8;
  size |= LITERALU64(0x01000000, 0x00000000);

  /* Seek back to the beginning of the element and write the new size. */
  fseeko(glob->stream, *ebmlLoc, SEEK_SET);
  Ebml_Serialize(glob, &size, sizeof(size), 8);

  /* Reset the stream pointer. */
  fseeko(glob->stream, pos, SEEK_SET);
}

void write_webm_seek_element(struct EbmlGlobal *ebml,
                             unsigned int id,
                             off_t pos) {
  uint64_t offset = pos - ebml->position_reference;
  EbmlLoc start;
  Ebml_StartSubElement(ebml, &start, Seek);
  Ebml_SerializeBinary(ebml, SeekID, id);
  Ebml_SerializeUnsigned64(ebml, SeekPosition, offset);
  Ebml_EndSubElement(ebml, &start);
}

void write_webm_seek_info(struct EbmlGlobal *ebml) {
  off_t pos;
  EbmlLoc start;
  EbmlLoc startInfo;
  uint64_t frame_time;
  char version_string[64];

  /* Save the current stream pointer. */
  pos = ftello(ebml->stream);

  if (ebml->seek_info_pos)
    fseeko(ebml->stream, ebml->seek_info_pos, SEEK_SET);
  else
    ebml->seek_info_pos = pos;

  Ebml_StartSubElement(ebml, &start, SeekHead);
  write_webm_seek_element(ebml, Tracks, ebml->track_pos);
  write_webm_seek_element(ebml, Cues, ebml->cue_pos);
  write_webm_seek_element(ebml, Info, ebml->segment_info_pos);
  Ebml_EndSubElement(ebml, &start);

  /* Create and write the Segment Info. */
  if (ebml->debug) {
    strcpy(version_string, "vpxenc");
  } else {
    strcpy(version_string, "vpxenc ");
    strncat(version_string,
            vpx_codec_version_str(),
            sizeof(version_string) - 1 - strlen(version_string));
  }

  frame_time = (uint64_t)1000 * ebml->framerate.den
               / ebml->framerate.num;
  ebml->segment_info_pos = ftello(ebml->stream);
  Ebml_StartSubElement(ebml, &startInfo, Info);
  Ebml_SerializeUnsigned(ebml, TimecodeScale, 1000000);
  Ebml_SerializeFloat(ebml, Segment_Duration,
                      (double)(ebml->last_pts_ms + frame_time));
  Ebml_SerializeString(ebml, 0x4D80, version_string);
  Ebml_SerializeString(ebml, 0x5741, version_string);
  Ebml_EndSubElement(ebml, &startInfo);
}

void write_webm_file_header(struct EbmlGlobal *glob,
                            const vpx_codec_enc_cfg_t *cfg,
                            const struct vpx_rational *fps,
                            stereo_format_t stereo_fmt,
                            unsigned int fourcc) {
  EbmlLoc start;
  EbmlLoc trackStart;
  EbmlLoc videoStart;
  unsigned int trackNumber = 1;
  uint64_t trackID = 0;
  unsigned int pixelWidth = cfg->g_w;
  unsigned int pixelHeight = cfg->g_h;

  /* Write the EBML header. */
  Ebml_StartSubElement(glob, &start, EBML);
  Ebml_SerializeUnsigned(glob, EBMLVersion, 1);
  Ebml_SerializeUnsigned(glob, EBMLReadVersion, 1);
  Ebml_SerializeUnsigned(glob, EBMLMaxIDLength, 4);
  Ebml_SerializeUnsigned(glob, EBMLMaxSizeLength, 8);
  Ebml_SerializeString(glob, DocType, "webm");
  Ebml_SerializeUnsigned(glob, DocTypeVersion, 2);
  Ebml_SerializeUnsigned(glob, DocTypeReadVersion, 2);
  Ebml_EndSubElement(glob, &start);

  /* Open and begin writing the segment element. */
  Ebml_StartSubElement(glob, &glob->startSegment, Segment);
  glob->position_reference = ftello(glob->stream);
  glob->framerate = *fps;
  write_webm_seek_info(glob);

  /* Open and write the Tracks element. */
  glob->track_pos = ftello(glob->stream);
  Ebml_StartSubElement(glob, &trackStart, Tracks);

  /* Open and write the Track entry. */
  Ebml_StartSubElement(glob, &start, TrackEntry);
  Ebml_SerializeUnsigned(glob, TrackNumber, trackNumber);
  glob->track_id_pos = ftello(glob->stream);
  Ebml_SerializeUnsigned32(glob, TrackUID, trackID);
  Ebml_SerializeUnsigned(glob, TrackType, 1);
  Ebml_SerializeString(glob, CodecID,
                       fourcc == VP8_FOURCC ? "V_VP8" : "V_VP9");
  Ebml_StartSubElement(glob, &videoStart, Video);
  Ebml_SerializeUnsigned(glob, PixelWidth, pixelWidth);
  Ebml_SerializeUnsigned(glob, PixelHeight, pixelHeight);
  Ebml_SerializeUnsigned(glob, StereoMode, stereo_fmt);
  Ebml_EndSubElement(glob, &videoStart);

  /* Close Track entry. */
  Ebml_EndSubElement(glob, &start);

  /* Close Tracks element. */
  Ebml_EndSubElement(glob, &trackStart);

  /* Segment element remains open. */
}

void write_webm_block(struct EbmlGlobal *glob,
                      const vpx_codec_enc_cfg_t *cfg,
                      const vpx_codec_cx_pkt_t *pkt) {
  unsigned int block_length;
  unsigned char track_number;
  uint16_t block_timecode = 0;
  unsigned char flags;
  int64_t pts_ms;
  int start_cluster = 0, is_keyframe;

  /* Calculate the PTS of this frame in milliseconds. */
  pts_ms = pkt->data.frame.pts * 1000
           * (uint64_t)cfg->g_timebase.num / (uint64_t)cfg->g_timebase.den;

  if (pts_ms <= glob->last_pts_ms)
    pts_ms = glob->last_pts_ms + 1;

  glob->last_pts_ms = pts_ms;

  /* Calculate the relative time of this block. */
  if (pts_ms - glob->cluster_timecode > SHRT_MAX)
    start_cluster = 1;
  else
    block_timecode = (uint16_t)pts_ms - glob->cluster_timecode;

  is_keyframe = (pkt->data.frame.flags & VPX_FRAME_IS_KEY);
  if (start_cluster || is_keyframe) {
    if (glob->cluster_open)
      Ebml_EndSubElement(glob, &glob->startCluster);

    /* Open the new cluster. */
    block_timecode = 0;
    glob->cluster_open = 1;
    glob->cluster_timecode = (uint32_t)pts_ms;
    glob->cluster_pos = ftello(glob->stream);
    Ebml_StartSubElement(glob, &glob->startCluster, Cluster);
    Ebml_SerializeUnsigned(glob, Timecode, glob->cluster_timecode);

    /* Save a cue point if this is a keyframe. */
    if (is_keyframe) {
      struct cue_entry *cue, *new_cue_list;

      new_cue_list = realloc(glob->cue_list,
                             (glob->cues + 1) * sizeof(struct cue_entry));
      if (new_cue_list)
        glob->cue_list = new_cue_list;
      else
        fatal("Failed to realloc cue list.");

      cue = &glob->cue_list[glob->cues];
      cue->time = glob->cluster_timecode;
      cue->loc = glob->cluster_pos;
      glob->cues++;
    }
  }

  /* Write the Simple Block. */
  Ebml_WriteID(glob, SimpleBlock);

  block_length = (unsigned int)pkt->data.frame.sz + 4;
  block_length |= 0x10000000;
  Ebml_Serialize(glob, &block_length, sizeof(block_length), 4);

  track_number = 1;
  track_number |= 0x80;
  Ebml_Write(glob, &track_number, 1);

  Ebml_Serialize(glob, &block_timecode, sizeof(block_timecode), 2);

  flags = 0;
  if (is_keyframe)
    flags |= 0x80;
  if (pkt->data.frame.flags & VPX_FRAME_IS_INVISIBLE)
    flags |= 0x08;
  Ebml_Write(glob, &flags, 1);

  Ebml_Write(glob, pkt->data.frame.buf, (unsigned int)pkt->data.frame.sz);
}

void write_webm_file_footer(struct EbmlGlobal *glob, int hash) {
  EbmlLoc start_cues;
  EbmlLoc start_cue_point;
  EbmlLoc start_cue_tracks;
  unsigned int i;

  if (glob->cluster_open)
    Ebml_EndSubElement(glob, &glob->startCluster);

  glob->cue_pos = ftello(glob->stream);
  Ebml_StartSubElement(glob, &start_cues, Cues);

  for (i = 0; i < glob->cues; i++) {
    struct cue_entry *cue = &glob->cue_list[i];
    Ebml_StartSubElement(glob, &start_cue_point, CuePoint);
    Ebml_SerializeUnsigned(glob, CueTime, cue->time);

    Ebml_StartSubElement(glob, &start_cue_tracks, CueTrackPositions);
    Ebml_SerializeUnsigned(glob, CueTrack, 1);
    Ebml_SerializeUnsigned64(glob, CueClusterPosition,
                             cue->loc - glob->position_reference);
    Ebml_EndSubElement(glob, &start_cue_tracks);

    Ebml_EndSubElement(glob, &start_cue_point);
  }

  Ebml_EndSubElement(glob, &start_cues);

  /* Close the Segment. */
  Ebml_EndSubElement(glob, &glob->startSegment);

  /* Patch up the seek info block. */
  write_webm_seek_info(glob);

  /* Patch up the track id. */
  fseeko(glob->stream, glob->track_id_pos, SEEK_SET);
  Ebml_SerializeUnsigned32(glob, TrackUID, glob->debug ? 0xDEADBEEF : hash);

  fseeko(glob->stream, 0, SEEK_END);
}
