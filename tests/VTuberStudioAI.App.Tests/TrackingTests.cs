using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VTuberStudioAI.App.Images;
using VTuberStudioAI.App.Preview;
using VTuberStudioAI.App.Projects;
using VTuberStudioAI.App.Tracking;
using VTuberStudioAI.App.ViewModels;
using Xunit;

namespace VTuberStudioAI.App.Tests;

public sealed class TrackingTests
{
    private static float[] Face()
    {
        var p = new float[478 * 3];
        void Set(int index, float x, float y, float z = 0)
        {
            p[index * 3] = x;
            p[index * 3 + 1] = y;
            p[index * 3 + 2] = z;
        }

        Set(234, -50, 0);
        Set(454, 50, 0);
        Set(10, 0, -60);
        Set(152, 0, 60);
        Set(13, 0, 5);
        Set(14, 0, 8);
        // Olho esquerdo da imagem aberto (razão 0,3) e direito quase fechado (0,1).
        Set(33, -30, -10);
        Set(133, -10, -10);
        foreach (var (upper, lower) in new[] { (159, 145), (160, 144), (158, 153) })
        {
            Set(upper, -20, -13);
            Set(lower, -20, -7);
        }

        Set(362, 10, -10);
        Set(263, 30, -10);
        foreach (var (upper, lower) in new[] { (386, 374), (385, 380), (387, 373) })
        {
            Set(upper, 20, -11);
            Set(lower, 20, -9);
        }

        Set(468, -20, -10);
        Set(473, 20, -10);
        return p;
    }

    private static float[] WithIris(float[] points, float dx, float dy)
    {
        var result = (float[])points.Clone();
        foreach (var index in new[] { 468, 473 })
        {
            result[index * 3] += dx;
            result[index * 3 + 1] += dy;
        }

        return result;
    }

    [Fact]
    public void IrisOffsetIsMeasuredInsideTheEyeAndIgnoresHeadRotation()
    {
        var centered = FaceLandmarkMath.Measure(Face())!;
        var right = FaceLandmarkMath.Measure(WithIris(Face(), 3, 2))!;
        var rotated = FaceLandmarkMath.Measure(Rotate(WithIris(Face(), 3, 2), 12, "roll"))!;

        Assert.True(centered.HasIris);
        Assert.Equal(0, centered.IrisX, 3);
        Assert.Equal(0, centered.IrisY, 3);
        Assert.Equal(0.15, right.IrisX, 3);
        Assert.Equal(0.1, right.IrisY, 3);
        Assert.Equal(right.IrisX, rotated.IrisX, 2);
        Assert.Equal(right.IrisY, rotated.IrisY, 2);
        Assert.False(FaceLandmarkMath.Measure(Face().Take(468 * 3).ToArray())!.HasIris);
    }

    [Fact]
    public void IrisGazeFollowsMirrorGainAndCalibration()
    {
        var m = new FaceMeasurements(0, 0, 0, 0.02, 0.28, 0.28, 0.05, 0.035, true);
        var mirrored = FaceMotionMapper.Map(m, FaceTrackingSettings.Default with { Mirror = true });
        var anatomical = FaceMotionMapper.Map(m, FaceTrackingSettings.Default with { Mirror = false });
        var off = FaceMotionMapper.Map(m, FaceTrackingSettings.Default with { IrisGain = 0 });
        var calibrated = FaceMotionMapper.Map(m, FaceTrackingSettings.Default with { IrisXOffset = 0.05, IrisYOffset = 0.035 });
        var noIris = FaceMotionMapper.Map(m with { HasIris = false }, FaceTrackingSettings.Default);

        Assert.Equal(-0.5, mirrored.IrisX, 3);
        Assert.Equal(0.5, anatomical.IrisX, 3);
        Assert.Equal(0.5, anatomical.IrisY, 3);
        Assert.Equal(0, off.IrisX, 3);
        Assert.Equal(0, calibrated.IrisX, 3);
        Assert.Equal(0, calibrated.IrisY, 3);
        Assert.Equal(0, noIris.IrisX, 3);
    }

