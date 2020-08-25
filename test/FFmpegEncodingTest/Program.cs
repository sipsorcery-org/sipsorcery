// ffplay -probesize 32 -protocol_whitelist "file,rtp,udp" -i ffplay.sdp

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;
using SCTP4CS.Utils;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using SIPSorceryMedia.FFmpeg;

namespace FFmpegEncodingTest
{
    class Program
    {
        private static string TEST_PATTERN_IMAGE_PATH = "media/testpattern.jpeg";
        private const int FRAMES_PER_SECOND = 30;
        private const int TEST_PATTERN_SPACING_MILLISECONDS = 33;
        private const float TEXT_SIZE_PERCENTAGE = 0.035f;       // height of text as a percentage of the total image height
        private const float TEXT_OUTLINE_REL_THICKNESS = 0.02f; // Black text outline thickness is set as a percentage of text height in pixels
        private const int TEXT_MARGIN_PIXELS = 5;
        private const int POINTS_PER_INCH = 72;
        private const int VIDEO_TIMESTAMP_SPACING = 3000;
        private const int FFPLAY_DEFAULT_VIDEO_PORT = 5024;

        private static Bitmap _testPattern;
        private static Timer _sendTestPatternTimer;
        private static VideoEncoder _ffmpegEncoder;
        private static VideoFrameConverter _videoFrameConverter;
        private static long _presentationTimestamp = 0;

        private static event Action<SDPMediaTypesEnum, uint, byte[]> OnTestPatternSampleReady;

        static void Main(string[] args)
        {
            Console.WriteLine("FFmpeg Encoding Test Console");

            InitialiseTestPattern();

            var videoCapabilities = new List<SDPMediaFormat>
                {
                    //new SDPMediaFormat(SDPMediaFormatsEnum.VP8)
                    new SDPMediaFormat(SDPMediaFormatsEnum.H264)
                    {
                        FormatID = "96"
                    }
                    //new SDPMediaFormat(SDPMediaFormatsEnum.H264)
                    //{
                    //    FormatParameterAttribute = $"packetization-mode=1",
                    //}
                };
            int payloadID = Convert.ToInt32(videoCapabilities.First().FormatID);

            var rtpSession = CreateRtpSession(videoCapabilities);
            //OnTestPatternSampleReady += (media, duration, payload) => rtpSession.SendVp8Frame(duration, payloadID, payload);
            OnTestPatternSampleReady += (media, duration, payload) => rtpSession.SendH264Frame(duration, payloadID, payload);
            rtpSession.Start();

            Console.WriteLine("press any key to start...");
            Console.ReadKey();

            _sendTestPatternTimer = new Timer(SendTestPattern, null, 0, TEST_PATTERN_SPACING_MILLISECONDS);

            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();
        }

        private static void InitialiseTestPattern()
        {
            _testPattern = new Bitmap(TEST_PATTERN_IMAGE_PATH);

            FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_DEBUG);

            //_ffmpegEncoder = new VideoEncoder(AVCodecID.AV_CODEC_ID_VP8, _testPattern.Width, _testPattern.Height, FRAMES_PER_SECOND);
            _ffmpegEncoder = new VideoEncoder(AVCodecID.AV_CODEC_ID_H264, _testPattern.Width, _testPattern.Height, FRAMES_PER_SECOND);
            Console.WriteLine($"Codec name {_ffmpegEncoder.GetCodecName()}.");

            _videoFrameConverter = new VideoFrameConverter(
                new Size(_testPattern.Width, _testPattern.Height),
                AVPixelFormat.AV_PIX_FMT_BGRA,
                new Size(_testPattern.Width, _testPattern.Height),
                AVPixelFormat.AV_PIX_FMT_YUV420P);
        }

        private static RTPSession CreateRtpSession(List<SDPMediaFormat> videoFormats)
        {
            var rtpSession = new RTPSession(false, false, false, IPAddress.Loopback);

            MediaStreamTrack videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, videoFormats, MediaStreamStatusEnum.SendRecv);
            rtpSession.addTrack(videoTrack);

            rtpSession.SetDestination(SDPMediaTypesEnum.video, new IPEndPoint(IPAddress.Loopback, FFPLAY_DEFAULT_VIDEO_PORT), new IPEndPoint(IPAddress.Loopback, FFPLAY_DEFAULT_VIDEO_PORT + 1));

