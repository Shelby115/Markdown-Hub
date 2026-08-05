using Microsoft.Extensions.Configuration;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Services;

public class PickClosestMatchTests
{
    [Fact]
    public void NoCandidates_ReturnsNull()
    {
        Assert.Null(HubPathService.PickClosestMatch([], "Angryria/Campaigns/Campaign 1/Sessions"));
    }

    [Fact]
    public void ExactlyOneCandidate_ReturnsItRegardlessOfContext()
    {
        string[] candidates = ["Angryria/Encounters/Side Adventures/Evil Fairy.md"];

        Assert.Equal(candidates[0], HubPathService.PickClosestMatch(candidates, null));
        Assert.Equal(candidates[0], HubPathService.PickClosestMatch(candidates, "Completely/Unrelated/Folder"));
    }

    [Fact]
    public void ExactlyOneCandidate_ReturnsIt_EvenWithNoContext()
    {
        string[] candidates = ["Notes/Idea.md"];
        Assert.Equal("Notes/Idea.md", HubPathService.PickClosestMatch(candidates, null));
    }

    // The reported bug: a link in Angryria/Campaigns/Campaign 1/Sessions/Session 5.md to
    // "Evil Fairy" should resolve to the one actually inside the Angryria tree
    // (Angryria/Encounters/Side Adventures/Evil Fairy.md) even if some unrelated campaign
    // elsewhere in the hub happens to have a same-named page too.
    [Fact]
    public void MultipleCandidates_PicksTheOneClosestToCurrentFolder()
    {
        string[] candidates =
        [
            "SomeOtherCampaign/NPCs/Evil Fairy.md",
            "Angryria/Encounters/Side Adventures/Evil Fairy.md",
        ];

        var result = HubPathService.PickClosestMatch(candidates, "Angryria/Campaigns/Campaign 1/Sessions");

        Assert.Equal("Angryria/Encounters/Side Adventures/Evil Fairy.md", result);
    }

    [Fact]
    public void MultipleCandidates_SameFolderAsCurrent_AlwaysWinsOverAnyOtherFolder()
    {
        string[] candidates =
        [
            "Angryria/Campaigns/Campaign 1/Sessions/Evil Fairy.md", // distance 0
            "Angryria/Encounters/Side Adventures/Evil Fairy.md",     // distance > 0
        ];

        var result = HubPathService.PickClosestMatch(candidates, "Angryria/Campaigns/Campaign 1/Sessions");

        Assert.Equal("Angryria/Campaigns/Campaign 1/Sessions/Evil Fairy.md", result);
    }

    [Fact]
    public void MultipleCandidates_CurrentFolderIsAncestorOfOneCandidate_PrefersTheDescendant()
    {
        // relativeToFolder "Angryria" is a direct ancestor of the first candidate's folder
        // (1 level down) and unrelated to the second (shares nothing).
        string[] candidates =
        [
            "Angryria/Encounters/Evil Fairy.md",
            "OtherCampaign/Deep/Nested/Folder/Evil Fairy.md",
        ];

        var result = HubPathService.PickClosestMatch(candidates, "Angryria");

        Assert.Equal("Angryria/Encounters/Evil Fairy.md", result);
    }

    [Fact]
    public void MultipleCandidates_CurrentFolderIsDescendantOfOneCandidatesFolder_StillPrefersIt()
    {
        // Symmetric case: relativeToFolder is nested *inside* one candidate's folder rather
        // than the other way around - distance is still smaller than to an unrelated branch.
        string[] candidates =
        [
            "Angryria/Evil Fairy.md",
            "OtherCampaign/Deep/Evil Fairy.md",
        ];

        var result = HubPathService.PickClosestMatch(candidates, "Angryria/Campaigns/Campaign 1/Sessions");

        Assert.Equal("Angryria/Evil Fairy.md", result);
    }

    [Fact]
    public void NoContext_StillDeterministic_NotJustFirstInList()
    {
        // With nothing to measure distance against, every candidate is equally "close" - the
        // result must still be picked by a stable rule (shorter path, then alphabetical), not
        // whatever order the caller happened to pass them in. Equal-length folder names so the
        // length tiebreak can't decide it first - this isolates the alphabetical rule.
        string[] candidatesA = ["Zeta/Evil Fairy.md", "Alph/Evil Fairy.md"];
        string[] candidatesB = ["Alph/Evil Fairy.md", "Zeta/Evil Fairy.md"];

        var resultA = HubPathService.PickClosestMatch(candidatesA, null);
        var resultB = HubPathService.PickClosestMatch(candidatesB, null);

        Assert.Equal(resultA, resultB);
        Assert.Equal("Alph/Evil Fairy.md", resultA);
    }

