using DeckLinkAPI;
using DirectShowLib;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Test6
{
    public partial class MainWindow : System.Windows.Window
    {
        private readonly EventWaitHandle m_applicationCloseWaitHandle;

        private Thread m_deckLinkMainThread;

        private DeckLinkDeviceDiscovery m_deckLinkDeviceDiscovery;
        private DeckLinkDevice m_inputDevice;
        private DeckLinkOutputDevice m_outputDevice;
        private ProfileCallback m_profileCallback;

        private CaptureCallback m_captureCallback;
        private PlaybackCallback m_playbackCallback;

        private int cameraIndex = -1;

        private VideoCapture cameraCapture;
        private bool captureRunning;
        private Thread captureThread;

        private Mat latestCameraFrame;
        private Mat latestDeckLinkFrame;
        private object frameLock = new object();

        public MainWindow()
        {
            InitializeComponent();
            m_applicationCloseWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
        }

        private void DeckLinkMainThread()
        {
            m_profileCallback = new ProfileCallback();
            m_captureCallback = new CaptureCallback();
            m_playbackCallback = new PlaybackCallback();

            m_captureCallback.FrameReceived += OnFrameReceived;

            m_deckLinkDeviceDiscovery = new DeckLinkDeviceDiscovery();
            m_deckLinkDeviceDiscovery.DeviceArrived += AddDevice;
            m_deckLinkDeviceDiscovery.Enable();

            m_applicationCloseWaitHandle.WaitOne();

            m_captureCallback.FrameReceived -= OnFrameReceived;

            DisposeDeckLinkResources();
        }

        private void FindCameraIndex()
        {
            DsDevice[] cameras = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i].Name == "HT-GE202GC-T-CL")
                {
                    cameraIndex = i;
                    break;
                }
            }

            if (cameraIndex == -1)
                throw new Exception("Camera not found");
        }


        private void AddDevice(object sender, DeckLinkDiscoveryEventArgs e)
        {
            var deviceName = DeckLinkDeviceTools.GetDisplayLabel(e.deckLink);
            if (deviceName.Contains("DeckLink Duo (2)"))
            {
                m_inputDevice = new DeckLinkDevice(e.deckLink, m_profileCallback);
                InitializeInputDevice();
            }
            else if (deviceName.Contains("DeckLink Duo (4)"))
            {
                m_outputDevice = new DeckLinkOutputDevice(e.deckLink, m_profileCallback);
                InitializeOutputDevice();
            }
        }


        private void InitializeInputDevice()
        {
            if (m_inputDevice != null)
            {
                m_inputDevice.StartCapture(_BMDDisplayMode.bmdModeHD1080p5994, m_captureCallback, false);
            }
        }

        private void InitializeOutputDevice()
        {
            if (m_outputDevice != null)
            {
                m_outputDevice.PrepareForPlayback(_BMDDisplayMode.bmdModeHD1080p6000, m_playbackCallback);
            }

        }

        private Mat ProcessFrameWithOpenCV(Mat inputFrame)
        {
            //double alpha = 1; 
            //double beta = 0;    
            //Mat contrastAdjustedFrame = AdjustContrastUYVY(inputFrame, alpha, beta);

            //Mat finalFrame = DrawRectangle(contrastAdjustedFrame);
            Mat finalFrame = DrawRectangle(inputFrame);

            return finalFrame;
        }

        private Mat DrawRectangle(Mat inputFrame)
        {
            int rectWidth = 400;
            int rectHeight = 250;
            int centerX = inputFrame.Width / 2;
            int centerY = inputFrame.Height / 2;

            int leftX = centerX - rectWidth / 2;
            int rightX = centerX + rectWidth / 2;
            int bottomY = centerY + rectHeight / 2;

            OpenCvSharp.Point leftTop = new OpenCvSharp.Point(leftX, centerY - rectHeight / 2);
            OpenCvSharp.Point leftBottom = new OpenCvSharp.Point(leftX, bottomY);
            OpenCvSharp.Point rightTop = new OpenCvSharp.Point(rightX, centerY - rectHeight / 2);
            OpenCvSharp.Point rightBottom = new OpenCvSharp.Point(rightX, bottomY);

            Scalar greenColor = new Scalar(0, 255, 0);
            int thickness = 2;

            Cv2.Line(inputFrame, leftTop, leftBottom, greenColor, thickness);
            Cv2.Line(inputFrame, rightTop, rightBottom, greenColor, thickness);

            Cv2.Line(inputFrame, leftBottom, rightBottom, greenColor, thickness);

            return inputFrame;
        }

        //private Mat AdjustContrastUYVY(Mat uyvyFrame, double alpha, double beta)
        //{
        //    // UYVY format: U0 Y0 V0 Y1 (for each two pixels)
        //    // Create a clone to work on
        //    Mat adjustedFrame = uyvyFrame.Clone();

        //    unsafe
        //    {
        //        byte* dataPtr = (byte*)adjustedFrame.DataPointer;
        //        int totalBytes = uyvyFrame.Rows * uyvyFrame.Cols * uyvyFrame.ElemSize();

        //        for (int i = 0; i < totalBytes; i += 4)
        //        {
        //            // Adjust Y values at positions 1 and 3 in the UYVY sequence
        //            for (int j = 1; j <= 3; j += 2)
        //            {
        //                int yValue = dataPtr[i + j];
        //                // Adjust the Y value
        //                yValue = (int)(alpha * yValue + beta);
        //                // Clamp the value to 0-255 range
        //                yValue = Math.Max(0, Math.Min(255, yValue));
        //                dataPtr[i + j] = (byte)yValue;
        //            }
        //        }
        //    }

        //    return adjustedFrame;
        //}


        private void StartCameraCapture()
        {
            if (cameraIndex == -1) return; // Check if the camera index is valid

            // Initialize the camera capture with Full HD resolution
            cameraCapture = new VideoCapture(cameraIndex)
            {
                FrameWidth = 1920,
                FrameHeight = 1080

            };
            cameraCapture.Set(VideoCaptureProperties.Fps, 60);
            // Start the capture thread
            captureRunning = true;
            captureThread = new Thread(CaptureCamera);
            captureThread.Start();
        }



        private Mat ProcessCameraFrame(Mat frame)
        {
            int numberOfChannels = frame.Channels();

            if (numberOfChannels == 2) // UYVY format
            {
                // Process the UYVY frame directly
                return ProcessUYVYFrame(frame);
            }
            else if (numberOfChannels == 3) // BGR format
            {
                // Convert BGR to UYVY and then process
                
                
                
                //Mat uyvyFrame = ConvertBGRToUYVY(frame);
                //return ProcessUYVYFrame(uyvyFrame);


                return ProcessUYVYFrame(frame);
            }
            else
            {
                throw new Exception("Unsupported frame format");
            }
        }
        private Mat ProcessUYVYFrame(Mat uyvyFrame)
        {
            // Apply your UYVY specific processing here
            // ...

            return uyvyFrame; // Return the processed UYVY frame
        }


        private Mat ConvertBGRToUYVY(Mat bgrFrame)
        {
            // Convert from BGR to YUV
            Mat yuvFrame = new Mat();
            Cv2.CvtColor(bgrFrame, yuvFrame, ColorConversionCodes.BGR2YUV);

            int rows = yuvFrame.Rows;
            int cols = yuvFrame.Cols;

            // Create UYVY (YUV 4:2:2) format image
            Mat uyvyFrame = new Mat(rows, cols, MatType.CV_8UC2);

            Parallel.For(0, rows, y =>
            {
                for (int x = 0; x < cols; x += 2)
                {
                    Vec3b pixel1 = yuvFrame.At<Vec3b>(y, x);
                    Vec3b pixel2 = yuvFrame.At<Vec3b>(y, x + 1);

                    Vec2b uyvyPixel1 = new Vec2b
                    {
                        Item0 = pixel1[1], // U
                        Item1 = pixel1[0]  // Y0
                    };
                    uyvyFrame.Set(y, x, uyvyPixel1);

                    Vec2b uyvyPixel2 = new Vec2b
                    {
                        Item0 = pixel1[2], // V (using U from pixel1)
                        Item1 = pixel2[0]  // Y1
                    };
                    uyvyFrame.Set(y, x + 1, uyvyPixel2);
                }
            });

            return uyvyFrame;
        }

        private void ProcessAndOutputCombinedFrame()
        {
            lock (frameLock)
            {
                if (latestCameraFrame != null && latestDeckLinkFrame != null)
                {
                    Mat combinedFrame = CombineFrames(latestCameraFrame, latestDeckLinkFrame);
                    Mat uyvyFrame = ConvertBGRToUYVY(combinedFrame);
                    m_outputDevice.ScheduleFrame(uyvyFrame);

                    combinedFrame.Dispose();
                    uyvyFrame.Dispose();

                    // Reset frames to null after processing
                    latestCameraFrame = null;
                    latestDeckLinkFrame = null;
                }
            }
        }

        private Mat CombineFrames(Mat cameraFrame, Mat deckLinkFrame)
        {
            int resizedWidth = 1920 / 2;
            int resizedHeight = 1080 / 2;

            Mat resizedCameraFrame = new Mat();
            Mat resizedDeckLinkFrame = new Mat();

            // Resize camera frame to half width and full height
            Cv2.Resize(cameraFrame, resizedCameraFrame, new OpenCvSharp.Size(resizedWidth, 1080));

            // Resize deckLink frame to half width and full height
            Cv2.Resize(deckLinkFrame, resizedDeckLinkFrame, new OpenCvSharp.Size(resizedWidth, 1080));

            Mat combinedFrame = new Mat(1080, 1920, MatType.CV_8UC3);  // Assuming BGR format

            OpenCvSharp.Rect leftRoi = new OpenCvSharp.Rect(0, 0, resizedWidth, 1080);  // Left half
            OpenCvSharp.Rect rightRoi = new OpenCvSharp.Rect(resizedWidth, 0, resizedWidth, 1080);  // Right half

            resizedCameraFrame.CopyTo(new Mat(combinedFrame, leftRoi));
            resizedDeckLinkFrame.CopyTo(new Mat(combinedFrame, rightRoi));

            resizedCameraFrame.Dispose();
            resizedDeckLinkFrame.Dispose();

            return combinedFrame;
        }

        private void CaptureCamera()
        {
            while (captureRunning)
            {
                using (Mat camframe = new Mat())
                {
                    if (cameraCapture.Read(camframe)) // Capture a frame
                    {
                        Mat processedFrame = ProcessCameraFrame(camframe);

                        lock (frameLock)
                        {
                            latestCameraFrame = processedFrame.Clone();

                            if (latestDeckLinkFrame != null)
                            {
                                ProcessAndOutputCombinedFrame();
                            }
                        }
                    }
                }
            }
        }

        private void OnFrameReceived(IDeckLinkVideoInputFrame videoFrame)
        {
            IntPtr frameBytes;
            videoFrame.GetBytes(out frameBytes);

            int width = videoFrame.GetWidth();
            int height = videoFrame.GetHeight();

            using (Mat capturedFrame = new Mat(height, width, OpenCvSharp.MatType.CV_8UC2, frameBytes))
            {
                Mat processedFrame = ProcessFrameWithOpenCV(capturedFrame);

                Mat bgrFrame = ConvertUYVYToBGR(capturedFrame);

                lock (frameLock)
                {
                    //latestDeckLinkFrame = processedFrame.Clone();
                    latestDeckLinkFrame = bgrFrame.Clone();

                    if (latestCameraFrame != null)
                    {
                        ProcessAndOutputCombinedFrame();
                    }
                }
            }
        }

        private Mat ConvertUYVYToBGR(Mat uyvyFrame)
        {
            Mat bgrFrame = new Mat();
            Cv2.CvtColor(uyvyFrame, bgrFrame, ColorConversionCodes.YUV2BGR_UYVY);
            return bgrFrame;
        }

        //private void OnFrameReceived(IDeckLinkVideoInputFrame videoFrame)
        //{
        //    IntPtr frameBytes;
        //    videoFrame.GetBytes(out frameBytes);

        //    int width = videoFrame.GetWidth();
        //    int height = videoFrame.GetHeight();

        //    using (Mat capturedFrame = new Mat(height, width, OpenCvSharp.MatType.CV_8UC2, frameBytes))
        //    {
        //        Mat processedFrame = ProcessFrameWithOpenCV(capturedFrame);

        //        //if (m_outputDevice != null)
        //        //{
        //        //    m_outputDevice.ScheduleFrame(processedFrame);
        //        //}

        //        lock (frameLock)
        //        {
        //            latestDeckLinkFrame = processedFrame.Clone();
        //        }

        //        ProcessAndOutputCombinedFrame();
        //    }
        //}

        //private void CaptureCamera()
        //{
        //    while (captureRunning)
        //    {
        //        using (Mat camframe = new Mat())
        //        {
        //            if (cameraCapture.Read(camframe)) // Capture a frame
        //            {
        //                Mat processedFrame = ProcessCameraFrame(camframe);
        //                //m_outputDevice.ScheduleFrame(processedFrame);

        //                lock (frameLock)
        //                {
        //                    latestCameraFrame = processedFrame.Clone();
        //                }

        //                ProcessAndOutputCombinedFrame();
        //            }
        //        }
        //    }
        //}

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            FindCameraIndex();
            StartCameraCapture();
            m_deckLinkMainThread = new Thread(() => DeckLinkMainThread());
            m_deckLinkMainThread.SetApartmentState(ApartmentState.MTA);
            m_deckLinkMainThread.Start();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopCameraCapture();
            // Signal the main thread to close
            m_applicationCloseWaitHandle.Set();

            // Wait for the main thread to finish
            if (m_deckLinkMainThread != null && m_deckLinkMainThread.IsAlive)
            {
                m_deckLinkMainThread.Join();
            }

            // Properly dispose of DeckLink resources
            DisposeDeckLinkResources();
        }

        private void StopCameraCapture()
        {
            captureRunning = false;
            captureThread?.Join();
            cameraCapture?.Dispose();
        }

        private void DisposeDeckLinkResources()
        {
            if (m_inputDevice != null)
            {
                m_inputDevice.StopCapture();
                m_inputDevice = null;
            }

            if (m_outputDevice != null)
            {
                m_outputDevice.StopPlayback();
                m_outputDevice = null;
            }

            if (m_deckLinkDeviceDiscovery != null)
            {
                m_deckLinkDeviceDiscovery.Disable();
                m_deckLinkDeviceDiscovery = null;
            }
        }
    }

    public class CaptureCallback : IDeckLinkInputCallback
    {
        public event Action<IDeckLinkVideoInputFrame> FrameReceived;

        public void VideoInputFrameArrived(IDeckLinkVideoInputFrame videoFrame, IDeckLinkAudioInputPacket audioPacket)
        {
            FrameReceived?.Invoke(videoFrame);
        }

        public void VideoInputFormatChanged(_BMDVideoInputFormatChangedEvents notificationEvents, IDeckLinkDisplayMode newDisplayMode, _BMDDetectedVideoInputFormatFlags detectedSignalFlags)
        {
        }
    }

    public class PlaybackCallback : IDeckLinkVideoOutputCallback
    {
        public event Action<IDeckLinkVideoFrame, _BMDOutputFrameCompletionResult> FrameCompleted;

        public void ScheduledFrameCompleted(IDeckLinkVideoFrame completedFrame, _BMDOutputFrameCompletionResult result)
        {
            FrameCompleted?.Invoke(completedFrame, result);
        }

        public void ScheduledPlaybackHasStopped()
        {
        }
    }
}