    [Fact]
    public void IrisMovesThePupilIndependentlyFromTheHead()
    {
        var (centered, looking) = RunOnSta(() =>
        {
            var layers = new List<PsdLayerInfo>
            {
                new("head", 0, 0, 100, 40, true, "h"),
                new("eyewhite", 20, 10, 40, 20, true, "w"),
                new("pupil", 36, 14, 8, 12, true, "p")
            };
            var plan = LayerRigger.Plan(layers);
            var scene = new LayerScene(100, 40,
            [
                new SceneLayer(layers[0], plan[0], null),
                new SceneLayer(layers[1], plan[1], Solid(40, 20, 255, 255, 255)),
                new SceneLayer(layers[2], plan[2], Solid(8, 12, 0, 0, 200))
            ]);
            var still = Render(scene, new AvatarPose(0, 0, 0, 0, 0, 0), 100, 40);
            var moved = Render(scene, new AvatarPose(0, 0, 0, 0, 0, 0, 1, 0), 100, 40);
            return (Pixel(still, 166, 80), Pixel(moved, 166, 80));
        });

        Assert.Equal(0, centered.R);
        Assert.Equal(200, centered.B);
        Assert.Equal(255, looking.R);
    }

    private static float[] Rotate(float[] points, double degrees, string axis)
    {
        var radians = degrees * Math.PI / 180;
        float cos = (float)Math.Cos(radians), sin = (float)Math.Sin(radians);
        var result = (float[])points.Clone();
        for (var i = 0; i < points.Length; i += 3)
        {
            float x = points[i], y = points[i + 1];
            switch (axis)
            {
                case "yaw":
                    result[i] = x * cos;
                    result[i + 2] = x * sin;
                    break;
                case "pitch":
                    result[i + 1] = y * cos;
                    result[i + 2] = y * sin;
                    break;
                default:
                    result[i] = x * cos - y * sin;
                    result[i + 1] = x * sin + y * cos;
                    break;
            }
        }

        return result;
    }

    [Fact]
    public void FrontalFaceMeasuresNeutralPoseMouthAndEyes()
    {
        var m = FaceLandmarkMath.Measure(Face())!;

        Assert.Equal(0, m.YawDegrees, 3);
        Assert.Equal(0, m.PitchDegrees, 3);
        Assert.Equal(0, m.RollDegrees, 3);
        Assert.Equal(0.025, m.MouthRatio, 3);
        Assert.Equal(0.3, m.EyeImageLeftRatio, 3);
        Assert.Equal(0.1, m.EyeImageRightRatio, 3);
    }

    [Theory]
    [InlineData("yaw", 20)]
    [InlineData("pitch", -15)]
    [InlineData("roll", 12)]
    public void RotatedFaceRecoversTheAngle(string axis, double degrees)
    {
        var m = FaceLandmarkMath.Measure(Rotate(Face(), degrees, axis))!;
        var measured = axis switch { "yaw" => m.YawDegrees, "pitch" => m.PitchDegrees, _ => m.RollDegrees };

        Assert.Equal(degrees, measured, 2);
    }

    [Fact]
    public void IncompleteOrDegenerateLandmarksAreRejected()
    {
        Assert.Null(FaceLandmarkMath.Measure(new float[30]));
        Assert.Null(FaceLandmarkMath.Measure(new float[478 * 3]));
        var broken = Face();
        broken[234 * 3] = float.NaN;
        Assert.Null(FaceLandmarkMath.Measure(broken));
    }

