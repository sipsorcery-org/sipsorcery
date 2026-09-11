#ifndef DEBUG_PROBE_H
#define DEBUG_PROBE_H

#include "../vp8/common/blockd.h"

#ifdef __cplusplus
extern "C" {
#endif

  // vpx_calloc((oci->mb_cols + 1) * (oci->mb_rows + 1), sizeof(MODE_INFO));
  void dump_motion_vectors(MODE_INFO* mib, int macroBlockCols, int macroBlockRows);

  void dump_macro_block(MACROBLOCKD * xd, int mb_idx);

  void dump_subblock_coefficients(MACROBLOCKD* xd);

  void dump_ysubblock(int i, uint8_t* dst, int dst_stride);

  void dump_above_and_left(uint8_t* above, uint8_t* left);

  const char * toHex(unsigned char* in, size_t insz);

  //void tohexC(unsigned char* in, size_t insz, char* out, size_t outsz)
  //{
  //  unsigned char* pin = in;
  //  const char* hex = "0123456789ABCDEF";
  //  char* pout = out;
  //  for (; pin < in + insz; pout += 2, pin++) {
  //    pout[0] = hex[(*pin >> 4) & 0xF];
  //    pout[1] = hex[*pin & 0xF];
  //    if (pout + 2 - out > outsz) {
  //      /* Better to truncate output string than overflow buffer */
  //      /* it would be still better to either return a status */
  //      /* or ensure the target buffer is large enough and it never happen */
  //      break;
  //    }
  //  }
  //  pout[-1] = 0;
  //}

#ifdef __cplusplus
}  // extern "C"
#endif

#endif