using VTuberStudioAI.App.Images;
using VTuberStudioAI.App.Preview;
using VTuberStudioAI.App.Projects;
using Xunit;

namespace VTuberStudioAI.App.Tests;

public sealed class LayerNamingTests
{
    [Theory]
    [InlineData("F.ear.L", LayerPart.Ear, LayerSide.Left, true)]
    [InlineData("F_ear_L", LayerPart.Ear, LayerSide.Left, true)]
    [InlineData("f-horn-r", LayerPart.Horn, LayerSide.Right, true)]
    [InlineData("eyewhite_R", LayerPart.EyeWhite, LayerSide.Right, false)]
    [InlineData("Eye White.L", LayerPart.EyeWhite, LayerSide.Left, false)]
    [InlineData("pupil.L.001", LayerPart.Pupil, LayerSide.Left, false)]
    [InlineData("EyeHighlight R copy", LayerPart.EyeHighlight, LayerSide.Right, false)]
    [InlineData("Íris direita", LayerPart.Iris, LayerSide.Right, false)]
    [InlineData("Olho esquerdo", LayerPart.EyeWhite, LayerSide.Left, false)]
    [InlineData("body", LayerPart.Body, LayerSide.None, false)]
    [InlineData("F.tail", LayerPart.Tail, LayerSide.None, true)]
    [InlineData("Layer 1", LayerPart.Unknown, LayerSide.None, false)]
    [InlineData("F", LayerPart.Unknown, LayerSide.None, false)]
    [InlineData("", LayerPart.Unknown, LayerSide.None, false)]
    public void ParsesPartSideAndFurryPrefix(string name, LayerPart part, LayerSide side, bool furry)
    {
        var role = LayerNaming.Parse(name);

        Assert.Equal(part, role.Part);
        Assert.Equal(side, role.Side);
        Assert.Equal(furry, role.IsFurry);
    }

    [Fact]
    public void EveryLayerNameOfTheRealCharacterIsRecognizedExceptTheDefaultPaintLayer()
    {
        string[] names =
        [
            "Plano de fundo", "F_Body", "Neck", "F_Ear_R", "F_Ear_L", "F_Head", "F_mouth", "F_nose",
            "hair_back", "hair_side_L", "hair_side_R", "hair_Bangs", "Horn_L", "Horn_R", "EyeWhite_L", "EyeWhite_R",
            "EyeLash_Upper_L", "EyeLash_Lower_L", "EyeLash_Upper_R", "EyeLash_Lower_R", "Iris_R", "Pupil_R",
            "Eye_highlight_R", "Brow_L", "Brow_R", "Iris_L", "Pupil_L", "Eye_highlight_L"
        ];

        Assert.All(names, name => Assert.True(LayerNaming.Parse(name).IsRecognized, name));
        Assert.Equal(LayerPart.EyelashLower, LayerNaming.Parse("EyeLash_Lower_R").Part);
        Assert.Equal(LayerSide.Right, LayerNaming.Parse("EyeLash_Lower_R").Side);
        Assert.Equal(LayerPart.HairFront, LayerNaming.Parse("hair_Bangs").Part);
        Assert.Equal(LayerPart.HairSide, LayerNaming.Parse("hair_side_L").Part);
        Assert.Equal(LayerPart.HairBack, LayerNaming.Parse("hair_back").Part);
        Assert.Equal(LayerPart.EyeHighlight, LayerNaming.Parse("Eye_highlight_L").Part);
        Assert.Equal(LayerPart.Background, LayerNaming.Parse("Plano de fundo").Part);
        Assert.Equal(LayerPart.Neck, LayerNaming.Parse("Neck").Part);
        Assert.Equal(LayerPart.Nose, LayerNaming.Parse("F_nose").Part);
        Assert.False(LayerNaming.Parse("Camada de pintura 2").IsRecognized);
    }

    [Fact]
    public void LashesFollowTheEyeOfTheirSideEvenWithoutOverlap()
    {
        var plan = LayerRigger.Plan(
        [
            new PsdLayerInfo("EyeWhite_L", 10, 20, 20, 10, true, "a"),
            new PsdLayerInfo("EyeWhite_R", 60, 20, 20, 10, true, "b"),
            new PsdLayerInfo("EyeLash_Upper_R", 60, 10, 20, 6, true, "c"),
            new PsdLayerInfo("EyeLash_Lower_L", 10, 32, 20, 3, true, "d")
        ]);

        Assert.Equal(LayerMotion.UpperLash, plan[2].Motion);
        Assert.Equal(1, plan[2].MaskIndex);
        Assert.Equal(LayerMotion.LowerLash, plan[3].Motion);
        Assert.Equal(0, plan[3].MaskIndex);
    }