    [Fact]
    public void MirrorSwapsTheEyeSidesAndYaw()
    {
        var measurements = new FaceMeasurements(15, 0, 0, 0.02, 0.05, 0.28);
        var mirrored = FaceMotionMapper.Map(measurements, FaceTrackingSettings.Default with { Mirror = true });
        var anatomical = FaceMotionMapper.Map(measurements, FaceTrackingSettings.Default with { Mirror = false });

        Assert.Equal(0, mirrored.ScreenLeftEyeClosed, 3);
        Assert.Equal(1, mirrored.ScreenRightEyeClosed, 3);
        Assert.Equal(1, anatomical.ScreenLeftEyeClosed, 3);
        Assert.Equal(0, anatomical.ScreenRightEyeClosed, 3);
        Assert.Equal(-0.5, mirrored.GazeX, 3);
        Assert.Equal(0.5, anatomical.GazeX, 3);
    }

    [Fact]
    public void MouthOpensRelativeToCalibratedClosedMouthAndStaysInRange()
    {
        var settings = FaceTrackingSettings.Default with { MouthClosed = 0.02 };

        Assert.Equal(0, FaceMotionMapper.Map(new(0, 0, 0, 0.02, 0.28, 0.28), settings).MouthOpen, 3);
        Assert.Equal(0.5, FaceMotionMapper.Map(new(0, 0, 0, 0.095, 0.28, 0.28), settings).MouthOpen, 3);
        Assert.Equal(1, FaceMotionMapper.Map(new(0, 0, 0, 0.9, 0.28, 0.28), settings).MouthOpen, 3);
        Assert.Equal(-1, FaceMotionMapper.Map(new(500, 500, 500, 0.02, 0.28, 0.28), settings).GazeX, 3);
    }

    [Fact]
    public void SmootherEasesTowardTheTargetAndResets()
    {
        var smoother = new PoseSmoother();
        var first = smoother.Apply(AvatarPose.Neutral, 0.5);
        var next = smoother.Apply(new AvatarPose(1, 0, 0, 0, 0, 0), 0.5);

        Assert.Equal(0, first.GazeX);
        Assert.InRange(next.GazeX, 0.01, 0.99);
        smoother.Reset();
        Assert.Equal(1, smoother.Apply(new AvatarPose(1, 0, 0, 0, 0, 0), 0.5).GazeX);
    }

    [Fact]
    public void CalibrationUsesTheMedianAndRejectsClosedEyesOrFewSamples()
    {
        var neutral = Enumerable.Repeat(new FaceMeasurements(4, -3, 2, 0.03, 0.30, 0.26), 20).ToList();
        var result = FaceCalibrationBuilder.Build(neutral, FaceTrackingSettings.Default);
        Assert.NotNull(result.Settings);
        Assert.Equal(4, result.Settings!.YawOffset, 3);
        Assert.Equal(0.26, result.Settings.EyeOpenImageRight, 3);
        Assert.Equal(0.03, result.Settings.MouthClosed, 3);

        Assert.NotNull(FaceCalibrationBuilder.Build(neutral.Take(5).ToList(), FaceTrackingSettings.Default).Error);
        var closed = Enumerable.Repeat(new FaceMeasurements(0, 0, 0, 0.02, 0.05, 0.05), 20).ToList();
        Assert.NotNull(FaceCalibrationBuilder.Build(closed, FaceTrackingSettings.Default).Error);
    }

    [Fact]
    public void SettingsPersistAcrossReopenAndInvalidValuesAreRejected()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new ProjectRepository(directory);
            var project = repository.Create("Tracking");
            var settings = FaceTrackingSettings.Default with { Mirror = false, HeadGain = 1.7, YawOffset = 5 };

            TrackingSettingsStore.Save(repository, project.Id, settings);
            Assert.Equal(settings, TrackingSettingsStore.Read(new ProjectRepository(directory).Load(project.Id)));

            Assert.Throws<ArgumentException>(() =>
                TrackingSettingsStore.Save(repository, project.Id, FaceTrackingSettings.Default with { HeadGain = 50 }));
            var corrupted = project with
            {
                Configurations = new Dictionary<string, JsonElement>
                {
                    [FaceTrackingSettings.ConfigurationKey] = JsonDocument.Parse("{\"HeadGain\":\"x\"}").RootElement.Clone()
                }
            };
            Assert.Equal(FaceTrackingSettings.Default, TrackingSettingsStore.Read(corrupted));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void ModelsLoadAndAnEmptyFrameFindsNoFace()
    {
        Assert.Null(MediaPipeFaceTracker.CheckModels());
        using var tracker = new MediaPipeFaceTracker();
        var frame = new BgraFrame(new byte[320 * 240 * 4], 320, 240);

        Assert.Null(tracker.Track(frame));
        Assert.Null(tracker.Track(frame));
    }

