using VTuberStudioAI.App.Furry;
using VTuberStudioAI.App.Images;
using Xunit;

namespace VTuberStudioAI.App.Tests;

public sealed class FurryDetectionTests
{
    private static PsdLayerInfo Layer(string name, bool visible = true) => new(name, 0, 0, 10, 10, visible, name);

    [Fact]
    public void Without_layers_nothing_is_detected()
    {
        var result = FurryDetection.Analyze([]);
        Assert.Equal(CharacterKind.NoLayers, result.Kind);
        Assert.Empty(result.Parts);
    }

    [Fact]
    public void Unrecognized_names_are_reported()
    {
        var result = FurryDetection.Analyze([Layer("Layer 1"), Layer("foo")]);
        Assert.Equal(CharacterKind.Unrecognized, result.Kind);
        Assert.Empty(result.Parts);
    }

    [Fact]
    public void F_prefix_marks_a_furry_and_lists_deformable_parts()
    {
        var result = FurryDetection.Analyze([
            Layer("head"), Layer("F.ear.L"), Layer("F_ear_R"), Layer("F.snout"), Layer("body"), Layer("background"), Layer("mouthopen", false)]);

        Assert.Equal(CharacterKind.Furry, result.Kind);
        Assert.Equal(3, result.FurryPartCount);
        Assert.Equal(7, result.Parts.Count);
        Assert.True(result.Parts.Single(part => part.LayerName == "F.ear.L").IsDeformable);
        Assert.True(result.Parts.Single(part => part.LayerName == "F.snout").IsDeformable);
        Assert.False(result.Parts.Single(part => part.LayerName == "body").IsDeformable);
        Assert.True(result.Parts.Single(part => part.LayerName == "mouthopen").IsHidden);
    }

    [Fact]
    public void Snout_without_prefix_still_counts_as_furry()
    {
        var result = FurryDetection.Analyze([Layer("head"), Layer("snout")]);
        Assert.Equal(CharacterKind.Furry, result.Kind);
        Assert.Equal(1, result.FurryPartCount);
    }

    [Fact]
    public void Human_without_f_parts_is_human()
    {
        var result = FurryDetection.Analyze([Layer("head"), Layer("eyewhite.L"), Layer("mouth")]);
        Assert.Equal(CharacterKind.Human, result.Kind);
        Assert.Contains("C deitado", result.Parts.Single(part => part.LayerName == "mouth").Behavior);
    }

    [Fact]
    public void Furry_mouth_describes_inverted_v()
    {
        var result = FurryDetection.Analyze([Layer("F.mouth")]);
        Assert.Contains("V invertido", result.Parts[0].Behavior);
    }
}
