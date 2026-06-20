using CleanScope.Core.Attribution;
using CleanScope.Domain.Models;

namespace CleanScope.Core.Tests;

// ① 特征库匹配: 公司归一 + 目录名兜底; 空库全 null (增强缺失不影响主流程)。
public sealed class KnownSoftwareCatalogTests
{
    private static readonly KnownSoftwareCatalog Catalog = new(
        new[]
        {
            new VendorAlias("Valve", "Steam（Valve）"),
            new VendorAlias("Tencent", "腾讯"),
        },
        new[]
        {
            new DirectoryAlias("wechat", "微信（WeChat）", "微信本地数据"),
            new DirectoryAlias("v2rayN-windows-64", "v2rayN", null),
            new DirectoryAlias("obsidian", "Obsidian", "笔记库"),
        },
        new[]
        {
            new AppDescription("Steam", "游戏平台/启动器"),
            new AppDescription("Zed", "代码编辑器/IDE"),
        });

    [Theory]
    [InlineData("Valve Corporation", "Steam（Valve）")]
    [InlineData("Tencent Technology(Shenzhen) Company Limited", "腾讯")]
    public void FriendlyVendor_matches_substring_case_insensitive(string raw, string expected)
        => Assert.Equal(expected, Catalog.FriendlyVendor(raw));

    [Theory]
    [InlineData("Microsoft Corporation")]
    [InlineData("")]
    [InlineData(null)]
    public void FriendlyVendor_returns_null_when_no_match(string? raw)
        => Assert.Null(Catalog.FriendlyVendor(raw));

    [Theory] // 精确 + 归一化 (忽略大小写/分隔符) + 前缀
    [InlineData("wechat", "微信（WeChat）")]
    [InlineData("WeChat", "微信（WeChat）")]
    [InlineData("obsidian", "Obsidian")]
    [InlineData("v2rayN-windows-64", "v2rayN")]
    public void MatchDirectory_resolves_known_leaf(string leaf, string expectedApp)
    {
        var hint = Catalog.MatchDirectory(leaf);
        Assert.NotNull(hint);
        Assert.Equal(expectedApp, hint!.Value.App);
    }

    [Theory]
    [InlineData("totally-unknown-xyz")]
    [InlineData("x")]
    [InlineData("")]
    [InlineData(null)]
    public void MatchDirectory_returns_null_for_unknown(string? leaf)
        => Assert.Null(Catalog.MatchDirectory(leaf));

    [Theory] // 问题#1: 应用名 → "它是什么/干嘛的"
    [InlineData("Steam", "游戏平台/启动器")]
    [InlineData("Zed", "代码编辑器/IDE")]
    public void DescribeApp_returns_semantic_description(string app, string expected)
        => Assert.Equal(expected, Catalog.DescribeApp(app));

    [Theory]
    [InlineData("UnknownApp")]
    [InlineData("")]
    [InlineData(null)]
    public void DescribeApp_returns_null_for_unknown(string? app)
        => Assert.Null(Catalog.DescribeApp(app));

    [Fact]
    public void Empty_catalog_matches_nothing()
    {
        Assert.Null(KnownSoftwareCatalog.Empty.FriendlyVendor("Valve Corporation"));
        Assert.Null(KnownSoftwareCatalog.Empty.MatchDirectory("wechat"));
        Assert.Null(KnownSoftwareCatalog.Empty.DescribeApp("Steam"));
        Assert.Equal(0, KnownSoftwareCatalog.Empty.VendorCount);
        Assert.Equal(0, KnownSoftwareCatalog.Empty.DirectoryCount);
        Assert.Equal(0, KnownSoftwareCatalog.Empty.DescriptionCount);
    }

    [Fact] // 构造时丢弃空白条目
    public void Blank_entries_are_dropped()
    {
        var c = new KnownSoftwareCatalog(
            new[] { new VendorAlias("", "x"), new VendorAlias("ok", "") },
            new[] { new DirectoryAlias("", "app", null), new DirectoryAlias("name", "", null) });
        Assert.Equal(0, c.VendorCount);
        Assert.Equal(0, c.DirectoryCount);
    }
}
