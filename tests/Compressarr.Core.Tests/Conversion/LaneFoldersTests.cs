using Compressarr.Core.Config;
using Compressarr.Core.Conversion;

namespace Compressarr.Core.Tests.Conversion;

file sealed class TokenExpander : IPathExpander
{
    public string Expand(string value) => value.Replace("%MEDIA%", @"D:\Media");
    public bool PathExists(string value) => true;
}

public class LaneFoldersTests
{
    private static readonly IPathExpander Expander = new TokenExpander();

    private static LaneConfig Lane(string id, string input, bool enabled = true) => new() { Id = id, DisplayName = id, Input = input, Enabled = enabled };

    private static LaneFolderRelation Relation(string mine, string theirs) =>
        LaneFolders.FindConflict(Lane("a", mine), new[] { Lane("b", theirs) }, Expander)?.Relation ?? LaneFolderRelation.None;

    [Fact]
    public void TheSameFolder_IsAConflict_IgnoringCaseAndTrailingSeparators()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(LaneFolderRelation.Same, Relation(@"D:\Media", @"D:\Media"));
        Assert.Equal(LaneFolderRelation.Same, Relation(@"D:\Media\", @"d:\MEDIA"));
        Assert.Equal(LaneFolderRelation.Same, Relation(@"D:\Media", @"%MEDIA%"));   // tokens are expanded first
    }

    [Fact]
    public void AFolderInsideOrAroundAnother_IsAConflict_InBothDirections()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(LaneFolderRelation.InsideOther, Relation(@"D:\Media\Anime", @"D:\Media"));
        Assert.Equal(LaneFolderRelation.ContainsOther, Relation(@"D:\Media", @"D:\Media\Anime\Sub"));
        Assert.Equal(LaneFolderRelation.InsideOther, Relation(@"D:\Media", @"D:\"));
    }

    [Fact]
    public void FoldersThatOnlyShareAPrefixInTheirName_AreDifferentFolders()
    {
        // The two real production lanes: "D:\Media Download" and "D:\Media Download Kids".
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(LaneFolderRelation.None, Relation(@"D:\Media Download", @"D:\Media Download Kids"));
        Assert.Equal(LaneFolderRelation.None, Relation(@"D:\Media Download Kids", @"D:\Media Download"));
        Assert.Equal(LaneFolderRelation.None, Relation(@"D:\Media", @"E:\Media"));
    }

    [Fact]
    public void OnlyEnabledLanes_AndOnlyOtherLanes_Count()
    {
        var mine = Lane("a", @"D:\Media");
        var disabledOther = Lane("b", @"D:\Media", enabled: false);
        var itself = Lane("a", @"D:\Media");
        var enabledOther = Lane("c", @"D:\Media");

        Assert.Null(LaneFolders.FindConflict(mine, new[] { disabledOther, itself }, Expander));      // a disabled lane never runs
        Assert.Null(LaneFolders.FindConflict(Lane("a", @"D:\Media", enabled: false), new[] { enabledOther }, Expander)); // nor does a disabled "mine"
        Assert.Same(enabledOther, LaneFolders.FindConflict(mine, new[] { disabledOther, itself, enabledOther }, Expander)!.Other);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankInput_NeverConflicts(string blank)
    {
        Assert.Null(LaneFolders.FindConflict(Lane("a", blank), new[] { Lane("b", blank) }, Expander));
    }

    [Fact]
    public void TheMessage_NamesTheOtherLane_AndSaysWhatToDo()
    {
        var conflict = LaneFolders.FindConflict(Lane("a", @"D:\Media"), new[] { Lane("Kids", @"D:\Media") }, Expander)!;

        Assert.Contains("Kids", conflict.Describe());
        Assert.Contains("turn one of them off", conflict.Describe());
    }
}
