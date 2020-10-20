//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Test program for the WinRT Media Foundation Wrapper to activate
// and capture output from a webcam.
//
// Main reference:
// https://docs.microsoft.com/en-us/windows/uwp/audio-video-camera/process-media-frames-with-mediaframereader.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 30 Sep 2020  Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace test
{
    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }

    class Program
    {
        //private const string WEBCAM_NAME = "Logitech QuickCam Pro 9000";
        private const string WEBCAM_NAME = "HD Pro Webcam C920";

        // These are the input formats the VP8 encoder supports. If the webcam
        // supports them natively a pixel conversion can be saved.
        private static readonly string MF_NV12_PIXEL_FORMAT = MediaEncodingSubtypes.Nv12.ToUpper();
        private static readonly string MF_I420_PIXEL_FORMAT = "{30323449-0000-0010-8000-00AA00389B71}";
        private static readonly string MF_RGB24_PIXEL_FORMAT = MediaEncodingSubtypes.Rgb24.ToUpper();

        private static int FRAME_WIDTH = 640;
        private static int FRAME_HEIGHT = 480;
        private static int FRAME_RATE = 30;

        private static Form _form;
        private static PictureBox _picBox;

        static async Task Main()
        {
            Console.WriteLine("Video Capture Test");

            //await ListDevicesAndFormats();
            //Console.ReadLine();

            var mediaFrameReader = await StartVideoCapture().ConfigureAwait(false);

            if (mediaFrameReader != null)
            {
                // Open a Window to display the video feed from video capture device
                _form = new Form();
                _form.AutoSize = true;
                _form.BackgroundImageLayout = ImageLayout.Center;
                _picBox = new PictureBox
                {
                    Size = new Size(FRAME_WIDTH, FRAME_HEIGHT),
                    Location = new Point(0, 0),
                    Visible = true
                };
                _form.Controls.Add(_picBox);

                bool taskRunning = false;
                SoftwareBitmap backBuffer = null;

                // Lambda handler for captured frames.
                mediaFrameReader.FrameArrived += async (MediaFrameReader sender, MediaFrameArrivedEventArgs e) =>
                {
                    if (taskRunning)
                    {
                        return;
                    }
                    taskRunning = true;

                    var mediaFrameReference = sender.TryAcquireLatestFrame();
                    var videoMediaFrame = mediaFrameReference?.VideoMediaFrame;
                    var softwareBitmap = videoMediaFrame?.SoftwareBitmap;

                    if (softwareBitmap == null && videoMediaFrame != null)
                    {
                        var videoFrame = videoMediaFrame.GetVideoFrame();
                        softwareBitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(videoFrame.Direct3DSurface);
                    }

                    if (softwareBitmap != null)
                    {
                        Console.WriteLine($"Software bitmap pixel fmt {softwareBitmap.BitmapPixelFormat}, alpha mode {softwareBitmap.BitmapAlphaMode}.");

                        if (softwareBitmap.BitmapPixelFormat != Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8 ||
                            softwareBitmap.BitmapAlphaMode != Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied)
                        {
                            softwareBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                        }

                        int width = softwareBitmap.PixelWidth;
                        int height = softwareBitmap.PixelHeight;

                        Console.WriteLine($"Software bitmap frame size {width}x{height}.");

                        // Swap the processed frame to _backBuffer and dispose of the unused image.
                        softwareBitmap = Interlocked.Exchange(ref backBuffer, softwareBitmap);
                        softwareBitmap?.Dispose();

                        _form.BeginInvoke(new Action(() =>
                        {
                            if (_picBox.Width != width || _picBox.Height != height)
                            {
                                _picBox.Size = new Size(width, height);
                            }

                            using (BitmapBuffer buffer = backBuffer.LockBuffer(BitmapBufferAccessMode.Read))
                            {
                                using (var reference = buffer.CreateReference())
                                {
                                    unsafe
                                    {
                                        byte* dataInBytes;
                                        uint capacity;
                                        ((IMemoryBufferByteAccess)reference).GetBuffer(out dataInBytes, out capacity);

                                        Bitmap bmpImage = new Bitmap((int)width, (int)height, (int)(capacity / height), PixelFormat.Format32bppArgb, (IntPtr)dataInBytes);
                                        _picBox.Image = bmpImage;
                                    }
                                }
                            }
                        }));
                    }
                    else
                    {
                        Console.WriteLine("null");
                    }

                    taskRunning = false;
                };

                Console.WriteLine("Starting media frame reader.");
                _ = Task.Run(async () => await mediaFrameReader.StartAsync()).ConfigureAwait(false);

                Console.WriteLine("Starting Windows Forms message loop.");
                Application.EnableVisualStyles();
                Application.Run(_form);
            }
            else
            {
                Console.WriteLine("Could not acquire a media frame reader.");
            }
        }

        /// <summary>
        /// Initialise the capture device and set the source format.
        /// </summary>
        private static async Task<MediaFrameReader> StartVideoCapture()
        {
            var mediaCaptureSettings = new MediaCaptureInitializationSettings()
            {
                StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                // It shouldn't be necessary to force the CPU. 
                // Better to allow the system to use the GPU if possible.
                //MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                VideoDeviceId = await GetDeviceID(WEBCAM_NAME),
                MediaCategory = MediaCategory.Communications,
            };

            var mediaCapture = new MediaCapture();
            await mediaCapture.InitializeAsync(mediaCaptureSettings);

            MediaFrameSourceInfo colorSourceInfo = null;
            foreach (var srcInfo in mediaCapture.FrameSources)
            {
                if (srcInfo.Value.Info.MediaStreamType == MediaStreamType.VideoRecord &&
                   srcInfo.Value.Info.SourceKind == MediaFrameSourceKind.Color)
                {
                    colorSourceInfo = srcInfo.Value.Info;
                    break;
                }
            }

            var colorFrameSource = mediaCapture.FrameSources[colorSourceInfo.Id];

            var preferredFormat = colorFrameSource.SupportedFormats.Where(format =>
            {
                return format.VideoFormat.Width >= FRAME_WIDTH &&
               (format.FrameRate.Numerator / format.FrameRate.Denominator) >= FRAME_RATE
               // Setting the pixel format has proven to be very error prone AND the software bitmap from the frame
               // reader can end up with a different format any way. On my older logitech 9000 webcam attempting to 
               // set the pixel format to I420, and thus save a conversion, resulted in the media frame reader only
               // having buffered samples which was crazy slow and seemed to be operating in some kind of preview
               // mode where the camera was closed and re-opened for each frame.
               // The best approach seems to try for NV12 and if not available let the system choose. In the 
               // frame reader loop then do a software bitmap conversion if NV12 wasn't chosen.
                && format.Subtype == MF_NV12_PIXEL_FORMAT;
            }).FirstOrDefault();

            if (preferredFormat == null)
            {
                // Our desired format is not supported
                return null;
            }

            await colorFrameSource.SetFormatAsync(preferredFormat);

            var mediaFrameReader = await mediaCapture.CreateFrameReaderAsync(colorFrameSource);
            //var mediaFrameReader = await mediaCapture.CreateFrameReaderAsync(colorFrameSource, MediaEncodingSubtypes.Rgb24.ToUpper());
            mediaFrameReader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;

            PrintFrameSourceInfo(colorFrameSource);

            return mediaFrameReader;
        }

        /// <summary>
        /// Diagnostic method to print the details of a video frame source.
        /// </summary>
        private static void PrintFrameSourceInfo(MediaFrameSource frameSource)
        {
            var width = frameSource.CurrentFormat.VideoFormat.Width;
            var height = frameSource.CurrentFormat.VideoFormat.Height;
            var fpsNumerator = frameSource.CurrentFormat.FrameRate.Numerator;
            var fpsDenominator = frameSource.CurrentFormat.FrameRate.Denominator;

            double fps = fpsNumerator / fpsDenominator;
            string pixFmt = frameSource.CurrentFormat.Subtype;
            string deviceName = frameSource.Info.DeviceInformation.Name;

            Console.WriteLine($"Video capture device {deviceName} successfully initialised: {width}x{height} {fps:0.##}fps pixel format {pixFmt}.");
        }

        /// <summary>
        /// Gets the ID of a video device from its name.
        /// </summary>
        private static async Task<string> GetDeviceID(string deviceName)
        {
            var vidCapDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            var vidDevice = vidCapDevices.FirstOrDefault(x => x.Name == deviceName);

            if (vidDevice == null)
            {
                Console.WriteLine($"Could not find video capture device for name {deviceName}.");
                return null;
            }
            else
            {
                return vidDevice.Id;
            }
        }

        /// <summary>
        /// Attempts to list the system video capture devices and supported video modes.
        /// </summary>
        public static async Task ListDevicesAndFormats()
        {
            var vidCapDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            foreach (var vidCapDevice in vidCapDevices)
            {
                // The block below is how the reference documentation shows how to list modes but as of Sep 2020 it does not work.
                // https://docs.microsoft.com/en-us/uwp/api/windows.media.capture.mediacapture.findallvideoprofiles?view=winrt-19041.
                //logger.LogDebug($"Supported formats for video capture device {vidCapDevice.Name}:");
                //foreach (var recordProfiles in MediaCapture.FindAllVideoProfiles(vidCapDevice.Id).Select(x => x.SupportedRecordMediaDescription))
                //{
                //    logger.LogDebug($"Support profile count {recordProfiles.Count}");
                //    foreach (var profile in recordProfiles)
                //    {
                //        logger.LogDebug($"Capture device frame source {profile.Width}x{profile.Height} {profile.FrameRate:0.##}fps {profile.Subtype}");
                //    }
                //}

                var mediaCaptureSettings = new MediaCaptureInitializationSettings()
                {
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                    VideoDeviceId = vidCapDevice.Id
                };

                MediaCapture mediaCapture = new MediaCapture();
                await mediaCapture.InitializeAsync(mediaCaptureSettings);

                foreach (var srcFmtList in mediaCapture.FrameSources.Values.Select(x => x.SupportedFormats).Select(y => y.ToList()))
                {
                    foreach (var srcFmt in srcFmtList)
                    {
                        var vidFmt = srcFmt.VideoFormat;
                        float vidFps = vidFmt.MediaFrameFormat.FrameRate.Numerator / vidFmt.MediaFrameFormat.FrameRate.Denominator;
                        string pixFmt = vidFmt.MediaFrameFormat.Subtype == MF_I420_PIXEL_FORMAT ? "I420" : vidFmt.MediaFrameFormat.Subtype;
                        Console.WriteLine($"Video Capture device {vidCapDevice.Name} format {vidFmt.Width}x{vidFmt.Height} {vidFps:0.##}fps {pixFmt}");
                    }
                }
            }
        }
    }
}