    [Fact]
    public void TrackedClosedEyeFollowsTheScreenSideNotTheLayerSuffix()
    {
        var (closedSide, openSide) = RunOnSta(() =>
        {
            // "eyewhite.L" está no lado direito da tela e "eyewhite.R" no esquerdo: a geometria decide.
            var layers = new List<PsdLayerInfo>
            {
                new("head", 0, 0, 100, 40, true, "h"),
                new("eyewhite.L", 65, 15, 20, 10, true, "a"),
                new("eyewhite.R", 15, 15, 20, 10, true, "b")
            };
            var plan = LayerRigger.Plan(layers);
            var scene = new LayerScene(100, 40,
            [
                new SceneLayer(layers[0], plan[0], null),
                new SceneLayer(layers[1], plan[1], Solid(20, 10, 255, 255, 255)),
                new SceneLayer(layers[2], plan[2], Solid(20, 10, 255, 255, 255))
            ]);
            var target = Render(scene, new AvatarPose(0, 0, 0, 0, 1, 0), 100, 40);
            return (Pixel(target, 115, 68).A, Pixel(target, 285, 68).A);
        });

        Assert.Equal(0, closedSide);
        Assert.Equal(255, openSide);
    }

    [Fact]
    public void HiddenOpenMouthLayerAppearsOnlyWhenTheMouthOpens()
    {
        var (closed, open) = RunOnSta(() =>
        {
            var layers = new List<PsdLayerInfo>
            {
                new("head", 0, 0, 100, 40, true, "h"),
                new("mouthopen", 40, 15, 20, 10, false, "m")
            };
            var plan = LayerRigger.Plan(layers);
            Assert.Equal(LayerMotion.MouthOpen, plan[1].Motion);
            var scene = new LayerScene(100, 40,
            [
                new SceneLayer(layers[0], plan[0], null),
                new SceneLayer(layers[1], plan[1], Solid(20, 10, 200, 0, 0))
            ]);
            var shut = Render(scene, new AvatarPose(0, 0, 0, 0, 0, 0), 100, 40);
            var wide = Render(scene, new AvatarPose(0, 0, 0, 1, 0, 0), 100, 40);
            return (Pixel(shut, 200, 80).A, Pixel(wide, 200, 80).A);
        });

        Assert.Equal(0, closed);
        Assert.Equal(255, open);
    }

