using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;

namespace SIPSorceryMedia.FFmpeg
{
    public unsafe class FFmpegCameraManager
    {
        static public List<Camera>? GetCameraDevices()
        {
            List<Camera>? result = null;

            string inputFormat = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dshow"
                                    : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "v4l2"
                                    : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "avfoundation"
                                    : throw new NotSupportedException($"Cannot find adequate input format - OSArchitecture:[{RuntimeInformation.OSArchitecture}] - OSDescription:[{RuntimeInformation.OSDescription}]");

            if (inputFormat == "dshow")
            {
                result = SIPSorceryMedia.FFmpeg.Interop.Win32.DShow.GetCameraDevices();
            }
            else if (inputFormat == "avfoundation")
            {
                result = SIPSorceryMedia.FFmpeg.Interop.MacOS.AvFoundation.GetCameraDevices();
            }
            else
            {
                AVInputFormat* avInputFormat = ffmpeg.av_find_input_format(inputFormat);
                AVDeviceInfoList* avDeviceInfoList = null;

                ffmpeg.avdevice_list_input_sources(avInputFormat, null, null, &avDeviceInfoList).ThrowExceptionIfError();
                int nDevices = avDeviceInfoList->nb_devices;
                var avDevices = avDeviceInfoList->devices;

                result = new List<Camera>();
                for (int i = 0; i < nDevices; i++)
                {
                    var avDevice = avDevices[i];
                    var name = Marshal.PtrToStringAnsi((IntPtr)avDevice->device_description);
                    var path = Marshal.PtrToStringAnsi((IntPtr)avDevice->device_name);

                    if ((name != null) && (name.Length > 0))
                    {
                        Camera camera = new Camera
                        {
                            Name = (name == null) ? "" : name,
                            Path = (path == null) ? "" : path,
                        };
                        result.Add(camera);
                    }
                }

                ffmpeg.avdevice_free_list_devices(&avDeviceInfoList);
            }
            return result;
        }

        static public Camera? GetCameraByPath(string path) => GetCameraDevices()?.FirstOrDefault(x => x.Path == path);
    }

    public class Camera
    {
        public struct CameraFormat
        {
            private int _pixFmt_;

            public AVPixelFormat PixelFormat 
            { 
                get => (AVPixelFormat)_pixFmt_ - 1; 
                set => _pixFmt_ = (int)value + 1;
            }
            public int Width { get; set; }
            public int Height { get; set; }
            public double FPS { get; set; }
        }

        public string Name { get; set; }

        public string Path { get; set; }

        public List<CameraFormat>? AvailableFormats {  get; set; }

        public List<Dictionary<string, string>>? AvailableOptions { get; set; }

        public Camera()
        {
            Name = Path = "";
        }

        public override bool Equals(object? obj)
        {
            return obj is not null && obj.GetType() == GetType()
                && ((Camera)obj).Name == Name && ((Camera)obj).Path == Path;
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }
    }
}