    [Fact]
    public void TrueTie_ShorterOverallPathWins()
    {
        // Both candidates are at the same tree-distance from the current folder (neither
        // shares any folder segment with it), so the shorter path breaks the tie.
        string[] candidates =
        [
            "Other/Deeply/Nested/Evil Fairy.md",
            "Other/Evil Fairy.md",
        ];

        var result = HubPathService.PickClosestMatch(candidates, "Unrelated");

        Assert.Equal("Other/Evil Fairy.md", result);
    }

    [Fact]
    public void TrueTie_SamePathLength_BreaksAlphabetically()
    {
        string[] candidates = ["Zeta/Evil Fairy.md", "Alph/Evil Fairy.md"];

        var result = HubPathService.PickClosestMatch(candidates, "Unrelated");

        Assert.Equal("Alph/Evil Fairy.md", result);
    }

    [Fact]
    public void FolderMatching_IsCaseInsensitive()
    {
        string[] candidates =
        [
            "angryria/encounters/Evil Fairy.md",
            "OtherCampaign/Evil Fairy.md",
        ];

        // Different casing on the shared "Angryria" segment shouldn't stop it from counting
        // as a match - wiki-link resolution elsewhere in the app is case-insensitive too.
        var result = HubPathService.PickClosestMatch(candidates, "ANGRYRIA/Campaigns");

        Assert.Equal("angryria/encounters/Evil Fairy.md", result);
    }

    [Fact]
    public void ResultOrder_IsIndependentOfInputOrder()
    {
        string[] forward =
        [
            "Angryria/Encounters/Evil Fairy.md",
            "Middle/Evil Fairy.md",
            "SomeOtherCampaign/NPCs/Evil Fairy.md",
        ];
        var backward = forward.Reverse().ToArray();

        const string from = "Angryria/Campaigns/Campaign 1/Sessions";

        Assert.Equal(HubPathService.PickClosestMatch(forward, from), HubPathService.PickClosestMatch(backward, from));
    }
}

public class HubPathServiceFindByFilenameTests : IDisposable
{
    private readonly string _hubRoot;
    private readonly HubPathService _sut;

    public HubPathServiceFindByFilenameTests()
    {
        _hubRoot = Directory.CreateTempSubdirectory("markdown-hub-tests-").FullName;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Hub:MarkdownRoot"] = _hubRoot })
            .Build();
        _sut = new HubPathService(config);
    }

    public void Dispose()
    {
        try { Directory.Delete(_hubRoot, recursive: true); } catch { /* best effort cleanup */ }
    }

    private void CreateFile(string relativePath)
    {
        var full = Path.Combine(_hubRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "content");
    }

    [Fact]
    public void SingleMatch_ResolvesRegardlessOfContext()
    {
        CreateFile("Angryria/Encounters/Side Adventures/Evil Fairy.md");

        var result = _sut.FindByFilename("Evil Fairy.md", "Angryria/Campaigns/Campaign 1/Sessions");

        Assert.Equal("Angryria/Encounters/Side Adventures/Evil Fairy.md", result);
    }

    [Fact]
    public void NoMatch_ReturnsNull()
    {
        CreateFile("Angryria/Encounters/Side Adventures/Evil Fairy.md");

        Assert.Null(_sut.FindByFilename("Nonexistent.md", null));
    }

    [Fact]
    public void FilenameMatch_IsCaseInsensitive()
    {
        CreateFile("Angryria/Encounters/Side Adventures/Evil Fairy.md");

        Assert.Equal("Angryria/Encounters/Side Adventures/Evil Fairy.md", _sut.FindByFilename("EVIL FAIRY.MD", null));
    }

    [Fact]
    public void HiddenDotFolders_AreSkipped()
    {
        CreateFile(".attachments/Evil Fairy.md");
        CreateFile("Angryria/Encounters/Side Adventures/Evil Fairy.md");

        var result = _sut.FindByFilename("Evil Fairy.md", null);

        Assert.Equal("Angryria/Encounters/Side Adventures/Evil Fairy.md", result);
    }

    // End-to-end version of the reported bug: two same-named pages actually on disk, resolved
    // from a page that's nested under one of them - must reliably pick that one, every time,
    // not depend on filesystem enumeration order.
    [Fact]
    public void AmbiguousFilename_OnRealDirectoryTree_ResolvesToClosestMatch()
    {
        CreateFile("SomeOtherCampaign/NPCs/Evil Fairy.md");
        CreateFile("Angryria/Encounters/Side Adventures/Evil Fairy.md");
        CreateFile("Angryria/Campaigns/Campaign 1/Sessions/Session 5.md");

        for (var i = 0; i < 5; i++)
        {
            var result = _sut.FindByFilename("Evil Fairy.md", "Angryria/Campaigns/Campaign 1/Sessions");
            Assert.Equal("Angryria/Encounters/Side Adventures/Evil Fairy.md", result);
        }
    }
}
