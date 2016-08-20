using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using Java.IO;
using OpenCV.Android;
using OpenCV.Core;
using OpenCV.ImgProc;
using OpenCV.ObjDetect;
using VocalEyes.Common.Enumeration;
using VocalEyes.Common.Utils;
using VocalEyes.Droid.Common.Helper;
using VocalEyes.Droid.Common.Model;
using VocalEyes.Droid.Engine;
using Point = OpenCV.Core.Point;
using Size = OpenCV.Core.Size;

namespace VocalEyes.Droid.Activities
{
    /// <summary>
    /// App name:       Vocal Eyes
    /// Description:    Mobile application for communication of ALS patients
    ///                 or other disabled individuals, based on the Becker Vocal 
    ///                 Eyes communication by Gary Becker (father of Jason Becker)
    /// Author:         Peter Brachwitz
    /// Last Update:    August 18 2016
    /// </summary>
    [Activity(Label = "Pupil tracking", ConfigurationChanges = ConfigChanges.Orientation,
        Icon = "@drawable/icon",
        ScreenOrientation = ScreenOrientation.Landscape)]
    public class CaptureActivity : Activity, CameraBridgeViewBase.ICvCameraViewListener2
    {
        public CaptureActivity()
        {
            Instance = this;
            Calibrating = true;
            _captureMethod = CaptureMethod.Subset;

            TextToSpeechHelper = new TextToSpeechHelper(this);
            var mDetectorName = new string[2];
            mDetectorName[JavaDetector] = "Java";
            mDetectorName[NativeDetector] = "Native (tracking)";
        }

        #region properties

            #region public

        public bool Running;

        public Direction Direction;

        public int FramesPerSecond;

        public bool Calibrating;

        public int Facing;

        public bool Speaking;
        public Rect AvgLeftEye;
        public Rect AvgRightEye;
        public List<Rect> RightRectCaptures = new List<Rect>();
        public List<Rect> LeftRectCaptures = new List<Rect>();

        public readonly CaptureActivity Instance;

        public TextToSpeechHelper TextToSpeechHelper;

        public List<string> CharacterBuffer = new List<string>();

        public Point PosLeft;
        public Point PosRight;

        public static readonly int JavaDetector = 0;
        public static readonly int NativeDetector = 1;

        public Mat MRgba;
        public Mat MGray;
        public File MCascadeFile { get; set; }
        public File MCascadeFileEye { get; set; }
        public CascadeClassifier MJavaDetector { get; set; }
        public CascadeClassifier MJavaDetectorEye { get; set; }
        public DetectionBasedTracker MNativeDetector { get; set; }
        public DetectionBasedTracker MNativeDetectorEye { get; set; }

        public TextView TextViewTimer;
        public Stopwatch Stopwatch = new Stopwatch();

        #endregion

            #region private

        private bool _readyToCapture;

        private CaptureMethod _captureMethod;
        private Subset _currentSubset;
        public List<Subset> SubSets;

        private bool _handling;

        private bool _fpsDetermined;

        private int _framecount;
        private DateTime? _start;

        private readonly int _mDetectorType = JavaDetector;

        private float _mRelativeFaceSize = 0.2f;
        private int _mAbsoluteFaceSize = 0;

        private CameraBridgeViewBase _mOpenCvCameraView;

        private Callback _mLoaderCallback;

      

        #endregion

        #endregion

        #region overrides

        private ProgressDialog _progress;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            RequestWindowFeature(WindowFeatures.NoTitle);
            base.OnCreate(savedInstanceState);
            Window.AddFlags(WindowManagerFlags.KeepScreenOn);
            Window.AddFlags(WindowManagerFlags.Fullscreen);

            SetContentView(Resource.Layout.face_detect_surface_view);

            _mOpenCvCameraView = FindViewById<CameraBridgeViewBase>(Resource.Id.fd_activity_surface_view);
            _mOpenCvCameraView.Visibility = ViewStates.Visible;

            Stopwatch.Start();
            TextViewTimer = FindViewById<TextView>(Resource.Id.tv1);

            Facing = Intent.GetIntExtra(typeof(CameraFacing).Name, CameraFacing.Front);
            _mOpenCvCameraView.SetCameraIndex(Facing);
            _mOpenCvCameraView.SetCvCameraViewListener2(this);

