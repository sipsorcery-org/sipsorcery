// See stream_info.h. Syntax references are to ITU-T H.264 section 7.3.

#include "stream_info.h"

#include <algorithm>
#include <map>
#include <sstream>
#include <stdexcept>

namespace {

struct ParseError : std::runtime_error {
  using std::runtime_error::runtime_error;
};

// RBSP bit reader (emulation prevention bytes already removed).
class BitReader {
 public:
  explicit BitReader (std::vector<uint8_t> rbsp) : d_ (std::move (rbsp)) {
    // Position of the rbsp_stop_one_bit, for more_rbsp_data().
    lastOne_ = -1;
    for (int i = (int)d_.size() - 1; i >= 0 && lastOne_ < 0; --i) {
      if (d_[i] != 0) {
        for (int b = 0; b < 8; ++b) {
          if (d_[i] & (1 << b)) {
            lastOne_ = i * 8 + (7 - b);
            break;
          }
        }
      }
    }
  }
  uint32_t u (int n) {
    uint32_t v = 0;
    for (int i = 0; i < n; ++i) v = (v << 1) | bit();
    return v;
  }
  uint32_t ue() {
    int zeros = 0;
    while (bit() == 0) {
      if (++zeros > 31) throw ParseError ("bad exp-golomb code");
    }
    return ((1u << zeros) - 1) + u (zeros);
  }
  int32_t se() {
    uint32_t k = ue();
    return (k & 1) ? (int32_t) ((k + 1) / 2) : -(int32_t) (k / 2);
  }
  bool moreRbspData() const { return pos_ < lastOne_; }

 private:
  uint32_t bit() {
    if (pos_ >= (int)d_.size() * 8) throw ParseError ("read past end of NAL");
    uint32_t b = (d_[pos_ >> 3] >> (7 - (pos_ & 7))) & 1;
    ++pos_;
    return b;
  }
  std::vector<uint8_t> d_;
  int pos_ = 0;
  int lastOne_ = -1;
};

struct Sps {
  int profile = 0, chromaFormat = 1, log2MaxFrameNum = 0, pocType = 0;
  int log2MaxPocLsb = 0;
  bool deltaPocAlwaysZero = false, frameMbsOnly = true, separateColourPlane = false;
};

struct Pps {
  int spsId = 0;
  bool bottomFieldPicOrderPresent = false, redundantPicCntPresent = false;
};

struct SliceKey {
  // The fields 7.4.1.2.4 uses to detect the first VCL NAL of a new picture.
  int frameNum = -1, ppsId = -1, nalRefIdcNonZero = -1, idr = -1, idrPicId = -1;
  int pocLsb = -1, deltaPocBottom = 0, deltaPoc0 = 0, deltaPoc1 = 0, field = 0, bottom = 0;
  bool operator!= (const SliceKey& o) const {
    return frameNum != o.frameNum || ppsId != o.ppsId || nalRefIdcNonZero != o.nalRefIdcNonZero ||
           idr != o.idr || idrPicId != o.idrPicId || pocLsb != o.pocLsb ||
           deltaPocBottom != o.deltaPocBottom || deltaPoc0 != o.deltaPoc0 ||
           deltaPoc1 != o.deltaPoc1 || field != o.field || bottom != o.bottom;
  }
};

bool IsHighProfile (int p) {
  switch (p) {
  case 100: case 110: case 122: case 244: case 44: case 83: case 86:
  case 118: case 128: case 138: case 139: case 134: case 135:
    return true;
  default:
    return false;
  }
}

bool ScalingList (BitReader& br, int size) {
  int last = 8, next = 8;
  for (int j = 0; j < size; ++j) {
    if (next != 0) {
      int delta = br.se();
      next = (last + delta + 256) % 256;
    }
    last = (next == 0) ? last : next;
  }
  return true;
}

std::vector<uint8_t> ToRbsp (const uint8_t* p, size_t n) {
  std::vector<uint8_t> out;
  out.reserve (n);
  int zeros = 0;
  for (size_t i = 0; i < n; ++i) {
    if (zeros >= 2 && p[i] == 3) {
      zeros = 0;
      continue;
    }
    out.push_back (p[i]);
    zeros = (p[i] == 0) ? zeros + 1 : 0;
  }
  return out;
}

// Splits an Annex B stream into NAL units (without start codes).
std::vector<std::pair<size_t, size_t>> SplitNals (const std::vector<uint8_t>& s) {
  std::vector<std::pair<size_t, size_t>> nals;
  size_t i = 0, n = s.size(), start = SIZE_MAX;
  while (i + 2 < n) {
    if (s[i] == 0 && s[i + 1] == 0 && s[i + 2] == 1) {
      if (start != SIZE_MAX) {
        size_t end = i;
        while (end > start && s[end - 1] == 0) --end;  // trailing_zero_8bits / 4-byte code
        nals.push_back ({start, end - start});
      }
      i += 3;
      start = i;
    } else {
      ++i;
    }
  }
  if (start != SIZE_MAX && start < n) nals.push_back ({start, n - start});
  return nals;
}

}  // namespace

