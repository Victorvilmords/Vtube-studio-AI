using System.Text.Json;
using VTuberStudioAI.App.Images;
using VTuberStudioAI.App.Preview;
using VTuberStudioAI.App.Projects;
using Xunit;

namespace VTuberStudioAI.App.Tests;

public sealed class LayerDeformTests
{
    private static LayerScene Scene(params (string Name, int X, int Y, int W, int H)[] items)
    {
        var infos = items.Select((item, i) => new PsdLayerInfo(item.Name, item.X, item.Y, item.W, item.H, true, $"{i}.png")).ToList();
        var plan = LayerRigger.Plan(infos);
        return new LayerScene(200, 200, infos.Select((info, i) => new SceneLayer(info, plan[i], null)).ToList());
    }

    private static LayerScene Face(string prefix = "") => Scene(
        ("head", 20, 20, 160, 160),
        ("eyewhite.L", 50, 70, 30, 20),
        ("eyewhite.R", 120, 70, 30, 20),
        (prefix + "nose", 90, 100, 20, 15),
        (prefix + "ear.L", 10, 0, 30, 40),
        ("body", 0, 150, 200, 50));

    [Fact]
    public void SymmetricEyesMoveOppositeInSizeButSameDirectionForYaw()
    {
        var scene = Face();
        var left = LayerDeform.Pose(scene, 1, 0.8, 0, RigSettings.Default);
        var right = LayerDeform.Pose(scene, 2, 0.8, 0, RigSettings.Default);

        Assert.True(left.Dx > 0 && right.Dx > 0);
        Assert.True(left.ScaleX < 1 || right.ScaleX < 1);
        Assert.NotEqual(left.ScaleX, right.ScaleX);
    }

    [Fact]
    public void NoseMovesMoreThanTheHead()
    {
        var scene = Face();
        var head = LayerDeform.Pose(scene, 0, 1, 0, RigSettings.Default);
        var nose = LayerDeform.Pose(scene, 3, 1, 0, RigSettings.Default);

        Assert.True(Math.Abs(nose.Dx) > Math.Abs(head.Dx));
    }

    [Fact]
    public void FurryMuzzleMovesMoreThanHumanNose()
    {
        var human = LayerDeform.Pose(Face(), 3, 1, 0, RigSettings.Default);
        var furry = LayerDeform.Pose(Face("F."), 3, 1, 0, RigSettings.Default);

        Assert.True(Math.Abs(furry.Dx) > Math.Abs(human.Dx));
    }

    [Fact]
    public void StaticLayersAndDisabledRigDoNotDeform()
    {
        var scene = Face();

        Assert.True(LayerDeform.Pose(scene, 5, 1, 1, RigSettings.Default).IsIdentity);
        Assert.True(LayerDeform.Pose(scene, 3, 1, 1, RigSettings.Default with { Enabled = false }).IsIdentity);
        Assert.True(LayerDeform.Pose(scene, 3, 0, 0, RigSettings.Default).IsIdentity);
    }

    [Fact]
    public void ZeroIntensityAndNonFiniteGazeAreNeutral()
    {
        var scene = Face();

        Assert.True(LayerDeform.Pose(scene, 3, 1, 1, RigSettings.Default with { Overall = 0 }).IsIdentity);
        Assert.True(LayerDeform.Pose(scene, 3, double.NaN, 1, RigSettings.Default).IsIdentity);
    }

    [Fact]
    public void PoseStaysFiniteAndBoundedAtExtremes()
    {
        var scene = Face("F.");
        var extreme = RigSettings.Default with { Overall = 2, Face = 2, Ears = 2, Hair = 2, Head = 2 };
        for (var i = 0; i < scene.Layers.Count; i++)
        {
            var pose = LayerDeform.Pose(scene, i, 1, -1, extreme);
            Assert.True(double.IsFinite(pose.Dx) && double.IsFinite(pose.Dy));
            Assert.InRange(pose.ScaleX, 0.35, 1.6);
            Assert.InRange(pose.ScaleY, 0.35, 1.6);
        }
    }

    [Fact]
    public void RigSettingsPersistAndReopen()
    {
        using var directory = new TempDir();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Rig");
        var settings = RigSettings.Default with { Face = 1.5, Ears = 0.25, Enabled = false };

        RigSettingsStore.Save(repository, project.Id, settings);

        var reopened = RigSettingsStore.Read(new ProjectRepository(directory.Path).Load(project.Id));
        Assert.Equal(settings, reopened);
    }

    [Fact]
    public void RigSettingsRejectOutOfRangeValuesOnSave()
    {
        using var directory = new TempDir();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Rig");

        Assert.Throws<ArgumentException>(() =>
            RigSettingsStore.Save(repository, project.Id, RigSettings.Default with { Hair = 5 }));
        Assert.Throws<ArgumentException>(() =>
            RigSettingsStore.Save(repository, project.Id, RigSettings.Default with { Head = double.NaN }));
    }

    [Fact]
    public void InvalidStoredSettingsFallBackToDefault()
    {
        using var directory = new TempDir();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Rig");
        var bad = JsonSerializer.SerializeToElement(RigSettings.Default with { Face = 9 });
        var configurations = new Dictionary<string, JsonElement> { [RigSettingsStore.ConfigurationKey] = bad };
        repository.Save(project with { Configurations = configurations });
        Assert.Equal(RigSettings.Default, RigSettingsStore.Read(repository.Load(project.Id)));

        configurations[RigSettingsStore.ConfigurationKey] = JsonSerializer.SerializeToElement("texto");
        repository.Save(repository.Load(project.Id) with { Configurations = configurations });
        Assert.Equal(RigSettings.Default, RigSettingsStore.Read(repository.Load(project.Id)));
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"VTuberStudioAI.Rig.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