            _mLoaderCallback = new Callback(this, _mOpenCvCameraView);

            _progress = new ProgressDialog(this) {Indeterminate = true};
            _progress.SetCancelable(true);
            _progress.SetProgressStyle(ProgressDialogStyle.Spinner);
            _progress.SetMessage(SpeechHelper.InitMessage);
            _progress.Show();
            _progress.CancelEvent += (sender, args) => Finish();
            Task.Run(async() =>
            {
                try
                {
                    while (!Running)
                    {
                        await Task.Delay(1000);
                        RunOnUiThread(() => TextViewTimer.Text = Stopwatch.Elapsed.ToString(@"m\:ss"));
                    }

                    RunOnUiThread(() =>
                    {
                        TextViewTimer.Text = string.Empty;
                        _progress.Dismiss();
                        Stopwatch.Stop();
                        //TextToSpeechHelper.Speak(SpeechHelper.CalibrationInit);
                        FindViewById<LinearLayout>(Resource.Id.l1).Visibility = ViewStates.Gone;
                    });

                    await Task.Run(() =>
                    {
                        //try
                        //{
                        //    while (TextToSpeechHelper.IsSpeaking)
                        //    {
                        //    }
                        //}
                        //catch
                        //{
                        //}

                        RunOnUiThread(() =>
                        {
                            try
                            {
                                var builder = new AlertDialog.Builder(this);
                                builder.SetMessage(SpeechHelper.CalibrationInit);
                                builder.SetPositiveButton("OK", (a, s) =>
                                {
                                    _calibrationStart = DateTime.Now;
                                    _readyToCapture = true;
                                });
                                builder.Show();
                            }
                            catch(WindowManagerBadTokenException){}
                        });
                    });
                }
                catch (WindowManagerBadTokenException)
                {
                }
            });
        }

        private DateTime _calibrationStart;

        protected override void OnPause()
        {
            base.OnPause();
            if (_mOpenCvCameraView != null)
                _mOpenCvCameraView.DisableView();
        }

        protected override void OnResume()
        {
            base.OnResume();
            if (!OpenCVLoader.InitDebug()) //Internal OpenCV library not found. Using OpenCV Manager for initialization
                OpenCVLoader.InitAsync(OpenCVLoader.OpencvVersion300, this, _mLoaderCallback);
            else  //OpenCV library found inside package. Using it!
                _mLoaderCallback.OnManagerConnected(LoaderCallbackInterface.Success);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _mOpenCvCameraView.DisableView();
        }

        #endregion

        #region ICvCameraViewListener2 implementation

        public void OnCameraViewStarted(int width, int height)
        {
            MGray = new Mat();
            MRgba = new Mat();
        }

        public void OnCameraViewStopped()
        {
            MGray.Release();
            MRgba.Release();
        }

        private EyeArea _leftEyeArea;
        private EyeArea _rightEyeArea;

