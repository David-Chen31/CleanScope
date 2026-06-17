using System.Collections.Concurrent;
using CleanScope.Domain.Abstractions;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Models;

namespace CleanScope.Application;

/// <summary>AI 解释触发模式 (S6): OnDemand=扫描不批量解释 (快, 详情页按需), Batch=扫描后并发批量解释。</summary>
public enum AiMode { OnDemand, Batch }

/// <summary>
/// AI 旁路注解器 (S6 性能): 把 脱敏→解释→校验 三步封装, 并加 (a) 按脱敏路径模式缓存,
/// (b) 批量并发限流。把"逐项串行云端调用"(百项十几分钟) 降为"并发 + 同类复用"。
///
/// 安全不变: 仅旁路建议; 系统关键跳过; 出云仍经脱敏网关 (唯一通道); 校验后才可展示。
/// </summary>
public sealed class AiAnnotator
{
    private readonly ISanitizationGateway? _sanitizer;
    private readonly IExplanationService? _explanation;
    private readonly IAiOutputValidator? _validator;
    private readonly ConcurrentDictionary<string, AiExplanation> _cache = new();

    public AiAnnotator(ISanitizationGateway? sanitizer, IExplanationService? explanation, IAiOutputValidator? validator)
    {
        _sanitizer = sanitizer;
        _explanation = explanation;
        _validator = validator;
    }

    public bool Enabled => _sanitizer is not null && _explanation is not null && _validator is not null;

    /// <summary>解释单项 (按需)。系统关键或未启用 → null。同脱敏模式命中缓存则复用。</summary>
    public async Task<AiExplanation?> AnnotateAsync(FileAnalysis analysis, CancellationToken ct = default)
    {
        if (!Enabled || analysis.RuleMatch?.IsSystemCritical == true) return null;

        var input = _sanitizer!.Sanitize(analysis);
        var key = input.PathPattern;
        if (!string.IsNullOrEmpty(key) && _cache.TryGetValue(key, out var cached))
            return cached;

        var raw = await _explanation!.ExplainAsync(input, ct);
        var validated = _validator!.Validate(raw, analysis.RuleMatch, analysis.Risk);
        if (!string.IsNullOrEmpty(key)) _cache[key] = validated;
        return validated;
    }

    /// <summary>批量并发解释 (限流)。返回与输入同序的解释 (跳过项为 null)。</summary>
    public async Task<AiExplanation?[]> AnnotateAllAsync(
        IReadOnlyList<FileAnalysis> analyses, int maxParallel = 8, CancellationToken ct = default)
    {
        if (!Enabled) return new AiExplanation?[analyses.Count];

        using var gate = new SemaphoreSlim(Math.Max(1, maxParallel));
        var tasks = analyses.Select(async a =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try { return await AnnotateAsync(a, ct).ConfigureAwait(false); }
            finally { gate.Release(); }
        });
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
