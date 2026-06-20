using System.Collections.ObjectModel;
using CleanScope.Ai.Chat;
using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.Composition;
using CleanScope.App.Wpf.Mvvm;
using CleanScope.Domain.Enums;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>脱敏档位的展示项 (问题#3: 让用户知情选择)。</summary>
public sealed record SanitizationChoice(SanitizationLevel Level, string Label, string Hint);

/// <summary>
/// AI 设置 (D): 在应用内配置 Base URL + Key → 后端检索模型 → 列表选择 → 测试连通 → 保存即时生效。
/// 保存经 <see cref="AppServices.ReconfigureAi"/> 热替换对话客户端 (无需重启); Key 经 DPAPI 加密落用户目录, 不入库。
/// </summary>
public sealed class AiSettingsViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public AiSettingsViewModel(AppServices services)
    {
        _services = services;
        var o = services.CurrentAiOptions;
        _baseUrl = o.BaseUrl;
        _apiKey = o.ApiKey;
        _cloudEnabled = o.CloudEnabled;
        if (!string.IsNullOrWhiteSpace(o.Model)) { Models.Add(o.Model); _selectedModel = o.Model; }
        _selectedSanitization = SanitizationChoices.FirstOrDefault(c => c.Level == o.Sanitization) ?? SanitizationChoices[0];

        ListModelsCommand = new AsyncRelayCommand(_ => ListModelsAsync(), _ => !_busy && HasBaseUrl);
        TestCommand = new AsyncRelayCommand(_ => TestAsync(), _ => !_busy && CanUse);
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => !_busy);
    }

    public AsyncRelayCommand ListModelsCommand { get; }
    public AsyncRelayCommand TestCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }

    public ObservableCollection<string> Models { get; } = new();

    private string _baseUrl;
    public string BaseUrl
    {
        get => _baseUrl;
        set { if (SetField(ref _baseUrl, value)) RaiseAll(); }
    }

    private string _apiKey;
    public string ApiKey
    {
        get => _apiKey;
        set { if (SetField(ref _apiKey, value)) RaiseAll(); }
    }

    private string? _selectedModel;
    public string? SelectedModel
    {
        get => _selectedModel;
        set { if (SetField(ref _selectedModel, value)) RaiseAll(); }
    }

    private bool _cloudEnabled;
    public bool CloudEnabled { get => _cloudEnabled; set => SetField(ref _cloudEnabled, value); }

    // 问题#3: 出云脱敏档位 (隐私 vs 识别力, 用户知情选择)。
    public IReadOnlyList<SanitizationChoice> SanitizationChoices { get; } = new[]
    {
        new SanitizationChoice(SanitizationLevel.Strict, "严格（最隐私，识别力受限）",
            "用户名和文件夹/文件名都不发送（替换为占位符）。最大化隐私，但 AI 往往认不出具体是哪个软件。"),
        new SanitizationChoice(SanitizationLevel.Balanced, "均衡（默认 · 推荐）",
            "发送文件夹/应用名以便 AI 识别（如 Steam、Zed），仍隐去用户名。识别力大幅提升；注意文件夹名会发送到云端。"),
        new SanitizationChoice(SanitizationLevel.Off, "关闭（识别最准）",
            "发送真实相对路径，AI 识别最准；完整路径与名称会发送到所配置的云端服务。请仅在信任该服务时使用。"),
    };

    private SanitizationChoice _selectedSanitization;
    public SanitizationChoice SelectedSanitization
    {
        get => _selectedSanitization;
        set
        {
            if (!SetField(ref _selectedSanitization, value)) return;
            OnPropertyChanged(nameof(SanitizationHint));
            OnPropertyChanged(nameof(IsStrict));
            OnPropertyChanged(nameof(IsBalanced));
            OnPropertyChanged(nameof(IsOff));
        }
    }

    // 三档位单选 (问题#4: 用 RadioButton 平铺三档, 让"可调脱敏"一眼可见, 不藏在下拉里)。
    public bool IsStrict
    {
        get => _selectedSanitization.Level == SanitizationLevel.Strict;
        set { if (value) SelectedSanitization = SanitizationChoices.First(c => c.Level == SanitizationLevel.Strict); }
    }
    public bool IsBalanced
    {
        get => _selectedSanitization.Level == SanitizationLevel.Balanced;
        set { if (value) SelectedSanitization = SanitizationChoices.First(c => c.Level == SanitizationLevel.Balanced); }
    }
    public bool IsOff
    {
        get => _selectedSanitization.Level == SanitizationLevel.Off;
        set { if (value) SelectedSanitization = SanitizationChoices.First(c => c.Level == SanitizationLevel.Off); }
    }

    public string StrictHint => SanitizationChoices.First(c => c.Level == SanitizationLevel.Strict).Hint;
    public string BalancedHint => SanitizationChoices.First(c => c.Level == SanitizationLevel.Balanced).Hint;
    public string OffHint => SanitizationChoices.First(c => c.Level == SanitizationLevel.Off).Hint;

    public string SanitizationHint => _selectedSanitization?.Hint ?? "";

    private bool _busy;
    public bool IsBusy
    {
        get => _busy;
        private set { if (SetField(ref _busy, value)) RaiseAll(); }
    }

    // 保存按钮三态: 闲置 → 保存中(转圈) → 已生效(✓), 让"保存并生效"就近可见地有反馈。
    public enum SavePhase { Idle, Saving, Saved }
    private SavePhase _phase = SavePhase.Idle;
    public SavePhase Phase
    {
        get => _phase;
        private set { if (SetField(ref _phase, value)) { OnPropertyChanged(nameof(IsSaving)); OnPropertyChanged(nameof(JustSaved)); OnPropertyChanged(nameof(ShowSaveIdle)); } }
    }
    public bool IsSaving => _phase == SavePhase.Saving;
    public bool JustSaved => _phase == SavePhase.Saved;
    public bool ShowSaveIdle => _phase == SavePhase.Idle;

    private string _status = "";
    public string Status { get => _status; private set => SetField(ref _status, value); }

    public string CurrentState => _services.AiEnabled
        ? $"当前：已启用（模型 {_services.CurrentAiOptions.Model}）"
        : "当前：未启用（全程本地，零 token）";

    private bool HasBaseUrl => !string.IsNullOrWhiteSpace(_baseUrl);
    private bool CanUse => HasBaseUrl && !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_selectedModel);

    private async Task ListModelsAsync()
    {
        IsBusy = true;
        Status = "正在检索可用模型…";
        try
        {
            var models = await AiProvisioning.ListModelsAsync(_services.Http, _baseUrl, _apiKey);
            var keep = _selectedModel;
            Models.Clear();
            foreach (var m in models) Models.Add(m);
            SelectedModel = models.Contains(keep, StringComparer.OrdinalIgnoreCase) ? keep : Models.FirstOrDefault();
            Status = models.Count > 0 ? $"检索到 {models.Count} 个模型，请选择。" : "未返回任何模型，请检查端点是否兼容 /models。";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally { IsBusy = false; }
    }

    private async Task TestAsync()
    {
        IsBusy = true;
        Status = "正在测试连接（发一条极小请求）…";
        try
        {
            var (ok, msg) = await AiProvisioning.TestAsync(_services.Http,
                new AiOptions(_baseUrl, _apiKey, _selectedModel ?? "", CloudEnabled: true));
            Status = (ok ? "✅ " : "❌ ") + msg;
            Toast.Show(ok ? "连接正常" : "连接失败：" + msg, ok ? ToastKind.Success : ToastKind.Error);
        }
        catch (Exception ex) { Status = "❌ 测试失败：" + ex.Message; Toast.Error("测试失败：" + ex.Message); }
        finally { IsBusy = false; }
    }

    private async Task SaveAsync()
    {
        IsBusy = true;
        Phase = SavePhase.Saving;
        try
        {
            var opts = new AiOptions(_baseUrl?.Trim() ?? "", _apiKey?.Trim() ?? "", _selectedModel ?? "", CloudEnabled,
                _selectedSanitization?.Level ?? SanitizationLevel.Strict);
            _services.ReconfigureAi(opts);   // 热替换 + 更新脱敏档位 + 持久化(加密) + 广播
            OnPropertyChanged(nameof(CurrentState));
            Status = _services.AiEnabled
                ? "已保存并启用。AI 解释 / 识别 / 建议现在可用（按需触发，仍只在你点击时出云）。"
                : "已保存。未启用云端（勾选「启用」并填全 Base URL/Key/模型 即可启用）。";
            Toast.Show(_services.AiEnabled ? $"已保存并生效 · 模型 {opts.Model}" : "已保存（未启用云端）", ToastKind.Success);
            Phase = SavePhase.Saved;
            await Task.Delay(1600);   // "✓ 已生效"停留片刻
        }
        catch (Exception ex) { Status = "保存失败：" + ex.Message; Toast.Error("保存失败：" + ex.Message); }
        finally { if (Phase == SavePhase.Saved) Phase = SavePhase.Idle; IsBusy = false; }
    }

    private void RaiseAll()
    {
        ListModelsCommand.RaiseCanExecuteChanged();
        TestCommand.RaiseCanExecuteChanged();
        SaveCommand.RaiseCanExecuteChanged();
    }
}
