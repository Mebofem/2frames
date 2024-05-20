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

            Cv2.Line(centeredFrame, new OpenCvSharp.Point(1, 0), new OpenCvSharp.Point(1, 1080), Scalar.Green, 1);
            Cv2.Line(centeredFrame, new OpenCvSharp.Point(240, 0), new OpenCvSharp.Point(240, 1080), Scalar.Green, 1);
            Cv2.Line(centeredFrame, new OpenCvSharp.Point(1680, 0), new OpenCvSharp.Point(1680, 1080), Scalar.Green, 1);
            Cv2.Line(centeredFrame, new OpenCvSharp.Point(1919, 0), new OpenCvSharp.Point(1919, 1080), Scalar.Green, 1);

            Cv2.Line(centeredFrame, new OpenCvSharp.Point(0, 1), new OpenCvSharp.Point(1920, 1), Scalar.Green, 1);
            Cv2.Line(centeredFrame, new OpenCvSharp.Point(0, 1079), new OpenCvSharp.Point(1920, 1079), Scalar.Green, 1);

            //Mat LeftSide = PlaceTextInColumns(centeredFrame, 25);    // For left side text
            //Mat RightSide = PlaceTextInColumns(LeftSide, 1705);  // For right side text
            

            // Mat LeftSide = DrawCameraIcon(centeredFrame, 0, 0, Scalar.FromRgb(255, 165, 0), Scalar.FromRgb(144, 238, 144));
            Mat LeftSide = cameraIcon(centeredFrame, 300);
            LeftSide = cameraGrayIcon(LeftSide, 500);
            LeftSide = cameraOrangeIcon(LeftSide, 700);
            LeftSide = Armor(LeftSide, 60);
            LeftSide = dum(LeftSide, 20, 900);
            LeftSide = dum(LeftSide, 84, 900);
            LeftSide = dum(LeftSide, 148, 900);
            LeftSide = dum(LeftSide, 20, 964);
            LeftSide = dum(LeftSide, 84, 964);
            LeftSide = dum(LeftSide, 148, 964);

            //Mat RightSide = xyCoordinate(centeredFrame, 1800, 155, 110, new Scalar(144, 238, 144), new Scalar(0, 100, 0, 128), 2);
            Mat RightSide = xyCoordinate(LeftSide, 1800, 155, 110, new Scalar(144, 238, 144), new Scalar(0, 100, 0, 128), 2);
            Mat addText = PlaceTextInColumns(RightSide);
            return addText;
            //return centeredFrame;
        }

        private Mat dum(Mat frame,int x,  int Y)
        {
            Mat overlayImage = Cv2.ImRead(@"C:\Users\User\Desktop\font\dum64.PNG");

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
            Mat overlayImage = Cv2.ImRead(@"C:\Users\User\Desktop\font\free.PNG");

            // Define the ROI in the centeredFrame where the image will be placed
            int overlayX = 55;
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

        private Mat cameraIcon(Mat src, int Y) 
        {
            Mat overlayImage = Cv2.ImRead(@"C:\Users\User\Desktop\font\CameraGreen.PNG");

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
        private Mat cameraOrangeIcon(Mat src, int Y)
        {
            Mat overlayImage = Cv2.ImRead(@"C:\Users\User\Desktop\font\CameraOrange.PNG");

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
        private Mat cameraGrayIcon(Mat src, int Y)
        {
            Mat overlayImage = Cv2.ImRead(@"C:\Users\User\Desktop\font\CameraGray.PNG");

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


        private Mat DrawCameraIcon(Mat img, int x, int y, Scalar mainColor, Scalar borderColor)
        {
            // Малюємо прямокутник для корпусу камери
            OpenCvSharp.Point[] cameraBody = new OpenCvSharp.Point[] { new OpenCvSharp.Point(20, 360), new OpenCvSharp.Point(20, 480), new OpenCvSharp.Point(220, 480), new OpenCvSharp.Point(220, 360) };
            Cv2.FillConvexPoly(img, cameraBody, Scalar.FromRgb(255, 165, 0), LineTypes.AntiAlias);

            // Малюємо лінії на верхній частині камери
            OpenCvSharp.Point[] lines = new OpenCvSharp.Point[] { new OpenCvSharp.Point(60, 360), new OpenCvSharp.Point(100, 340), new OpenCvSharp.Point(140, 340), new OpenCvSharp.Point(180, 360) };
            Cv2.FillConvexPoly(img, lines, Scalar.FromRgb(255, 165, 0), LineTypes.AntiAlias);

            // Малюємо отвори на камері
            Cv2.Circle(img, new OpenCvSharp.Point(65, 390), 10, Scalar.FromRgb(144, 238, 144), -1, LineTypes.AntiAlias);
            Cv2.Circle(img, new OpenCvSharp.Point(120, 420), 40, Scalar.FromRgb(144, 238, 144), -1, LineTypes.AntiAlias);
            Cv2.Circle(img, new OpenCvSharp.Point(120, 420), 30, Scalar.FromRgb(255, 165, 0), -1, LineTypes.AntiAlias);

            // Малюємо рамку навколо камери
            Cv2.Polylines(img, new OpenCvSharp.Point[][] { cameraBody }, true, Scalar.FromRgb(144, 238, 144), 2, LineTypes.AntiAlias);
            Cv2.Polylines(img, new OpenCvSharp.Point[][] { lines }, true, Scalar.FromRgb(144, 238, 144), 2, LineTypes.AntiAlias);
            return img;
        }


        private Mat xyCoordinate(Mat frame, int x, int y, int radius, Scalar outlineColor, Scalar fillColor, int thickness)
        {
            // Draw the filled circle
            Cv2.Circle(frame, new OpenCvSharp.Point(x, y), radius, fillColor, -1, LineTypes.AntiAlias);

            // Draw the circle outline
            Cv2.Circle(frame, new OpenCvSharp.Point(x, y), radius, outlineColor, thickness, LineTypes.AntiAlias);

            // Draw the additional shapes (orange line and small circles)
            // Draw the orange line
            Cv2.Line(frame, new OpenCvSharp.Point(x, y), new OpenCvSharp.Point(x, y - 107), new Scalar(0, 165, 255), 3);

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
            int startAngle = 30; // Кут початку дуги (12 годинник)
            int endAngle = -90; // Кут кінця дуги (4 годинник)

            // Малювання заповненого сектора кола
            Cv2.Ellipse(frame, center, new OpenCvSharp.Size(radius, radius), 0, startAngle, endAngle, new Scalar(0, 100, 0), -1, LineTypes.AntiAlias);

            // Малювання рамки сектора кола
            Cv2.Ellipse(frame, center, new OpenCvSharp.Size(radius, radius), 0, startAngle, endAngle, color, thickness, LineTypes.AntiAlias);

            // Малювання ліній від центру до крайніх точок сегмента
            Cv2.Line(frame, center, point12, color, thickness, LineTypes.AntiAlias);
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
            OpenCvSharp.Point[] rectanglePoints = { new OpenCvSharp.Point(1700, 745),
                                             new OpenCvSharp.Point(1900, 745),
                                             new OpenCvSharp.Point(1900, 770),
                                             new OpenCvSharp.Point(1700, 770) };


            // Малювання прямокутника з темно-зеленим кольором
            Cv2.FillPoly(frame, new OpenCvSharp.Point[][] { rectanglePoints }, new Scalar(0, 100, 0));

            // Малювання рамки світло-зеленим кольором
            Cv2.Polylines(frame, new OpenCvSharp.Point[][] { rectanglePoints }, isClosed: true, color: new Scalar(144, 238, 144), thickness: 2, lineType: LineTypes.AntiAlias);

            OpenCvSharp.Point[] rectanglePoints1 = { new OpenCvSharp.Point(1702 , 747),
                                             new OpenCvSharp.Point(1900-x , 747),
                                             new OpenCvSharp.Point(1900-x , 768),
                                             new OpenCvSharp.Point(1702 , 768) };
            Cv2.FillPoly(frame, new OpenCvSharp.Point[][] { rectanglePoints1 }, new Scalar(0, 165, 255));
        }


        private Mat PlaceTextInColumns(Mat frame)//, int startX)
        {
        //    int startY = 200;
        //    int stepY = 45;

    //        string[] lines = {
    //    "Відеокамера:Н", "Дальність:1234", "Дальність 0000", "Тепловізор х8",
    //    "Збільшення:х50", "Збільшення:х48", "Режим вогню", "Р.вогню: КЧ", "Р.вогню КЧ", "Яскравість: 10", "Палітра: ГЧ", "Супровід", "Подвійний А", "Подвійний: А", "Г:-125.00", "В:-120.00", "В:-120.00"
    //};
            Bitmap bitmap = MatToBitmap(frame); 

            using (Graphics g = Graphics.FromImage(bitmap))
            {
                using (Font font = new Font("Arial", 18))
                {
                    System.Drawing.Brush brush = new SolidBrush(System.Drawing.Color.Green);

                    //for (int i = 0; i < lines.Length; i++)
                    //{
                    //    PointF point = new PointF(startX, startY + i * stepY);
                    //    g.DrawString(lines[i], font, brush, point);
                    //}
                    PointF point = new PointF(30, 420);
                    g.DrawString("Відеокамера:Н", font, brush, point);
                    PointF point1 = new PointF(30, 620);
                    g.DrawString("Відеокамера:G", font, brush, point1);
                    PointF point2 = new PointF(30, 820);
                    g.DrawString("Відеокамера:J", font, brush, point2);
                    PointF point3 = new PointF(30, 220);
                    g.DrawString("Кількість набоїв:", font, brush, point3);
                    PointF point4 = new PointF(90, 242);
                    g.DrawString("99999", font, brush, point4);
                    PointF point5 = new PointF(1690, 285);
                    g.DrawString("Кут повороту башні", font, brush, point5);
                    PointF point6 = new PointF(1690, 590);
                    g.DrawString("Кут підйому стволу", font, brush, point6);
                    PointF point7 = new PointF(1690, 680);
                    g.DrawString("Зум Х2", font, brush, point7);
                    PointF point8 = new PointF(1690, 780);
                    g.DrawString("Заряд: 89%", font, brush, point8);
                    PointF point9 = new PointF(1690, 830);
                    g.DrawString("Дальність 0000", font, brush, point9);
                    PointF point10 = new PointF(1690, 880);
                    g.DrawString("Палітра: ГЧ", font, brush, point10);
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