    [Fact]
    public void ClosedMouthDeformsTheArtAsInvertedVForFurryAndCurveForHumans()
    {
        var r = RunOnSta(() =>
        {
            LayerScene Build(bool furry)
            {
                var layers = new List<PsdLayerInfo> { new("head", 0, 0, 100, 40, true, "h") };
                if (furry)
                {
                    layers.Add(new PsdLayerInfo("F.snout", 20, 5, 60, 10, true, "s"));
                }

                layers.Add(new PsdLayerInfo("mouth", 40, 15, 20, 10, true, "m"));
                var plan = LayerRigger.Plan(layers);
                return new LayerScene(100, 40, layers.Select((info, i) =>
                    new SceneLayer(info, plan[i], info.Name == "head" ? null : Solid(info.Width, info.Height, 200, 0, 0))).ToList());
            }

            var furryShut = Render(Build(true), new AvatarPose(0, 0, 0, 0, 0, 0), 100, 40);
            var furryWide = Render(Build(true), new AvatarPose(0, 0, 0, 1, 0, 0), 100, 40);
            var humanShut = Render(Build(false), new AvatarPose(0, 0, 0, 0, 0, 0), 100, 40);
            var humanWide = Render(Build(false), new AvatarPose(0, 0, 0, 1, 0, 0), 100, 40);
            var off = new InteractivePreview
            {
                Scene = Build(false), Source = Solid(100, 40, 1, 1, 1), Rig = RigSettings.Default with { CloseMouth = false },
                Tracking = AvatarPose.Neutral, Width = 400, Height = 160
            };
            off.Measure(new Size(400, 160));
            off.Arrange(new Rect(0, 0, 400, 160));
            var offTarget = new RenderTargetBitmap(400, 160, 96, 96, PixelFormats.Pbgra32);
            offTarget.Render(off);

            // A boca ocupa x 166..234 e y 63..97 na tela de teste.
            return new[]
            {
                Pixel(furryShut, 200, 66).A, Pixel(furryShut, 168, 66).A, Pixel(furryShut, 168, 78).A, Pixel(furryWide, 168, 66).A,
                Pixel(humanShut, 200, 90).A, Pixel(humanShut, 200, 70).A, Pixel(humanShut, 168, 80).A, Pixel(humanShut, 168, 90).A,
                Pixel(humanWide, 200, 70).A, Pixel(offTarget, 200, 70).A
            };
        });

        Assert.Equal(255, r[0]);
        Assert.Equal(0, r[1]);
        Assert.Equal(255, r[2]);
        Assert.Equal(255, r[3]);
        Assert.Equal(255, r[4]);
        Assert.Equal(0, r[5]);
        Assert.Equal(255, r[6]);
        Assert.Equal(0, r[7]);
        Assert.Equal(255, r[8]);
        Assert.Equal(255, r[9]);
    }

    [Fact]
    public void HeadStaysAttachedToTheNeckAndBodyFollowsTheGaze()
    {
        var (pivot, tilted, neutralEdge, shiftedEdge) = RunOnSta(() =>
        {
            var layers = new List<PsdLayerInfo>
            {
                new("body", 30, 60, 40, 40, true, "b"),
                new("head", 30, 10, 40, 55, true, "h")
            };
            var plan = LayerRigger.Plan(layers);
            var scene = new LayerScene(100, 100,
            [
                new SceneLayer(layers[0], plan[0], Solid(40, 40, 0, 0, 200)),
                new SceneLayer(layers[1], plan[1], Solid(40, 55, 200, 0, 0))
            ]);
            var (nx, ny) = scene.NeckPivot;
            var rolled = Render(scene, new AvatarPose(0, 0, 25, 0, 0, 0), 100, 100);
            var neutral = Render(scene, new AvatarPose(0, 0, 0, 0, 0, 0), 100, 100);
            var shifted = Render(scene, new AvatarPose(1, 0, 0, 0, 0, 0), 100, 100);
            var scale = 1.36;
            var px = (int)(132 + nx * scale);
            var py = (int)(12 + ny * scale);
            return ((nx, ny), Pixel(rolled, px, py).A, Pixel(neutral, 174, 140).A, Pixel(shifted, 174, 140).A);
        });

        Assert.Equal(50, pivot.Item1, 3);
        Assert.Equal(60.6, pivot.Item2, 1);
        Assert.Equal(255, tilted);
        Assert.Equal(255, neutralEdge);
        Assert.True(shiftedEdge < 255, "o corpo desliza junto com o olhar");
    }

