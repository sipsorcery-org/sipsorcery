using System;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using Xunit;

namespace SIPSorcery.UnitTests.Net
{
    [Trait("Category", "unit")]
    public class RTPHeaderExtensionUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTPHeaderExtensionUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void RTPHeaderExtensionAbsSendTime()
        {
            // Ads Send Time extension use always DateTimeOffset.Now for data
            // So to test Marshalling we use
            //  - the static method AbsSendTime() used by AbsSendTimeExtension.Marshal()
            //  - and a specific DateTimeOffset value

            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);
            
            var extensionId = 2; // Id / Extmap of the extension

            var time = new DateTimeOffset(2024, 2, 11, 14, 51, 02, 999, new TimeSpan(-5, 0, 0));
            var bytes = AbsSendTimeExtension.AbsSendTime(extensionId, AbsSendTimeExtension.RTP_HEADER_EXTENSION_SIZE, time);

            Assert.Equal(0x22, bytes[0]); // 2 for Extension ID and 2 for Length (AbsSendTimeExtension.RTP_HEADER_EXTENSION_SIZE - 1)
            Assert.Equal(155,  bytes[1]);
            Assert.Equal(254,  bytes[2]);
            Assert.Equal(249,  bytes[3]);


        }

        [Fact]
        public void RTPHeaderExtensionAudioLevel()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var extensionId = 2; // Id / Extmap of the extension
            var extension = new AudioLevelExtension(extensionId);

            // Create an audio Level
            var audioLevel = new AudioLevelExtension.AudioLevel()
            { 
                Voice = false,
                Level = 80 // "01010000" in bytes representation
            };
            extension.Set(audioLevel);

            // Marshal
            var bytesMarshalled = extension.Marshal();

            Assert.Equal(0x20, bytesMarshalled[0]); // 2 for Extension ID and 0 for Length (AudioLevelExtension.RTP_HEADER_EXTENSION_SIZE - 1)
            Assert.Equal(Convert.ToByte("01010000", 2), bytesMarshalled[1]);

            // Unmarshal
            var audioLevelFromBytes = (AudioLevelExtension.AudioLevel)extension.Unmarshal(null, new byte[] { bytesMarshalled[1] });
            Assert.Equal(audioLevel.Voice, audioLevelFromBytes.Voice);
            Assert.Equal(audioLevel.Level, audioLevelFromBytes.Level);
        }

        [Fact]
        public void RTPHeaderExtensionCVO()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var extensionId = 2; // Id / Extmap of the extension
            var extension = new CVOExtension(extensionId);

            // Create an CVO
            var cvo = new CVOExtension.CVO()
            {
                CameraBackFacing = true,
                HorizontalFlip = false,
                VideoRotation = CVOExtension.VideoRotation.CW_90
            };
            extension.Set(cvo);

            // Marshal
            var bytesMarshalled = extension.Marshal();
            Assert.Equal(0x20, bytesMarshalled[0]); // 2 for Extension ID and 0 for Length (CVOExtension.RTP_HEADER_EXTENSION_SIZE - 1)

            // Unmarshal
            var cvoFromBytes = (CVOExtension.CVO) extension.Unmarshal(null, new byte[] { bytesMarshalled[1] });
            Assert.Equal(cvo.CameraBackFacing, cvoFromBytes.CameraBackFacing);
            Assert.Equal(cvo.HorizontalFlip, cvoFromBytes.HorizontalFlip);
            Assert.Equal(cvo.VideoRotation, cvoFromBytes.VideoRotation);
        }
    }
}