        public Mat OnCameraFrame(CameraBridgeViewBase.ICvCameraViewFrame inputFrame)
        {
            MRgba = inputFrame.Rgba();
            MGray = inputFrame.Gray();

            if (!Running)
                return MRgba;

            _framecount++;
            PosLeft = PosRight = null;

            var faces = new MatOfRect();

            DetermineFps();

            if (!_fpsDetermined || !_readyToCapture)
                return MRgba;

            if(_leftEyeArea == null)
                _leftEyeArea = new EyeArea(2);

            if(_rightEyeArea == null)
                _rightEyeArea = new EyeArea(2);

            var w = MRgba.Width();
            var h = MRgba.Height();

            if (SubSets == null)
                CreateGridSubSet(w, h);

            if (Calibrating)
            {
                var passed = (DateTime.Now - _calibrationStart).Seconds.ToString();
                this.PutOutlinedText(passed, 10, 10);
            }

            if (_mAbsoluteFaceSize == 0)
            {
                var height = MGray.Rows();
                if (Math.Round(height * _mRelativeFaceSize) > 0)
                    _mAbsoluteFaceSize = Java.Lang.Math.Round(height * _mRelativeFaceSize);

                MNativeDetector.SetMinFaceSize(_mAbsoluteFaceSize);
            }

            if (_mDetectorType == JavaDetector)
            {
                if (MJavaDetector != null)
                    MJavaDetector.DetectMultiScale(MGray, faces, 1.1, 2, 2,
                            new Size(_mAbsoluteFaceSize, _mAbsoluteFaceSize), new Size());
            }
            else if (_mDetectorType == NativeDetector && MNativeDetector != null)
                MNativeDetector.Detect(MGray, faces);

            var face = DetectionHelper.GetNearestFace(faces.ToArray());

            if (face != null)
            {
                var eyeareaLeft = _leftEyeArea.Insert(new Rect(face.X + face.Width / 16 + (face.Width - 2 * face.Width / 16) / 2,
                    (int)(face.Y + (face.Height / 4.5)), (face.Width - 2 * face.Width / 16) / 2, (int)(face.Height / 3.0))).GetShape();
                var eyeareaRight = _rightEyeArea.Insert(new Rect(face.X + face.Width / 16, (int)(face.Y + (face.Height / 4.5)),
                    (face.Width - 2 * face.Width / 16) / 2, (int)(face.Height / 3.0))).GetShape();

                Imgproc.Rectangle(MRgba, eyeareaLeft.Tl(), eyeareaLeft.Br(), new Scalar(255, 0, 0, 255), 2);
                Imgproc.Rectangle(MRgba, eyeareaRight.Tl(), eyeareaRight.Br(), new Scalar(255, 0, 0, 255), 2);

                this.DetectRightEye(MJavaDetectorEye, eyeareaRight, face, 24);
                this.DetectLeftEye(MJavaDetectorEye, eyeareaLeft, face, 24);
                var positionToDraw = PosLeft;

                if (positionToDraw == null)
                    return MRgba;

                if(DetectionHelper.CenterPoint != null)
                    PopulateGrid(GetDirection(positionToDraw));

                if (ShouldAct())
                    RunOnUiThread(() => HandleEyePosition(positionToDraw));
            }
            return MRgba;
        }
        #endregion

        #region custom methods

