using AVT.VmbAPINET;
using DeckLinkAPI;
using DirectShowLib;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Frame = AVT.VmbAPINET.Frame;
using Rect = OpenCvSharp.Rect;

namespace Test6
{
    public partial class MainWindow : System.Windows.Window
    {
        private readonly EventWaitHandle m_applicationCloseWaitHandle;

        private Thread m_deckLinkMainThread;

        private DeckLinkDeviceDiscovery m_deckLinkDeviceDiscovery;
        private DeckLinkDevice m_inputDevice1;
        private DeckLinkDevice m_inputDevice2;

        private DeckLinkOutputDevice m_outputDevice;
        
        private ProfileCallback m_profileCallback;

        private CaptureCallback m_captureCallback1;
        private CaptureCallback m_captureCallback2;

        private PlaybackCallback m_playbackCallback;


        //previous camera*
        //private int cameraIndex = -1;
        //private VideoCapture cameraCapture;
        //private bool captureRunning;
        //private Thread captureThread;
        //*

        //new camera*
        private Camera _camera;
        private Vimba _vimba;
        private bool _acquiring;
        private Frame[] _frameArray;
        //*

        private Mat latestCameraFrame;
        private Mat latestDeckLinkFrame1;
        private Mat latestDeckLinkFrame2;

        private object frameLock = new object();

        public MainWindow()
        {
            InitializeComponent();
            m_applicationCloseWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
        }

        private void DeckLinkMainThread()
        {
            m_profileCallback = new ProfileCallback();
            m_captureCallback1 = new CaptureCallback();
            m_captureCallback2 = new CaptureCallback(); 
            m_playbackCallback = new PlaybackCallback();

            m_captureCallback1.FrameReceived += OnFrameReceived;

            m_deckLinkDeviceDiscovery = new DeckLinkDeviceDiscovery();
            m_deckLinkDeviceDiscovery.DeviceArrived += AddDevice;
            m_deckLinkDeviceDiscovery.Enable();

            m_applicationCloseWaitHandle.WaitOne();

            m_captureCallback1.FrameReceived -= OnFrameReceived;

            DisposeDeckLinkResources();
        }
        #region Previous camera*
        //Previous camera*
        //private void FindCameraIndex()
        //{
        //    DsDevice[] cameras = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
        //    for (int i = 0; i < cameras.Length; i++)
        //    {
        //        if (cameras[i].Name == "HT-GE202GC-T-CL")
        //        {
        //            cameraIndex = i;
        //            break;
        //        }
        //    }

        //    if (cameraIndex == -1)
        //        throw new Exception("Camera not found");
        //}

        //private void StartCameraCapture()
        //{
        //    if (cameraIndex == -1) return;

        //    cameraCapture = new VideoCapture(cameraIndex)
        //    {
        //        FrameWidth = 1920,
        //        FrameHeight = 1080

        //    };
        //    cameraCapture.Set(VideoCaptureProperties.Fps, 60);
        //    captureRunning = true;
        //    captureThread = new Thread(CaptureCamera);
        //    captureThread.Start();
        //}
        //private void CaptureCamera()
        //{
        //    while (captureRunning)
        //    {
        //        using (Mat camframe = new Mat())
        //        {
        //            if (cameraCapture.Read(camframe))
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
        //Previous camera*
        #endregion


        //Gt1910*
        private void InitializeVimba()
        {
            _vimba = new Vimba();
            _vimba.Startup();
            var cameras = _vimba.Cameras;
            if (cameras.Count > 0)
            {
                _camera = cameras[0];
                _camera.Open(VmbAccessModeType.VmbAccessModeFull);
                StartImageAcquisition();
            }
            else
            {
                MessageBox.Show("No cameras found.");
            }
        }
        private void StartImageAcquisition()
        {
            if (_camera != null)
            {
                AdjustPacketSize();
                SetupCameraForCapture();
            }
            else
            {
                MessageBox.Show("Camera is not initialized.");
            }
        }