    [Fact]
    public void OldRigSettingsWithoutBodyOrCloseMouthKeepTheDefaults()
    {
        var settings = JsonSerializer.Deserialize<RigSettings>(
            "{\"enabled\":true,\"overall\":1,\"face\":1,\"ears\":1,\"hair\":1,\"head\":1,\"eyeLook\":1}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(settings);
        Assert.Equal(1, settings.Body);
        Assert.True(settings.CloseMouth);
        Assert.True(settings.IsValid);
        Assert.False((settings with { Body = double.NaN }).IsValid);
    }

    [Fact]
    public void MotionLabFollowsTheCameraAndReleasesItOnStop()
    {
        RunOnSta(() =>
        {
            var camera = new FakeCamera();
            var lab = new MotionLabViewModel(
                new ProjectRepository(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
                camera,
                _ => new FakeTracker(new FaceMeasurements(0, 0, 0, 0.02, 0.05, 0.28)),
                action => action(),
                (_, _) => { });

            lab.RefreshCamerasAsync().GetAwaiter().GetResult();
            Assert.NotNull(lab.SelectedCamera);
            lab.StartAsync().GetAwaiter().GetResult();
            Assert.True(lab.IsRunning);
            Assert.Equal(lab.SelectedCamera!.Id, camera.StartedWith);

            for (var i = 0; i < 3; i++)
            {
                camera.Raise(new BgraFrame(new byte[4 * 4 * 4], 4, 4));
            }

            Assert.True(lab.FaceDetected);
            Assert.NotNull(lab.CameraImage);
            Assert.True(lab.Pose!.ScreenRightEyeClosed > 0.9);
            Assert.True(lab.Pose.ScreenLeftEyeClosed < 0.1);

            lab.StopAsync().GetAwaiter().GetResult();
            Assert.False(lab.IsRunning);
            Assert.True(camera.Stopped);
            Assert.Null(lab.Pose);
        });
    }

    [Fact]
    public void MotionLabReportsCameraFailureWithoutPretendingToRun()
    {
        RunOnSta(() =>
        {
            var camera = new FakeCamera { FailWith = new CameraException("O Windows bloqueou a câmera.") };
            var lab = new MotionLabViewModel(
                new ProjectRepository(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
                camera,
                _ => new FakeTracker(null),
                action => action(),
                (_, _) => { });

            lab.RefreshCamerasAsync().GetAwaiter().GetResult();
            lab.StartAsync().GetAwaiter().GetResult();

            Assert.False(lab.IsRunning);
            Assert.Contains("bloqueou", lab.ErrorMessage);
        });
    }

    private sealed class FakeTracker(FaceMeasurements? result) : IFaceTracker
    {
        public FaceMeasurements? Track(BgraFrame frame) => result;

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeCamera : ICameraSource
    {
        public event Action<BgraFrame>? FrameArrived;

        public string? StartedWith { get; private set; }

        public bool Stopped { get; private set; }

        public CameraException? FailWith { get; init; }

        public Task<IReadOnlyList<CameraDevice>> ListAsync() =>
            Task.FromResult<IReadOnlyList<CameraDevice>>([new CameraDevice("cam-1", "Câmera de teste")]);

        public Task StartAsync(string deviceId)
        {
            if (FailWith is not null)
            {
                throw FailWith;
            }

            StartedWith = deviceId;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Stopped = true;
            return Task.CompletedTask;
        }

        public void Raise(BgraFrame frame) => FrameArrived?.Invoke(frame);

        public void Dispose()
        {
        }
    }

    private static RenderTargetBitmap Render(LayerScene scene, AvatarPose pose, int width, int height)
    {
        var preview = new InteractivePreview
        {
            Source = Solid(width, height, 10, 10, 10),
            Scene = scene,
            Eyes = scene.EyeRegions,
            Tracking = pose,
            Width = 400,
            Height = 160
        };
        preview.Measure(new Size(400, 160));
        preview.Arrange(new Rect(0, 0, 400, 160));
        var target = new RenderTargetBitmap(400, 160, 96, 96, PixelFormats.Pbgra32);
        target.Render(preview);
        return target;
    }

    private static (byte B, byte G, byte R, byte A) Pixel(BitmapSource bitmap, int x, int y)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (pixel[0], pixel[1], pixel[2], pixel[3]);
    }

    private static BitmapSource Solid(int width, int height, byte r, byte g, byte b)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static void RunOnSta(Action action) => RunOnSta(() =>
    {
        action();
        return 0;
    });

    private static T RunOnSta<T>(Func<T> action)
    {
        T? result = default;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
        return result!;
    }
}
