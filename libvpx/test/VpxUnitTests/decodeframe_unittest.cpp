/******************************************************************************
* Filename: decodeframe_unittest.cpp
*
* Description:
* Unit tests for the logic in:
*  - decodeframe.c
*
* Author:
* Aaron Clauson (aaron@sipsorcery.com)
*
* History:
* 04 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
*
* License: Public Domain (no warranty, use at own risk)
/******************************************************************************/

#include "pch.h"
#include "imgutils.h"
#include "strutils.h"
#include "CppUnitTest.h"
#include "vp8/common/alloccommon.h"
#include "vp8/decoder/onyxd_int.h"
#include "vpx/vp8cx.h"

#include <fstream>
#include <iostream>
#include <streambuf>
#include <string>
#include <vector>

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace VpxUnitTests
{
  TEST_CLASS(decodeframe_unittest)
  {
  public:

    TEST_METHOD(DecodeDummyFrameTest)
    {
      vpx_codec_enc_cfg_t vpxConfig;

      // Initialise codec configuration.
      vpx_codec_err_t res = vpx_codec_enc_config_default(vpx_codec_vp8_cx(), &vpxConfig, 0);

      Assert::AreEqual((int)VPX_CODEC_OK, (int)res);

      vpx_codec_ctx_t decoder;
      res = vpx_codec_dec_init(&decoder, vpx_codec_vp8_dx(), NULL, 0);

      Assert::AreEqual((int)VPX_CODEC_OK, (int)res);

      uint8_t dummy[100];
      res = vpx_codec_decode(&decoder, dummy, 100, nullptr, 0);

      Assert::AreEqual((int)VPX_CODEC_UNSUP_BITSTREAM, (int)res);
    }

    TEST_METHOD(DecodeKeyFrameTest)
    {
      Logger::WriteMessage("DecodeKeyFrameFrameTest");

      vpx_codec_enc_cfg_t vpxConfig;

      // Initialise codec configuration.
      vpx_codec_err_t res = vpx_codec_enc_config_default(vpx_codec_vp8_cx(), &vpxConfig, 0);

      Assert::AreEqual((int)VPX_CODEC_OK, (int)res);

      vpx_codec_ctx_t decoder;
      res = vpx_codec_dec_init(&decoder, vpx_codec_vp8_dx(), NULL, 0);

      Assert::AreEqual((int)VPX_CODEC_OK, (int)res);

      std::string kfHex = "5043009d012a8002e00102c7088585889984880f0201d807f007f4040d6d4a7c9ee0d02c93ed7b7364e70f64e70f64e70c84f70681649f6bdb9b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387900fefc1bddffff15ac3ace2ffc56fffffc56b0eb38bff153dd80";
      std::vector<uint8_t> kfData = ParseHex(kfHex);

      res = vpx_codec_decode(&decoder, kfData.data(), kfData.size(), nullptr, 0);

      Assert::AreEqual((int)VPX_CODEC_OK, (int)res);

      vpx_codec_iter_t decoder_iter = NULL;
      vpx_image_t* decodedImg = vpx_codec_get_frame(&decoder, &decoder_iter);

      Assert::IsNotNull(decodedImg);

      std::string yPlane = toHex(decodedImg->planes[0], decodedImg->planes[0] + decodedImg->stride[0]);
      std::string uPlane = toHex(decodedImg->planes[1], decodedImg->planes[1] + decodedImg->stride[1]);
      std::string vPlane = toHex(decodedImg->planes[2], decodedImg->planes[2] + decodedImg->stride[2]);

      Logger::WriteMessage("y plane: ");
      Logger::WriteMessage(yPlane.c_str());
      Logger::WriteMessage("\n");
      Logger::WriteMessage("u plane: ");
      Logger::WriteMessage(uPlane.c_str());
      Logger::WriteMessage("\n");
      Logger::WriteMessage("v plane: ");
      Logger::WriteMessage(vPlane.c_str());
      Logger::WriteMessage("\n");
    }

    /// <summary>
    /// Test decode of a 32x24 sized image key frame.
    /// </summary>
    TEST_METHOD(DecodeKeyFrameSmallTest)
    {
      Logger::WriteMessage("DecodeKeyFrameFrameSmallTest");

      vpx_codec_enc_cfg_t vpxConfig;

      // Initialise codec configuration.
      vpx_codec_err_t res = vpx_codec_enc_config_default(vpx_codec_vp8_cx(), &vpxConfig, 0);

      Assert::AreEqual((int)VPX_CODEC_OK, (int)res);

      vpx_codec_ctx_t decoder;
      res = vpx_codec_dec_init(&decoder, vpx_codec_vp8_dx(), NULL, 0);

      Assert::AreEqual((int)VPX_CODEC_OK, (int)res);

      std::string kfHex = "9019009d012a2000180000070885858899848802020275ba24f8de73c58dbdeeeb752712ff80fc8ee701f51cfee1f8e5c007f80ff0dfe73c003fa21e881d603fc07f8e7a287fa3ff25f023fab9fe6bfc4fc00ff1cfe65f3ff800ff46f00fbc5f6f3d5bfdb9cbc7f27fc6dfc88e101fc01f51bfca3f103f29f3817e19fd0ff1d3f243900fa07fe03fc6ff18bf93ed02ff2dfebdfcdff557fa07ba3fecdf8abeb97e10fe9bf8ddf403fc1ff8bff33feaffae5fd73ff9f801fd33f606fd1ff6c52ce5c70fb5b31d19c4d1585982a1d52c92d5044bc6aa90fef98e25c70b5cf745c149e105a557265f8bc910ddd4cb886b7cab7d10d34adb33e89d81e79b23b3a3ff957ee062251d2a350da030f3835bc63663210934f752180ffb727ff1ac46176ff32907dd7e3136e783b35efaa7942bfd44dd8a235af1bffe17985fffecf7417c6a03bfc1a1ff1e474a5479d36a984997847937cf7de46dc9d8424924a7dc90824d92568e635ab5c4cab28adeee56ffca4b7028431c57bf29ffd0701a77d57d889e00cdf4246f94c7b8194e9ad794bf04e08f5e8dfd8e3ba85c9a53b79e07c0a6d522e450d2ba59615f4f32eec7ae529aa1a871fffda4ab9f0eb584bb38392ba87671a35de7973c05c29fff88a95de247f6655a0f2e8797ffd68abf90d359fcde42b78024fce7892f06dd5575f4aa219675afcc85394428ebbbf936ebb3d81f450fab8eef7b81ef5d6227a3b407ffc14c75532c8d63acc8dcdf9b3a1ffedf5b100dab2fd860df7d26843529006b70dacfc8268965c55bf618fc8ff4f04fe10332828dc085ff0aab9895f725562063dda67442d6b9ca8be8c3b70f554050da944adfe1cc2376c6281e4fff013f0f100955110987a750de86d1fb7fe1aba62217c31dda0724eea48372f9e61f8838a080ee4e1bd3233ea3afefabf5cf05f77fe410622f9ef87d3d537ff8a73b22787a00542a940442bfad80c41fb5d46080bba901d21ade640c613c61ad4b15f8a0f91da42ccfa575ee4957adff967140aff4a206acf3c9ab3782d143b9466924de898db1c9cbd5b63736ffc89bda8a44f6f1082f8517a52ad728935e1f0c34927f73600b6dab38ff1e6608ed9b15428092f08bb3e62955bd4bd5513f624fb5ae3618e8dbfeaf992bbc3282ad97653164983f4f2438fad2f7f683b5d6fc6175bb07d3a65ea3483b32fe2125349d3a92c79c011b6c15056ad73bd3620402d301057a904ab755692eb271d2475b6f48acf2538ef6f637d65dfe3f8b70d4603bad4b837def9978d193795afe313bb7ffca3bfcc1aa3dfdf3e325249c59e8b81868f080801ecc7824bb0f0e50ecb3c86ca7e0487fff85bee14ad77c104158879fd1cddd63327ef8fff9b5f84c597dd4723025d87f1dd79bdcd6b7d62625b45f6de1ecb49739363d3ed99fe0fd4d62898af987fc2cda27c6b4bd6816557338d93ddc25632b668fe7fffd70e1027eb39241eb02077844bb7888a09659b1508601742cbdc438ac3bd51130a3fc7caab667259a10914a1743685e196f66df1f4ec0365e69dbab16259d65cb406275c560664079ffd4779362e1f875d3ffe440dd4fe464d64800";
      std::vector<uint8_t> kfData = ParseHex(kfHex);

      res = vpx_codec_decode(&decoder, kfData.data(), kfData.size(), nullptr, 0);

      Assert::AreEqual((int)VPX_CODEC_OK, (int)res);

      vpx_codec_iter_t decoder_iter = NULL;
      vpx_image_t* decodedImg = vpx_codec_get_frame(&decoder, &decoder_iter);

      Assert::IsNotNull(decodedImg);

      std::string yPlane = toHex(decodedImg->planes[0], decodedImg->planes[0] + decodedImg->stride[0]);
      std::string uPlane = toHex(decodedImg->planes[1], decodedImg->planes[1] + decodedImg->stride[1]);
      std::string vPlane = toHex(decodedImg->planes[2], decodedImg->planes[2] + decodedImg->stride[2]);

      Logger::WriteMessage("y plane: ");
      Logger::WriteMessage(yPlane.c_str());
      Logger::WriteMessage("\n");
      Logger::WriteMessage("u plane: ");
      Logger::WriteMessage(uPlane.c_str());
      Logger::WriteMessage("\n");
      Logger::WriteMessage("v plane: ");
      Logger::WriteMessage(vPlane.c_str());
      Logger::WriteMessage("\n");
    }

    TEST_METHOD(DecodeKeyFrameFromFileTest)
    {
      Logger::WriteMessage("DecodeKeyFrameFrameTest");

      vpx_codec_enc_cfg_t vpxConfig;

      // Initialise codec configuration.
      vpx_codec_err_t res = vpx_codec_enc_config_default(vpx_codec_vp8_cx(), &vpxConfig, 0);

      Assert::AreEqual((int)VPX_CODEC_OK, (int)res);

      vpx_codec_ctx_t decoder;
      res = vpx_codec_dec_init(&decoder, vpx_codec_vp8_dx(), NULL, 0);

      Assert::AreEqual((int)VPX_CODEC_OK, (int)res);

      std::ifstream keyFrameStm("testpattern_keyframe.vp8");
      std::string kfHex((std::istreambuf_iterator<char>(keyFrameStm)), std::istreambuf_iterator<char>());
      std::vector<uint8_t> kfData = ParseHex(kfHex);

      Assert::AreEqual(15399ULL, kfData.size());

      res = vpx_codec_decode(&decoder, kfData.data(), kfData.size(), nullptr, 0);

      Assert::AreEqual((int)VPX_CODEC_OK, (int)res);

      vpx_codec_iter_t decoder_iter = NULL;
      vpx_image_t* decodedImg = vpx_codec_get_frame(&decoder, &decoder_iter);

      Assert::IsNotNull(decodedImg);

      std::string yPlane = toHex(decodedImg->planes[0], decodedImg->planes[0] + decodedImg->stride[0]);
      std::string uPlane = toHex(decodedImg->planes[1], decodedImg->planes[1] + decodedImg->stride[1]);
      std::string vPlane = toHex(decodedImg->planes[2], decodedImg->planes[2] + decodedImg->stride[2]);

      Logger::WriteMessage("y plane: ");
      Logger::WriteMessage(yPlane.c_str());
      Logger::WriteMessage("\n");
      Logger::WriteMessage("u plane: ");
      Logger::WriteMessage(uPlane.c_str());
      Logger::WriteMessage("\n");
      Logger::WriteMessage("v plane: ");
      Logger::WriteMessage(vPlane.c_str());
      Logger::WriteMessage("\n");

      std::vector<uint8_t> rgb = I420toBGR(
        decodedImg->planes[0], decodedImg->stride[0],
        decodedImg->planes[1], decodedImg->stride[1],
        decodedImg->planes[2], decodedImg->stride[2],
        640, 480);

      //std::vector<uint8_t> rgb = convertYV12toRGB(decodedImg);

      Assert::AreEqual(921600ULL, rgb.size());

      CreateBitmapFile(L"testpattern_keyframe.bmp", 640, 480, 24, rgb.data(), rgb.size());
    }

    /// <summary>
    /// Tests that attempting to decode a truncated encoded frame results in an error.
    /// </summary>
    TEST_METHOD(DecodeInvalidFrameTest)
    {
      vpx_codec_enc_cfg_t vpxConfig;

      // Initialise codec configuration.
      vpx_codec_err_t res = vpx_codec_enc_config_default(vpx_codec_vp8_cx(), &vpxConfig, 0);

      Assert::AreEqual((int)VPX_CODEC_OK, (int)res);

      vpx_codec_ctx_t decoder;
      res = vpx_codec_dec_init(&decoder, vpx_codec_vp8_dx(), NULL, 0);

      Assert::AreEqual((int)VPX_CODEC_OK, (int)res);

      std::string encFrameHex = "5043009d012a8002e00102c708";
      std::vector<uint8_t> encFramefData = ParseHex(encFrameHex);

      res = vpx_codec_decode(&decoder, encFramefData.data(), encFramefData.size(), nullptr, 0);

      Assert::AreEqual((int)VPX_CODEC_CORRUPT_FRAME, (int)res);
    }

    /// <summary>
    /// Tests the macroblock decode stage for a key frame.
    /// </summary>
    TEST_METHOD(DecodeKeyFrameMacroBlocksTest)
    {
      vpx_codec_enc_cfg_t vpxConfig;

      // Initialise codec configuration.
      vpx_codec_err_t res = vpx_codec_enc_config_default(vpx_codec_vp8_cx(), &vpxConfig, 0);

      Assert::AreEqual((int)VPX_CODEC_OK, (int)res);

      vpx_codec_ctx_t decoder;
      res = vpx_codec_dec_init(&decoder, vpx_codec_vp8_dx(), NULL, 0);

      Assert::AreEqual((int)VPX_CODEC_OK, (int)res);

      std::string encFrameHex = "5043009d012a8002e00102c708";
      std::vector<uint8_t> encFramefData = ParseHex(encFrameHex);

      res = vpx_codec_decode(&decoder, encFramefData.data(), encFramefData.size(), nullptr, 0);

      Assert::AreEqual((int)VPX_CODEC_CORRUPT_FRAME, (int)res);
    }

    /// <summary>
    /// Test decode a sequence of 32x24 encoded frames.
    /// </summary>
    TEST_METHOD(DecodeFrameSequenceSmallTest)
    {
      Logger::WriteMessage("DecodeKeyFrameFrameSmallTest\n");

      vpx_codec_enc_cfg_t vpxConfig;

      // Initialise codec configuration.
      vpx_codec_err_t res = vpx_codec_enc_config_default(vpx_codec_vp8_cx(), &vpxConfig, 0);

      Assert::AreEqual((int)VPX_CODEC_OK, (int)res);

      vpx_codec_ctx_t decoder;
      res = vpx_codec_dec_init(&decoder, vpx_codec_vp8_dx(), NULL, 0);

      Assert::AreEqual((int)VPX_CODEC_OK, (int)res);

      std::vector<std::string> encodedFramesHex = {
        "9018009d012a2000180000470885858899848802020275bb8dd4fecdf8abcb8dbdfdb9da24247d35fd47f22bfc96f16ff49fc80e003fa61fd8bd527a807f49bac07d007f80ff2bf475fd8cf817fd77ff6bfe9be02ff927f30bb05fa37807e13fafbea2e4a2f9f7e44fe336a1afe59f8b1f8e59c0bf0cfe6bf8e9f913c667f881f01bf837f2efcb8feb3b417fc8ffa8ff2bfd80fe4fef13f9bfe0bfab5f8d7f9d7e35fd00ff0cfe31fd1ffb07ec8ff5cfff1f481d41bfa65ed8a59cb8e130f4aaac8d3237adbeeb1ca3bcb6680e00fef3affe087e767c16538572fceece4e3be74dfe581641891a620517c648d8f556486cdf342c8ab7e431c1ed50fabb6aee29dad8fa9fd73e931b20ff6a5db9b92de9dfffe062047b5022cf6522bb9e2e2383db3e90ffb7c8874a31d2e3c3ffff72192ffff59cf5363370485d0d87c9087179763da106e0955bd9bc772eb1badfe823ee272f1038b3db2b091578a623fe9ee9c508f981838d9744ee15ce2d1508075e661abc305925d21fd4e2849526d803c5a4acb1ba9d2f05448d9b165d93216507c8cd24e1caf4dffc551b2f9b2d847598ef771140626fc52150726b1c3f361b737814de3b1c26222194ff42fbcd3da5fd1ece1facb53d668f625d5db3db0646f7592ef9d3018ceb0ec9ca139a4a544fa225dc3fe4375c94df25b2ba998dcf82c7ffaff1478a50778a7a0312557c3b9326e745faa0a09aed2f574acc75a24fb2f4591096691f31aac45bfab0d3e4766feacae7e04faed37eafbed8276bfdf41445feb297fe244905702f7a09fd823c97127d29ccb9048122d490662b6405ea984d4eddad68afccceaa8a96c095cffff365b88872e17efd9b2b1de34623c03164d3ab5fb083f2c8ff84e51b1231b7e1738bfcfbf52b2f38eb762e79f02c5fb8617f8b7be85142f1a94f806be88770976826015e752fffe152dd2d240fa7dcbb69e55710c843077da93892ece44333754274e77b3fe136f8a82e0812bcbfe15a60cb8b17b200790074788313b7fedb2669dd917ce79898ecc1706ae0ac5da2e0eb79e5bd9cd433d2a36b2a4f571f4f735acfe03aefaaff20ae0c69ba591a00bfc322f987f7f2db529662c86c2395b63a68c8384b4327450c2d6217d87fff619fd96f9ae910bd720096bcbd48a81dbc5c9149c4d2e2f9ad10ddfe059fbaf769e714454a1fdd2e39587eadfbd9a7e1926c3e311c724bcd963bc6280a7ba05eb193c176868bcba10d8dc7fc2193c3ef19d560e08ec555e657fe9032ce3db6a3a1ad2b5782812a13718ecdb53216bee4e2a658d90bd147b23f2ab1869b9d24eaf3aa08df38825798cfe0f4f4096d4acad4cea1cf2b10b8fe83dc977f7efbc83f471aad72bb8163f71a4753ff818e1ea4ef674c6ffb74bd57f487c74610eff459b43bd06304035deab41d00bd4b0c3be41f7c024d01d1ba7fd5785bb3d06eec5f1391ffd976d9031374bbaf527187fd2188d1b3034bfd5005fd3f70e9462ef684e000",
        "110300011010001e7d6793bb0281476fb1cd5fe1f905f7820021c0c501fd15cddaf31b35cf70ec386f714c008dc01b8d38e79c55214387a3c3aca44c018d8004273408fda90175befca1ae4f24b72bb2f575792a216bfeaa80",
        "d1010001101000180d2ad1b731bb900214c78b0fff015a1c0015db3e51ac3e40090a6f9f48fa9b8018409f80",
        "d10100011010001ee7401595557aa0021c76001db5973accd2fa004a207000",
        "b1010001101000180030282ff400043876002495973accd2fa0012a08740",
        "b1010001101000180030282ff400043876001db5973accd2fa004a207000"
      };

      int count = 0;
      for each (std::string frameHex in encodedFramesHex)
      {
        Logger::WriteMessage(std::string("DECODE FRAME " + std::to_string(count) + ":\n").c_str());

        std::vector<uint8_t> buffer = ParseHex(frameHex);

        res = vpx_codec_decode(&decoder, buffer.data(), buffer.size(), nullptr, 0);

        Assert::AreEqual((int)VPX_CODEC_OK, (int)res);

        vpx_codec_iter_t decoder_iter = NULL;
        vpx_image_t* decodedImg = vpx_codec_get_frame(&decoder, &decoder_iter);

        Assert::IsNotNull(decodedImg);

        std::string yPlane = toHex(decodedImg->planes[0], decodedImg->planes[0] + decodedImg->stride[0]);
        std::string uPlane = toHex(decodedImg->planes[1], decodedImg->planes[1] + decodedImg->stride[1]);
        std::string vPlane = toHex(decodedImg->planes[2], decodedImg->planes[2] + decodedImg->stride[2]);

        Logger::WriteMessage(std::string("Frame decode " + std::to_string(count) + ":\n").c_str());
        Logger::WriteMessage("y plane: ");
        Logger::WriteMessage(yPlane.c_str());
        Logger::WriteMessage("\n");
        Logger::WriteMessage("u plane: ");
        Logger::WriteMessage(uPlane.c_str());
        Logger::WriteMessage("\n");
        Logger::WriteMessage("v plane: ");
        Logger::WriteMessage(vPlane.c_str());
        Logger::WriteMessage("\n");

        switch (count) {
        case 0:
          Assert::AreEqual("b66065dbaa3dafd75b65dba939afcf575ad2a63db6d85a68d8ab3cafda5a67b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b51010101010101010101010101010101010101010101010101010101010101010",
            yPlane.c_str());
          Assert::AreEqual("817e7f7e7f817d7e7f7e7e7f807e82a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a284848484848484848484848484848484",
            uPlane.c_str());
          Assert::AreEqual("807e7f7f7f807f7e7f807e7f7f7e6163636363636363636363636363636363637d7d7d7d7d7d7d7d7d7d7d7d7d7d7d7d",
            vPlane.c_str());
          break;
        case 1:
          Assert::AreEqual("b86165dba93db0d85b65dba93aaed15659d1a53cb6d85a68d8ab3cafda5a67b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b51010101010101010101010101010101010101010101010101010101010101010",
            yPlane.c_str());
          Assert::AreEqual("817e7f7e7f807e7e807e7e7e7f7f83a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a184848484848484848484848484848484",
            uPlane.c_str());
          Assert::AreEqual("7f7f807e7f807f7e7f807e7f807f6162626262626262626262626262626262627e7e7e7e7e7e7e7e7e7e7e7e7e7e7e7e",
            vPlane.c_str());
          break;
        case 2:
          Assert::AreEqual("b86165dba93db0d85b65dba93bafd15559d1a53cb6d85a68d8ab3cafda5a67b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b51010101010101010101010101010101010101010101010101010101010101010",
            yPlane.c_str());
          Assert::AreEqual("817e7f7e7f817d7e807e7e7e7f7f83a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a184848484848484848484848484848484",
            uPlane.c_str());
          Assert::AreEqual("7f7f807e7f807f7e7f807e7f817e6063636363636363636363636363636363637e7e7e7e7e7e7e7e7e7e7e7e7e7e7e7e",
            vPlane.c_str());
          break;
        case 3:
          Assert::AreEqual("b86165dba93db0d85b65dba93bafd15559d1a53cb6d85a68d8ab3cafda5a67b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b51010101010101010101010101010101010101010101010101010101010101010",
            yPlane.c_str());
          Assert::AreEqual("817e7f7e7f807e7e807e7e7e7f7f83a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a184848484848484848484848484848484",
            uPlane.c_str());
          Assert::AreEqual("7f7f807e7f807f7e7f807e7f817e6063636363636363636363636363636363637e7e7e7e7e7e7e7e7e7e7e7e7e7e7e7e",
            vPlane.c_str());
          break;
        case 4:
          Assert::AreEqual("b86165dba93db0d85b65dba93bafd15559d1a53cb6d85a68d8ab3cafda5a67b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b51010101010101010101010101010101010101010101010101010101010101010",
            yPlane.c_str());
          Assert::AreEqual("817e7f7e7f817d7e807e7e7e7f7f83a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a184848484848484848484848484848484",
            uPlane.c_str());
          Assert::AreEqual("7f7f807e7f807f7e7f807e7f817e6063636363636363636363636363636363637e7e7e7e7e7e7e7e7e7e7e7e7e7e7e7e",
            vPlane.c_str());
          break;
        case 5:
          Assert::AreEqual("b86165dba93db0d85b65dba93bafd15559d1a53cb6d85a68d8ab3cafda5a67b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b51010101010101010101010101010101010101010101010101010101010101010",
            yPlane.c_str());
          Assert::AreEqual("817e7f7e7f807e7e807e7e7e7f7f83a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a184848484848484848484848484848484",
            uPlane.c_str());
          Assert::AreEqual("7f7f807e7f807f7e7f807e7f817e6063636363636363636363636363636363637e7e7e7e7e7e7e7e7e7e7e7e7e7e7e7e",
            vPlane.c_str());
          break;
        default:
          break;
        }

        count++;
      }
    }
  };
}