        private void AdjustPacketSize()
        {
            try
            {
                var adjustPacketSizeFeature = _camera.Features["GVSPAdjustPacketSize"];
                if (adjustPacketSizeFeature != null)
                {
                    adjustPacketSizeFeature.RunCommand();
                    while (!adjustPacketSizeFeature.IsCommandDone())
                    {
                        Debug.WriteLine("Adjusting packet size...");
                    }
                    Debug.WriteLine("Packet size adjusted.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception while adjusting packet size: {ex.Message}");
            }
        }

        private void SetupCameraForCapture()
        {
            long payloadSize = _camera.Features["PayloadSize"].IntValue;
            _frameArray = new Frame[5];
            for (int i = 0; i < _frameArray.Length; i++)
            {
                _frameArray[i] = new Frame(payloadSize);
                _camera.AnnounceFrame(_frameArray[i]);
            }
            _camera.StartCapture();
            foreach (var frame in _frameArray)
            {
                _camera.QueueFrame(frame);
            }

            _camera.OnFrameReceived += OnCameraFrameReceived;
            _camera.Features["AcquisitionMode"].EnumValue = "Continuous";
            _camera.Features["AcquisitionStart"].RunCommand();
            _acquiring = true;
        }

        private void OnCameraFrameReceived(Frame frame)
        {
            if (!_acquiring) return;

            try
            {
                Debug.WriteLine($"Frame received: {frame.FrameID}, Status: {frame.ReceiveStatus}");
                ProcessFrame(frame);
                if (_acquiring)
                {
                    _camera.QueueFrame(frame);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing frame: {ex.Message}");
            }
        }
        private void ProcessFrame(Frame frame)
        {
            if (frame.ReceiveStatus == VmbFrameStatusType.VmbFrameStatusComplete)
            {
                using (var mono8 = new Mat((int)frame.Height, (int)frame.Width, MatType.CV_8UC1, frame.Buffer))
                {
                    //var bitmapSource = BitmapSourceConverter.ToBitmapSource(mat);
                    //imgFrame.Source = bitmapSource;

                    Mat bgrImage = new Mat();
                    Cv2.CvtColor(mono8, bgrImage, ColorConversionCodes.GRAY2BGR);


                    Mat processedFrame = ProcessCameraFrame(bgrImage);
                    
                    Mat AddText = CenterFrame(processedFrame);


                    SaveFrameAsImage(AddText);

                    //********************************for one frame
                    Mat uyvyFrame = ConvertBGRToUYVY(AddText);
                    if (m_outputDevice != null)
                    {
                        m_outputDevice.ScheduleFrame(uyvyFrame);
                    }

                    //**********************************for two frames
                    //lock (frameLock)
                    //{
                    //    latestCameraFrame = processedFrame.Clone();
                    //}

                    //ProcessAndOutputCombinedFrame();
                }
            }
        }

        private Mat ProcessCameraFrame(Mat frame)
        {
            return CropAndResizeFrame(frame); //for 2
        }
        //Gt1910*

        private void SaveFrameAsImage(Mat uyvyFrame)
        {
            string imagePath = @"C:\Users\User\Desktop\font\frame_uyvy_Arial22.jpg";

            Cv2.ImWrite(imagePath, uyvyFrame);
        }

        private Mat CenterFrame(Mat originalFrame)
        {
            Mat centeredFrame = new Mat(new OpenCvSharp.Size(1920, 1080), originalFrame.Type(), Scalar.All(0));

            int startX = (centeredFrame.Width - originalFrame.Width) / 2;
            int startY = (centeredFrame.Height - originalFrame.Height) / 2;
            Rect roi = new Rect(startX, startY, originalFrame.Width, originalFrame.Height);
            originalFrame.CopyTo(centeredFrame[roi]);

            // Draw vertical green lines
            Cv2.Line(centeredFrame, new OpenCvSharp.Point(240, 0), new OpenCvSharp.Point(240, 1080), Scalar.Green, 2);
            Cv2.Line(centeredFrame, new OpenCvSharp.Point(1680, 0), new OpenCvSharp.Point(1680, 1080), Scalar.Green, 2);

            // Place text in columns
            PlaceTextInColumns(centeredFrame, 30);    // For left side text
            PlaceTextInColumns(centeredFrame, 1710);  // For right side text

            return centeredFrame;
        }

        private void PlaceTextInColumns(Mat frame, int startX)
        {
            int startY = 200;
            int stepY = 100;
            //double[] fontScales = { 2.0, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8 };
            double[] fontScales = { 0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.3 };

            string[] lines = {
        "Coordinate", "Coordinate", "Coordinate", "Coordinate",
        "Coordinate", "Coordinate", "Coordinate", "Coordinate", 
    };

            for (int i = 0; i < lines.Length; i++)
            {
                double fontScale = fontScales[i];
                int yPosition = startY + i * stepY;
                string text = lines[i] + " // text size //" + fontScale.ToString();

                Cv2.PutText(
                    frame,
                    text,
                    new OpenCvSharp.Point(startX, yPosition),
                    HersheyFonts.HersheySimplex,
                    fontScale,
                    Scalar.White,
                    1,
                    LineTypes.AntiAlias
                );
            }
        }


        private void AddDevice(object sender, DeckLinkDiscoveryEventArgs e)
        {
            var deviceName = DeckLinkDeviceTools.GetDisplayLabel(e.deckLink);
            if (deviceName.Contains("DeckLink Duo (2)"))
            {
                m_inputDevice1 = new DeckLinkDevice(e.deckLink, m_profileCallback);
                InitializeInputDevice1();
            }
            //else if (deviceName.Contains("DeckLink Duo (3)"))
            //{
            //    m_inputDevice2 = new DeckLinkDevice(e.deckLink, m_profileCallback);
            //    InitializeInputDevice2();
            //}
            else if (deviceName.Contains("DeckLink Duo (4)"))
            {
                m_outputDevice = new DeckLinkOutputDevice(e.deckLink, m_profileCallback);
                InitializeOutputDevice();
            }
        }


        private void InitializeInputDevice1()
        {
            if (m_inputDevice1 != null)
            {
                m_inputDevice1.StartCapture(_BMDDisplayMode.bmdModeHD1080p5994, m_captureCallback1, false);
            }
        }

        //private void InitializeInputDevice2()
        //{
        //    if (m_inputDevice2 != null)
        //    {
        //        m_inputDevice2.StartCapture(_BMDDisplayMode.bmdModeHD1080p5994, m_captureCallback2, false);
        //    }
        //}

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

        private Mat ConvertUYVYToBGR(Mat uyvyFrame)
        {
            Mat bgrFrame = new Mat();
            Cv2.CvtColor(uyvyFrame, bgrFrame, ColorConversionCodes.YUV2BGR_UYVY);
            return bgrFrame;
        }

        private void OnFrameReceived(IDeckLinkVideoInputFrame videoFrame)
        {
            IntPtr frameBytes;
            videoFrame.GetBytes(out frameBytes);

            int width = videoFrame.GetWidth();
            int height = videoFrame.GetHeight();

            using (Mat capturedFrame = new Mat(height, width, OpenCvSharp.MatType.CV_8UC2, frameBytes))
            {
                //Mat processedFrame = ProcessFrameWithOpenCV(capturedFrame);

                Mat bgrImage = ConvertUYVYToBGR(capturedFrame);
                Mat processedFrame = CropAndResizeFrame(bgrImage);

                //********************************for one frame
                //Mat uyvyFrame = ConvertBGRToUYVY(processedFrame);
                //if (m_outputDevice != null)
                //{
                //    m_outputDevice.ScheduleFrame(uyvyFrame);
                //}


                //********************************for 2 frames
                //lock (frameLock)
                //{
                //    latestDeckLinkFrame1 = processedFrame.Clone();
                //}

                //ProcessAndOutputCombinedFrame();
            }
        }

        private Mat CropAndResizeFrame(Mat originalFrame)
        {
            OpenCvSharp.Rect cropRect = new OpenCvSharp.Rect(240, 0, 1440, 1080);
            Mat croppedFrame = new Mat(originalFrame, cropRect);

            //Mat resizedFrame = new Mat();
            //Cv2.Resize(croppedFrame, resizedFrame, new OpenCvSharp.Size(960, 720));

            //return resizedFrame;
            return croppedFrame;
        }

        private Mat CombineFrames(Mat leftFrame, Mat rightFrame)
        {
            Mat combinedFrame = new Mat(1080, 1920, OpenCvSharp.MatType.CV_8UC3, new Scalar(0, 0, 0));

            int offsetX = (1920 - (2 * leftFrame.Width)) / 2;
            int offsetY = (1080 - leftFrame.Height) / 2;

            leftFrame.CopyTo(combinedFrame[new OpenCvSharp.Rect(offsetX, offsetY, leftFrame.Width, leftFrame.Height)]);
            rightFrame.CopyTo(combinedFrame[new OpenCvSharp.Rect(offsetX + leftFrame.Width, offsetY, rightFrame.Width, rightFrame.Height)]);

            return combinedFrame;
        }
        private void ProcessAndOutputCombinedFrame()
        {
            lock (frameLock)
            {
                if (latestCameraFrame != null && latestDeckLinkFrame1 != null)
                {
                    Mat combinedFrame = CombineFrames(latestCameraFrame, latestDeckLinkFrame1);
                    Mat uyvyFrame = ConvertBGRToUYVY(combinedFrame);
                    m_outputDevice.ScheduleFrame(uyvyFrame);

                    combinedFrame.Dispose();
                    uyvyFrame.Dispose();

                    latestCameraFrame = null;
                    latestDeckLinkFrame1 = null;
                }
            }
        }


        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            //previus camera*
            //FindCameraIndex();
            //StartCameraCapture();
            //*

            InitializeVimba();

            m_deckLinkMainThread = new Thread(() => DeckLinkMainThread());
            m_deckLinkMainThread.SetApartmentState(ApartmentState.MTA);
            m_deckLinkMainThread.Start();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //previous camera*
            //StopCameraCapture();
            //previous camera*

            //gt1910*
            StopCamera();
            //gt1910*

            m_applicationCloseWaitHandle.Set();

            if (m_deckLinkMainThread != null && m_deckLinkMainThread.IsAlive)
            {
                m_deckLinkMainThread.Join();
            }

            DisposeDeckLinkResources();
        }

        //previous camera*
        //private void StopCameraCapture()
        //{
        //    captureRunning = false;
        //    captureThread?.Join();
        //    cameraCapture?.Dispose();
        //}
        //previous camera*

        //gt1910*
        private void StopCamera()
        {
            if (_camera != null)
            {
                _acquiring = false;

                try
                {
                    _camera.Features["AcquisitionStop"].RunCommand();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error stopping acquisition: {ex.Message}");
                }

                try
                {
                    _camera.EndCapture();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error ending capture: {ex.Message}");
                }

                try
                {
                    _camera.FlushQueue();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error flushing queue: {ex.Message}");
                }

                try
                {
                    _camera.RevokeAllFrames();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error revoking frames: {ex.Message}");
                }

                try
                {
                    _camera.Close();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error closing camera: {ex.Message}");
                }
                finally
                {
                    _camera = null;
                    Debug.WriteLine("Camera set to null.");
                }
            }

            Debug.WriteLine("Shutting down Vimba...");
            _vimba.Shutdown();
        }
        //gt1910*

        private void DisposeDeckLinkResources()
        {
            if (m_inputDevice1 != null)
            {
                m_inputDevice1.StopCapture();
                m_inputDevice1 = null;
            }

            if (m_inputDevice2 != null)
            {
                m_inputDevice2.StopCapture();
                m_inputDevice2 = null;
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
