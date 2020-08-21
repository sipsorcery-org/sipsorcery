#include "vp8cx.h"
#include "vp8dx.h"
#include "vpx_decoder.h"
#include "vpx_encoder.h"

#include <iostream>

int main()
{
    std::cout << "libvpx test console\n";

    std::cout << "vp8 encoder version " << vpx_codec_version_str() << "." << std::endl;

    vpx_codec_enc_cfg_t vpxConfig;
    vpx_codec_err_t res;

    //res = vpx_codec_enc_config_default(&vpx_codec_vp8_cx_algo, &vpxConfig, 0);
    res = vpx_codec_enc_config_default(vpx_codec_vp8_cx(), &vpxConfig, 0);

    if (res) {
      printf("Failed to get VPX codec config: %s\n", vpx_codec_err_to_string(res));
      return -1;
    }

    std::cout << "VPX_ENCODER_ABI_VERSION=" << VPX_ENCODER_ABI_VERSION << "." << std::endl;
}

