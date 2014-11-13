#pragma once

#include "vpx/vpx_encoder.h"

long InitMFStreamer();
long GetSampleFromMFStreamer(/* out */ const vpx_codec_cx_pkt_t *& pkt);