    [Fact]
    public void DescriptionMentionsSideAndFurry()
    {
        var text = LayerNaming.Describe(LayerNaming.Parse("F.ear.L"));

        Assert.Contains("Orelha", text);
        Assert.Contains("esquerda", text);
        Assert.Contains("furry", text);
        Assert.Contains("Sem papel", LayerNaming.Describe(LayerNaming.Parse("xyz")));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("F.ear.L", true)]
    [InlineData("a\tb", false)]
    public void ValidatesLayerNames(string name, bool valid) =>
        Assert.Equal(valid, LayerNaming.Validate(name) is null);

    [Fact]
    public void RejectsTooLongNames() =>
        Assert.NotNull(LayerNaming.Validate(new string('a', LayerNaming.MaxNameLength + 1)));

    [Fact]
    public void RigMasksEachEyeContentWithTheEyeWhiteOfTheSameSide()
    {
        var layers = new List<PsdLayerInfo>
        {
            new("body", 0, 0, 100, 100, true, "a"),
            new("eyewhite.L", 10, 10, 20, 10, true, "b"),
            new("eyewhite.R", 50, 10, 20, 10, true, "c"),
            new("pupil.L", 12, 12, 6, 6, true, "d"),
            new("pupil.R", 52, 12, 6, 6, true, "e"),
            new("eyehighlight.R", 55, 12, 2, 2, true, "f"),
            new("F.ear.L", 0, 0, 10, 10, true, "g"),
            new("Layer 9", 0, 0, 5, 5, true, "h")
        };

        var plan = LayerRigger.Plan(layers);

        Assert.Equal(LayerMotion.Static, plan[0].Motion);
        Assert.Equal(LayerMotion.EyeBase, plan[1].Motion);
        Assert.Equal(1, plan[3].MaskIndex);
        Assert.Equal(2, plan[4].MaskIndex);
        Assert.Equal(2, plan[5].MaskIndex);
        Assert.Equal(LayerMotion.Ear, plan[6].Motion);
        Assert.Equal(LayerMotion.FollowsHead, plan[7].Motion);
        Assert.Contains("furry", LayerRigger.Summarize(plan));
    }

    [Fact]
    public void ContentWithoutAnOverlappingEyeWhiteHasNoMask()
    {
        var plan = LayerRigger.Plan(
        [
            new PsdLayerInfo("eyewhite.L", 0, 0, 10, 10, true, "a"),
            new PsdLayerInfo("pupil.L", 80, 80, 5, 5, true, "b")
        ]);

        Assert.Null(plan[1].MaskIndex);
    }

    [Fact]
    public void RenamePersistsAfterReopeningAndRejectsInvalidRequests()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new ProjectRepository(directory);
            var project = repository.Create("Rename");
            var imagePath = repository.GetResourcePath(project.Id, "resources/a.png");
            Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
            File.WriteAllBytes(imagePath, [1]);
            var set = new PsdLayerSet("resources/a.png", "a.psd", [new PsdLayerInfo("Layer 1", 0, 0, 2, 2, true, "resources/layers/001.png")]);
            repository.Save(project with
            {
                ResourceReferences = ["resources/a.png"],
                Configurations = new Dictionary<string, System.Text.Json.JsonElement>
                {
                    [PsdLayerStore.ConfigurationKey] = System.Text.Json.JsonSerializer.SerializeToElement(set)
                }
            });

            var renamed = PsdLayerStore.Rename(repository, project.Id, "resources/a.png", "resources/layers/001.png", "  F.ear.L ");

            Assert.Equal("F.ear.L", Assert.Single(renamed).Name);
            var reopened = new ProjectRepository(directory).Load(project.Id);
            Assert.Equal("F.ear.L", Assert.Single(PsdLayerStore.Read(reopened, "resources/a.png")).Name);
            Assert.Throws<ArgumentException>(() =>
                PsdLayerStore.Rename(repository, project.Id, "resources/a.png", "resources/layers/001.png", " "));
            Assert.Throws<InvalidDataException>(() =>
                PsdLayerStore.Rename(repository, project.Id, "resources/a.png", "resources/layers/zzz.png", "x"));
            Assert.Throws<InvalidDataException>(() =>
                PsdLayerStore.Rename(repository, project.Id, "resources/outra.png", "resources/layers/001.png", "x"));
            Assert.Equal("F.ear.L", PsdLayerStore.Read(repository.Load(project.Id), "resources/a.png")[0].Name);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
