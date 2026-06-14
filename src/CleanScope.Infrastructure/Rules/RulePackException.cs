namespace CleanScope.Infrastructure.Rules;

/// <summary>
/// 规则包加载失败 (非法 JSON / 字段缺失 / 系统关键规则被放宽 / 目录缺失)。
/// 受控类型异常 —— 让调用方"报错不崩": 宁可拒绝启动, 也不带着残缺/被弱化的规则集运行
/// (🔴 系统关键黑名单不可缺漏或放宽)。
/// </summary>
public sealed class RulePackException : Exception
{
    public string? File { get; }

    public RulePackException(string message, string? file = null, Exception? inner = null)
        : base(file is null ? message : $"[{file}] {message}", inner)
        => File = file;
}
