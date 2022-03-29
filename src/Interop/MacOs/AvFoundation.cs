using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Text;

namespace SIPSorceryMedia.FFmpeg.Interop.MacOS
{
    internal class AvFoundation
    {
        static private String AVFOUNDATION_VIDEO_DEVICE_LOG_OUTPUT = "AVFoundation video devices";
        static private String AVFOUNDATION_AUDIO_DEVICE_LOG_OUTPUT = "AVFoundation audio devices";
        static private String AVFOUNDATION_CAPTURE_SCREEN_LOG_OUTPUT = "Capture screen";


        private static unsafe String GetAvFoundationLogsAboutDevicesList()
        {
            String inputFormat = "avfoundation";
            
            AVInputFormat* avInputFormat = ffmpeg.av_find_input_format(inputFormat);
            AVFormatContext* pFormatCtx = ffmpeg.avformat_alloc_context();
            AVDictionary* options = null;

            ffmpeg.av_dict_set(&options, "list_devices", "true", 0);
            
            // We use temporarily a specific callback to log FFmpeg entries
            FFmpegInit.UseSpecificLogCallback();
            ffmpeg.avformat_open_input(&pFormatCtx, null, avInputFormat, &options); // Here nb is < 0 ... But we have anyway an output from av_log which can be parsed ...
            ffmpeg.avformat_close_input(&pFormatCtx);

            // We no more need to use temporarily a specific callback to log FFmpeg entries
            FFmpegInit.UseDefaultLogCallback();

            // returns logs 
            return FFmpegInit.GetStoredLogs();
        }

        static public unsafe List<Monitor>? GetMonitors()
        {
            String logs = GetAvFoundationLogsAboutDevicesList();
            return ParseAvFoundationLogsForMonitors(logs);
        }

        static public unsafe List<Camera>? GetCameraDevices()
        {
            String logs = GetAvFoundationLogsAboutDevicesList();
            return ParseAvFoundationLogsForCameras(logs);
        }

        static private List<Monitor>? ParseAvFoundationLogsForMonitors(String logs)
        {
            List<String> importantLines = new List<string>();
            List<Monitor>? result = null;
            if (logs?.Length > 0)
            {
                // Do we have at least a video device ?
                if (logs.Contains(AVFOUNDATION_VIDEO_DEVICE_LOG_OUTPUT))
                {
                    String[] lines = logs.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    String header = null;
                    int index;

                    foreach (String line in lines)
                    {
                        // If we reach audio devices, we have finish the parsing
                        if (line.Contains(AVFOUNDATION_AUDIO_DEVICE_LOG_OUTPUT))
                            break;

                        // Get "header"
                        if (header == null)
                        {
                            index = line.IndexOf(AVFOUNDATION_VIDEO_DEVICE_LOG_OUTPUT);
                            if (index > 0)
                                header = line.Substring(0, index);
                        }
                        else
                        {
                            // We want "capture screen" here
                            if (line.Contains(AVFOUNDATION_CAPTURE_SCREEN_LOG_OUTPUT))
                            {
                                if (line.Contains(header))
                                {
                                    // remove header
                                    String ln = line.Replace(header, "");
                                    if (ln.StartsWith("["))
                                    {
                                        index = ln.IndexOf("]");
                                        string name = ln.Substring(index + 2);
                                        string path = ln.Substring(1, index - 1) + ":";

                                        Monitor monitor = new Monitor
                                        {
                                            Name = name,
                                            Path = path,
                                            Primary = (index == 0)
                                        };

                                        if (result == null)
                                            result = new List<Monitor>();

                                        result.Add(monitor);
                                    }
                                    importantLines.Add(ln);
                                }
                            }
                        }
                    }
                }
            }
            return result;
        }


        static private List<Camera>? ParseAvFoundationLogsForCameras(String logs)
        {
            List<String> importantLines = new List<string>();    
            List<Camera>? result = null;
            if (logs?.Length > 0)
            {
                // Do we have at least a video device ?
                if (logs.Contains(AVFOUNDATION_VIDEO_DEVICE_LOG_OUTPUT))
                {
                    String[] lines = logs.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    String header = null;
                    int index;

                    foreach (String line in lines)
                    {
                        // If we reach audio devices, we have finish the parsing
                        if (line.Contains(AVFOUNDATION_AUDIO_DEVICE_LOG_OUTPUT))
                            break;

                        // Get "header"
                        if(header == null)
                        {
                            index = line.IndexOf(AVFOUNDATION_VIDEO_DEVICE_LOG_OUTPUT);
                            if (index > 0)
                                header = line.Substring(0, index);
                        }
                        else
                        {
                            // We don't want "capture screen" here
                            if (!line.Contains(AVFOUNDATION_CAPTURE_SCREEN_LOG_OUTPUT))
                            {
                                if (line.Contains(header))
                                {
                                    // remove header
                                    String ln = line.Replace(header, "");
                                    if(ln.StartsWith("["))
                                    {
                                        index = ln.IndexOf("]");
                                        string name = ln.Substring(index+2);
                                        string path = ln.Substring(1, index - 1) + ":";

                                        Camera camera = new Camera
                                        {
                                            Name = name,
                                            Path = path
                                        };

                                        if (result == null)
                                            result = new List<Camera>();

                                        result.Add(camera);
                                    }
                                    importantLines.Add(ln);
                                }
                            }
                        }
                    }
                }
            }
            return result;
        }
    }
}
