using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VTuberStudioAI.App.Images;
using VTuberStudioAI.App.Preview;
using VTuberStudioAI.App.Projects;
using VTuberStudioAI.App.Themes;
using VTuberStudioAI.App.Tracking;
using VTuberStudioAI.App.ViewModels;
using Xunit;

namespace VTuberStudioAI.App.Tests;

public sealed class BrowAndThemeTests
{
    private static float[] FaceWithBrows(float browY)
    {
        var p = new float[478 * 3];
        void Set(int index, float x, float y)
        {
            p[index * 3] = x;
            p[index * 3 + 1] = y;
        }

        Set(234, -50, 0);
        Set(454, 50, 0);
        Set(10, 0, -60);
        Set(152, 0, 60);
        Set(13, 0, 5);
        Set(14, 0, 8);
        Set(33, -30, -10);
        Set(133, -10, -10);
        Set(362, 10, -10);
        Set(263, 30, -10);
        foreach (var (upper, lower) in new[] { (159, 145), (160, 144), (158, 153), (386, 374), (385, 380), (387, 373) })
        {
            Set(upper, upper < 300 ? -20 : 20, -13);
            Set(lower, upper < 300 ? -20 : 20, -7);
        }

        foreach (var index in new[] { 105, 66, 107 })
        {
            Set(index, -20, browY);
        }

        foreach (var index in new[] { 334, 296, 336 })
        {
            Set(index, 20, browY);
        }

        Set(468, -20, -10);
        Set(473, 20, -10);
        return p;
    }

    [Fact]
    public void RaisingTheBrowsIncreasesTheMeasurementAndBlinkingDoesNot()
    {
        var rest = FaceLandmarkMath.Measure(FaceWithBrows(-25))!;
        var raised = FaceLandmarkMath.Measure(FaceWithBrows(-31))!;

        Assert.Equal(0.125, rest.BrowImageLeft, 3);
        Assert.Equal(0.125, rest.BrowImageRight, 3);
        Assert.True(raised.BrowImageLeft > rest.BrowImageLeft);
        Assert.True(raised.BrowImageRight > rest.BrowImageRight);

        var blink = FaceWithBrows(-25);
        foreach (var index in new[] { 159, 160, 158, 386, 385, 387 })
        {
            blink[index * 3 + 1] = -8;
        }

        Assert.Equal(rest.BrowImageLeft, FaceLandmarkMath.Measure(blink)!.BrowImageLeft, 3);
    }

    [Fact]
    public void BrowsStayStillUntilCalibratedAndThenFollowTheCalibratedRest()
    {
        var raised = new FaceMeasurements(0, 0, 0, 0.02, 0.28, 0.28) { BrowImageLeft = 0.17, BrowImageRight = 0.10 };
        var uncalibrated = FaceMotionMapper.Map(raised, FaceTrackingSettings.Default);
        Assert.Equal(0, uncalibrated.ScreenLeftBrow);
        Assert.Equal(0, uncalibrated.ScreenRightBrow);

        var calibrated = FaceTrackingSettings.Default with { Mirror = false, BrowRestImageLeft = 0.13, BrowRestImageRight = 0.13 };
        var pose = FaceMotionMapper.Map(raised, calibrated);
        Assert.Equal(1, pose.ScreenLeftBrow, 3);
        Assert.Equal(-0.75, pose.ScreenRightBrow, 3);

        var mirrored = FaceMotionMapper.Map(raised, calibrated with { Mirror = true });
        Assert.Equal(-0.75, mirrored.ScreenLeftBrow, 3);
        Assert.Equal(1, mirrored.ScreenRightBrow, 3);

        var halfGain = FaceMotionMapper.Map(raised, calibrated with { BrowGain = 0.5 });
        Assert.Equal(0.5, halfGain.ScreenLeftBrow, 3);
    }

    [Fact]
    public void CalibrationStoresTheBrowRestAndInvalidBrowSettingsAreRejected()
    {
        var samples = Enumerable.Repeat(new FaceMeasurements(0, 0, 0, 0.03, 0.30, 0.26) { BrowImageLeft = 0.12, BrowImageRight = 0.14 }, 20).ToList();
        var result = FaceCalibrationBuilder.Build(samples, FaceTrackingSettings.Default);

        Assert.NotNull(result.Settings);
        Assert.Equal(0.12, result.Settings!.BrowRestImageLeft, 3);
        Assert.Equal(0.14, result.Settings.BrowRestImageRight, 3);
        Assert.True(result.Settings.BrowsCalibrated);
        Assert.False(FaceTrackingSettings.Default.BrowsCalibrated);
        Assert.False((FaceTrackingSettings.Default with { BrowGain = 9 }).IsValid);
        Assert.False((FaceTrackingSettings.Default with { BrowRestImageLeft = 0.9 }).IsValid);
    }