StreamInfo AnalyseStream (const std::vector<uint8_t>& stream) {
  StreamInfo info;
  std::map<int, Sps> spss;
  std::map<int, Pps> ppss;
  SliceKey prevKey;
  bool havePic = false;
  int lastFirstMb = -1, slicesInPic = 0;

  try {
    for (auto [off, len] : SplitNals (stream)) {
      if (len < 1) continue;
      const uint8_t hdr = stream[off];
      const int nalRefIdc = (hdr >> 5) & 3;
      const int type = hdr & 0x1f;
      if (type >= 2 && type <= 4) info.dataPartitioning = true;
      if (type == 14 || type == 15 || type == 20) info.svcExtension = true;
      if (type != 1 && type != 5 && type != 7 && type != 8) continue;

      BitReader br (ToRbsp (&stream[off + 1], len - 1));

      if (type == 7) {
        Sps sps;
        sps.profile = (int)br.u (8);
        int cs = (int)br.u (8);
        int level = (int)br.u (8);
        int id = (int)br.ue();
        if (IsHighProfile (sps.profile)) {
          sps.chromaFormat = (int)br.ue();
          if (sps.chromaFormat == 3) sps.separateColourPlane = br.u (1);
          int bdl = (int)br.ue(), bdc = (int)br.ue();
          if (bdl || bdc || sps.chromaFormat != 1) info.highBitDepthOrChroma = true;
          br.u (1);  // qpprime_y_zero_transform_bypass_flag
          if (br.u (1)) {
            info.scalingMatrix = true;
            int lists = (sps.chromaFormat != 3) ? 8 : 12;
            for (int i = 0; i < lists; ++i)
              if (br.u (1)) ScalingList (br, i < 6 ? 16 : 64);
          }
        }
        sps.log2MaxFrameNum = (int)br.ue() + 4;
        sps.pocType = (int)br.ue();
        if (sps.pocType == 0) {
          sps.log2MaxPocLsb = (int)br.ue() + 4;
        } else if (sps.pocType == 1) {
          sps.deltaPocAlwaysZero = br.u (1);
          br.se();
          br.se();
          int cycle = (int)br.ue();
          for (int i = 0; i < cycle; ++i) br.se();
        }
        int maxRefs = (int)br.ue();
        if (maxRefs > 1) info.multiRef = true;
        br.u (1);  // gaps_in_frame_num_value_allowed_flag
        int wMbs = (int)br.ue() + 1, hMapUnits = (int)br.ue() + 1;
        sps.frameMbsOnly = br.u (1);
        if (!sps.frameMbsOnly) info.interlaced = true;
        if (info.width == 0) {
          info.width = wMbs * 16;
          info.height = hMapUnits * 16 * (sps.frameMbsOnly ? 1 : 2);
        }
        spss[id] = sps;
        info.profiles.push_back (sps.profile);
        info.constraintFlags.push_back (cs);
        info.levels.push_back (level);
      } else if (type == 8) {
        Pps pps;
        int id = (int)br.ue();
        pps.spsId = (int)br.ue();
        if (br.u (1)) info.cabac = true;
        pps.bottomFieldPicOrderPresent = br.u (1);
        int groups = (int)br.ue() + 1;
        if (groups > 1) {
          info.fmo = true;
          int mapType = (int)br.ue();
          if (mapType == 0) {
            for (int i = 0; i < groups; ++i) br.ue();
          } else if (mapType == 2) {
            for (int i = 0; i < groups - 1; ++i) { br.ue(); br.ue(); }
          } else if (mapType >= 3 && mapType <= 5) {
            br.u (1);
            br.ue();
          } else if (mapType == 6) {
            int units = (int)br.ue() + 1;
            int bits = 0;
            while ((1 << bits) < groups) ++bits;
            for (int i = 0; i < units; ++i) br.u (bits);
          }
        }
        br.ue();  // num_ref_idx_l0_default_active_minus1
        br.ue();  // num_ref_idx_l1_default_active_minus1
        if (br.u (1)) info.weightedPred = true;
        if (br.u (2)) info.weightedPred = true;
        br.se();
        br.se();
        br.se();
        br.u (1);  // deblocking_filter_control_present_flag
        if (br.u (1)) info.constrainedIntraPred = true;
        pps.redundantPicCntPresent = br.u (1);
        if (pps.redundantPicCntPresent) info.redundantFlag = true;
        if (br.moreRbspData()) {
          if (br.u (1)) info.transform8x8 = true;
          if (br.u (1)) info.scalingMatrix = true;
          // Scaling lists and second_chroma_qp_index_offset are not needed.
        }
        ppss[id] = pps;
      } else {  // slice, nal_unit_type 1 or 5
        ++info.vclNals;
        SliceKey key;
        int firstMb = (int)br.ue();
        int sliceType = (int)br.ue() % 5;
        key.ppsId = (int)br.ue();
        auto pit = ppss.find (key.ppsId);
        if (pit == ppss.end()) throw ParseError ("slice refers to unknown PPS");
        auto sit = spss.find (pit->second.spsId);
        if (sit == spss.end()) throw ParseError ("PPS refers to unknown SPS");
        const Sps& sps = sit->second;
        const Pps& pps = pit->second;

        switch (sliceType) {
        case 0: info.pSlices = true; break;
        case 1: info.bSlices = true; break;
        case 2: info.iSlices = true; break;
        default: info.spSiSlices = true; break;
        }

        if (sps.separateColourPlane) br.u (2);
        key.frameNum = (int)br.u (sps.log2MaxFrameNum);
        if (!sps.frameMbsOnly) {
          key.field = br.u (1);
          if (key.field) key.bottom = br.u (1);
        }
        key.idr = (type == 5);
        key.nalRefIdcNonZero = nalRefIdc != 0;
        if (type == 5) key.idrPicId = (int)br.ue();
        if (sps.pocType == 0) {
          key.pocLsb = (int)br.u (sps.log2MaxPocLsb);
          if (pps.bottomFieldPicOrderPresent && !key.field) key.deltaPocBottom = br.se();
        }
        if (sps.pocType == 1 && !sps.deltaPocAlwaysZero) {
          key.deltaPoc0 = br.se();
          if (pps.bottomFieldPicOrderPresent && !key.field) key.deltaPoc1 = br.se();
        }
        if (pps.redundantPicCntPresent && br.ue() > 0) info.redundantSlices = true;

        if (!havePic || key != prevKey) {
          havePic = true;
          ++info.pictures;
          slicesInPic = 1;
        } else {
          ++slicesInPic;
          info.multiSlice = true;
          if (firstMb <= lastFirstMb) info.aso = true;
        }
        lastFirstMb = firstMb;
        prevKey = key;
      }
    }
  } catch (const std::exception& e) {
    info.parseError = e.what();
  }
  return info;
}