        /// <summary>
        /// Draw the full character-grid on screen
        /// </summary>
        private void PopulateGrid(Direction direction)
        {
            if (_captureMethod == CaptureMethod.Subset)
            {
                var rect = new Rect(0, 0, MRgba.Width() / 3, MRgba.Height() / 2);
                switch (direction)
                {
                    case Direction.TopLeft:
                        rect.X = 0;
                        break;
                    case Direction.BottomLeft:
                        rect.Y = MRgba.Height()/2;
                        break;
                    case Direction.TopRight:
                        rect.X = (MRgba.Width()/3)*2;
                        break;
                    case Direction.BottomRight:
                        rect.X = (MRgba.Width() / 3) * 2;
                        rect.Y = MRgba.Height() / 2;
                        break;
                    case Direction.TopCenter:
                        rect.X = MRgba.Width() / 3;
                        break;
                    case Direction.BottomCenter:
                        rect.X = MRgba.Width()/3;
                        rect.Y = MRgba.Height() / 2;
                        break;
                }
                Imgproc.Rectangle(MRgba, rect.Tl(), rect.Br(), new Scalar(255, 0, 0), 2);
            }
            
           
            //var scalar = new Scalar(0, 255, 0);
            ////absolute positioning in screen
            //foreach (var rect in SubSets)
            //{
            //    //if (rect.Coordinate.X > 0 && rect.Coordinate.Y > 0)
            //    //{
            //    //    var t = 0;
            //    //}
            //    //if (!rect.Coordinate.Contains((int) position.X, (int) position.Y))
            //    //    return;
            //    var width = rect.Coordinate.Width / 3;
            //    var height = rect.Coordinate.Height / 3;

            //    var color = new Scalar(255, 255, 255, 50);
            //    this.PutOutlinedText(rect.Characters[0].ToString(), rect.Coordinate.X + width / 2 - 25, 25 +
            //        rect.Coordinate.Y + rect.Coordinate.Height / 2, 5, color); //A E I M Q U

            //    this.PutOutlinedText(rect.Characters[2].ToString(), rect.Coordinate.X + rect.Coordinate.Width - width / 2 - 25, 25 +
            //       rect.Coordinate.Y + rect.Coordinate.Height / 2, 5, color); //C G K O S W

            //    this.PutOutlinedText(rect.Characters[1].ToString(), rect.Coordinate.X + rect.Coordinate.Width / 2 - 10,
            //      rect.Coordinate.Y + height / 2, 5, color); //B

            //    this.PutOutlinedText(rect.Characters[3].ToString(), rect.Coordinate.X + rect.Coordinate.Width / 2 - 10,
            //    rect.Coordinate.Y + rect.Coordinate.Height - height / 2, 5, color); //D H L P T X

            //    if (rect.Characters.Count == 6)
            //    {
            //        this.PutOutlinedText(rect.Characters[4].ToString(), rect.Coordinate.X + width / 2 - 25,
            //           rect.Coordinate.Y + rect.Coordinate.Height - 10, 5, color); //Y

            //        this.PutOutlinedText(rect.Characters[5].ToString(), rect.Coordinate.X + rect.Coordinate.Width - width / 2 - 25,
            //            rect.Coordinate.Y + rect.Coordinate.Height - 10, 5, color); //Z
            //    }

            //    //if (!rect.Coordinate.Contains((int)position.X, (int)position.Y))
            //    //    return;

            //    //Rect closestRect = null;
            //    //double? distance = null;
            //    //var closestLetter = string.Empty;
            //    //for (var i = 0; i < 3; i++)
            //    //{
            //    //    var rect1 = new Rect(new Point(rect.Coordinate.X + (width * i), rect.Coordinate.Y),
            //    //      new Point(rect.Coordinate.X + (width * (i + 1)), rect.Coordinate.Y + height));
            //    //    var rect2 = new Rect(new Point(rect.Coordinate.X + (width * i), rect.Coordinate.Y + height),
            //    //       new Point(rect.Coordinate.X + (width * (i + 1)), rect.Coordinate.Y + height + height));
            //    //    var rect3 = new Rect(new Point(rect.Coordinate.X + (width * i), rect.Coordinate.Y + height + height),
            //    //      new Point(rect.Coordinate.X + (width * (i + 1)), rect.Coordinate.Y + height + height + height));

            //    //    //var c1 = color;
            //    //    //var c2 = color;
            //    //    //var c3 = color;

            //    //    if (position != null)
            //    //    {
            //    //        var distance1 = GetDistance(position, new Point(rect1.X + rect1.Width / 2, rect1.Y + rect1.Height / 2));
            //    //        var distance2 = GetDistance(position, new Point(rect2.X + rect2.Width / 2, rect2.Y + rect2.Height / 2));
            //    //        var distance3 = GetDistance(position, new Point(rect3.X + rect3.Width / 2, rect3.Y + rect3.Height / 2));
            //    //        var d = new Dictionary<Rect, double>
            //    //        {
            //    //            {rect1, distance1},
            //    //            {rect2, distance2},
            //    //            {rect3, distance3}
            //    //        };
            //    //        var smallestDistance = d.Min(v => v.Value);
            //    //        var c = d.Single(v => v.Value == smallestDistance).Key;
            //    //        ;
            //    //        if (!distance.HasValue || smallestDistance < distance)
            //    //        {
            //    //            distance = smallestDistance;
            //    //            closestRect = c;
            //    //            //closestLetter = rect.Partition.ToString().Single(p => p.ToString()
            //    //            //    .ToUpper() == "A").ToString();
            //    //        }
            //    //    }

            //    //    //Imgproc.Rectangle(MRgba, rect1.Tl(), rect1.Br(), c1, 2); //row 1
            //    //    //Imgproc.Rectangle(MRgba, rect2.Tl(), rect2.Br(), c2, 2); //row 2
            //    //    //Imgproc.Rectangle(MRgba, rect3.Tl(), rect3.Br(), c3, 2); //row 3
            //    //}

            //    //Imgproc.Rectangle(MRgba, new Point(rect.Coordinate.X, rect.Coordinate.Y),
            //    //   new Point((rect.Coordinate.X + rect.Coordinate.Width),
            //    //       (rect.Coordinate.Y + rect.Coordinate.Height)), scalar, 5);

            //    //if (closestRect != null)
            //    //    Imgproc.Rectangle(MRgba, closestRect.Tl(), closestRect.Br(), new Scalar(255, 0, 0), 2);
            //}
        }

