using CleanScope.Domain.Models;

namespace CleanScope.Application;

/// <summary>全盘目录树的统计 (P2): 从**整棵树**而非 Top-N 估算可清理空间, 把深埋各 app 的缓存也算进来。</summary>
public static class ScanTreeStats
{
    /// <summary>
    /// 去重的可清理总量: 累加"最顶层可清理节点"的大小 —— 一旦某目录可清理, 计入其聚合大小且不再下钻
    /// (其可清理子目录已含在内), 避免父子重复计数。
    /// </summary>
    public static long CleanableTotal(ScanTreeNode? node)
    {
        if (node is null) return 0;
        if (node.IsCleanable) return node.Size;
        long sum = 0;
        foreach (var c in node.Children) sum += CleanableTotal(c);
        return sum;
    }

    /// <summary>可清理的最顶层节点数 (供概览展示"X 处可清理")。</summary>
    public static int CleanableCount(ScanTreeNode? node)
    {
        if (node is null) return 0;
        if (node.IsCleanable) return 1;
        var n = 0;
        foreach (var c in node.Children) n += CleanableCount(c);
        return n;
    }
}
