#pragma once

#include "vpx/vpx_encoder.h"

long InitMFStreamer();
//void StartMFStreamer();
long GetSampleFromMFStreamer2(/* out */ vpx_codec_cx_pkt_t *& pkt);
const vpx_codec_cx_pkt_t * GetSampleFromMFStreamer();