    [Fact]
    public void EyebrowLayersAreRiggedAsEyebrows()
    {
        var layers = new List<PsdLayerInfo> { new("head", 0, 0, 100, 40, true, "h"), new("eyebrow.L", 65, 15, 20, 6, true, "b") };
        var plan = LayerRigger.Plan(layers);

        Assert.Equal(LayerMotion.Eyebrow, plan[1].Motion);
    }

    [Fact]
    public void RaisedBrowMovesTheEyebrowLayerUp()
    {
        var (neutral, raised) = RunOnSta(() =>
        {
            var layers = new List<PsdLayerInfo>
            {
                new("head", 0, 0, 100, 40, true, "h"),
                new("eyebrow.L", 65, 15, 20, 6, true, "b")
            };
            var plan = LayerRigger.Plan(layers);
            var scene = new LayerScene(100, 40,
            [
                new SceneLayer(layers[0], plan[0], null),
                new SceneLayer(layers[1], plan[1], Solid(20, 6, 255, 255, 255))
            ]);
            var up = AvatarPose.Neutral with { ScreenLeftBrow = 1, ScreenRightBrow = 1 };
            return (Pixel(Render(scene, AvatarPose.Neutral, 100, 40), 285, 55).A, Pixel(Render(scene, up, 100, 40), 285, 55).A);
        });

        Assert.Equal(0, neutral);
        Assert.Equal(255, raised);
    }

    [Fact]
    public void EveryThemeDefinesAllKeysWithValidColors()
    {
        Assert.Equal(["azul-escuro", "rosa", "branco"], AppThemes.All.Select(theme => theme.Id));
        foreach (var theme in AppThemes.All)
        {
            Assert.Equal(AppThemes.Keys.Count, theme.Colors.Count);
            foreach (var key in AppThemes.Keys)
            {
                Assert.True(AppThemes.IsHexColor(theme.Colors[key]), $"{theme.Id}:{key}");
            }
        }

        Assert.Null(AppThemes.Find("neon"));
        Assert.Equal("rosa", AppThemes.Find("rosa")!.Id);
    }

    [Fact]
    public void SettingsStoreRoundTripsAndFallsBackOnBadFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AppSettingsStore(Path.Combine(folder, "settings.json"));
            Assert.Null(store.ReadThemeId());

            store.SaveThemeId("branco");
            Assert.Equal("branco", store.ReadThemeId());
            Assert.Throws<ArgumentException>(() => store.SaveThemeId("neon"));

            File.WriteAllText(store.FilePath, "{ nao é json");
            Assert.Null(store.ReadThemeId());
            File.WriteAllText(store.FilePath, "{\"ThemeId\":\"neon\"}");
            Assert.Null(store.ReadThemeId());
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Fact]
    public void ThemeServiceAppliesAndPersistsTheSelection()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AppSettingsStore(Path.Combine(folder, "settings.json"));
            var applied = new List<string>();
            var service = new ThemeService(theme => applied.Add(theme.Id), store);

            service.Initialize();
            Assert.Equal(AppThemes.Default.Id, service.Current.Id);

            Assert.Null(service.Select("rosa"));
            Assert.Equal("rosa", store.ReadThemeId());
            Assert.Equal(["azul-escuro", "rosa"], applied);
            Assert.Throws<ArgumentException>(() => service.Select("neon"));

            var reopened = new ThemeService(_ => { }, store);
            reopened.Initialize();
            Assert.Equal("rosa", reopened.Current.Id);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Fact]
    public void IntegrationsAreMarkedComingSoonAndThemeSelectionRequestsARestart()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var themes = new ThemeService(_ => { }, new AppSettingsStore(Path.Combine(folder, "settings.json")));
            themes.Initialize();
            var restarts = 0;
            var viewModel = new MainWindowViewModel(
                new ProjectRepository(Path.Combine(folder, "projects")),
                _ => false,
                (_, _) => { },
                themeService: themes,
                requestRestart: () => restarts++);

            var integrations = Assert.Single(viewModel.NavigationItems, item => item.Key == "integrations");
            Assert.Equal("EM BREVE", integrations.Badge);
            viewModel.NavigateCommand.Execute(integrations);
            Assert.Contains("early access", viewModel.CurrentPage.PanelTitle);

            Assert.Equal(3, viewModel.ThemeOptions.Count);
            viewModel.SelectThemeCommand.Execute("branco");
            Assert.Equal(1, restarts);
            Assert.Equal("branco", Assert.Single(viewModel.ThemeOptions, option => option.IsCurrent).Id);

            viewModel.SelectThemeCommand.Execute(AppThemes.Default.Id);
            Assert.Equal(1, restarts);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
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