        private double GetDistance(Point a, Point b)
        {
            var distanceX = a.X - b.X;
            if (distanceX < 0)
                distanceX = distanceX*-1;

            var distanceY = a.Y - b.Y;
            if (distanceY < 0)
                distanceY = distanceY*-1;

            return Math.Sqrt(Math.Pow(distanceX, 2) + Math.Pow(distanceY, 2));
        }

        /// <summary>
        /// Create the grid of character-areas, so that the position of the pupil can be mapped to 
        /// a subset of characters. Subsequently, the next measured position can be mapped to a single 
        /// letter in the subset or the end of a word
        /// </summary>
        private void CreateGridSubSet(int width, int height)
        {
            var ySize = height/2;
            var xSize = width/3;
            SubSets = new List<Subset>
            {
                new Subset
                {
                    Direction = Direction.TopLeft, Partition = SubsetPartition.Abcd, Coordinate = new Rectangle(0, 0, xSize, ySize), Characters = SubsetPartition.Abcd.ToString().ToCharArray().ToList()
                },
                new Subset
                {
                    Direction = Direction.TopCenter, Partition = SubsetPartition.Efgh, Coordinate = new Rectangle(xSize, 0, xSize, ySize), Characters = SubsetPartition.Efgh.ToString().ToCharArray().ToList()
                },
                new Subset
                {
                    Direction = Direction.TopRight, Partition = SubsetPartition.Ijkl, Coordinate = new Rectangle(xSize*2, 0, xSize, ySize), Characters = SubsetPartition.Ijkl.ToString().ToCharArray().ToList()
                },
                new Subset
                {
                    Direction = Direction.BottomLeft, Partition = SubsetPartition.Mnop, Coordinate = new Rectangle(0, ySize, xSize, ySize), Characters = SubsetPartition.Mnop.ToString().ToCharArray().ToList()
                },
                new Subset
                {
                    Direction = Direction.BottomCenter, Partition = SubsetPartition.Qrst, Coordinate = new Rectangle(xSize, ySize, xSize, ySize), Characters = SubsetPartition.Qrst.ToString().ToCharArray().ToList()
                },
                new Subset
                {
                    Direction = Direction.BottomRight, Partition = SubsetPartition.Uvwxyz, Coordinate = new Rectangle(xSize*2, ySize, xSize, ySize), Characters = SubsetPartition.Uvwxyz.ToString().ToCharArray().ToList()
                }
            };
        }

        private Direction GetDirection(Point point)
        {
            var marginX = 10;
            var marginY = 10;
            _handling = true;

            var direction = Direction.Center;

            var centerpoint = DetectionHelper.CenterPoint;
            var diffX = point.X - centerpoint.X;
            var diffY = point.Y - centerpoint.Y;

            var diffXInPixels = diffX < 0 ? diffX * -1 : diffX;
            var diffYInPixels = diffY < 0 ? diffY * -1 : diffY;

            //top-center or bottom-center
            if (diffXInPixels <= marginX)
            {
                if (diffY < 0 && diffYInPixels > marginY)
                    direction = Direction.TopCenter;
                if (diffY > 0 && diffYInPixels > marginY)
                    direction = Direction.BottomCenter;
            }

            if (Facing == CameraFacing.Front)
            {
                //right, bottom-right or top-right
                if (diffX < 0 && diffXInPixels > marginX)
                {
                    if (diffY > 0 && diffYInPixels > marginY)
                        direction = Direction.BottomRight;
                    else if (diffY < 0 && diffYInPixels > marginY)
                        direction = Direction.TopRight;
                    else
                        direction = Direction.Right;
                }

                //left, bottom-left or top-left
                if (diffX > 0 && diffXInPixels > marginX)
                {
                    if (diffY > 0 && diffYInPixels > marginY)
                        direction = Direction.BottomLeft;
                    else if (diffY < 0 && diffYInPixels > marginY)
                        direction = Direction.TopLeft;
                    else
                        direction = Direction.Left;
                }
            }
            else
            {
                //right, bottom-right or top-right
                if (diffX > 0 && diffXInPixels > marginX)
                {
                    if (diffY > 0 && diffYInPixels > marginY)
                        direction = Direction.BottomRight;
                    else if (diffY < 0 && diffYInPixels > marginY)
                        direction = Direction.TopRight;
                    else
                        direction = Direction.Right;
                }

                //left, bottom-left or top-left
                if (diffX < 0 && diffXInPixels > marginX)
                {
                    if (diffY > 0 && diffYInPixels > marginY)
                        direction = Direction.BottomLeft;
                    else if (diffY < 0 && diffYInPixels > marginY)
                        direction = Direction.TopLeft;
                    else
                        direction = Direction.Left;
                }
            }
            return direction;
        }