            return rtpSession;
        }

        private static void SendTestPattern(object state)
        {
            lock (_sendTestPatternTimer)
            {
                unsafe
                {
                    if (OnTestPatternSampleReady != null)
                    {
                        var stampedTestPattern = _testPattern.Clone() as System.Drawing.Image;
                        AddTimeStampAndLocation(stampedTestPattern, DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff"), "Test Pattern");
                        var sampleBuffer = BitmapToRGBA(stampedTestPattern as System.Drawing.Bitmap, _testPattern.Width, _testPattern.Height);

                        var i420Frame = _videoFrameConverter.Convert(sampleBuffer);

                        //byte[] i420Buffer = PixelConverter.RGBAtoYUV420Planar(sampleBuffer, _testPattern.Width, _testPattern.Height);
                        //var encodedBuffer = _vp8Codec.Encode(i420, _forceKeyFrame);

                        _presentationTimestamp += VIDEO_TIMESTAMP_SPACING;

                        //i420Frame.key_frame = _forceKeyFrame ? 1 : 0;
                        i420Frame.pts = _presentationTimestamp;

                        byte[] encodedBuffer = _ffmpegEncoder.Encode(i420Frame);
                        //byte[] encodedBuffer = BufferUtils.ParseHexStr("000000016764001eacd940a03da10000030001000003003c0f162d960000000168ebe3cb22c00000010605ffff9ddc45e9bde6d948b7962cd820d923eeef78323634202d20636f726520313631202d20482e3236342f4d5045472d342041564320636f646563202d20436f70796c65667420323030332d32303230202d20687474703a2f2f7777772e766964656f6c616e2e6f72672f783236342e68746d6c202d206f7074696f6e733a2063616261633d31207265663d33206465626c6f636b3d313a303a3020616e616c7973653d3078333a3078313133206d653d686578207375626d653d37207073793d31207073795f72643d312e30303a302e3030206d697865645f7265663d31206d655f72616e67653d3136206368726f6d615f6d653d31207472656c6c69733d31203878386463743d312063716d3d3020646561647a6f6e653d32312c313120666173745f70736b69703d31206368726f6d615f71705f6f66667365743d2d3220746872656164733d3132206c6f6f6b61686561645f746872656164733d3220736c696365645f746872656164733d30206e723d3020646563696d6174653d3120696e7465726c616365643d3020626c757261795f636f6d7061743d3020636f6e73747261696e65645f696e7472613d3020626672616d65733d3320625f707972616d69643d3220625f61646170743d3120625f626961733d30206469726563743d3120776569676874623d31206f70656e5f676f703d3020776569676874703d32206b6579696e743d323530206b6579696e745f6d696e3d3235207363656e656375743d343020696e7472615f726566726573683d302072635f6c6f6f6b61686561643d34302072633d637266206d62747265653d31206372663d32332e302071636f6d703d302e36302071706d696e3d302071706d61783d3639207170737465703d342069705f726174696f3d312e34302061713d313a312e30300080000001658884005fe89f8677b600fd616763bed58622ce7651a4f1cda99202677e71f334ec6194d4a6485e585812544cbac6755f3e5a4908543c2eab60170fc741f23ef68edc93ff16bfa36c98d7549ea2e1f74d1bafe3cedb15a1b08bb86d804455f4f87d5b5c8ac84edabaa228ef41bb002d16db3be5223900e12129b18fd7b186d33d5f2043ad324200f476fb48272ec5cd166e98419f6c1297e6033031f4fa0ecf2ed7d0c6038e1598dc7923d15b4d918f00bbe7e4dc1296494b0bf6f209c3e84b8bd37c0283ec289b865f471aa8f93495c3cc7ed16332f88dc0bc48b5c3801769dee0dd244492271a89202f6d264031de9810eea8098e9075f56b8a375233ec48651e70ceaee5f0d40922762c46f40681dfdf9c3bd54cffc61a7b1c6c030723be13381247f3ff8de05d21d0053b3022815e6c4b236c84940e4a5de6db03b8f1731279c921bf2a0c92ba62000deaea30e23b2067b2da7c6583cc0d81e652c3fb56a8ae4ffed900983481bf7d14a208748d8ba767ed1b5a68b62c8f548ad5d1857c283b9ac4310767f54a06d6341d7a6a0c05bd1493f8b54b1992f0e528030571f494cf9857c419288115cbc872518338ac9497454439fc28632913f04a514653f1dbae98db306f207aef6467e5cbf977f5936880012e69160cdcab02d91d8fcf8d760cb459bec58b688aadcd9704d47d7e335edd0d14552b305148a2b43681adec4ba9320c3d63d7e742d43d77c08c4c9e46c28cb4f2e55f5384227ac138d3eccdc0dd2884075cbb329f37843e09df7a65e2fcc9555e579840c702385d53a97b021074ede6ed5cc56fe5a43e41751de604e4c1397b60463cbe2393ebce670fb6b56ccdd93f05f989376115d72f30d0d1b7956e56d85885d398f88869eb9ec752048429d30b952c5e8cd1dd624f80a3c8102c37e5b05380b10873ea7d3fcee4a4cf78dd653c62102d4428ee7ef401d2cf74e07b23e26a16c9db26ec390d86c504879e25c698992fdb0628ab4ff80d95125d992fd9ab56433d4c3c7f9294bf3f33e60de3019a9881b86c7a84c9e46cd5a2283dafa974652758bf232d401ec692dc14611d0495e804005a35b474cd63b874e9ffa0166133bc167391dc5c36bc18d90bef2bcf6d7df8f960463cbe2393ebce66e3f643b54afe41d1fca0ff7cdb115d72f4598b6746adcadb0b10e828a6133767c69f2273e853a60b41e590f9f0d004f5475f23e098f4122ece175e98d64a25f26ac44dd65c4618f1b779f0d6e57744aee7835729e311a171be863c1b2df729d000335e412e5b62fbdf60c97eedef55c769b947865ba9a3ccad2411722fe24e15c701c1cc7f875310c7f3367f029042ae2fdaaf3b117fbdd61a6f975e3e32184d21b0c2438b63764015d6eb4b010f821af01a58e881351a5d10a3dff7d754665530cb1d6464e122a3b94e2560dcd9704d47d7e32703c820259d65f0b165686d035bd89bcacba5363d7e742d43f3c4d719bb3e27491c85694e9bb54b18bf9f0d0025e4b1344792ed9ed5b705d7b048a19b83341d580440fb417cda076bdc0a5e996c04a96514311fc938cce9866a812810f519e097defb064bf76f7aad7c8b52d052cb12d364571208b917f126ecc75017f88b2a98c40ff2ccfc4c9e4764eaa353dafa99dddf597599569569e0285afe833ce80f471533fb71727714b151f867708d447d86200111e3e23e2fd9193b068ae0cb9a5ed5604295da0df192f72f03d14d6846571208b917f12709726012e63fc3a988208549ae33767c625f3909da846eed67365f369059c1636dae0a30935216cb7d3827f2454180348f111bc34375c81a4a8acc61e862cf9c760a73d1b2e536b2274000a64b3f1fa5e5eab3cbe2393ebce4adce11c6fa605b3e824cf9b622bae5e8cb697eee987ad616245f3e1f132791dd18dc88f6bea73e05acbaccab500712b0ec4306644fc66a0100166af1e219f190b596898bf13a5cd73cb68bb249603bc487705be58118f2f88e4faf39264330a406aa7d4242af01db0fe0b97a337cedada4bb9ebbff66cc9af64f22328a9e981758f76e936edd95a93aa0fc6dae0a30912d9c8d61e6f7eba5924c5e650d462a401104de6ea146111326a9a130356bbb66d927c938ccdb90e80138a51832fc97defb064bf76f7aad8cb052eed750fb787474cced9cbf893765f51b4717eee02cd869eccfc4c9e4471d65654f5dabc0f387ca5fca870c6bab5fd067e1d03bedfe6afa61f4b9fc5f6a725b6ba2ed537b74adae12ba909651c088a0000600e5a99f2c2ab7365c1351f5f8bc12708dafafa28fc183890cb86d035bd89be14b7e32bd7e742d4c22526b8cdd9fa6e3ce42b4a74f0527362fe7c340f2691f998b1cbdd10b5070baf45611282f18cae646e49a07ce550af5930043d728c0bc2e75b43d0d960766000010f3e1648304bef7d8325fbb7bd571aae2117725c06d3b98946899b236ef71520ad12e63fc3a98834a999f8993c88e3acaca9ebb565ad70f94bf950ec373c9f5c146197f3bb6e64793af2190341263531ff0b4fe0202c57d83b392fe164019c487981b169f5e1afb1e5f11c9f5e722c8cf82247f75a8005e681403b61fc172f3171cbf974c3d6b0b13edee334434f5f00568769da8469526b9b359b482b51f04ee5c28e9aa59f0c802badfee1d2f9e3763b47a898260e95e97a865be63dc9ab693f2ae97d43efa556e6cb826a3ebf19956472192b5f9e936cc2ae42f662d2f67343112f6bf6831e2c67b32010b2e43fba2d4bbb5d82a2f239e827e08b8364af2d8ba50dedff0976c07fae005428f34316369d7486cf5fa1527e2f975e3d2910a63961ac0c0942991f5fc2d5f046aa8d41138e574a9bdc81bc735d484b28e05060001e73f1809b8ac1b9b2e09a8fafc5dd73847797a00944c83d522ee32c1e606597a1e1b4590692a93defbf9d4d7c8f297ee447b5f52a7e43d68c7fcb518be0aab61704ffa33eb4f5abbeb8107f9b6b55db1e8902f580c8b4fecd6c350d64fddaf26d110a9a6342f6ab0214aed06f8c9a91292317b355150d133646ddee29f1206be22caa63109a0a953ec9e479209a4b8bdda7ca856b34af4b768e7d240421f04c8d10d0670bb0fef1893c210ee0ad01b47974d53d1b8a0e678e8009b51ad99fdd7dcac5c98aba3e2d7e47f4436f353ea62597b2f812be6d88aeb97a3402edd9a4bb9ebc001ded08bec9e4779a9a3f72afa9df9ee6ca79196a1cfd23c6a1f4e9bfa77dc050b1badbd0ad6c4803812e3e745165c95df8a100b83cf67f30a381b8eb276a188aadcd9704d47d7e2ed445cecdd9f71e27e39091771960f30162056649e6553ffc42192d1d3ec9e478ed9e51f3b1f1e889b59b4ff9695f95b075a0cfc3a8781d1f5fc2f8b508537b01c911aba80e0f7f6ade4db9fe1aa0e09873834aa67fd8aadcd9704d47d7e32711edbb3926513127170da06b7b137c022fe5a2c8349548f64c7c099bb3f37554b410a72bfd2163afba18455199378e17612413c22e64793b84fbb5ae812be93c02295949ddad71eeb37cb5d10ce41dae168a8204bd56797c4727d79c919463a6ddba17833e82a886ec22bae5e630697fa5c6dc8f74633ed16ffb2791e24a693bd45c9b3b8a18f60eda105618af3421474872181d1f5fc2e8bd474277fec0c66eb0fde2b879e4516a2bb46ad25f23642eac000142e36275078416cd9704d47d7e2ee7afcbff150ca08eaa7063090e2c1e60647e9cc67ccaa7ff8847c5a117d93c8f247347ee55f53d5d86b39b32ad445a51bcc4305ef9f4efb80a1638041208ee1ad0941d4d2b5621efabaa0f76e01c83550d89d431155b9b2e09a8fafc5c3835fa1b44bebaa9c848bb8cb07981948b3324f32a9ffe21c3eae2d1d3ec9dbf3c3c61776dad4eea9a7d3960fcb89067ed861230dd1ca200aeb7e14f1dfb161bfccad72cdcb432b63970829f3c27b00000df2ab81fcec556e6cb826a3ebf177e48cfcee0c31399538618487160f303353247e98f5f9d0b5289ee57f64f23bb9cd25b957d379beaca793d093d5bf9491385d866c0cee402002e355d17e5268a85c06a4b7f014325db22fbaffdaec23d71fbae530c5a1021d8148b66e3fb4960463cbe2393ebce45ac55915f9f7dd1f6f827be6d88aeb97955334eb971b723dd1967ea63132791e5ea68dcf6be9dd11380fabfebbc5a20b161a0cfc3590c1b4015d6ef84bdd8990c62578232d46c68a49cd5f4558b48340f84ed466155b9b2e09a8fafc5cb277537e978923b34b35c3681adec4af0c2cca7ccaa7ff885065a5054d7c8f375349dea2e51e37bcd9a7232d2dabe69060fdc28def0f575e6d4b76c944a9033069b846fac6797b7a6e719078ad5f4571e541c2eb2c90ad17b55810a576837c5a37ac08414dee67a508b1b33b672fe24dca3a65ea663fc3a98881d565a117d93b83ec8e51e222fc41ce1a82975e3d2ff540cea18c084b472c802badcd41b0d55a40cde3d59eba888ae543e17d86a1b6d517d00000b0a6c5ca7e420b66cb826a3ebf1779cdcfcee16528d6f1be8c2438b07981960a445ed1641a4aa525ad08e132791e8efdcb3e763e28b0f55cdcccab513335702e0a30f7e01b5a6803f9f1292b5761b0c6f23f7afb0e536891b2e5381680001119c6f453af231598d4563d1d501e9b0a102dfeb01e63855b51bfc9e34d3ff6d3e374183e5c4264143604c9e479df432c3dafa9f26655e93c8cb510ae56f310c19d01f8e1df4e09ef52c9b08268513e566c0f7f6d2364799227d8faf5eb4e1440004b3d1cad00a97aacf2f88e4faf3929fa62c2de6272d268377cdb115d72f46821f7c6e36e47ba31be5a3a7d93c8f0b5349dea2e51fa67cd9a7232d44ac71c5c43067456780201000a575fbb1fc8376f6a12140b88bbdbff4b965cf101a27b80c15c62b9b3b0831a8ce8f76a709d7ec1890bdd092d7891246899b236ef6df88ae7322fddc059b19768b7fd93c8f125349dea2e53d0eb6b369ff2d292ac307ee147482b181d1f5fc2e19331908bf382bce41df292d2ea5c0664239ec62a13eda0b27fd8aadcd9704d47d7e2eda7de728b848caa8a7063090e2c1e603a476231e8b20d25526e4b422fb2791e4ca68fdcabe9be7430897876d082bcc143f10a3a451bc028fafe168614c2cbf2f495f3a136eea1e78a7168e309ea1b8163a9f9082d9b2e09a8fafc5d5330c98fd85c531388f5dc4a65c3681adec4af2386fb74590692a9304fdf17d93c8f00516767b5f526dc85737332ad2c1dc9ec4428e9936420ff357d323cae70bf97cf608456c2b6b48ed379bd8a6709e1f368ce001c5d303a9eb882d9b2e09a8fafc5d505724801b199d77127170da06b7b137f0a3cb5a2c8349549b27c54fb2791e2a6525b957d37706622661db4230e3ceb5382ec34b86675cc8f27819629ce218ece5f088aff6a34d53d1b8a4640000c3e3e119f971afb1e5f11c9f5e724f1ab446bb6b7b6b5c0ebe6d88aeb97a36a94e93a61eb5858a49d7640a6be47a9d372cf9d8f89fafdae6e6655a890ec974e0a30f5fc886b4d007f400eb459bd73ac9a491e5ca105252674b898325fe5ebf5e7dd0800479b39fe02533c25cb0cc5fa025a46db088d7b5a09570d5b4d0f485bb16c38108780d61e730027294a1f1cf8dab11ffff7c41551ee757ddff993a03622a7e10b591b7b4821f2d05baae32b8a930b4e379e82132a25985d6c93ae2af0235fe000369a441a3bca02997c65646ddedbcbd31a1c45954c6207ac1ee132791dc046bfb957d395c7bf9dd7a1278727066b4819f86eb8b30802bade339e9604c0441b117141d0b0299881b0ed7624b62030c7bf94d83b22e60eeb8a033940f44db6271bdc740d6f62572a41d0d5ebf3a16a4fb31e54d7c8eef5dcc4f6bea75d0bad267fcb4a5624799033f0e38614200aeac7eb1e00b011833aa57b3d46dfdc45708dfb5ea55013bdcc857afaaf5bc43259ee1f1bbb0e4170b28e46792896c592d974542088a9c5c979ad60949e0f791761f0874fdd2bde624f15c68f45fbb80b36363cd95f19bb3ad83d3e52758f0b498fe052967620ca8a790035810a3306d00575bf3378cba74c37f2132a7bf7d6445d7e334b282706179595aa7d00c98f0ba24a476a330aadcd9704d47d7e332aae8ae84383e93d8858e8b43681adec4af62c394f9954fff1065bbc21099bb3f2cb68b52d29c9c98408d501ba1052dc0d73409f483de9efa0588b49e7e8b6ae728e2964605c61b8df1127e5420476495ca360059af5f3c3b485308b8007bfc0d2d3da3f5aa9f06c20f3835c1c833efb4cc5ddaeaa6ec3090e2c1e6065be222d68b20d2552872b3b1fb2791e89e6925cabea3cf189b333fe5a896be4e8e0a30d821ded7323c753a8622f4e6edcb5d3e76c98019803097f11b5c46697f9492e677420c6a33a3dda9c28035324a3c1fe8da195150d133646ddee46258dbc8bf770166c8850653ce733f543458aad29d2474c172fb3477691e1475843e0994743121c2ec3fd280aa1c987f61f8cbedb048316f2d04708552ec1dfaf0926245c1ce51a6f60e520a26c359e3f6432b2b17262ae8f8b5f5f327f3064e9179fe5a301403b61fc172f2a0680b8b49773d780053fbb1fb2791dd8aaa83cec7c77fb026a67fcb4a8ff92e173d70a34119cd579b52df62e9c8a56b0921e39962f5a67db8a23de7d44d263a875083fda0e22f6ab0214aed06f8c967177686cc19d6e01045c8bf8938951cd1af88b2a98c41308ca9e33767c6f790ed5a5395e8bb11d03d7422a18a911087c1322c7773705d87bf4eb0a223b0717ea440304943372e3ae57c75e8d66820b7c0839589900002536f7667d11f72b17262ae8f8b5f9e5f4d2639748bad7028076c3f82e5e8e1c54f2e987ad61624a9b3b1fb2791ddb2aa83cec7c773e39b333fe5a7ffd4db5e218331a63ba2e64793bc26d7a495017342a35a1e57870405354f05d272be909296e1453e7960463cbe2393ebce495f6d3102303e1e0d028076c3f82e5e8b859171692ee7aefff3663704cdd9f9f9443b5694e57c2d8c740f5d08a8a5244421f04c8be7651c1761ef12800d2ef18c6f62390ff516762980bf6a1d277bb09b3d1b22f40c16ec1023ae59412d95958b9315747c5afb0fdbc6d4b879f7ab413c4376115d72f29c748b8b49773d77ffab31b84c9e4776caaa7f3b1eedac4863cc3b6840cb1677410a3a41e23964015d6ee3bb78f67983aa7cc0eedab1352ed0f9cc7e4ab0000973e1ea1fbc2ab7365c1351f5f8bc2a44d3956d2d7def8df46121c583cc0cab491ef2bd7e742d50c552d78cdd9fb079a6908539368f101d1dd0c231e2c9b0987c13b309e3ee05097e7818efa5a0f136a9988b62ddb8a2bd299e59d3bc687226b91dab720000215fdc2b3fbafb958b9315747c5afcec833f3b69794f52c2b781db0fe0b97a370efbd9a61eb5858a26cd08bec9e4497625634f5dab37e8c05ca56a4ec7868de621833b143bfae64792dd857a3edcdea00e0a45dcee0acd67a3645e894102237ff145402a5eab3cbe2393ebce458e76766ee5f4b54b0af8076c3f82e5e54d098bd749773d7802e160e679ce67f057dd0fa14e4d9e8c078774308353f5940f13409f1059f33ebe002adb44f9832e493b3b512855930686a5afa90594acdaa00357d0ae8811668636e0d4c1afb1e5f11c9f5e722daed4bbb7066e838857c03b61fc172f31daf9d46e36e47ba379d5e9faa7d93c78091aa263c91fe97387ce20ce8715abac1fb851d32f82bc200aeb77204203b5134ba7a5b062ace99deba909651c35085c061940f50ffb155b9b2e09a8fafc5e0738471cdfd08d529c18c2438b07981cc5e7bf2d1641a4aa52b36845f64f23d29d51fb957d4fbc51cd94f232d44a4d3a4ae0a30d56bfd4402002f0bfae4615e86b0b0bd01370d3ecaa1ac59fc7c115884b5cbdc4b60c2d724bcdac218722622c178968dbd95a6528c9dbf72b6ec9acdbebd0c155faabbfb4485787cdb115d72f462c9dbd749773d77ff59b474fb2791dd72aa83cec7c76cd6c35565c778059e51ce0a2f0a053b573f458e1107d59c37d9e02a366f07df791152347ab93847887d8351e360ec8b983bae085aef2ccc6b5899ef71d035bd895e16180cf32a9ffe20ee507b535f23bf5555cf9d8f774578c3f2ffaef0e82d8dad067e1d396148fafe175d894ad41444d0b25f8ba6fc5ad88ebbe31a6d1795863dfca6c1d917307773a84d25785adf5e2feef3e80a69f194550c29083fe8f336f29d3601940893c65f0ec3434117431f33e6d9f046aa13f045c1b2579755d48a7c3544bb5f6f974dc3e954ebb8fee5e5c996a989d3d87d6c8ec4d2311f161ac0d52c645fcd5f4fadb48e09f4347d85a8e5f1c4f4eb53b93f7ea4a12e1cb3e7f6a67c05eff6fc46edd40c960dcd9704d47d7e32ad86c9ab18384e9842ffff0c2438b07980ecec9b43a2c834954a223e3e68869eaf0b8ebf63234f39e1fefbb979b2692b9221831c1a573041ee6251cb86da02b3932b56dcaa8076a37fe67949a863f4c590208b176ea109fad54f8361079c1ae1224a28ffe1b36dc716332d0da06b7b12eedf6fe457afce85aa804d08bc66ecec9d91b92cd9f6a1d5202c71d3af185d246b2040b1a4e99dbe8588a54810e60a1f4a687ef6f29602a586dd3c7b25533eb9a3c6643498399f2d24d240ac0b750858b2b43681adec4bbcc1d29b1ebf3a16a8813e1f132791e4bfb911ed7d4aca11867bc8cb4b2ef0b5fd067e1efb82b242128d562d2785378f1c9d288453b93c593cea732e0ede777c6da51ac9f30d1e3321a4c1ccf9692f53d64b73493290dc8bb8cb07981ca85a601e6553ffc41f57d9f8993c8dae76a63d45ca6962a78cf79196a04605465c1461283133500800b918f95c7b663bebcc0b2ddccd7023fd54a7ea2111a72afbf4446bec797c4727d79c94e032f00155de10b4a1267cdb115d72f297e3cb5b49773d780016a935c66ecfd2821f76b4a72c74ab11c83d74206788175902058755d88bb325d6fd065f6efaea76942d65f4d87e0135eb326b96423ce289e98b3c46b0556e6cb826a3ebf194142092abbc4398da06121c583cc0e3529300f32a9ffe20cb69fa290431f250a3353dafa9d34609a31ff2d421bdc346c2e09f82364fb38aeb7348541a866485d72d7fba7bce39fd4a82468849cc3dd6ad4f2e708304bef7d8325fbb7bd58df51f89dca4adb1208b917f12713df7512e63fc3a98875c2cfb3f1327869de2164c7923d3d1402dbf098d4d8c8a8cb828c250e7e6a010017789be48e069ebc1f7e9e7795f98ac429aefd96200029985f7502097aacf2f88e4faf3912e99289fecfe6c4f80901db0fe0b97a3ab6edada4bb9ebbffd9cff4520863f512ee447b5f53cf7c6b2eb32ad3ff9a479d43ea5f76776dcc8f1e46ceb4ab66182a09feba509c5161edfae23c2c7a84f8911dcdd8331c6ad8f119d1eed4e13a71490f6b568e13c6159600822e45fc49c24ba8db08bf770166c6bfa329e7399f990e43b5694e9953dab9699a3bb380f522210f8264e29328e0bb0fb62ca16b2745d97e6eb81b4cb18de29ec12d38f2e1240b8635b33e88fb958b9315747c5afb2ecb2a9e3127c197c0500ed87f05cbd184e76e2d25dcf5dffef541704c9e478228b4972afa9f01ceb9a59956a0ffdb246eee0a26f08000380a163e7392c9337e8bf763948eea906a289a4aa1f1d503dc4a237ec7905b365c1351f5f8bbdf39d65efbd8778ecd8938b86d035bd895ca1fa5ad1641a4aa49016763f64f23b894d24b957d4f516d36667fcb4a98d2c2b5cb8519f86490bcda96fb071f30ed5b628d92b1f5b3a408545faa47484739200000481fe9d9311db6ab0214aed06f8b90f031073c0e36d1dcc54344cd91b77b8a5cf2f3731fe1d4c41854a54f19bb3f3e1b43b5694e4cf86863a07ae8460f252e4d11e4bd650bb470bb0daa83286f28d8f0a874fcb5776f0babe0ee12bfb91b456000026f171e3f5e32b2b17262ae8f8b5fa2da20bae6e75a63744926ec22bae5e8ea65ef2e987ad61626e5a953ec9e478239a4b72afa76ed8863cc3b68460a25f13e0509471cef1b991e4f1de917e90fa133d793e55aca3735cf718c8ef600008461b169f971afb1e5f11c9f5e725f644d2653d6051c2b481db0fe0b97a377845cfa4bb9ebc004c2cf8bec9e477f39a3b3dafa9e2a3e6ce4ff96a1fe29cf37051846dceedb991e4f121b78440475092b9ab56f1e90c0ecb5f17c1eb143926c05f585a29f5e1afb1e5f11c9f5e725582e772569f4791d3e827886ec22bae5e541fb171692ee7aeffd6ec194f39ccfcb71b9dd694e575bc00e5ee86103178925e409f484a4d4a502c4598b0ac156cef3960965d1aeb9ee3f59f9c6e35244d36d2a6db9e4ee60327b32a1fd9d1e3321a4c1ccf9683e22d4bbb7ff4ecc0c90245dc6583cc056f14463d1641a4aa4989528fd93c8eda5346e7b5f5353b16b391ff2d2a4be5315f04819e7ca49caf36a5bee82a3f594a8dcee9cf2d1072b497c1ea57cb48217e8ed90ad17b55810a576837c5bc86e76b7857035e24432676ce5fc49bb474cbd4cc7f87531056ba91d4d7c8eef5346e7b5f4d5f500c7c876d082be5587b70a3a42ca395fcd5f4e5287583748b5ab1ca688d");

                        if (encodedBuffer != null)
                        {
                            //Console.WriteLine($"encoded buffer: {encodedBuffer.HexStr()}");
                            Console.WriteLine($"H264 encoded buffer length {encodedBuffer.Length}.");

                            int zeroes = 0;

                            // Parse NALs from H264 bitstream.
                            int currPosn = 0;
                            for (int i=0; i<encodedBuffer.Length; i++)
                            {
                                if(encodedBuffer[i] == 0x00)
                                {
                                    zeroes++;
                                }
                                else if(encodedBuffer[i] == 0x01 && zeroes >= 2)
                                {
                                    // This is a NAL start sequence.
                                    int nalStart = i + 1;
                                    if(nalStart - currPosn > 4)
                                    {
                                        int endPosn = nalStart - ((zeroes == 2) ? 3 : 4);
                                        int nalSize = endPosn - currPosn;

                                        //Console.WriteLine($"nal: {encodedBuffer.Skip(currPosn).Take(nalSize).ToArray().HexStr()}");
                                        Console.WriteLine($"sending nal length {nalSize}.");

                                        OnTestPatternSampleReady?.Invoke(SDPMediaTypesEnum.video, VIDEO_TIMESTAMP_SPACING, encodedBuffer.Skip(currPosn).Take(nalSize).ToArray());
                                    }

                                    currPosn = nalStart;
                                }
                                else
                                {
                                    zeroes = 0;
                                }
                            }

                            if(currPosn < encodedBuffer.Length)
                            {
                                //Console.WriteLine($"last nal: {encodedBuffer.Skip(currPosn).ToArray().HexStr()}");
                                Console.WriteLine($"sending last nal length {encodedBuffer.Length - currPosn}.");

                                OnTestPatternSampleReady?.Invoke(SDPMediaTypesEnum.video, VIDEO_TIMESTAMP_SPACING, encodedBuffer.Skip(currPosn).ToArray());
                            }
                        }

                        //Console.WriteLine("Press any key to continue.");
                        //Console.ReadKey();

                        stampedTestPattern.Dispose();
                    }
                }
            }
        }

        private static void AddTimeStampAndLocation(System.Drawing.Image image, string timeStamp, string locationText)
        {
            int pixelHeight = (int)(image.Height * TEXT_SIZE_PERCENTAGE);

            Graphics g = Graphics.FromImage(image);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (StringFormat format = new StringFormat())
            {
                format.LineAlignment = StringAlignment.Center;
                format.Alignment = StringAlignment.Center;

                using (Font f = new Font("Tahoma", pixelHeight, GraphicsUnit.Pixel))
                {
                    using (var gPath = new GraphicsPath())
                    {
                        float emSize = g.DpiY * f.Size / POINTS_PER_INCH;
                        if (locationText != null)
                        {
                            gPath.AddString(locationText, f.FontFamily, (int)FontStyle.Bold, emSize, new Rectangle(0, TEXT_MARGIN_PIXELS, image.Width, pixelHeight), format);
                        }

                        gPath.AddString(timeStamp /* + " -- " + fps.ToString("0.00") + " fps" */, f.FontFamily, (int)FontStyle.Bold, emSize, new Rectangle(0, image.Height - (pixelHeight + TEXT_MARGIN_PIXELS), image.Width, pixelHeight), format);
                        g.FillPath(Brushes.White, gPath);
                        g.DrawPath(new Pen(Brushes.Black, pixelHeight * TEXT_OUTLINE_REL_THICKNESS), gPath);
                    }
                }
            }
        }

        public static byte[] BitmapToRGBA(Bitmap bmp, int width, int height)
        {
            int pixelSize = 0;
            switch (bmp.PixelFormat)
            {
                case PixelFormat.Format24bppRgb:
                    pixelSize = 3;
                    break;
                case PixelFormat.Format32bppArgb:
                case PixelFormat.Format32bppPArgb:
                case PixelFormat.Format32bppRgb:
                    pixelSize = 4;
                    break;
                default:
                    throw new ArgumentException($"Bitmap pixel format {bmp.PixelFormat} was not recognised in BitmapToRGBA.");
            }

            BitmapData bmpDate = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, bmp.PixelFormat);
            IntPtr ptr = bmpDate.Scan0;
            byte[] buffer = new byte[width * height * 4];

            int cnt = 0;
            for (int y = 0; y <= height - 1; y++)
            {
                for (int x = 0; x <= width - 1; x++)
                {
                    int pos = y * bmpDate.Stride + x * pixelSize;

                    var r = Marshal.ReadByte(ptr, pos + 0);
                    var g = Marshal.ReadByte(ptr, pos + 1);
                    var b = Marshal.ReadByte(ptr, pos + 2);

                    buffer[cnt + 0] = r; // r
                    buffer[cnt + 1] = g; // g
                    buffer[cnt + 2] = b; // b
                    buffer[cnt + 3] = 0x00;         // a
                    cnt += 4;
                }
            }

            bmp.UnlockBits(bmpDate);

            return buffer;
        }
    }
}
