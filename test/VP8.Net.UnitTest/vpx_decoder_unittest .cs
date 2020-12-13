//-----------------------------------------------------------------------------
// Filename: vpx_decoder_unittest.cs
//
// Description: Unit tests for logic in vpx_decoder.cs.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 27 Oct 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

namespace Vpx.Net.UnitTest
{
    public class vpx_decoder_unittest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public vpx_decoder_unittest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = TestLogger.GetLogger(output).CreateLogger(this.GetType().Name);
        }

        /// <summary>
        /// Tests initialising and destorying the VP8 decoder.
        /// </summary>
        [Fact]
        public unsafe void InitialiseDecoderTest()
        {
            vpx_codec_ctx_t decoder = new vpx_codec_ctx_t();
            vpx_codec_dec_cfg_t cfg = new vpx_codec_dec_cfg_t { threads = 1 };
            vpx_codec_err_t res = vpx_decoder.vpx_codec_dec_init(decoder, vp8_dx.vpx_codec_vp8_dx(), cfg, 0);

            Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, res);
            Assert.NotNull(decoder);

            vpx_codec.vpx_codec_destroy(decoder);
        }

        /// <summary>
        /// Tests that calling decode on null data returns the expected error code.
        /// </summary>
        [Fact]
        public unsafe void DecodeNullDataTest()
        {
            vpx_codec_ctx_t decoder = new vpx_codec_ctx_t();
            vpx_codec_iface_t algo = vp8_dx.vpx_codec_vp8_dx();
            vpx_codec_dec_cfg_t cfg = new vpx_codec_dec_cfg_t { threads = 4 };
            vpx_codec_err_t res = vpx_decoder.vpx_codec_dec_init(decoder, algo, cfg, 0);

            Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, res);
            Assert.NotNull(decoder);

            var result = vpx_decoder.vpx_codec_decode(decoder, null, 0, IntPtr.Zero, 0);

            Assert.Equal(vpx_codec_err_t.VPX_CODEC_INVALID_PARAM, result);
        }

        /// <summary>
        /// Tests that calling decode on an empty data buffer returns the expected error code.
        /// </summary>
        [Fact]
        public unsafe void DecodeEmptyFrameDataTest()
        {
            vpx_codec_ctx_t decoder = new vpx_codec_ctx_t();
            vpx_codec_iface_t algo = vp8_dx.vpx_codec_vp8_dx();
            vpx_codec_dec_cfg_t cfg = new vpx_codec_dec_cfg_t { threads = 4 };
            vpx_codec_err_t res = vpx_decoder.vpx_codec_dec_init(decoder, algo, cfg, 0);

            Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, res);
            Assert.NotNull(decoder);

            byte[] dummyData = new byte[1024];
            fixed (byte* dummy = dummyData)
            {
                var result = vpx_decoder.vpx_codec_decode(decoder, dummy, (uint)dummyData.Length, IntPtr.Zero, 0);

                Assert.Equal(vpx_codec_err_t.VPX_CODEC_UNSUP_BITSTREAM, result);
            }
        }

        [Fact]
        public unsafe void DecodeKeyFrame()
        {
            string hexKeyFrame = "5043009d012a8002e00102c7088585889984880f0201d807f007f4040d6d4a7c9ee0d02c93ed7b7364e70f64e70f64e70c84f70681649f6bdb9b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387900fefc1bddffff15ac3ace2ffc56fffffc56b0eb38bff153dd80";

            byte[] keyFrame = HexStr.ParseHexStr(hexKeyFrame);

            Assert.Equal(573, keyFrame.Length);

            vpx_codec_ctx_t decoder = new vpx_codec_ctx_t();
            vpx_codec_iface_t algo = vp8_dx.vpx_codec_vp8_dx();
            vpx_codec_dec_cfg_t cfg = new vpx_codec_dec_cfg_t { threads = 4 };
            vpx_codec_err_t res = vpx_decoder.vpx_codec_dec_init(decoder, algo, cfg, 0);

            Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, res);
            Assert.NotNull(decoder);

            fixed (byte* pKeyFrame = keyFrame)
            {
                var result = vpx_decoder.vpx_codec_decode(decoder, pKeyFrame, (uint)keyFrame.Length, IntPtr.Zero, 0);

                Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, result);
            }

            IntPtr iter = IntPtr.Zero;
            var img = vpx_decoder.vpx_codec_get_frame(decoder, iter);

            Assert.NotNull(img);
            Assert.Equal(640U, img.d_w);
            Assert.Equal(480U, img.d_h);

            byte[] yPlane = new byte[img.stride[0]];
            byte[] uPlane = new byte[img.stride[1]];
            byte[] vPlane = new byte[img.stride[2]];

            Marshal.Copy((IntPtr)img.planes[0], yPlane, 0, yPlane.Length);
            Marshal.Copy((IntPtr)img.planes[1], uPlane, 0, uPlane.Length);
            Marshal.Copy((IntPtr)img.planes[2], vPlane, 0, vPlane.Length);

            logger.LogDebug(HexStr.ToHexStr(yPlane));
            logger.LogDebug(HexStr.ToHexStr(uPlane));
            logger.LogDebug(HexStr.ToHexStr(vPlane));

            string expectedYPlaneHexStr = "0101010101010101010101010101010100000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000101010101010101010101010101010101010101010101010101010101010101";
            string expectedUPlaneHexStr = "02020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202";
            string expectedVPlaneHexStr = "02020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202";

            Assert.Equal(704, yPlane.Length);
            Assert.Equal(352, uPlane.Length);
            Assert.Equal(352, vPlane.Length);
            Assert.Equal(expectedYPlaneHexStr, HexStr.ToHexStr(yPlane));
            Assert.Equal(expectedUPlaneHexStr, HexStr.ToHexStr(uPlane));
            Assert.Equal(expectedVPlaneHexStr, HexStr.ToHexStr(vPlane));
        }

        [Fact]
        public unsafe void DecodeIntraFrame()
        {
            string hexKeyFrame = "5043009d012a8002e00102c7088585889984880f0201d807f007f4040d6d4a7c9ee0d02c93ed7b7364e70f64e70f64e70c84f70681649f6bdb9b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387b27387900fefc1bddffff15ac3ace2ffc56fffffc56b0eb38bff153dd80";
            string hexIntraFrame = "b103000f11fc001809d69ffc0d19f9868cfcc343467ffe6001f87fbbbbbbb940fee8d71f8deba446c5ff1bd7488d3c00";

            byte[] keyFrame = HexStr.ParseHexStr(hexKeyFrame);
            byte[] intraFrame = HexStr.ParseHexStr(hexIntraFrame);

            Assert.Equal(573, keyFrame.Length);

            vpx_codec_ctx_t decoder = new vpx_codec_ctx_t();
            vpx_codec_iface_t algo = vp8_dx.vpx_codec_vp8_dx();
            vpx_codec_dec_cfg_t cfg = new vpx_codec_dec_cfg_t { threads = 4 };
            vpx_codec_err_t res = vpx_decoder.vpx_codec_dec_init(decoder, algo, cfg, 0);

            Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, res);
            Assert.NotNull(decoder);

            fixed (byte* pKeyFrame = keyFrame)
            {
                var result = vpx_decoder.vpx_codec_decode(decoder, pKeyFrame, (uint)keyFrame.Length, IntPtr.Zero, 0);
                Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, result);
            }

            IntPtr iter = IntPtr.Zero;
            var img = vpx_decoder.vpx_codec_get_frame(decoder, iter);

            Assert.NotNull(img);
            Assert.Equal(640U, img.d_w);
            Assert.Equal(480U, img.d_h);

            img.Dispose();
            img = null;

            fixed (byte* pIntraFrame = intraFrame)
            {
                var result = vpx_decoder.vpx_codec_decode(decoder, pIntraFrame, (uint)intraFrame.Length, IntPtr.Zero, 0);
                Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, result);
            }

            img = vpx_decoder.vpx_codec_get_frame(decoder, iter);

            Assert.NotNull(img);
            Assert.Equal(640U, img.d_w);
            Assert.Equal(480U, img.d_h);
        }

        /// <summary>
        /// Attempts to decode a series of VP8 encoded frames that were captured from a Chrome WebRTC session.
        /// </summary>
        //[Fact(Skip ="TODO")]
        public unsafe void DecodeChromeFrameSequence()
        {
            string[] frames =  {
                 "D04C009D012A4001F000392F00171C221616226612205C0EEE032AD4822069ADD34F6945BA7BAD9C443224B9CD25E10FF73B7A373746523EC2614922EF8F2819E93B7F2FECBF4A1C3F2915AA529E7F947A50B36C68301E5208B5DE5DD4A3A64354BEA6E010FFED6154A9DDBD77CCE4B3396FFD4EC3E38A083249F04EC98F87B56B3289A6FBC1997DDE229C526EA2C7A91BE61F9589FD205FCDE191995AA93621015FA1C6679956314F6C8A9A81C2905A4BDC53B68337E4FC166C79EC227A45D8F37EA52D9607A6340522F947E9D5B99002A2BFB2F986922913B2A1DA73EE858E2B731987BE6A642998FCE34C440CF5CACF29BAB6C665534004CA1429A4E91E8A6E8B489460182F2AC9ADFF1061E95DBE522B2345E5722E82379FF15863076D6DF51EA02BDD3B9CB2226275E58D102757C02361D893E79A41A7D982B78DFD1EFF10C4E8D3AA75DB266FFD33DE10AFD2F42100F6D6CDA6670998468C1A84008B1B27E7AE0D5546CC3351251FE10D2A95A36EF374AC00D2B4DB02E2A9D8D5506B3A2B859627C459F23718B23ED7E3073454984E843FE096ABF787B8699AB21F0D7BFCEB028DF98161B3A06ADAE6C9726EB56B00758A9AF43479DB0AA565955C64F1964916B107CC860E19A1DB4FB4E9DC4FF37C7AB5E800DCF05AC09A16E89BF0E85B515441BE088567188FB8200D59AB7834562D56C8175EF174F5D9925520359486C16898494C25347588F31BEF2C9E3211B5FA4A17E2000F0CADFC94C6CCED4A9F1CC0F56F0FAD946D8FA443F047224360604446F2A5090BC26A2FB2F658598570D8D8CC3AC2C4317547168A6CB3C93DD3B96D0AE94C239340505B92C20119869383AAE24805EA489C4FF842BA720C00FEE1132760711868527B241870C7DB03F4DB232BD52546B417C2321245BDDF22501528699248E06A1233742E6004CD169062D95EFE821E71854E1EBEBB8F132FC791B7FD1AB56CD8A77705C653BDA1415510C25A0F1EB6A78A0C61EB8B9D8BAC149D09E0A1671EC88E487D136487B6F2976FE86BA477B10C30BA432DAAF8F5AAF907E1082943B473DCF8F9DD1E3165B4A74A7CDCD7CA8780BA11FA9A74CF29BB5753B7172EAC589C2A59697C6A66BD26BE179A66C1452F49A4ACF67FD970856CA837FBB47F51FEDFCCE321CC9E5742FB4746641074630DE9EEC51409FD4B740CEE6D5D087E4DECCAE982889AB9E1C7D2F75461A7EF712044734948D9BD1A9A9F7E111CAA17532CA00990B2E9E7CCF393839D97743A423EDA8D81A0E51C5D17CE9120B73E60BA5B42BB64C92FC9BE2DE96995BCC20934633EC78B486520B10C31AF7941FA3D5E0F67689410521130196A3078390476A8A93DB77DE64E645D8408591F5FFD0D5A18A72ADD5B2AB7C5FA344CF2544AA228FB0B9256CE8E38D5E6F9D44D30F9886ACF3A220D6C126160E2FC3A9D68A269219124B3CABE0E529098A87D31C7B1C13B65A4A65690EFAE6B5C810C52D3EB77392855823803579F2A2996E61061074F5F6E380E7E4AE16994428E776202BCFB52868C475458D172056379DA7FC7150C4124186E3641181EAD32D02018A70EAA9D46587307F35A8CE6EC81B96C809166EE28FB9B962759E9D33EF3B82C4AD4B56C5F76B726503A1EF93DF6500228D26B6BA163C9263D8F5533EF2AF23DB79967001F9DCC7A9FBC6FBF130FF3810C7C76337ABF17475CFF85951F19F1A5AA6A5B09C3EAABEC8577D3CD4D0175A40866644C08C6A5A91E174A785A41D5FCFFA37BCFA70C7B353BF759D1F2C9451DA028FF9FD698546D47593E591A82312F78252E16269FC1797FCDA441D5D0E00926F6D3C0CCF1E1611907B7D588F2566BE8E5C0B0EF1DFEBBBCF6CEA9C500EB192F885823D6D6CA2EB344B0E35EC0809827E6346BF7E4F332A765CF845F9312793FEE9E7C55D5ECF869CBAAF4C244AE2EDB0AFAB1D134440A36F96D36115E4F469B6A648F9FF96CF84CEA639C4F74540520A1A7694DA1661BABBEB75BD52458A239A1F191DB9E99BF38EC71F4763EE0A27ED159713A9CBE0FCA4659FB89C685E12DF293F149CAAB7F8C99FD54A581CA68EF5850D3A1E5B1A801719EAB0DF39CD5FBEAE6E7149B8AB4801CF1668313903E0B1CD82770B3D753F31B2122536CB99CA9F2EB529DE0C7F4BB67520E950E27D3ECAD85B0DA3780646C1E3BDED4D44189D44725087F4F5E4CCE56ADD4265C6C919A437A0BFE574314B25F993DAE21E522E3946B0129180F90966CD1A1777DD30C7856298DA87ECA2884E70B75F2ADA5EFE2B44B9443C8AF254EF94E113B5EC012E93A881E5236181CF7C0E80250FC0AA4ADC5DD57357322B80551616CABAFF276DEADB2FBE3DE2BD07D0471CE56EEEAC23BF4A0D2861ED1548AA4E66A10B9FFFE0DF78BB9F5609DDFAEFB50935E6D8F0BD2266BFFA953D7049517FECDD917196AB32FA5BBFDDED0201C535E1B92F9B8BB054932B61F24422E1C2D0E7FCA8319A1E9C70E7E7060E2C7D1D5BAA4E5E555DD6B365EB22650E4AD116C2E5A97A82F7D86CC39ED66BE73559EF861D67869FD7989D9FC2DE1A32D23DA9355011102BD0D99069A3F2E7D9CFBCBFF3A31CD56F0E526CD9E846DA8A460A24BD51FC2EF55AB33864BAE4F877D0DD7734AF2FA50673EC337DE0FFD75A322E0CFBF511373AC0DEF8CC4AE73724E82E6F9D783198F57095980F219791F83862313BE813D387EF3F6430542FDE1776018737CD29F3535A67AFBB4D096BCF5E4792340DE78ED9A34A2A802FF3F997023FE1E9C1AFE8CABC5D06088E17B6A6A1077F56666AA2882176487D218415359742297A2F14F0838D6207D2DB4C34DF9717673F0B81D24E1EAACCF64F22556BCA10E876D32CDF11117FFC91A9923CC8B62666294E270E32413C33438B0B2A08A7731B1056C8451663DE58BB4C166E8F7C20D8A2CEDB2FA613DF9471844C1CBCAC6A3A69432518D6E172F313C6DB21C554E8EB2B930BF0FB5F69F2A327D34C83078F08C48E84D614746579E6641F3DB6D97477D0B5A7ADB49F7918506C9161405B9681E39005BE66015DFD506382BBD6F8B85B080BDE5A3D5D8DF118B67676C7FAA8B420A934569449EFA346794B0B6F896899B7436CF16750E7E7EB4BC9139687497E7A99EFEC430284E2EB55EF47B4F8AA1AF927523D56B87AA01A3207785051612A8A0200125ABF36E49D0F6603973577396B152AAA050FEC2C9B14328808330CC14709EAF06043256742605D36F6D7572B0B51894ABBB30D48AD64B0B3041B1D50845B8D82311ACB988E4D9B868A25F0E5C9383A1D8A3542450F6E3FB84DB1634105F16CFC8ECCE5C9F333E265B871593F835AAFD59F4AA0F37944876BE53F0BE52BFD2E377E868A04CFB043CFCA36A1CDCB5903C76CCF5FC43BF0B0551F3B3F287D5F995E82BDE433894346AA15DBFF6231339503980008367B819607407EF8580B1F0E95D63D0E4CFE0B77C5D9C3951A44C6A0756CD8D14E747BC54312E47B85F70C9E43FA66728A1C221B56BBE22A4F56505EBA91116A7E4C9939C418FD32A797ADBC9C73DC6F7AA5761F78D7BBA50F722D4323BFA65396F9C50CE01F3A6BE884525CA2A41A85B4315BC1F27D9C6E13BA6073439B186D775CD3B26A2A2E5447A0E4F19535640B30BDB51E4A6655E4ACC0C5CFD5A5C198E560FEF437AC7691CF23A9700068CED10C74A54BFF713255D79465A7C7FCCF36FFACA58CF54F9B818344A3F3B3DE5A897E1F7142C5BBC8A60D76C476B8CE16DC63242E037215A305F004C94B13B297741BE9E1ED8253D1B0CC88EED1F90685D20F6400FA5EBFAD0AEA66F41223EA9DB8E06FFF24BA372612804856DAF7546E33C69FBAC05BE53554F383E531AF3F098",
                 "310700E4E0AFB8BFCFA747A7D88517F2496030B237169B9086BE506176B28BBABE2B28BBABB83A05847F7440814B175022108FAEAE06A36B717221400F908385A6CC9620E39E0700",
                 "510600E4E0AFB8D37E12C05F406760BFD000CA3831ABD68CE9EEDCDBA87A6BAC7740EEDA37A587D7F6A8A75F01661AE8A0EB0F7AE007EA2C9E68955E3F791BDC80",
                 "F10500E4A91FB8CB8D24A05F406520BFD0009744A8E87FAF48851B25992CC6001D462EFE16F778938D8B15FD06AD580B9140B160963AB2CE2A615840",
                 "310700E4797FB8B3BC6620604FDC0F1A1AF9402FF8730DE0342C11FAB3BB3BABF557EAB7F7D04AE308CDAC073D56F4526572EA0EE83997538725ECC08B3E15FB7ACE8CD77AA858F0B31DC438",
                 "910800E441EFB8C39B87E061714813CD2077E0613810C867FEEE55AF44B1EB89E2B300D3C565D030A3D7061E7D6C1C4529520505E8D74EECA8B1D5AE9A2CE73D5FB22E89752A00CF90F497929B8C7417E6AE749C6116B4C8D1602D7F590889C1D90D339A53ED97C3323893D80DCC04C138E162A10944813D00",
                 "510900E41A3FB8CF82F9007ABE51A4CAFB4E6AB4A3D702AC3DE01A0AE1109C07AFBF27D04103D4CAB08583E81EE72301FEB39962440C225D348D2C0E348F426BCA8EF0B6B24B83D928737BDA584951AD6F81F9115EC83C6B54ED083D9B71DB8F0A4F0BF13017D583B32532179668D4B7D78B7908125F98136C2ACE8C70B26C7522A7D422D5BF229B5E165B387AE70258AC6CFF72DCB8FAE84B0201AF72F28F61FC81CE1FC7B9F9AD154CA78092310FA213230AE8A8A555C59BB87DF540988E466D50017F723620D5D821945D6C1A1F8FEB3A4400"};

            vpx_codec_ctx_t decoder = new vpx_codec_ctx_t();
            vpx_codec_iface_t algo = vp8_dx.vpx_codec_vp8_dx();
            vpx_codec_dec_cfg_t cfg = new vpx_codec_dec_cfg_t { threads = 1 };
            vpx_codec_err_t res = vpx_decoder.vpx_codec_dec_init(decoder, algo, cfg, 0);

            Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, res);
            Assert.NotNull(decoder);

            foreach (var frame in frames)
            {
                byte[] buffer = HexStr.ParseHexStr(frame);

                fixed (byte* pBuffer = buffer)
                {
                    var result = vpx_decoder.vpx_codec_decode(decoder, pBuffer, (uint)buffer.Length, IntPtr.Zero, 0);
                    Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, result);
                }

                IntPtr iter = IntPtr.Zero;
                var img = vpx_decoder.vpx_codec_get_frame(decoder, iter);

                Assert.NotNull(img);
                Assert.Equal(320U, img.d_w);
                Assert.Equal(240U, img.d_h);
            }
        }

        [Fact]
        public unsafe void DecodeKeyFrameFromFileTest()
        {
            byte[] kfBytes = HexStr.ParseHexStr(File.ReadAllText("testpattern_keyframe.vp8"));

            Assert.Equal(15399, kfBytes.Length);

            vpx_codec_ctx_t decoder = new vpx_codec_ctx_t();
            vpx_codec_iface_t algo = vp8_dx.vpx_codec_vp8_dx();
            vpx_codec_dec_cfg_t cfg = new vpx_codec_dec_cfg_t { threads = 4 };
            vpx_codec_err_t res = vpx_decoder.vpx_codec_dec_init(decoder, algo, cfg, 0);

            Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, res);
            Assert.NotNull(decoder);

            fixed (byte* pKeyFrame = kfBytes)
            {
                var result = vpx_decoder.vpx_codec_decode(decoder, pKeyFrame, (uint)kfBytes.Length, IntPtr.Zero, 0);

                Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, result);
            }

            IntPtr iter = IntPtr.Zero;
            var img = vpx_decoder.vpx_codec_get_frame(decoder, iter);

            Assert.NotNull(img);
            Assert.Equal(640U, img.d_w);
            Assert.Equal(480U, img.d_h);

            byte[] bgr = ImgHelper.I420toBGR(img.planes[0], img.stride[0],
                img.planes[1], img.stride[1],
                img.planes[2], img.stride[2],
                640, 480);

            Assert.Equal(921600, bgr.Length);

            fixed (byte* bmpPtr = bgr)
            {
                Bitmap bmp = new Bitmap((int)img.d_w, (int)img.d_h, (int)img.d_w * 3, System.Drawing.Imaging.PixelFormat.Format24bppRgb, new IntPtr(bmpPtr));
                bmp.Save("decodekeyframe.bmp");
                bmp.Dispose();
            }
        }

        /// <summary>
        /// Test decode of a 32x24 sized image key frame.
        /// </summary>
        [Fact]
        public unsafe void DecodeKeyFrameSmallTest()
        {
            string hexKeyFrame = "9019009d012a2000180000070885858899848802020275ba24f8de73c58dbdeeeb752712ff80fc8ee701f51cfee1f8e5c007f80ff0dfe73c003fa21e881d603fc07f8e7a287fa3ff25f023fab9fe6bfc4fc00ff1cfe65f3ff800ff46f00fbc5f6f3d5bfdb9cbc7f27fc6dfc88e101fc01f51bfca3f103f29f3817e19fd0ff1d3f243900fa07fe03fc6ff18bf93ed02ff2dfebdfcdff557fa07ba3fecdf8abeb97e10fe9bf8ddf403fc1ff8bff33feaffae5fd73ff9f801fd33f606fd1ff6c52ce5c70fb5b31d19c4d1585982a1d52c92d5044bc6aa90fef98e25c70b5cf745c149e105a557265f8bc910ddd4cb886b7cab7d10d34adb33e89d81e79b23b3a3ff957ee062251d2a350da030f3835bc63663210934f752180ffb727ff1ac46176ff32907dd7e3136e783b35efaa7942bfd44dd8a235af1bffe17985fffecf7417c6a03bfc1a1ff1e474a5479d36a984997847937cf7de46dc9d8424924a7dc90824d92568e635ab5c4cab28adeee56ffca4b7028431c57bf29ffd0701a77d57d889e00cdf4246f94c7b8194e9ad794bf04e08f5e8dfd8e3ba85c9a53b79e07c0a6d522e450d2ba59615f4f32eec7ae529aa1a871fffda4ab9f0eb584bb38392ba87671a35de7973c05c29fff88a95de247f6655a0f2e8797ffd68abf90d359fcde42b78024fce7892f06dd5575f4aa219675afcc85394428ebbbf936ebb3d81f450fab8eef7b81ef5d6227a3b407ffc14c75532c8d63acc8dcdf9b3a1ffedf5b100dab2fd860df7d26843529006b70dacfc8268965c55bf618fc8ff4f04fe10332828dc085ff0aab9895f725562063dda67442d6b9ca8be8c3b70f554050da944adfe1cc2376c6281e4fff013f0f100955110987a750de86d1fb7fe1aba62217c31dda0724eea48372f9e61f8838a080ee4e1bd3233ea3afefabf5cf05f77fe410622f9ef87d3d537ff8a73b22787a00542a940442bfad80c41fb5d46080bba901d21ade640c613c61ad4b15f8a0f91da42ccfa575ee4957adff967140aff4a206acf3c9ab3782d143b9466924de898db1c9cbd5b63736ffc89bda8a44f6f1082f8517a52ad728935e1f0c34927f73600b6dab38ff1e6608ed9b15428092f08bb3e62955bd4bd5513f624fb5ae3618e8dbfeaf992bbc3282ad97653164983f4f2438fad2f7f683b5d6fc6175bb07d3a65ea3483b32fe2125349d3a92c79c011b6c15056ad73bd3620402d301057a904ab755692eb271d2475b6f48acf2538ef6f637d65dfe3f8b70d4603bad4b837def9978d193795afe313bb7ffca3bfcc1aa3dfdf3e325249c59e8b81868f080801ecc7824bb0f0e50ecb3c86ca7e0487fff85bee14ad77c104158879fd1cddd63327ef8fff9b5f84c597dd4723025d87f1dd79bdcd6b7d62625b45f6de1ecb49739363d3ed99fe0fd4d62898af987fc2cda27c6b4bd6816557338d93ddc25632b668fe7fffd70e1027eb39241eb02077844bb7888a09659b1508601742cbdc438ac3bd51130a3fc7caab667259a10914a1743685e196f66df1f4ec0365e69dbab16259d65cb406275c560664079ffd4779362e1f875d3ffe440dd4fe464d64800";

            byte[] keyFrame = HexStr.ParseHexStr(hexKeyFrame);

            Assert.Equal(1134, keyFrame.Length);

            vpx_codec_ctx_t decoder = new vpx_codec_ctx_t();
            vpx_codec_iface_t algo = vp8_dx.vpx_codec_vp8_dx();
            vpx_codec_dec_cfg_t cfg = new vpx_codec_dec_cfg_t { threads = 4 };
            vpx_codec_err_t res = vpx_decoder.vpx_codec_dec_init(decoder, algo, cfg, 0);

            Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, res);
            Assert.NotNull(decoder);

            fixed (byte* pKeyFrame = keyFrame)
            {
                var result = vpx_decoder.vpx_codec_decode(decoder, pKeyFrame, (uint)keyFrame.Length, IntPtr.Zero, 0);

                Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, result);
            }

            IntPtr iter = IntPtr.Zero;
            var img = vpx_decoder.vpx_codec_get_frame(decoder, iter);

            Assert.NotNull(img);
            Assert.Equal(32U, img.d_w);
            Assert.Equal(24U, img.d_h);

            byte[] yPlane = new byte[img.stride[0]];
            byte[] uPlane = new byte[img.stride[1]];
            byte[] vPlane = new byte[img.stride[2]];

            Marshal.Copy((IntPtr)img.planes[0], yPlane, 0, yPlane.Length);
            Marshal.Copy((IntPtr)img.planes[1], uPlane, 0, uPlane.Length);
            Marshal.Copy((IntPtr)img.planes[2], vPlane, 0, vPlane.Length);

            logger.LogDebug(HexStr.ToHexStr(yPlane));
            logger.LogDebug(HexStr.ToHexStr(uPlane));
            logger.LogDebug(HexStr.ToHexStr(vPlane));

            string expectedYPlaneHexStr = "c45f65eab237b7e95864ebb034b8e45257e0ae33c3e85764eab536bae95863c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c10202020202020202020202020202020202020202020202020202020202020202";
            string expectedUPlaneHexStr = "81817f7f7d80817e7f80818181827f808080808080808080808080808080808082828282828282828282828282828282";
            string expectedVPlaneHexStr = "7e85837e7f837f8081807d7f81837f818181818181818181818181818181818180808080808080808080808080808080";

            Assert.Equal(96, yPlane.Length);
            Assert.Equal(48, uPlane.Length);
            Assert.Equal(48, vPlane.Length);
            Assert.Equal(expectedYPlaneHexStr, HexStr.ToHexStr(yPlane));
            Assert.Equal(expectedUPlaneHexStr, HexStr.ToHexStr(uPlane));
            Assert.Equal(expectedVPlaneHexStr, HexStr.ToHexStr(vPlane));
        }

        /// <summary>
        /// Decode a sequence of encoded 32x24 frames. The encode was performed using the native C libvpx library as
        /// were the expected decode results.
        /// </summary>
        [Fact]
        public unsafe void DecodeFrameSequenceSmallTest()
        {
            string[] frames =  {
                 "9018009d012a2000180000470885858899848802020275bb8dd4fecdf8abcb8dbdfdb9da24247d35fd47f22bfc96f16ff49fc80e003fa61fd8bd527a807f49bac07d007f80ff2bf475fd8cf817fd77ff6bfe9be02ff927f30bb05fa37807e13fafbea2e4a2f9f7e44fe336a1afe59f8b1f8e59c0bf0cfe6bf8e9f913c667f881f01bf837f2efcb8feb3b417fc8ffa8ff2bfd80fe4fef13f9bfe0bfab5f8d7f9d7e35fd00ff0cfe31fd1ffb07ec8ff5cfff1f481d41bfa65ed8a59cb8e130f4aaac8d3237adbeeb1ca3bcb6680e00fef3affe087e767c16538572fceece4e3be74dfe581641891a620517c648d8f556486cdf342c8ab7e431c1ed50fabb6aee29dad8fa9fd73e931b20ff6a5db9b92de9dfffe062047b5022cf6522bb9e2e2383db3e90ffb7c8874a31d2e3c3ffff72192ffff59cf5363370485d0d87c9087179763da106e0955bd9bc772eb1badfe823ee272f1038b3db2b091578a623fe9ee9c508f981838d9744ee15ce2d1508075e661abc305925d21fd4e2849526d803c5a4acb1ba9d2f05448d9b165d93216507c8cd24e1caf4dffc551b2f9b2d847598ef771140626fc52150726b1c3f361b737814de3b1c26222194ff42fbcd3da5fd1ece1facb53d668f625d5db3db0646f7592ef9d3018ceb0ec9ca139a4a544fa225dc3fe4375c94df25b2ba998dcf82c7ffaff1478a50778a7a0312557c3b9326e745faa0a09aed2f574acc75a24fb2f4591096691f31aac45bfab0d3e4766feacae7e04faed37eafbed8276bfdf41445feb297fe244905702f7a09fd823c97127d29ccb9048122d490662b6405ea984d4eddad68afccceaa8a96c095cffff365b88872e17efd9b2b1de34623c03164d3ab5fb083f2c8ff84e51b1231b7e1738bfcfbf52b2f38eb762e79f02c5fb8617f8b7be85142f1a94f806be88770976826015e752fffe152dd2d240fa7dcbb69e55710c843077da93892ece44333754274e77b3fe136f8a82e0812bcbfe15a60cb8b17b200790074788313b7fedb2669dd917ce79898ecc1706ae0ac5da2e0eb79e5bd9cd433d2a36b2a4f571f4f735acfe03aefaaff20ae0c69ba591a00bfc322f987f7f2db529662c86c2395b63a68c8384b4327450c2d6217d87fff619fd96f9ae910bd720096bcbd48a81dbc5c9149c4d2e2f9ad10ddfe059fbaf769e714454a1fdd2e39587eadfbd9a7e1926c3e311c724bcd963bc6280a7ba05eb193c176868bcba10d8dc7fc2193c3ef19d560e08ec555e657fe9032ce3db6a3a1ad2b5782812a13718ecdb53216bee4e2a658d90bd147b23f2ab1869b9d24eaf3aa08df38825798cfe0f4f4096d4acad4cea1cf2b10b8fe83dc977f7efbc83f471aad72bb8163f71a4753ff818e1ea4ef674c6ffb74bd57f487c74610eff459b43bd06304035deab41d00bd4b0c3be41f7c024d01d1ba7fd5785bb3d06eec5f1391ffd976d9031374bbaf527187fd2188d1b3034bfd5005fd3f70e9462ef684e000",
                 "110300011010001e7d6793bb0281476fb1cd5fe1f905f7820021c0c501fd15cddaf31b35cf70ec386f714c008dc01b8d38e79c55214387a3c3aca44c018d8004273408fda90175befca1ae4f24b72bb2f575792a216bfeaa80",
                 "d1010001101000180d2ad1b731bb900214c78b0fff015a1c0015db3e51ac3e40090a6f9f48fa9b8018409f80",
                 "d10100011010001ee7401595557aa0021c76001db5973accd2fa004a207000",
                 "b1010001101000180030282ff400043876002495973accd2fa0012a08740",
                 "b1010001101000180030282ff400043876001db5973accd2fa004a207000" };

            vpx_codec_ctx_t decoder = new vpx_codec_ctx_t();
            vpx_codec_iface_t algo = vp8_dx.vpx_codec_vp8_dx();
            vpx_codec_dec_cfg_t cfg = new vpx_codec_dec_cfg_t { threads = 1 };
            vpx_codec_err_t res = vpx_decoder.vpx_codec_dec_init(decoder, algo, cfg, 0);

            Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, res);
            Assert.NotNull(decoder);

            int count = 0;
            foreach (var frame in frames)
             {
                logger.LogDebug($"DECODING FRAME {count}:");

                byte[] buffer = HexStr.ParseHexStr(frame);

                fixed (byte* pBuffer = buffer)
                {
                    var result = vpx_decoder.vpx_codec_decode(decoder, pBuffer, (uint)buffer.Length, IntPtr.Zero, 0);
                    Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, result);
                }

                IntPtr iter = IntPtr.Zero;
                var img = vpx_decoder.vpx_codec_get_frame(decoder, iter);

                Assert.NotNull(img);
                Assert.Equal(32U, img.d_w);
                Assert.Equal(24U, img.d_h);

                byte[] yPlane = new byte[img.stride[0]];
                byte[] uPlane = new byte[img.stride[1]];
                byte[] vPlane = new byte[img.stride[2]];

                Marshal.Copy((IntPtr)img.planes[0], yPlane, 0, yPlane.Length);
                Marshal.Copy((IntPtr)img.planes[1], uPlane, 0, uPlane.Length);
                Marshal.Copy((IntPtr)img.planes[2], vPlane, 0, vPlane.Length);

                switch (count)
                {
                    case 0:
                        Assert.Equal("b66065dbaa3dafd75b65dba939afcf575ad2a63db6d85a68d8ab3cafda5a67b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b51010101010101010101010101010101010101010101010101010101010101010",
                         HexStr.ToHexStr(yPlane));
                        Assert.Equal("817e7f7e7f817d7e7f7e7e7f807e82a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a284848484848484848484848484848484",
                          HexStr.ToHexStr(uPlane));
                        Assert.Equal("807e7f7f7f807f7e7f807e7f7f7e6163636363636363636363636363636363637d7d7d7d7d7d7d7d7d7d7d7d7d7d7d7d",
                          HexStr.ToHexStr(vPlane));
                        break;
                    case 1:
                        Assert.Equal("b86165dba93db0d85b65dba93aaed15659d1a53cb6d85a68d8ab3cafda5a67b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b51010101010101010101010101010101010101010101010101010101010101010",
                          HexStr.ToHexStr(yPlane));
                        Assert.Equal("817e7f7e7f807e7e807e7e7e7f7f83a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a184848484848484848484848484848484",
                          HexStr.ToHexStr(uPlane));
                        Assert.Equal("7f7f807e7f807f7e7f807e7f807f6162626262626262626262626262626262627e7e7e7e7e7e7e7e7e7e7e7e7e7e7e7e",
                          HexStr.ToHexStr(vPlane));
                        break;
                    case 2:
                        Assert.Equal("b86165dba93db0d85b65dba93bafd15559d1a53cb6d85a68d8ab3cafda5a67b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b51010101010101010101010101010101010101010101010101010101010101010",
                          HexStr.ToHexStr(yPlane));
                        Assert.Equal("817e7f7e7f817d7e807e7e7e7f7f83a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a184848484848484848484848484848484",
                          HexStr.ToHexStr(uPlane));
                        Assert.Equal("7f7f807e7f807f7e7f807e7f817e6063636363636363636363636363636363637e7e7e7e7e7e7e7e7e7e7e7e7e7e7e7e",
                          HexStr.ToHexStr(vPlane));
                        break;
                    case 3:
                        Assert.Equal("b86165dba93db0d85b65dba93bafd15559d1a53cb6d85a68d8ab3cafda5a67b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b51010101010101010101010101010101010101010101010101010101010101010",
                          HexStr.ToHexStr(yPlane));
                        Assert.Equal("817e7f7e7f807e7e807e7e7e7f7f83a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a184848484848484848484848484848484",
                          HexStr.ToHexStr(uPlane));
                        Assert.Equal("7f7f807e7f807f7e7f807e7f817e6063636363636363636363636363636363637e7e7e7e7e7e7e7e7e7e7e7e7e7e7e7e",
                          HexStr.ToHexStr(vPlane));
                        break;
                    case 4:
                        Assert.Equal("b86165dba93db0d85b65dba93bafd15559d1a53cb6d85a68d8ab3cafda5a67b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b51010101010101010101010101010101010101010101010101010101010101010",
                          HexStr.ToHexStr(yPlane));
                        Assert.Equal("817e7f7e7f817d7e807e7e7e7f7f83a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a184848484848484848484848484848484",
                          HexStr.ToHexStr(uPlane));
                        Assert.Equal("7f7f807e7f807f7e7f807e7f817e6063636363636363636363636363636363637e7e7e7e7e7e7e7e7e7e7e7e7e7e7e7e",
                          HexStr.ToHexStr(vPlane));
                        break;
                    case 5:
                        Assert.Equal("b86165dba93db0d85b65dba93bafd15559d1a53cb6d85a68d8ab3cafda5a67b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b5b51010101010101010101010101010101010101010101010101010101010101010",
                          HexStr.ToHexStr(yPlane));
                        Assert.Equal("817e7f7e7f807e7e807e7e7e7f7f83a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a184848484848484848484848484848484",
                          HexStr.ToHexStr(uPlane));
                        Assert.Equal("7f7f807e7f807f7e7f807e7f817e6063636363636363636363636363636363637e7e7e7e7e7e7e7e7e7e7e7e7e7e7e7e",
                          HexStr.ToHexStr(vPlane));
                        break;
                    default:
                        break;
                }

                count++;
            }
        }
    }
}
