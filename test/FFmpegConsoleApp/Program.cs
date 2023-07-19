using System;
using System.Collections.Generic;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace FFmpegConsoleApp
{
    class Program
    {
        // A valid to a video file - usefull if you want to test streaming of a video file
        // It's alo possible to set a remote file
        const String VIDEO_FILE_PATH = @"https://upload.wikimedia.org/wikipedia/commons/3/36/Cosmos_Laundromat_-_First_Cycle_-_Official_Blender_Foundation_release.webm"; 
        //const String VIDEO_FILE_PATH = @"C:\media\big_buck_bunny.mp4";
        //const String VIDEO_FILE_PATH = @"C:\media\Armello_Trailer.webm";
        

        static private AsciiFrame? asciiFrame = null;

        static void Main(string[] args)
        {
            VideoCodecsEnum VideoCodec = VideoCodecsEnum.H264;
            IVideoSource? videoSource = null;

            // Initialise FFmpeg librairies
            FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_FATAL);

            // Get cameras and monitors
            List<Camera>? cameras = FFmpegCameraManager.GetCameraDevices();
            List<Monitor>? monitors = FFmpegMonitorManager.GetMonitorDevices();

            char keyChar = ' ';
            while (true)
            {
                Console.Clear();
                if (!(cameras?.Count > 0))
                    Console.WriteLine("\nNo Camera found ...");
                if (!(monitors?.Count > 0))
                    Console.WriteLine("\nNo Monitor found ...");

                Console.WriteLine("\nWhat do you want to use ?");
                if (cameras?.Count > 0)
                    Console.Write("\n [c] - Camera ");
                if (monitors?.Count > 0)
                    Console.Write("\n [m] - Monitor ");
                Console.Write($"\n [f] - File - Path:[{VIDEO_FILE_PATH}]");

                Console.WriteLine("\n");
                Console.Out.Flush();

                var keyConsole = Console.ReadKey();
                if ( ( (keyConsole.KeyChar == 'c') && (cameras?.Count > 0) )
                    || ( (keyConsole.KeyChar == 'm') && (monitors?.Count > 0)) 
                    || (keyConsole.KeyChar == 'f'))
                {
                    keyChar = keyConsole.KeyChar;
                    break;
                }
            }
            // Do we manage a camera ?
            if (keyChar == 'c')
            {
                int cameraIndex = 0;
                if (cameras?.Count > 1)
                {
                    while (true)
                    {
                        Console.Clear();
                        Console.WriteLine("\nWhich camera do you want to use:");
                        int index = 0;
                        foreach (Camera camera in cameras)
                        {
                            Console.Write($"\n [{index}] - {camera.Name} ");
                            index++;
                        }
                        Console.WriteLine("\n");
                        Console.Out.Flush();

                        var keyConsole = Console.ReadKey();
                        if (int.TryParse("" + keyConsole.KeyChar, out int keyValue) && keyValue < index && keyValue >= 0)
                        {
                            cameraIndex = keyValue;
                            break;
                        }
                    }
                }
                if (cameras != null)
                {
                    var selectedCamera = cameras[cameraIndex];
                    SIPSorceryMedia.FFmpeg.FFmpegCameraSource cameraSource = new SIPSorceryMedia.FFmpeg.FFmpegCameraSource(selectedCamera.Path);
                    videoSource = cameraSource as IVideoSource;
                }
            }
            // Do we manage a Monitor ?
            else if (keyChar == 'm')
            {
                int monitorIndex = 0;
                if (monitors?.Count > 1)
                {
                    while (true)
                    {
                        Console.Clear();
                        Console.WriteLine("\nWhich Monitor do you want to use:");
                        int index = 0;
                        foreach (Monitor monitor in monitors)
                        {
                            Console.Write($"\n [{index}] - Monitor {monitor.Name} [{monitor.Rect.Width}x{monitor.Rect.Height}] {(monitor.Primary ? " PRIMARY" : "")}");
                            index++;
                        }
                        Console.WriteLine("\n");
                        Console.Out.Flush();

                        var keyConsole = Console.ReadKey();
                        if (int.TryParse("" + keyConsole.KeyChar, out int keyValue) && keyValue < index && keyValue >= 0)
                        {
                            monitorIndex = keyValue;
                            break;
                        }
                    }
                }

                if (monitors != null)
                {
                    var selectedMonitor = monitors[monitorIndex];
                    SIPSorceryMedia.FFmpeg.FFmpegScreenSource screenSource = new SIPSorceryMedia.FFmpeg.FFmpegScreenSource(selectedMonitor.Path, selectedMonitor.Rect, 20);
                    videoSource = screenSource as IVideoSource;
                }
            }
            // Do we manage a File ?
            else
            {
                SIPSorceryMedia.FFmpeg.FFmpegFileSource fileSource = new SIPSorceryMedia.FFmpeg.FFmpegFileSource(VIDEO_FILE_PATH, true, null, 960, true);
                videoSource = fileSource as IVideoSource;
            }


            if(videoSource == null)
            {
                Console.WriteLine("No video source defined ...");
                return;
            }

            // Create object used to display video in Ascii
            asciiFrame = new AsciiFrame();

            videoSource.RestrictFormats(x => x.Codec == VideoCodec);
            videoSource.SetVideoSourceFormat(videoSource.GetVideoSourceFormats().Find(x => x.Codec == VideoCodec));
            videoSource.OnVideoSourceRawSampleFaster+= FileSource_OnVideoSourceRawSampleFaster;
            videoSource.StartVideo();

            for (var loop = true; loop;)
            {
                var cki = Console.ReadKey(true);
                switch (cki.Key)
                {
                    case ConsoleKey.Q:
                    case ConsoleKey.Enter:
                    case ConsoleKey.Escape:
                        Console.CursorVisible = true;
                        loop = false;
                        break;
                }
            }
        }

        private static void FileSource_OnVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage)
        {
            asciiFrame?.GotRawImage(ref rawImage);
        }
    }
}