        /// <summary>
        /// Handle calculated position of the pupil compared to the surface of the entire eye
        /// </summary>
        /// <param name="position"></param>
        public void HandleEyePosition(Point position)
        {
            try
            {
                if (TextToSpeechHelper.IsSpeaking)
                    return;

                if (position == null || _handling || TextToSpeechHelper.IsSpeaking)
                    return;

                var direction = GetDirection(position);

                TextToSpeechHelper.Speak(direction.ToString());

                //if (_captureMethod == CaptureMethod.Subset)
                //{
                //    //Determine distance between each subset and the iris-position
                //    foreach (var s in SubSets)
                //    {
                //        var center = new Point(s.Coordinate.X + s.Coordinate.Width/2,
                //            s.Coordinate.Y + s.Coordinate.Height/2);

                //        var distanceX = position.X - center.X;
                //        if (distanceX < 0)
                //            distanceX = distanceX * -1;

                //        var distanceY = position.Y - center.Y;
                //        if (distanceY < 0)
                //            distanceY = distanceY * -1;

                //        s.DistanceToPoint = Math.Sqrt(Math.Pow(distanceX, 2) + Math.Pow(distanceY, 2));
                //    }

                //    //the current subset is the one with the smallest distance to the iris-position
                //    _currentSubset = SubSets.Single(s => s.DistanceToPoint == SubSets.Min(s2 => s2.DistanceToPoint));

                //    if (_currentSubset != null)
                //    {
                //        TextToSpeechHelper.Speak(_currentSubset.Partition.ToString().First().ToString());
                //        _handling = false;
                //    }
                //}
                //else if (_captureMethod == CaptureMethod.Character)
                //{
                //    _captureMethod = CaptureMethod.Subset;

                //    switch (_currentSubset.Partition)
                //    {
                //        case SubsetPartition.Abcd:
                //            break;
                //        case SubsetPartition.Efgh:
                //            break;
                //        case SubsetPartition.Ijkl:
                //            break;
                //        case SubsetPartition.Mnop:
                //            break;
                //        case SubsetPartition.Qrst:
                //            break;
                //        case SubsetPartition.Uvwxyz:
                //            break;
                //    }

                //    CharacterBuffer.Add("P");
                //    TextToSpeechHelper.Speak(CharacterBuffer.Last());
                //}
                //else if (_captureMethod == CaptureMethod.Word)
                //{
                //    TextToSpeechHelper.Speak(string.Join("", CharacterBuffer));
                //    CharacterBuffer.Clear();
                //}
            }
            catch (Exception ex)
            {
            }
            finally
            {
                _handling = false;
            }
        }

        /// <summary>
        /// Determines the amount of processed frames per second
        /// </summary>
        private void DetermineFps()
        {
            if (_fpsDetermined)
                return;

            var now = DateTime.Now;
            if (!_start.HasValue)
                _start = now;
            else if (now >= _start.Value.AddSeconds(1))
            {
                FramesPerSecond = _framecount;
                _fpsDetermined = true;
            }
        }

        /// <summary>
        /// Should action be taken with the processed frame
        /// </summary>
        /// <returns>bool</returns>
        private bool ShouldAct()
        {
            if (Calibrating)
                return false;

            if (_fpsDetermined && _framecount >= FramesPerSecond && _readyToCapture)
            {
                _framecount = 0;
                return true;
            }
            return false;
        }

        #endregion
    }
}