// Stream feature classification for the H.264 conformance vectors.
//
// A deliberately small, independent parser (not openh264's) that reads the
// SPS / PPS / slice headers of an Annex B stream and records which coding
// tools it actually uses. The port's scope is Constrained Baseline (the
// WebRTC realtime profile), so the question it answers is: can a Constrained
// Baseline decoder decode this vector?

#pragma once

#include <string>
#include <vector>
#include <cstdint>

struct StreamInfo {
  // Profiles / levels seen in SPSs (base layer, nal_unit_type 7).
  std::vector<int> profiles;
  std::vector<int> constraintFlags;  // the 8-bit constraint_set byte
  std::vector<int> levels;
  int width = 0, height = 0;          // from the first SPS, in pixels (uncropped)

  // Features that fall outside Constrained Baseline.
  bool cabac = false;                 // entropy_coding_mode_flag = 1 in a PPS
  bool fmo = false;                   // num_slice_groups_minus1 > 0
  bool aso = false;                   // slices of a picture out of MB order
  bool redundantSlices = false;       // redundant_pic_cnt > 0 in a slice
  bool redundantFlag = false;         // redundant_pic_cnt_present_flag = 1 in a PPS
  bool weightedPred = false;          // weighted_pred_flag / weighted_bipred_idc
  bool interlaced = false;            // frame_mbs_only_flag = 0
  bool transform8x8 = false;          // transform_8x8_mode_flag = 1
  bool scalingMatrix = false;         // SPS or PPS scaling matrices
  bool highBitDepthOrChroma = false;  // chroma_format_idc != 1 or bit depth > 8
  bool dataPartitioning = false;      // nal_unit_type 2..4
  bool svcExtension = false;          // nal_unit_type 14 / 15 / 20
  bool bSlices = false, spSiSlices = false;
  bool iSlices = false, pSlices = false;
  bool multiSlice = false;            // more than one slice in some picture
  bool constrainedIntraPred = false;  // allowed in CB, noted for coverage
  bool multiRef = false;              // max_num_ref_frames > 1
  bool pcm = false;                   // not detected here (needs MB parsing)

  int vclNals = 0;
  int pictures = 0;
  std::string parseError;

  bool IsConstrainedBaseline() const {
    return parseError.empty() && !cabac && !fmo && !aso && !redundantSlices &&
           !weightedPred && !interlaced && !transform8x8 && !scalingMatrix &&
           !highBitDepthOrChroma && !dataPartitioning && !svcExtension &&
           !bSlices && !spSiSlices;
  }

  std::string Describe() const;
};

StreamInfo AnalyseStream (const std::vector<uint8_t>& annexB);
