using AVT.VmbAPINET;
using DeckLinkAPI;
using DirectShowLib;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
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
using System.Xml.Linq;
using Frame = AVT.VmbAPINET.Frame;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
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

        #region
        //previous camera*
        //private int cameraIndex = -1;
        //private VideoCapture cameraCapture;
        //private bool captureRunning;
        //private Thread captureThread;
        //*
        #endregion
        //new camera*
        private Camera _camera;
        private Vimba _vimba;
        private bool _acquiring;
        private Frame[] _frameArray;
        //*

        private Mat latestCameraFrame;
        private Mat latestDeckLinkFrame1;

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


                    //SaveFrameAsImage(AddText);

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

            Cv2.Line(centeredFrame, new OpenCvSharp.Point(1, 0), new OpenCvSharp.Point(1, 1080), Scalar.Green, 1);
            Cv2.Line(centeredFrame, new OpenCvSharp.Point(240, 0), new OpenCvSharp.Point(240, 1080), Scalar.Green, 1);
            Cv2.Line(centeredFrame, new OpenCvSharp.Point(1680, 0), new OpenCvSharp.Point(1680, 1080), Scalar.Green, 1);
            Cv2.Line(centeredFrame, new OpenCvSharp.Point(1919, 0), new OpenCvSharp.Point(1919, 1080), Scalar.Green, 1);

            Cv2.Line(centeredFrame, new OpenCvSharp.Point(0, 1), new OpenCvSharp.Point(1920, 1), Scalar.Green, 1);
            Cv2.Line(centeredFrame, new OpenCvSharp.Point(0, 1079), new OpenCvSharp.Point(1920, 1079), Scalar.Green, 1);
            //Radar
            Cv2.Line(centeredFrame, new OpenCvSharp.Point(1680, 610), new OpenCvSharp.Point(1920, 610), Scalar.Green, 1);
            //Radar + zoom\batery
            Cv2.Line(centeredFrame, new OpenCvSharp.Point(1680, 880), new OpenCvSharp.Point(1920, 880), Scalar.Green, 1);
            //zoom\batery
            //Smoke
            Cv2.Line(centeredFrame, new OpenCvSharp.Point(1, 330), new OpenCvSharp.Point(240, 330), Scalar.Green, 1);
            //Smoke + cameras
            Cv2.Line(centeredFrame, new OpenCvSharp.Point(1, 675), new OpenCvSharp.Point(240, 675), Scalar.Green, 1);
            //Cameras + text
            Cv2.Line(centeredFrame, new OpenCvSharp.Point(1, 820), new OpenCvSharp.Point(240, 820), Scalar.Green, 1);
            //text

            //Mat LeftSide = PlaceTextInColumns(centeredFrame, 25);    // For left side text
            //Mat RightSide = PlaceTextInColumns(LeftSide, 1705);  // For right side text


            // Mat LeftSide = DrawCameraIcon(centeredFrame, 0, 0, Scalar.FromRgb(255, 165, 0), Scalar.FromRgb(144, 238, 144));
            Mat LeftSide = cameraIcon(centeredFrame, 365, 0);
            LeftSide = coreanCameraA(LeftSide, 460, 1, 2);
            LeftSide = cameraIcon(LeftSide, 565, 0);
            //Mat LeftSide = cameraIcon(centeredFrame, 370);
            //LeftSide = cameraGrayIcon(LeftSide, 500);
            //LeftSide = cameraGrayIcon(LeftSide, 630);
            LeftSide = Armor(LeftSide, 60);
            LeftSide = dum(LeftSide, 20, 245, 1);
            LeftSide = dum(LeftSide, 50, 245, 2);
            LeftSide = dum(LeftSide, 80, 245, 1);
            LeftSide = dum(LeftSide, 130, 245, 2);
            LeftSide = dum(LeftSide, 160, 245, 1);
            LeftSide = dum(LeftSide, 190, 245, 2);
            //LeftSide = dum(LeftSide, 62, 205, 1);
            //LeftSide = dum(LeftSide, 112, 205, 2);
            //LeftSide = dum(LeftSide, 168,205, 1);
            //LeftSide = dum(LeftSide, 60, 275,  2);
            //LeftSide = dum(LeftSide, 112, 275, 1);
            //LeftSide = dum(LeftSide, 168,275, 2);

            //Mat RightSide = xyCoordinate(centeredFrame, 1800, 155, 110, new Scalar(144, 238, 144), new Scalar(0, 100, 0, 128), 2);
            Mat RightSide = xyCoordinate(LeftSide, 1800, 155, 110, new Scalar(144, 238, 144), new Scalar(0, 100, 0, 128), 2);
            Mat Centre = scope(RightSide);
            Mat addText = PlaceTextInColumns(Centre, 1, 2, 1282);
            addText = PlaceProblemsInColumns(addText);
            return addText;
            //return centeredFrame;
        }

        private Mat scope(Mat frame) 
        {
            //centre
            Cv2.Line(frame, new OpenCvSharp.Point(930, 540), new OpenCvSharp.Point(990, 540), Scalar.Green, 2);
            Cv2.Line(frame, new OpenCvSharp.Point(960, 510), new OpenCvSharp.Point(960, 570), Scalar.Green, 2);
            //up
            Cv2.Line(frame, new OpenCvSharp.Point(870, 480), new OpenCvSharp.Point(900, 480), Scalar.Green, 2);
            Cv2.Line(frame, new OpenCvSharp.Point(1020, 480), new OpenCvSharp.Point(1050, 480), Scalar.Green, 2);
            Cv2.Line(frame, new OpenCvSharp.Point(870, 480), new OpenCvSharp.Point(870, 510), Scalar.Green, 2);
            Cv2.Line(frame, new OpenCvSharp.Point(1050, 480), new OpenCvSharp.Point(1050, 510), Scalar.Green, 2);
            //down
            Cv2.Line(frame, new OpenCvSharp.Point(870, 600), new OpenCvSharp.Point(900, 600), Scalar.Green, 2);
            Cv2.Line(frame, new OpenCvSharp.Point(1020, 600), new OpenCvSharp.Point(1050, 600), Scalar.Green, 2);
            Cv2.Line(frame, new OpenCvSharp.Point(870, 570), new OpenCvSharp.Point(870, 600), Scalar.Green, 2);
            Cv2.Line(frame, new OpenCvSharp.Point(1050, 570), new OpenCvSharp.Point(1050, 600), Scalar.Green, 2);

            return frame;
        }
        private Mat dum(Mat frame, int x, int Y, int z)
        {
            Mat overlayImage = Cv2.ImRead(@"C:\Users\User\Desktop\font1\SmokeGreen26x50.PNG");

            if (z == 1) 
            {
                overlayImage = Cv2.ImRead(@"C:\Users\User\Desktop\font1\SmokeGray26x50.PNG");
            }
            // Define the ROI in the centeredFrame where the image will be placed
            int overlayX = x;
            int overlayY = Y;
            Rect overlayRoi = new Rect(overlayX, overlayY, overlayImage.Width, overlayImage.Height);

            // Ensure the ROI is within the bounds of the centeredFrame
            if (overlayX + overlayImage.Width <= frame.Width && overlayY + overlayImage.Height <= frame.Height)
            {
                Mat roiMat = new Mat(frame, overlayRoi);
                overlayImage.CopyTo(roiMat);
            }
            return frame;
        }
        private Mat Armor(Mat frame, int Y) 
        {
            Mat overlayImage = Cv2.ImRead(@"C:\Users\User\Desktop\font1\PatronsGray80x60.PNG");

            // Define the ROI in the centeredFrame where the image will be placed
            int overlayX = 80;
            int overlayY = Y;
            Rect overlayRoi = new Rect(overlayX, overlayY, overlayImage.Width, overlayImage.Height);

            // Ensure the ROI is within the bounds of the centeredFrame
            if (overlayX + overlayImage.Width <= frame.Width && overlayY + overlayImage.Height <= frame.Height)
            {
                Mat roiMat = new Mat(frame, overlayRoi);
                overlayImage.CopyTo(roiMat);
            }
            return frame;
        }

        private Mat cameraIcon(Mat src, int Y, int activeCamera) 
        {
            Mat overlayImage = Cv2.ImRead(@"C:\Users\User\Desktop\font1\CameraGray80x40.PNG");

            if (activeCamera == 1)
            {
                overlayImage = Cv2.ImRead(@"C:\Users\User\Desktop\font1\CameraGreen80x40.PNG");
                
            }
            // Define the ROI in the centeredFrame where the image will be placed
            int overlayX = 55;
            int overlayY = Y;
            Rect overlayRoi = new Rect(overlayX, overlayY, overlayImage.Width, overlayImage.Height);

            // Ensure the ROI is within the bounds of the centeredFrame
            if (overlayX + overlayImage.Width <= src.Width && overlayY + overlayImage.Height <= src.Height)
            {
                Mat roiMat = new Mat(src, overlayRoi);
                overlayImage.CopyTo(roiMat);
            }
            return src;
        }

        private Mat coreanCameraA(Mat src, int Y, int activeCamera, int zoom)
        {
            Mat overlayImage = Cv2.ImRead(@"C:\Users\User\Desktop\font1\CameraGray80x40.PNG");

            if (activeCamera == 1) overlayImage = Cv2.ImRead(@"C:\Users\User\Desktop\font1\CameraGreen80x40.PNG");

            // Define the ROI in the centeredFrame where the image will be placed
            int overlayX = 55;
            int overlayY = Y;
            Rect overlayRoi = new Rect(overlayX, overlayY, overlayImage.Width, overlayImage.Height);

            // Ensure the ROI is within the bounds of the centeredFrame
            if (overlayX + overlayImage.Width <= src.Width && overlayY + overlayImage.Height <= src.Height)
            {
                Mat roiMat = new Mat(src, overlayRoi);
                overlayImage.CopyTo(roiMat);
            }
            return src;
        }

        private Mat xyCoordinate(Mat frame, int x, int y, int radius, Scalar outlineColor, Scalar fillColor, int thickness)
        {
            int moove = -90;

            int startAngle = moove + 25; // Кут початку дуги (12 годинник)
            int endAngle = moove - 25;

            OpenCvSharp.Point center = new OpenCvSharp.Point(x, y);

            // Draw the filled circle
            Cv2.Circle(frame, new OpenCvSharp.Point(x, y), radius, fillColor, -1, LineTypes.AntiAlias);

            Cv2.Ellipse(frame, center, new OpenCvSharp.Size(radius, radius), 0, startAngle, endAngle, new Scalar(0, 130, 0), -1, LineTypes.AntiAlias);

            // Draw the circle outline
            Cv2.Circle(frame, new OpenCvSharp.Point(x, y), radius, outlineColor, thickness, LineTypes.AntiAlias);

            // Draw the divisions
            for (int i = 0; i < 12; i++)
            {
                double angle = 2 * Math.PI * i / 12;
                OpenCvSharp.Point outerPoint = new OpenCvSharp.Point((int)(x + radius * Math.Cos(angle)), (int)(y + radius * Math.Sin(angle)));
                OpenCvSharp.Point innerPoint = new OpenCvSharp.Point((int)(x + (radius - 10) * Math.Cos(angle)), (int)(y + (radius - 10) * Math.Sin(angle)));

                Cv2.Line(frame, outerPoint, innerPoint, new Scalar(144, 238, 144), 2, LineTypes.AntiAlias);
            }

            // Draw the additional shapes (orange line and small circles)
            // Draw the orange line
            OpenCvSharp.Point mooveP = new OpenCvSharp.Point((int)(x + radius * Math.Cos(moove * Math.PI / 180)), (int)(y + radius * Math.Sin(moove * Math.PI / 180)));

            Cv2.Line(frame, center, mooveP, new Scalar(0, 165, 255), thickness, LineTypes.AntiAlias);

            // Draw the small orange circle
            Cv2.Circle(frame, new OpenCvSharp.Point(x, y), 6, new Scalar(0, 165, 255), -1, LineTypes.AntiAlias);

            // Draw the small dark green circle
            Cv2.Circle(frame, new OpenCvSharp.Point(x, y), 3, new Scalar(0, 100, 0), -1, LineTypes.AntiAlias);

            zCoordinate(frame, new OpenCvSharp.Point(1730, 495), new OpenCvSharp.Point(1730, 325), 2, new Scalar(144, 238, 144));

            zoom(frame, 50);

            battery(frame, 50);

            return frame;
        }


        private void zCoordinate(Mat frame, OpenCvSharp.Point center, OpenCvSharp.Point point12, int thickness, Scalar color)
        {
            // Визначення радіуса за допомогою відстані між центром та точкою 12 годин
            double radius = Math.Sqrt(Math.Pow(point12.X - center.X, 2) + Math.Pow(point12.Y - center.Y, 2));

            // Координати початку і кінця дуги в градусах
            int startAngle = 10; // Кут початку дуги (12 годинник)
            int endAngle = -75; // Кут кінця дуги (4 годинник)

            // Малювання заповненого сектора кола
            Cv2.Ellipse(frame, center, new OpenCvSharp.Size(radius, radius), 0, startAngle, endAngle, new Scalar(0, 100, 0), -1, LineTypes.AntiAlias);

            // Малювання рамки сектора кола
            Cv2.Ellipse(frame, center, new OpenCvSharp.Size(radius, radius), 0, startAngle, endAngle, color, thickness, LineTypes.AntiAlias);

            // Малювання ліній від центру до крайніх точок сегмента
            Cv2.Line(frame, center, new OpenCvSharp.Point((int)(center.X + radius * Math.Cos(endAngle * Math.PI / 180)), (int)(center.Y + radius * Math.Sin(endAngle * Math.PI / 180))), color, thickness, LineTypes.AntiAlias);
            Cv2.Line(frame, center, new OpenCvSharp.Point((int)(center.X + radius * Math.Cos(startAngle * Math.PI / 180)), (int)(center.Y + radius * Math.Sin(startAngle * Math.PI / 180))), color, thickness, LineTypes.AntiAlias);
            
            // Малювання оранжевого кола радіусом 6 пікселів
            Cv2.Circle(frame, center, 6, new Scalar(0, 165, 255), thickness, LineTypes.AntiAlias);

            // Визначення координати точки, яка відповідає 7 годинам
            int angle7Hours = 0; // Кут для 7 годин
            OpenCvSharp.Point point7Hours = new OpenCvSharp.Point((int)(center.X + radius * Math.Cos(angle7Hours * Math.PI / 180)), (int)(center.Y + radius * Math.Sin(angle7Hours * Math.PI / 180)));

            // Малювання оранжевої лінії
            Cv2.Line(frame, center, point7Hours, new Scalar(0, 165, 255), thickness, LineTypes.AntiAlias);

            // Малювання зеленого кола радіусом 3 пікселя
            Cv2.Circle(frame, center, 3, new Scalar(0, 100, 0), -1, LineTypes.AntiAlias);
        }

        private void zoom(Mat frame, int x)
        {
            // Координати прямокутника
            OpenCvSharp.Point[] rectanglePoints = { new OpenCvSharp.Point(1700, 645),
                                             new OpenCvSharp.Point(1900, 645),
                                             new OpenCvSharp.Point(1900, 670),
                                             new OpenCvSharp.Point(1700, 670) };


            // Малювання прямокутника з темно-зеленим кольором
            Cv2.FillPoly(frame, new OpenCvSharp.Point[][] { rectanglePoints }, new Scalar(0, 100, 0));

            // Малювання рамки світло-зеленим кольором
            Cv2.Polylines(frame, new OpenCvSharp.Point[][] { rectanglePoints }, isClosed: true, color: new Scalar(144, 238, 144), thickness: 2, lineType: LineTypes.AntiAlias);

            OpenCvSharp.Point[] rectanglePoints1 = { new OpenCvSharp.Point(1702 , 647),
                                             new OpenCvSharp.Point(1700+x , 647),
                                             new OpenCvSharp.Point(1700+x , 668),
                                             new OpenCvSharp.Point(1702 , 668) };
            Cv2.FillPoly(frame, new OpenCvSharp.Point[][] { rectanglePoints1 }, new Scalar(0, 165, 255));
        }

        private void battery(Mat frame, int x)
        {
            // Координати прямокутника
            OpenCvSharp.Point[] rectanglePoints = { new OpenCvSharp.Point(1700, 775),
                                             new OpenCvSharp.Point(1900, 775),
                                             new OpenCvSharp.Point(1900, 800),
                                             new OpenCvSharp.Point(1700, 800) };


            // Малювання прямокутника з темно-зеленим кольором
            Cv2.FillPoly(frame, new OpenCvSharp.Point[][] { rectanglePoints }, new Scalar(0, 100, 0));

            // Малювання рамки світло-зеленим кольором
            Cv2.Polylines(frame, new OpenCvSharp.Point[][] { rectanglePoints }, isClosed: true, color: new Scalar(144, 238, 144), thickness: 2, lineType: LineTypes.AntiAlias);

            OpenCvSharp.Point[] rectanglePoints1 = { new OpenCvSharp.Point(1702 , 777),
                                             new OpenCvSharp.Point(1900-x , 777),
                                             new OpenCvSharp.Point(1900-x , 798),
                                             new OpenCvSharp.Point(1702 , 798) };
            Cv2.FillPoly(frame, new OpenCvSharp.Point[][] { rectanglePoints1 }, new Scalar(0, 165, 255));
        }


        private Mat PlaceProblemsInColumns(Mat frame)
        {
            Bitmap bitmap = MatToBitmap(frame);

            using (Graphics g = Graphics.FromImage(bitmap))
            {
                string text = "Не працює ч/б камера. Немає сигналу з далекоміру. Пошкоджено двигун повороту башти. Пошкоджено центральне живлення системи.";
                Font font = new Font("Arial", 18);
                System.Drawing.Brush brush = new SolidBrush(System.Drawing.Color.FromArgb(128, 0, 0));
                //System.Drawing.Brush brush = new SolidBrush(System.Drawing.Color.Red);
                PointF point = new PointF(255, 1000);

                // Виміряти розмір тексту
                SizeF textSize = g.MeasureString(text, font);
                float maxWidth = 1400;
                float lineHeight = textSize.Height;

                // Розділити текст на рядки, якщо його ширина перевищує maxWidth
                List<string> lines = new List<string>();
                string[] words = text.Split(' ');

                string currentLine = "";
                foreach (string word in words)
                {
                    string testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
                    SizeF testLineSize = g.MeasureString(testLine, font);

                    if (testLineSize.Width > maxWidth)
                    {
                        lines.Add(currentLine);
                        currentLine = word;
                    }
                    else
                    {
                        currentLine = testLine;
                    }
                }
                lines.Add(currentLine); // Додати останній рядок

                // Визначити загальну висоту текстового блоку
                float totalHeight = lineHeight * lines.Count;

                // Додати напівпрозорий чорний прямокутник
                RectangleF textBackground = new RectangleF(point.X, point.Y, maxWidth, totalHeight);
                System.Drawing.Brush backgroundBrush = new SolidBrush(System.Drawing.Color.FromArgb(160, 0, 0, 0)); // Напівпрозорий чорний

                g.FillRectangle(backgroundBrush, textBackground);

                // Додати текст поверх прямокутника
                for (int i = 0; i < lines.Count; i++)
                {
                    PointF linePoint = new PointF(point.X, point.Y + i * lineHeight);
                    g.DrawString(lines[i], font, brush, linePoint);
                }
            }

            Mat imageWithText = BitmapToMat(bitmap);
            bitmap.Dispose();
            return imageWithText;
        }



        private Mat PlaceTextInColumns(Mat frame,int activeCorean, int zoom, int distance)
        {
            Bitmap bitmap = MatToBitmap(frame); 

            using (Graphics g = Graphics.FromImage(bitmap))
            {
                using (Font font = new Font("Arial", 18))
                {
                    System.Drawing.Brush brush = new SolidBrush(System.Drawing.Color.Green);
                    PointF point = new PointF(30, 415);
                    g.DrawString("Камера: Ч/Б", font, brush, point);
                    PointF point1 = new PointF(30, 510);
                    g.DrawString("К: Ширококутна", font, brush, point1);
                    PointF point2 = new PointF(30, 615);
                    g.DrawString("К: Тепловізор", font, brush, point2);
                    //PointF point = new PointF(30, 460);
                    //g.DrawString("Відеокамера:Н", font, brush, point);
                    //PointF point1 = new PointF(30, 590);
                    //g.DrawString("Відеокамера:G", font, brush, point1);
                    //PointF point2 = new PointF(30, 720);
                    //g.DrawString("Відеокамера:J", font, brush, point2);
                    PointF point3 = new PointF(30, 140);
                    g.DrawString("Кількість набоїв:", font, brush, point3);
                    PointF point4 = new PointF(90, 175);
                    g.DrawString("99999", font, brush, point4);
                    PointF point5 = new PointF(1690, 285);
                    g.DrawString("Кут повороту башти", font, brush, point5);
                    PointF point6 = new PointF(1690, 540);
                    g.DrawString("Кут підйому стволу", font, brush, point6);
                    //PointF point7 = new PointF(1740, 690);
                    //g.DrawString("Зум Х2", font, brush, point7);


                    //PointF point8 = new PointF(1690, 795);
                    //g.DrawString("Заряд: 89%", font, brush, point8);


                    PointF point9 = new PointF(30, 760);
                    g.DrawString("Дальність: " + distance + "м", font, brush, point9);
                    PointF point10 = new PointF(30, 710);
                    g.DrawString("Палітра: ГЧ", font, brush, point10);


                    string distanceText = distance + "м";
                    SizeF textSize = g.MeasureString(distanceText, font);
                    RectangleF textBackground = new RectangleF(1050, 600, textSize.Width, textSize.Height);
                    System.Drawing.Brush backgroundBrush = new SolidBrush(System.Drawing.Color.FromArgb(128, 0, 0, 0)); // Semi-transparent black

                    g.FillRectangle(backgroundBrush, textBackground);

                    // Draw the distance text with semi-transparent burgundy color
                    System.Drawing.Brush brush1 = new SolidBrush(System.Drawing.Color.FromArgb(255, 139, 0, 0)); // Burgundy color
                    PointF point11 = new PointF(1050, 600);
                    g.DrawString(distanceText, font, brush1, point11);
                }
                using (Font font = new Font("Arial", 22)) 
                {
                    System.Drawing.Brush brush = new SolidBrush(System.Drawing.Color.Green);
                    PointF point7 = new PointF(1740, 690);
                    g.DrawString("Зум Х2", font, brush, point7);
                    PointF point8 = new PointF(1715, 820);
                    g.DrawString("Заряд: 89%", font, brush, point8);
                    if (activeCorean == 1)
                    {
                        PointF point9 = new PointF(160, 465);
                        g.DrawString("Х" + zoom + "", font, brush, point9);
                    }
                }
            }

            Mat imageWithText = BitmapToMat(bitmap); 
            bitmap.Dispose();
            return imageWithText;

        }

        private Bitmap MatToBitmap(Mat mat)
        {
            Bitmap bitmap = new Bitmap(mat.Width, mat.Height, PixelFormat.Format24bppRgb);
            BitmapData data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, mat.Width, mat.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            if (mat.Type() == MatType.CV_8UC1)
            {
                Mat temp = new Mat();
                Cv2.CvtColor(mat, temp, ColorConversionCodes.GRAY2BGR);
                mat = temp;
            }

            int bytes = (int)mat.Step() * mat.Rows;
            byte[] buffer = new byte[bytes];

            if (bytes <= int.MaxValue)
            {
                Marshal.Copy(mat.Data, buffer, 0, bytes);
                Marshal.Copy(buffer, 0, data.Scan0, bytes);
            }
            else
            {
                throw new ArgumentException("The image is too large to be processed by this method.");
            }

            bitmap.UnlockBits(data);
            return bitmap;
        }
        private Mat BitmapToMat(Bitmap bitmap)
        {
            Mat mat = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC3);
            BitmapData data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            int bytes = data.Stride * bitmap.Height;
            byte[] buffer = new byte[bytes];
            Marshal.Copy(data.Scan0, buffer, 0, bytes);
            Marshal.Copy(buffer, 0, mat.Data, bytes);
            bitmap.UnlockBits(data);

            return mat;
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
            #region
            //previous camera*
            //StopCameraCapture();
            //previous camera*
            #endregion

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
        #region
        //previous camera*
        //private void StopCameraCapture()
        //{
        //    captureRunning = false;
        //    captureThread?.Join();
        //    cameraCapture?.Dispose();
        //}
        //previous camera*
        #endregion



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
