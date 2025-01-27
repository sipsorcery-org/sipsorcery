using SIPSorcery.Net;
using System;

namespace SIPSorcery.Net
{
    // CVO (Coordination of Video Orientation) is a extension payload format in https://www.3gpp.org/ftp/Specs/archive/26_series/26.114/26114-i70.zip

    // Code reference:
    //  - https://chromium.googlesource.com/external/webrtc/+/e2a017725570ead5946a4ca8235af27470ca0df9/webrtc/modules/rtp_rtcp/source/rtp_header_extensions.cc#134
    //  - https://chromium.googlesource.com/external/webrtc/+/e2a017725570ead5946a4ca8235af27470ca0df9/webrtc/modules/rtp_rtcp/include/rtp_cvo.h

    public class CVOExtension: RTPHeaderExtension
    {
        public class CVO
        {
            public Boolean CameraBackFacing;
            public Boolean HorizontalFlip;
            public VideoRotation VideoRotation;

            public CVO()
            {
                CameraBackFacing = false;
                HorizontalFlip = false;
                VideoRotation = VideoRotation.CW_0;
            }

            /* CVO byte: |0 0 0 0 C F R1 R0|
                With the following definitions: 

                C = Camera: indicates the direction of the camera used for this video stream. It can be used by the MTSI client in 
                receiver to e.g. display the received video differently depending on the source camera. 
                    0: Front-facing camera, facing the user. If camera direction is unknown by the sending MTSI client in the terminal then this is the default value used. 
                    1: Back-facing camera, facing away from the user. 
                
                F = Flip: indicates a horizontal (left-right flip) mirror operation on the video as sent on the link. 
                    0: No flip operation. If the sending MTSI client in terminal does not know if a horizontal mirror operation is necessary, then this is the default value used. 
                    1: Horizontal flip operation 

                R1, R0 = Rotation: indicates the rotation of the video as transmitted on the link.
                    0, 0 =   0° rotation                            => needs   0° CW rotation
                    0, 1 =  90° Counter Clockwise (CCW) rotation    => needs  90° CW rotation 
                    1, 0 = 180° CCW rotation                        => needs 180° CW rotation
                    1, 1 = 270° CCW rotation                        => needs 270° CW rotation
             */

            public CVO(byte cvo_byte)
            {
                CameraBackFacing = (cvo_byte & 0x8) == 0x8;
                HorizontalFlip = (cvo_byte & 0x4) == 0x4;
                VideoRotation = ConvertCVOByteToVideoRotation(cvo_byte);
            }
        }

        public const string RTP_HEADER_EXTENSION_URI = "urn:3gpp:video-orientation";
        internal const int RTP_HEADER_EXTENSION_SIZE = 1;

        private byte _cvo_byte;
        private CVO _cvo;

        public CVOExtension(int id) : base(id, RTP_HEADER_EXTENSION_URI, RTP_HEADER_EXTENSION_SIZE, RTPHeaderExtensionType.OneByte, Net.SDPMediaTypesEnum.video)
        {
            _cvo_byte = 0;
            _cvo = new CVO();
        }

        /// <summary>
        /// To set video rotation
        /// </summary>
        /// <param name="value">A <see cref="CVO"/> object is expected here</param>
        public override void Set(Object value)
        {
            if (value is CVO cvo)
            {
                _cvo_byte = ConvertCVOToCVOByte(cvo);
                _cvo = cvo;
            }
        }

        public override byte[] Marshal()
        {
            return new[]
            {
                (byte)((Id << 4) | ExtensionSize - 1),
                _cvo_byte
            };
        }

        public override Object Unmarshal(RTPHeader header, byte[] data)
        {
            if (data?.Length == ExtensionSize)
            {
                var cvoByte = data[0];
                if (_cvo_byte != cvoByte)
                {
                    _cvo_byte = cvoByte;
                    _cvo = new CVO(_cvo_byte);
                }
            }
            return _cvo;
        }

        /// <summary>
        /// Enum for clockwise (CW) rotation in degree.
        /// </summary>
        public enum VideoRotation
        {
            CW_0 = 0,
            CW_90 = 90,
            CW_180 = 180,
            CW_270 = 270
        };

        static byte ConvertCVOToCVOByte(CVO cvo)
        {
            return (byte) ( (cvo.CameraBackFacing ? 0x8 : 0x0)
                            + (cvo.HorizontalFlip ? 0x4 : 0x0) 
                            + ConvertVideoRotationToCVOByte(cvo.VideoRotation));
        }

        static byte ConvertVideoRotationToCVOByte(VideoRotation rotation)
        {
            switch (rotation)
            {
                case VideoRotation.CW_0:
                    return 0;
                case VideoRotation.CW_90:
                    return 1;
                case VideoRotation.CW_180:
                    return 2;
                case VideoRotation.CW_270:
                    return 3;
                default:
                    return 0;
            }
        }

        static VideoRotation ConvertCVOByteToVideoRotation(byte cvo_byte)
        {
            /* CVO byte: |0 0 0 0 C F R1 R0|
                With the following definitions: 

                C = Camera: indicates the direction of the camera used for this video stream. It can be used by the MTSI client in 
                receiver to e.g. display the received video differently depending on the source camera. 
                    0: Front-facing camera, facing the user. If camera direction is unknown by the sending MTSI client in the terminal then this is the default value used. 
                    1: Back-facing camera, facing away from the user. 
                
                F = Flip: indicates a horizontal (left-right flip) mirror operation on the video as sent on the link. 
                    0: No flip operation. If the sending MTSI client in terminal does not know if a horizontal mirror operation is necessary, then this is the default value used. 
                    1: Horizontal flip operation 

                R1, R0 = Rotation: indicates the rotation of the video as transmitted on the link.
                    0, 0 =   0° rotation                            => needs   0° CW rotation
                    0, 1 =  90° Counter Clockwise (CCW) rotation    => needs  90° CW rotation 
                    1, 0 = 180° CCW rotation                        => needs 180° CW rotation
                    1, 1 = 270° CCW rotation                        => needs 270° CW rotation
             */

            uint rotation_bits = (uint)cvo_byte & 0x3;
            switch (rotation_bits)
            {
                case 0:
                    return VideoRotation.CW_0;
                case 1:
                    return VideoRotation.CW_90;
                case 2:
                    return VideoRotation.CW_180;
                case 3:
                    return VideoRotation.CW_270;
                default:
                    return VideoRotation.CW_0;
            }
        }
    }
}