std::string StreamInfo::Describe() const {
  std::ostringstream o;
  auto uniq = [] (std::vector<int> v) {
    std::sort (v.begin(), v.end());
    v.erase (std::unique (v.begin(), v.end()), v.end());
    return v;
  };
  o << "profile=";
  bool first = true;
  for (int p : uniq (profiles)) { o << (first ? "" : "/") << p; first = false; }
  o << " cs=";
  first = true;
  for (int c : uniq (constraintFlags)) {
    char b[8];
    snprintf (b, sizeof b, "%02x", c);
    o << (first ? "" : "/") << b;
    first = false;
  }
  o << " " << width << "x" << height << " pics=" << pictures << " slices=";
  if (iSlices) o << "I";
  if (pSlices) o << "P";
  if (bSlices) o << "B";
  if (spSiSlices) o << "S";
  std::vector<std::string> f;
  if (cabac) f.push_back ("cabac");
  if (fmo) f.push_back ("fmo");
  if (aso) f.push_back ("aso");
  if (redundantSlices) f.push_back ("redundant");
  if (weightedPred) f.push_back ("weighted");
  if (interlaced) f.push_back ("interlaced");
  if (transform8x8) f.push_back ("8x8");
  if (scalingMatrix) f.push_back ("scaling");
  if (highBitDepthOrChroma) f.push_back ("hbd/chroma");
  if (dataPartitioning) f.push_back ("dp");
  if (svcExtension) f.push_back ("svc");
  if (multiSlice) f.push_back ("+multislice");
  if (multiRef) f.push_back ("+multiref");
  if (constrainedIntraPred) f.push_back ("+cip");
  if (redundantFlag && !redundantSlices) f.push_back ("+redundant_flag");
  for (auto& s : f) o << " " << s;
  if (!parseError.empty()) o << " PARSE_ERROR(" << parseError << ")";
  return o.str();
}
