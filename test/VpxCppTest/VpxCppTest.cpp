#include "vp8cx.h"
#include "vp8dx.h"
#include "vpx_decoder.h"
#include "vpx_encoder.h"

#include <iostream>
#include <vector>

int main()
{
  std::cout << "libvpx test console\n";

  std::cout << "vp8 encoder version " << vpx_codec_version_str() << "." << std::endl;
  std::cout << "VPX_ENCODER_ABI_VERSION=" << VPX_ENCODER_ABI_VERSION << "." << std::endl;
  std::cout << "VPX_DECODER_ABI_VERSION=" << VPX_DECODER_ABI_VERSION << "." << std::endl;

  int width = 640;
  int height = 480;
  int stride = 1;

  vpx_codec_ctx_t codec;
  vpx_image_t* img{ nullptr };

  img = vpx_img_alloc(NULL, VPX_IMG_FMT_I420, width, height, stride);

  vpx_codec_enc_cfg_t vpxConfig;
  vpx_codec_err_t res;

  // Initialise codec configuration.
  res = vpx_codec_enc_config_default(vpx_codec_vp8_cx(), &vpxConfig, 0);

  if (res) {
    printf("Failed to get VPX codec config: %s\n", vpx_codec_err_to_string(res));
    return -1;
  }

  vpxConfig.g_w = width;
  vpxConfig.g_h = height;

  // Initialise codec.
  res = vpx_codec_enc_init(&codec, vpx_codec_vp8_cx(), &vpxConfig, 0);

  if (res) {
    printf("Failed to initialise VPX codec: %s\n", vpx_codec_err_to_string(res));
    return -1;
  }

  // Do a test encode.
  std::vector<uint8_t> dummyI420(640 * 480 * 2);
  vpx_enc_frame_flags_t flags = 0;

  vpx_img_wrap(img, VPX_IMG_FMT_I420, width, height, 1, dummyI420.data());

  res = vpx_codec_encode(&codec, img, 1, 1, flags, VPX_DL_REALTIME);

  if (res) {
    printf("VPX codec failed to encode dummy frame. %s\n", vpx_codec_err_to_string(res));
    return -1;
  }

  vpx_codec_iter_t iter = NULL;
  const vpx_codec_cx_pkt_t* pkt;

  while ((pkt = vpx_codec_get_cx_data(&codec, &iter))) {
    switch (pkt->kind) {
    case VPX_CODEC_CX_FRAME_PKT:
      printf("%s %i\n", (pkt->data.frame.flags & VPX_FRAME_IS_KEY) ? "K" : ".", pkt->data.frame.sz);
      break;
    default:
      printf("Got unknown packet type %d.\n", pkt->kind);
      break;
    }
  }
}

