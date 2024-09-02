using AVT.VmbAPINET;
using DeckLinkAPI;
using DirectShowLib;
using Microsoft.Win32;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml.Linq;
using Camera = AVT.VmbAPINET.Camera;
using Frame = AVT.VmbAPINET.Frame;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Point = OpenCvSharp.Point;
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

        private Mat deckLinkFrame1;
        private Mat deckLinkFrame2;
        private Mat cameraFrame;

        private int frameSkipCount = 5;

        //new camera*+
        private Camera _camera;
        private Vimba _vimba;
        private bool _acquiring;
        private Frame[] _frameArray;
        //*
        private bool isActive = false;
        
        private object frameLock = new object();

        private SerialPort serialPort;


        #region inputDatas
        static int gunElevation = -45;
        static int turretAngle = 0;
        static byte camera = 0, palette = 2, fireMode, color, contrast, brightness, range, ammo, menu;
        static byte plus, select, minus, combatMode, button_sleep;
        static int distance = 50, batteryLevel = 81, smokeGrenades;
        static double zoomLevel = 3.1;
        static int  barrelTemp;
        static int  errors, remainingAmmo, impulseCount, addDistance = 1;
        static int angle_OB = -10;

        static int scaleFactor1 = 1400;

        static int camera1A, camera2A, camera3A;

        static double scopeX, scopeY;

        Mat green = new Mat(1080, 1440, MatType.CV_8UC3, new Scalar(0, 0, 0));
        #endregion

        private readonly Dictionary<int, string> palletesForTeplak = new()
        {
            {0, "ГБ"},
            {1, "ГЧ"},
            {2, "ПК"},
            {3, "СУ"},
            {4, "НЧ"}
        };

        private readonly Dictionary<int, int> offsetTable = new()
        {
            { 50, -320}, { 100, -303}, { 150, -286}, { 200, -270},
            { 250, -252}, { 300, -236}, { 350, -219}, { 400, -202},
            { 450, -185}, { 500, -168}, { 600, -135}, { 700, -101},
            { 800, -67}, { 900, -33}, { 1000, 0}, { 1100, 33},
            { 1200, 67}, { 1300, 101}, { 1400, 135}, { 1500, 168},
            { 1600, 202}, { 1700, 236}, { 1800, 270}, { 1900, 303},
            { 2000, 337}
        };

        private readonly Dictionary<int, double> elevationOffsetTable = new()
        {
            {75, 307.9},
            {74, 308.5},
            {73, 309},
            {72, 309.4},
            {71, 309.8},
            {70, 310},
            {69, 310.17},
            {68, 310.22},
            {67, 310.19},
            {66, 310.1},
            {65, 309.8},
            {64, 309.5},
            {63, 309.1},
            {62, 308.6},
            {61, 308},
            {60, 307.3},
            {59, 306.5},
            {58, 305.6},
            {57, 304.6},
            {56, 303.6},
            {55, 302.4},
            {54, 301.2},
            {53, 299.8},
            {52, 298.4},
            {51, 296.8},
            {50, 295.2},
            {49, 293.5},
            {48, 291.7},
            {47, 289.8},
            {46, 293.5},
            {45, 285.8},
            {44, 283.7},
            {43, 281.4},
            {42, 279.1},
            {41, 276.7},
            {40, 274.2},
            {39, 271.6},
            {38, 269},
            {37, 266},
            {36, 263.4},
            {35, 260.5},
            {34, 257.6},
            {33, 254.5},
            {32, 251.4},
            {31, 248.2},
            {30, 244.9},
            {29, 241.5},
            {28, 238.1},
            {27, 234.6},
            {26, 231},
            {25, 227.4},
            {24, 223.7},
            {23, 219.9},
            {22, 216},
            {21, 212.1},
            {20, 208.1},
            {19, 204.1},
            {18, 200},
            {17, 195.8},
            {16, 191.6},
            {15, 187.3},
            {14, 183},
            {13, 178.6},
            {12, 174.1},
            {11, 169.6},
            {10, 165.1},
            {9, 160.5},
            {8, 155.8},
            {7, 151.1},
            {6, 146.4},
            {5, 141.6},
            {4, 136.7},
            {3, 131.9},
            {2, 126.9},
            {1, 122},
            {0, 117},
            {-1, 112},
            {-2, 106.9},
            {-3, 101.8},
            {-4, 96.7},
            {-5, 91.5},
            {-6, 86.4},
            {-7, 81.2},
            {-8, 75.9},
            {-9, 70.7},
            {-10, 65.4}
        };

        public MainWindow()
        {
            InitializeComponent();
            InitializeUART();
            m_applicationCloseWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);

        }
        #region Uart

        private void InitializeUART()
        {
            serialPort = new SerialPort("COM5", 115200, Parity.None, 8, StopBits.One);
            serialPort.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);
            serialPort.Open();
        }

        private void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort sp = (SerialPort)sender;
            List<byte> buffer = new List<byte>();

            try
            {
                while (sp.BytesToRead > 0)
                {
                    int byteRead = sp.ReadByte();
                    buffer.Add((byte)byteRead);

                    if (buffer.Count == 19)  // Перевіряємо, чи отримали весь пакет
                    {
                        UpdateVariables(buffer.ToArray());
                        buffer.Clear();  // Очищаємо буфер для наступного пакету
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error receiving data: " + ex.Message);
            }
        }


        private void UpdateVariables(byte[] data_packet)
        {
            if (data_packet[0] == 0x55 && data_packet[18] == 0xFF)
            {
                if (data_packet[1] <= 10)
                {
                    gunElevation = 10 - data_packet[1];
                }
                else 
                {
                    gunElevation = 0 - data_packet[1] + 10;
                }

                turretAngle = (data_packet[2] * 2) - 90;

                camera = (byte)(data_packet[3] & 0x03);
                palette = (byte)((data_packet[3] >> 2) & 0x07);
                fireMode = (byte)((data_packet[3] >> 5) & 0x03);
                color = (byte)((data_packet[3] >> 7) & 0x01);

                contrast = (byte)(data_packet[4] & 0x03);
                brightness = (byte)((data_packet[4] >> 2) & 0x03);
                range = (byte)((data_packet[4] >> 4) & 0x01);
                ammo = (byte)((data_packet[4] >> 5) & 0x01);
                menu = (byte)((data_packet[4] >> 6) & 0x01);

                plus = (byte)(data_packet[5] & 0x01);
                select = (byte)((data_packet[5] >> 1) & 0x01);
                minus = (byte)((data_packet[5] >> 2) & 0x01);
                combatMode = (byte)((data_packet[5] >> 3) & 0x01);

                distance = (ushort)((data_packet[6] << 8) | data_packet[7]);

                batteryLevel = data_packet[8];
                zoomLevel = data_packet[9];
                smokeGrenades = data_packet[10];
                barrelTemp = (sbyte)(data_packet[11] - 30);  

                errors = data_packet[12];
                remainingAmmo = data_packet[13];
                impulseCount = data_packet[14];
                addDistance = data_packet[15];
                angle_OB = (sbyte)data_packet[16];
                button_sleep = data_packet[17];

                //UpdateTextBlocks();
            }
        }

        //private void UpdateTextBlocks()
        //{
        //    TextGunElevation.Text = gunElevation.ToString();
        //    TextTurretAngle.Text = turretAngle.ToString();
        //    TextCamera.Text = camera.ToString();
        //    TextPalette.Text = palette.ToString();
        //    TextFireMode.Text = fireMode.ToString();
        //    TextColor.Text = color.ToString();
        //    TextContrast.Text = contrast.ToString();
        //    TextBrightness.Text = brightness.ToString();
        //    TextRange.Text = range.ToString();
        //    TextAmmo.Text = ammo.ToString();
        //    TextMenu.Text = menu.ToString();
        //    TextPlus.Text = plus.ToString();
        //    TextSelect.Text = select.ToString();
        //    TextMinus.Text = minus.ToString();
        //    TextCombatMode.Text = combatMode.ToString();
        //    TextDistance.Text = distance.ToString();
        //    TextBatteryLevel.Text = batteryLevel.ToString();
        //    TextZoomLevel.Text = zoomLevel.ToString();
        //    TextSmokeGrenades.Text = smokeGrenades.ToString();
        //    TextBarrelTemp.Text = barrelTemp.ToString();
        //    TextErrors.Text = errors.ToString();
        //    TextRemainingAmmo.Text = remainingAmmo.ToString();
        //    TextImpulseCount.Text = impulseCount.ToString();
        //    TextAddDistance.Text = addDistance.ToString();
        //    TextAngle_OB.Text = angle_OB.ToString();
        //    TextButton_Sleep.Text = button_sleep.ToString();
        //}

        #endregion
        private void DeckLinkMainThread()
        {
            m_profileCallback = new ProfileCallback();
            m_captureCallback1 = new CaptureCallback();
            m_captureCallback2 = new CaptureCallback();
            m_playbackCallback = new PlaybackCallback();

            

            m_captureCallback1.FrameReceived += OnFrameReceived1;
            m_captureCallback2.FrameReceived += OnFrameReceived2;

            m_deckLinkDeviceDiscovery = new DeckLinkDeviceDiscovery();
            m_deckLinkDeviceDiscovery.DeviceArrived += AddDevice;
            m_deckLinkDeviceDiscovery.Enable();


            m_applicationCloseWaitHandle.WaitOne();

            m_captureCallback1.FrameReceived -= OnFrameReceived1;
            m_captureCallback2.FrameReceived -= OnFrameReceived2;

            DisposeDeckLinkResources();
        }

        #region Camera
        //Gt1910*
        private async void InitializeVimba()  //private async void InitializeVimba()
        {
            await Task.Delay(17000);
            
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
            _frameArray = new Frame[2];
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

                    Mat bgrImage = new Mat();

                    Cv2.CvtColor(mono8, bgrImage, ColorConversionCodes.GRAY2BGR);

                    Mat processedFrame = ProcessCameraFrame(bgrImage);

                    //SaveFrameAsImage(AddText);

                    //********************************for one frame
                    isActive = true;
                    lock (frameLock)
                    {
                        cameraFrame = processedFrame.Clone();
                    }
                    finalPartOfProcessingFrame();


                    //Mat AddText = CenterFrame(processedFrame);
                    //Mat uyvyFrame = ConvertBGRToUYVY(AddText);
                    //if (m_outputDevice != null)
                    //{
                    //    m_outputDevice.ScheduleFrame(uyvyFrame);
                    //}
                }
            }
        }
        private Mat ProcessCameraFrame(Mat frame)
        {
            return CropAndResizeFrame(frame); //for 2
        }
        //Gt1910*

        #endregion

        private void SaveFrameAsImage(Mat uyvyFrame)
        {
            string imagePath = @"C:\Users\Kressol\Desktop\font\frame_uyvy_Arial22.jpg";

            Cv2.ImWrite(imagePath, uyvyFrame);
        }

        #region settingsForVideo
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
            if (camera == 0)
            {
                camera1A = 1;
                camera2A = 0;
                camera3A = 0;
            }
            else if (camera == 1)
            {
                camera1A = 0;
                camera2A = 1;
                camera3A = 0;
            }
            else 
            {
                camera1A = 0;
                camera2A = 0;
                camera3A = 1;
            }
            Mat LeftSide = cameraIcon(centeredFrame, 365, camera1A);
            LeftSide = coreanCameraA(LeftSide, 460, camera2A, zoomLevel);
            LeftSide = coreanCameraA(LeftSide, 565, camera3A, zoomLevel);
            LeftSide = Armor(LeftSide, 60, 1);
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
            Mat RightSide = xyCoordinate(LeftSide, 1800, 155, 110, new Scalar(144, 238, 144), new Scalar(0, 100, 0, 128), 2, turretAngle);
            Mat Centre = scope(RightSide);
            //scope(RightSide);
            Mat addText = PlaceTextInColumns(Centre, camera, zoomLevel, distance, batteryLevel, palletesForTeplak, palette, scopeX, scopeY);
            //addText = PlaceProblemsInColumns(addText);
            //Mat r = DrawTrianglesAndRectangle(addText, 1);
            //return r;
            return addText;
        }
        private Mat DrawTrianglesAndRectangle(Mat frame, int triangleToChange)
        {
            // Крайні положення прямокутника
            int rectLeft = (frame.Width - 960) / 2;
            int rectTop = (frame.Height - 540) / 2;
            int rectWidth = 960;
            int rectHeight = 540;

            // Малювання сірого прямокутника з сірою рамкою
            OpenCvSharp.Rect rectangle = new OpenCvSharp.Rect(rectLeft, rectTop, rectWidth, rectHeight);
            Cv2.Rectangle(frame, rectangle, new Scalar(0, 255, 0), 3); // Зелений
            Cv2.Rectangle(frame, rectangle, new Scalar(0, 0, 0), -1); // Чорний

            // Визначення рівностороннього трикутника
            int sideLength = 70;
            int height = (int)(Math.Sqrt(3) / 2 * sideLength);
            int centerX = frame.Width / 2;
            int centerY = frame.Height / 2;

            // Функція для малювання трикутника
            void DrawTriangle(int offsetX, int offsetY, Scalar color, bool mirrored)
            {
                Point[] triangle = new Point[]
                {
            new Point(centerX + offsetX, centerY + offsetY + (mirrored ? -height / 2 : height / 2)),
            new Point(centerX + offsetX - sideLength / 2, centerY + offsetY + (mirrored ? height / 2 : -height / 2)),
            new Point(centerX + offsetX + sideLength / 2, centerY + offsetY + (mirrored ? height / 2 : -height / 2))
                };
                Cv2.FillConvexPoly(frame, triangle, color);
            }

            // Малювання трикутників
            Scalar orange = new Scalar(0, 165, 255);
            Scalar green = new Scalar(0, 255, 0);

            int distanceBetweenTriangles = 70;

            // Верхні трикутники
            DrawTriangle(-150, 0, triangleToChange == 1 ? orange : green, false);
            DrawTriangle(0, 0, triangleToChange == 2 ? orange : green, false);
            DrawTriangle(150, 0, triangleToChange == 3 ? orange : green, false);

            // Нижні трикутники
            DrawTriangle(-150, distanceBetweenTriangles + height, triangleToChange == 1 ? orange : green, true);
            DrawTriangle(0, distanceBetweenTriangles + height, triangleToChange == 2 ? orange : green, true);
            DrawTriangle(150, distanceBetweenTriangles + height, triangleToChange == 3 ? orange : green, true);

            // Налаштування шрифту для цифр
            double fontScale = 2.0; // Розмір шрифту для висоти цифр приблизно 40 пікселів
            int thickness = 2;
            Cv2.PutText(frame, "1", new Point(centerX - 140 - sideLength / 2, centerY + height + 25), HersheyFonts.HersheySimplex, fontScale, new Scalar(0, 0, 255), thickness);
            Cv2.PutText(frame, "2", new Point(centerX + 15 - sideLength / 2, centerY + height + 25), HersheyFonts.HersheySimplex, fontScale, new Scalar(0, 0, 255), thickness);
            Cv2.PutText(frame, "3", new Point(centerX + 165 - sideLength / 2, centerY + height + 25), HersheyFonts.HersheySimplex, fontScale, new Scalar(0, 0, 255), thickness);

            return frame;
        }
        private Mat scope(Mat frame)
        {
            #region previous scope
            //// Draw the black borders for the center lines
            //Cv2.Line(frame, new OpenCvSharp.Point(929, 539), new OpenCvSharp.Point(991, 539), Scalar.Black, 4);
            //Cv2.Line(frame, new OpenCvSharp.Point(929, 541), new OpenCvSharp.Point(991, 541), Scalar.Black, 4);
            //Cv2.Line(frame, new OpenCvSharp.Point(959, 509), new OpenCvSharp.Point(961, 509), Scalar.Black, 4);
            //Cv2.Line(frame, new OpenCvSharp.Point(959, 571), new OpenCvSharp.Point(961, 571), Scalar.Black, 4);

            //// Draw the green lines for the center
            //Cv2.Line(frame, new OpenCvSharp.Point(930, 540), new OpenCvSharp.Point(990, 540), Scalar.White, 2);
            //Cv2.Line(frame, new OpenCvSharp.Point(960, 510), new OpenCvSharp.Point(960, 570), Scalar.White, 2);

            //// Draw the black borders for the up lines
            //Cv2.Line(frame, new OpenCvSharp.Point(869, 479), new OpenCvSharp.Point(901, 479), Scalar.Black, 4);
            //Cv2.Line(frame, new OpenCvSharp.Point(869, 481), new OpenCvSharp.Point(901, 481), Scalar.Black, 4);
            //Cv2.Line(frame, new OpenCvSharp.Point(1019, 479), new OpenCvSharp.Point(1051, 479), Scalar.Black, 4);
            //Cv2.Line(frame, new OpenCvSharp.Point(1019, 481), new OpenCvSharp.Point(1051, 481), Scalar.Black, 4);
            //Cv2.Line(frame, new OpenCvSharp.Point(869, 479), new OpenCvSharp.Point(869, 511), Scalar.Black, 4);
            //Cv2.Line(frame, new OpenCvSharp.Point(871, 479), new OpenCvSharp.Point(871, 511), Scalar.Black, 4);
            //Cv2.Line(frame, new OpenCvSharp.Point(1049, 479), new OpenCvSharp.Point(1049, 511), Scalar.Black, 4);
            //Cv2.Line(frame, new OpenCvSharp.Point(1051, 479), new OpenCvSharp.Point(1051, 511), Scalar.Black, 4);

            //// Draw the green lines for the up
            //Cv2.Line(frame, new OpenCvSharp.Point(870, 480), new OpenCvSharp.Point(900, 480), Scalar.White, 2);
            //Cv2.Line(frame, new OpenCvSharp.Point(1020, 480), new OpenCvSharp.Point(1050, 480), Scalar.White, 2);
            //Cv2.Line(frame, new OpenCvSharp.Point(870, 480), new OpenCvSharp.Point(870, 510), Scalar.White, 2);
            //Cv2.Line(frame, new OpenCvSharp.Point(1050, 480), new OpenCvSharp.Point(1050, 510), Scalar.White, 2);

            //// Draw the black borders for the down lines
            //Cv2.Line(frame, new OpenCvSharp.Point(869, 599), new OpenCvSharp.Point(901, 599), Scalar.Black, 4);
            //Cv2.Line(frame, new OpenCvSharp.Point(869, 601), new OpenCvSharp.Point(901, 601), Scalar.Black, 4);
            //Cv2.Line(frame, new OpenCvSharp.Point(1019, 599), new OpenCvSharp.Point(1051, 599), Scalar.Black, 4);
            //Cv2.Line(frame, new OpenCvSharp.Point(1019, 601), new OpenCvSharp.Point(1051, 601), Scalar.Black, 4);
            //Cv2.Line(frame, new OpenCvSharp.Point(869, 569), new OpenCvSharp.Point(869, 601), Scalar.Black, 4);
            //Cv2.Line(frame, new OpenCvSharp.Point(871, 569), new OpenCvSharp.Point(871, 601), Scalar.Black, 4);
            //Cv2.Line(frame, new OpenCvSharp.Point(1049, 569), new OpenCvSharp.Point(1049, 601), Scalar.Black, 4);
            //Cv2.Line(frame, new OpenCvSharp.Point(1051, 569), new OpenCvSharp.Point(1051, 601), Scalar.Black, 4);

            //// Draw the green lines for the down
            //Cv2.Line(frame, new OpenCvSharp.Point(870, 600), new OpenCvSharp.Point(900, 600), Scalar.White, 2);
            //Cv2.Line(frame, new OpenCvSharp.Point(1020, 600), new OpenCvSharp.Point(1050, 600), Scalar.White, 2);
            //Cv2.Line(frame, new OpenCvSharp.Point(870, 570), new OpenCvSharp.Point(870, 600), Scalar.White, 2);
            //Cv2.Line(frame, new OpenCvSharp.Point(1050, 570), new OpenCvSharp.Point(1050, 600), Scalar.White, 2);
            #endregion
            // Get offset based on distance
            var offset = GetOffsetForDistance((int)distance, (int)angle_OB);

            // Calculate the scale factor based on distance and zoom level
            double scaleFactor = CalculateScaleFactor(distance, zoomLevel, scaleFactor1);

            // Apply the scale factor to the offsets
            double scaledOffsetX = offset.X * scaleFactor;
            double scaledOffsetY = offset.Y * scaleFactor; // Note the inversion of Y due to image coordinates

            // Calculate barrel crosshair position
            double barrelX = 1920 / 2 + scaledOffsetX;
            double barrelY = 1080 / 2 - scaledOffsetY; // Note the inversion of Y due to image coordinates

            scopeX = barrelX;
            scopeY = barrelY;

            // Ensure crosshair positions are within image bounds
            barrelX = Math.Max(0, Math.Min(1920 - 1, barrelX));
            barrelY = Math.Max(0, Math.Min(1080 - 1, barrelY));

            // Draw the custom white crosshair for the barrel

            DrawCustomCrosshair(frame, (int)barrelX, (int)barrelY);
            return frame;
        }
        private void DrawCustomCrosshair(Mat frame, int centerX, int centerY)
        {
            // Define the radius of the small central dot
            int dotRadius = 2; // Smaller dot
            int lineLength = 30;
            int gap = 10;

            // Draw the small white dot with a black border
            Cv2.Circle(frame, new OpenCvSharp.Point(centerX, centerY), dotRadius + 1, Scalar.Black, -1); // Black border
            Cv2.Circle(frame, new OpenCvSharp.Point(centerX, centerY), dotRadius, Scalar.White, -1); // White dot

            // Draw the thinner white lines with black borders and a gap around the dot
            // Horizontal line
            Cv2.Line(frame, new OpenCvSharp.Point(centerX - lineLength - gap, centerY),
                     new OpenCvSharp.Point(centerX - gap, centerY), Scalar.Black, 2); // Left line black border
            Cv2.Line(frame, new OpenCvSharp.Point(centerX + gap, centerY),
                     new OpenCvSharp.Point(centerX + lineLength + gap, centerY), Scalar.Black, 2); // Right line black border

            Cv2.Line(frame, new OpenCvSharp.Point(centerX - lineLength - gap, centerY),
                     new OpenCvSharp.Point(centerX - gap, centerY), Scalar.White, 1); // Left line white
            Cv2.Line(frame, new OpenCvSharp.Point(centerX + gap, centerY),
                     new OpenCvSharp.Point(centerX + lineLength + gap, centerY), Scalar.White, 1); // Right line white

            // Vertical line
            Cv2.Line(frame, new OpenCvSharp.Point(centerX, centerY - lineLength - gap),
                     new OpenCvSharp.Point(centerX, centerY - gap), Scalar.Black, 2); // Top line black border
            Cv2.Line(frame, new OpenCvSharp.Point(centerX, centerY + gap),
                     new OpenCvSharp.Point(centerX, centerY + lineLength + gap), Scalar.Black, 2); // Bottom line black border

            Cv2.Line(frame, new OpenCvSharp.Point(centerX, centerY - lineLength - gap),
                     new OpenCvSharp.Point(centerX, centerY - gap), Scalar.White, 1); // Top line white
            Cv2.Line(frame, new OpenCvSharp.Point(centerX, centerY + gap),
                     new OpenCvSharp.Point(centerX, centerY + lineLength + gap), Scalar.White, 1); // Bottom line white
        }
        private (int X, int Y) GetOffsetForDistance(int distance, int angle)
        {
            // Interpolate the X offset based on the distance
            int xOffset = InterpolateOffset(offsetTable, distance);

            // Get the Y offset based on the angle
            double yOffset = InterpolateElevation(elevationOffsetTable, angle);

            return (xOffset, (int)yOffset);
        }
        private int InterpolateOffset(Dictionary<int, int> table, int distance)
        {
            // Ensure the distance is within the range of the dictionary keys
            if (table.ContainsKey(distance))
            {
                return table[distance];
            }

            // Interpolate between nearest values with finer granularity
            var keys = new List<int>(table.Keys);
            keys.Sort();

            // Find the nearest two keys for interpolation
            for (int i = 0; i < keys.Count - 1; i++)
            {
                if (distance >= keys[i] && distance <= keys[i + 1])
                {
                    var x1 = table[keys[i]];
                    var x2 = table[keys[i + 1]];

                    // Calculate the interpolation factor with a finer step
                    double t = (distance - keys[i]) / (double)(keys[i + 1] - keys[i]);
                    return (int)(x1 + t * (x2 - x1));
                }
            }

            return table.Values.FirstOrDefault(); // Default value if out of bounds
        }
        private double InterpolateElevation(Dictionary<int, double> table, int angle)
        {
            if (table.ContainsKey(angle))
            {
                return table[angle];
            }

            // Interpolate between nearest values
            var keys = new List<int>(table.Keys);
            keys.Sort();

            for (int i = 0; i < keys.Count - 1; i++)
            {
                if (angle >= keys[i] && angle <= keys[i + 1])
                {
                    var y1 = table[keys[i]];
                    var y2 = table[keys[i + 1]];

                    double t = (angle - keys[i]) / (double)(keys[i + 1] - keys[i]);
                    return y1 + t * (y2 - y1);
                }
            }

            return table.Values.FirstOrDefault(); // Default value if out of bounds
        }
        private double CalculateScaleFactor(double distance, double zoomLevel, double scaleFactor1)
        {
            // Camera parameters
            double horizontalFovDegrees = 52.62; // degrees for wide angle (horizontal FoV)
            double verticalFovDegrees = 31.08;   // degrees for wide angle (vertical FoV)
            double horizontalFovRadians = horizontalFovDegrees * Math.PI / 180;
            double verticalFovRadians = verticalFovDegrees * Math.PI / 180;

            // Width and height of the image
            double imageWidth = 1920;
            double imageHeight = 1080;

            // Calculate field of view dimensions at the given distance
            double fieldOfViewWidth = 2 * distance * Math.Tan(horizontalFovRadians / 2);
            double fieldOfViewHeight = 2 * distance * Math.Tan(verticalFovRadians / 2);

            // Calculate pixels per millimeter for both width and height
            double pixelsPerMmX = imageWidth / fieldOfViewWidth;
            double pixelsPerMmY = imageHeight / fieldOfViewHeight;

            // Scale factor based on zoom level and scale factor 1
            double scaleFactorX = pixelsPerMmX * zoomLevel / scaleFactor1;
            double scaleFactorY = pixelsPerMmY * zoomLevel / scaleFactor1;

            // You can return either the X or Y scale factor or their average
            return (scaleFactorX + scaleFactorY) / 2; // Average scale factor for balanced scaling
        }
        private Mat dum(Mat frame, int x, int Y, int z)
        {
            Mat overlayImage = Cv2.ImRead(@"C:\Users\Kressol\Desktop\font1\SmokeGreen26x50.PNG");

            if (z == 1) 
            {
                overlayImage = Cv2.ImRead(@"C:\Users\Kressol\Desktop\font1\SmokeGray26x50.PNG");
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
        private Mat Armor(Mat frame, int Y, int remainingAmmo) 
        {
            Mat overlayImage = Cv2.ImRead(@"C:\Users\Kressol\Desktop\font1\PatronsGray80x60.PNG");
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
            Mat overlayImage = Cv2.ImRead(@"C:\Users\Kressol\Desktop\font1\CameraGray80x40.PNG");

            if (activeCamera == 1)
            {
                overlayImage = Cv2.ImRead(@"C:\Users\Kressol\Desktop\font1\CameraGreen80x40.PNG");
                
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
        private Mat coreanCameraA(Mat src, int Y, int activeCamera, double zoomLevel)
        {
            Mat overlayImage = Cv2.ImRead(@"C:\Users\Kressol\Desktop\font1\CameraGray80x40.PNG");

            if (activeCamera == 1) overlayImage = Cv2.ImRead(@"C:\Users\Kressol\Desktop\font1\CameraGreen80x40.PNG");

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
        private Mat xyCoordinate(Mat frame, int x, int y, int radius, Scalar outlineColor, Scalar fillColor, int thickness, int turretAngle)
        {
            int startAngle = turretAngle + 25; // Кут початку дуги (12 годинник)
            int endAngle = turretAngle - 25;

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
            OpenCvSharp.Point mooveP = new OpenCvSharp.Point((int)(x + radius * Math.Cos(turretAngle * Math.PI / 180)), (int)(y + radius * Math.Sin(turretAngle * Math.PI / 180)));

            Cv2.Line(frame, center, mooveP, new Scalar(0, 165, 255), thickness, LineTypes.AntiAlias);

            // Draw the small orange circle
            Cv2.Circle(frame, new OpenCvSharp.Point(x, y), 6, new Scalar(0, 165, 255), -1, LineTypes.AntiAlias);

            // Draw the small dark green circle
            Cv2.Circle(frame, new OpenCvSharp.Point(x, y), 3, new Scalar(0, 100, 0), -1, LineTypes.AntiAlias);

            zCoordinate(frame, new OpenCvSharp.Point(1730, 495), new OpenCvSharp.Point(1730, 325), 2, new Scalar(144, 238, 144), gunElevation);

            zoom(frame, zoomLevel);

            battery(frame, batteryLevel);

            return frame;
        }
        private void zCoordinate(Mat frame, OpenCvSharp.Point center, OpenCvSharp.Point point12, int thickness, Scalar color, int gunElevation)
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

            
            OpenCvSharp.Point point7Hours = new OpenCvSharp.Point((int)(center.X + radius * Math.Cos(gunElevation * Math.PI / 180)), (int)(center.Y + radius * Math.Sin(gunElevation * Math.PI / 180)));

            // Малювання оранжевої лінії
            Cv2.Line(frame, center, point7Hours, new Scalar(0, 165, 255), thickness, LineTypes.AntiAlias);

            // Малювання зеленого кола радіусом 3 пікселя
            Cv2.Circle(frame, center, 3, new Scalar(0, 100, 0), -1, LineTypes.AntiAlias);
        }
        private void zoom(Mat frame, double zoomLevel)
        {
            double x = 0;
            if (camera == 1)
            {
                x = (zoomLevel * 200) / 32;
            }
            else if (camera == 2)
            {
                x = (zoomLevel * 200) / 4;
            }
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
        private void battery(Mat frame, int batteryLevel)
        {
            int x = batteryLevel * 2;
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
                                             new OpenCvSharp.Point(1700+x , 777),
                                             new OpenCvSharp.Point(1700+x , 798),
                                             new OpenCvSharp.Point(1702 , 798) };
            Cv2.FillPoly(frame, new OpenCvSharp.Point[][] { rectanglePoints1 }, new Scalar(0, 165, 255));
        }
        private Mat PlaceProblemsInColumns(Mat frame)
        {
            Bitmap bitmap = MatToBitmap(frame);

            using (Graphics g = Graphics.FromImage(bitmap))
            {
                string text = "Не працює ч/б камера. Немає сигналу з далекоміру. Пошкоджено двигун повороту башти. Пошкоджено центральне живлення системи.";
                Font font = new Font("Arial", 19);
                //System.Drawing.Brush brush = new SolidBrush(System.Drawing.Color.Orange);
                //System.Drawing.Brush brush = new SolidBrush(System.Drawing.Color.Crimson);
                //System.Drawing.Brush brush = new SolidBrush(System.Drawing.Color.IndianRed);
                System.Drawing.Brush brush = new SolidBrush(System.Drawing.Color.LightCoral);
                //System.Drawing.Brush brush = new SolidBrush(System.Drawing.Color.Red);
                PointF point = new PointF(255, 1000);

                // Виміряти розмір тексту
                SizeF textSize = g.MeasureString(text, font);
                float maxWidth = 1420;
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
        private Mat PlaceTextInColumns(Mat frame, int camera, double zoomLevel, int distance, int batteryLevel, Dictionary<int, string> palletesForTeplak,int palette, double scopeX, double scopeY)
        {
            Bitmap bitmap = MatToBitmap(frame);

            using (Graphics g = Graphics.FromImage(bitmap))
            {
                using (Font font = new Font("Arial", 18))
                {
                    System.Drawing.Brush lightGreenBrush = new SolidBrush(System.Drawing.Color.LightGreen);
                    System.Drawing.Brush orangeBrush = new SolidBrush(System.Drawing.Color.Orange);

                    // Draw the static text in light green
                    g.DrawString("Камера: Ч/Б", font, lightGreenBrush, new PointF(30, 415));
                    g.DrawString("Ширококутна", font, lightGreenBrush, new PointF(25, 510));
                    g.DrawString("Тепловізор", font, lightGreenBrush, new PointF(30, 615));
                    g.DrawString("Кількість набоїв:", font, lightGreenBrush, new PointF(30, 140));
                    g.DrawString("Кут повороту башти", font, lightGreenBrush, new PointF(1690, 285));
                    g.DrawString("Кут підйому стволу", font, lightGreenBrush, new PointF(1690, 540));

                    // Draw the number of rounds in orange
                    g.DrawString("99999", font, orangeBrush, new PointF(90, 175));

                    // Draw the distance text in parts
                    g.DrawString("Дальність: ", font, lightGreenBrush, new PointF(30, 760));
                    SizeF distanceTextSize = g.MeasureString("Дальність: ", font);
                    g.DrawString(distance + "м", font, orangeBrush, new PointF(30 + distanceTextSize.Width, 760));

                    // Draw the semi-transparent black rectangle with distance value
                    if (addDistance == 1) 
                    {
                        string distanceText = distance + "м";
                        SizeF textSize = g.MeasureString(distanceText, font);
                        RectangleF textBackground = new RectangleF((float)scopeX + 30, (float)scopeY + 40, textSize.Width, textSize.Height);
                        System.Drawing.Brush backgroundBrush = new SolidBrush(System.Drawing.Color.FromArgb(128, 0, 0, 0)); // Semi-transparent black
                        g.FillRectangle(backgroundBrush, textBackground);
                        g.DrawString(distanceText, font, orangeBrush, new PointF((float)scopeX + 30, (float)scopeY + 40));
                    }
                    // Draw the palitra text in parts
                    g.DrawString("Палітра: ", font, lightGreenBrush, new PointF(30, 710));
                    SizeF palitraTextSize = g.MeasureString("Палітра: ", font);
                    var keys = new List<int>(palletesForTeplak.Keys);
                    string palitra = palletesForTeplak[palette];
                    g.DrawString(palitra, font, orangeBrush, new PointF(30 + palitraTextSize.Width, 710));
                }
                using (Font font = new Font("Arial", 20))
                {
                    System.Drawing.Brush lightGreenBrush = new SolidBrush(System.Drawing.Color.LightGreen);
                    System.Drawing.Brush orangeBrush = new SolidBrush(System.Drawing.Color.Orange);

                    // Draw the power text in parts
                    g.DrawString("Заряд: ", font, lightGreenBrush, new PointF(1715, 820));
                    SizeF powerTextSize = g.MeasureString("Заряд: ", font);
                    g.DrawString(batteryLevel + "%", font, orangeBrush, new PointF(1715 + powerTextSize.Width, 820));

                    // Draw the zoom text in parts
                    g.DrawString("Зум: ", font, lightGreenBrush, new PointF(1740, 690));
                    SizeF zoomTextSize = g.MeasureString("Зум: ", font);
                    g.DrawString("X" + zoomLevel, font, orangeBrush, new PointF(1740 + zoomTextSize.Width, 690));

                    if (camera == 1)
                    {
                        g.DrawString("Х" + zoomLevel, font, orangeBrush, new PointF(160, 465));
                    }
                    else if (camera == 2) 
                    {
                        g.DrawString("Х" + zoomLevel, font, orangeBrush, new PointF(160, 570));
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
        #endregion
        private void AddDevice(object sender, DeckLinkDiscoveryEventArgs e)
        {
            var deviceName = DeckLinkDeviceTools.GetDisplayLabel(e.deckLink);
            if (deviceName.Contains("DeckLink Duo (2)"))
            {
                m_inputDevice1 = new DeckLinkDevice(e.deckLink, m_profileCallback);
                InitializeInputDevice1();
            }
            else if (deviceName.Contains("DeckLink Duo (3)"))
            {
                m_inputDevice2 = new DeckLinkDevice(e.deckLink, m_profileCallback);
                InitializeInputDevice2();
            }
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
                m_inputDevice1.StartCapture(_BMDDisplayMode.bmdModeHD1080p30, m_captureCallback1, false); 
            }
        }

        private void InitializeInputDevice2()
        {
            if (m_inputDevice2 != null)
            {
                m_inputDevice2.StartCapture(_BMDDisplayMode.bmdModeHD1080p30, m_captureCallback2, false); 
            }
        }

        private void InitializeOutputDevice()
        {
            if (m_outputDevice != null)
            {
                m_outputDevice.PrepareForPlayback(_BMDDisplayMode.bmdModeHD1080p5994, m_playbackCallback);
            }

        }
        #region convertingFormatFrame
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
        #endregion
        private void OnFrameReceived1(IDeckLinkVideoInputFrame videoFrame)
        {
            Task.Run(() =>
            {
                if (frameSkipCount > 0)
                {
                    frameSkipCount--;
                    return;
                }
                IntPtr frameBytes;
                videoFrame.GetBytes(out frameBytes);
                int width = videoFrame.GetWidth();
                int height = videoFrame.GetHeight();

                using (Mat capturedFrame = new Mat(height, width, OpenCvSharp.MatType.CV_8UC2, frameBytes))
                {
                    Mat bgrImage = ConvertUYVYToBGR(capturedFrame);
                    Mat processedFrame = CropAndResizeFrame(bgrImage);
                    if (isActive)
                    {
                        lock (frameLock)
                        {
                            deckLinkFrame1 = processedFrame.Clone();
                        }
                        finalPartOfProcessingFrame();
                    }
                    else if (camera == 2)
                    {
                        Mat AddText = CenterFrame(processedFrame);
                        Mat uyvyFrame = ConvertBGRToUYVY(AddText);

                        if (m_outputDevice != null)
                        {
                            m_outputDevice.ScheduleFrame(uyvyFrame);
                        }
                        uyvyFrame.Dispose();
                    }
                }
                Marshal.ReleaseComObject(videoFrame);
            });
        }
        private void OnFrameReceived2(IDeckLinkVideoInputFrame videoFrame)
        {
            Task.Run(() =>
            {
                if (frameSkipCount > 0)
                {
                    frameSkipCount--;
                    return;
                }
                IntPtr frameBytes;
                videoFrame.GetBytes(out frameBytes);

                using (Mat capturedFrame = new Mat(1080, 1920, OpenCvSharp.MatType.CV_8UC2, frameBytes))
                {
                    Mat bgrImage = ConvertUYVYToBGR(capturedFrame);
                    Mat processedFrame = CropAndResizeFrame(bgrImage);

                    // Зберігаємо кадр, якщо потрібно
                    //lock (frameLock)
                    //{
                    //    deckLinkFrame2 = processedFrame.Clone(); // Якщо deckLinkFrame2 є спільною змінною
                    //}
                    if (isActive)
                    {
                        lock (frameLock)
                        {
                            deckLinkFrame2 = processedFrame.Clone();
                        }
                        finalPartOfProcessingFrame();
                    }
                    else if (camera == 1)
                    {
                        Mat AddText = CenterFrame(processedFrame);
                        Mat uyvyFrame = ConvertBGRToUYVY(AddText);

                        if (m_outputDevice != null)
                        {
                            m_outputDevice.ScheduleFrame(uyvyFrame);
                        }
                        uyvyFrame.Dispose();
                    }
                    else if (camera == 0)
                    {
                        Mat AddText = CenterFrame(green);
                        Mat uyvyFrame = ConvertBGRToUYVY(AddText);

                        if (m_outputDevice != null)
                        {
                            m_outputDevice.ScheduleFrame(uyvyFrame);
                        }
                        uyvyFrame.Dispose();
                    }
                }

                Marshal.ReleaseComObject(videoFrame);
        });
        }
        private Mat CropAndResizeFrame(Mat originalFrame)
        {
            OpenCvSharp.Rect cropRect = new OpenCvSharp.Rect(240, 0, 1440, 1080);
            Mat croppedFrame = new Mat(originalFrame, cropRect);
            return croppedFrame;
        }
        private void finalPartOfProcessingFrame()
        {
            lock (frameLock)
            {
                if (deckLinkFrame1 != null && deckLinkFrame2 != null && cameraFrame != null)
                {
                    using (Mat exfinalFrame = new Mat(1080, 1920, MatType.CV_8UC3, new Scalar(0, 0, 0)))
                    {
                   
                        switch (camera)
                        {
                            case 2:
                                deckLinkFrame1.CopyTo(exfinalFrame);
                                break;
                            case 1:
                                deckLinkFrame2.CopyTo(exfinalFrame);
                                break;
                            case 0:
                                  cameraFrame.CopyTo(exfinalFrame);
                                break;
                            default:
                                MessageBox.Show("Error copying frame");
                                break;
                        }

                        Mat AddText = CenterFrame(exfinalFrame);
                        Mat uyvyFrame = ConvertBGRToUYVY(AddText);

                        if (m_outputDevice != null)
                        {
                            m_outputDevice.ScheduleFrame(uyvyFrame);
                        }
                        uyvyFrame.Dispose();
                    }
                    DisposeUsedFrames();
                }
            }
        }

        private void DisposeUsedFrames()
        {
            deckLinkFrame1?.Dispose();
            deckLinkFrame2?.Dispose();
            cameraFrame?.Dispose();
            deckLinkFrame1 = null;
            deckLinkFrame2 = null;
            cameraFrame = null;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeVimba();

            //InitializeUART();

            m_deckLinkMainThread = new Thread(() => DeckLinkMainThread());
            m_deckLinkMainThread.SetApartmentState(ApartmentState.MTA);
            m_deckLinkMainThread.Start();

            await Task.Delay(5000);

            AutoRun(false);
        }
            

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            

            //gt1910*
            StopCamera();
            //gt1910*

            if (serialPort != null && serialPort.IsOpen)
            {
                serialPort.Close();
            }

            m_applicationCloseWaitHandle.Set();

            if (m_deckLinkMainThread != null && m_deckLinkMainThread.IsAlive)
            {
                m_deckLinkMainThread.Join();
            }

            DisposeDeckLinkResources();
        }
        
        private bool AutoRun(bool autorun)
        {
            const string name = "Test6"; 
            string exePath = @"C:\Users\Kressol\Desktop\JoeBaibai\Нова папка\Test6\Test6\bin\x64\Debug\net6.0-windows\Test6.exe"; 
            RegistryKey reg = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");

            try
            {
                if (autorun)
                {
                    reg.SetValue(name, exePath);
                }
                else
                {
                    reg.DeleteValue(name, false); // 'false' не викликає виняток, якщо ключ не знайдено
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting autorun: {ex.Message}");
                return false;
            }
            return true;
        }



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
