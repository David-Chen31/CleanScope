using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.Mvvm;
using CleanScope.Domain.Enums;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>
/// P2: 引导式"腾出空间"向导 (小白友好)。三步把"安全瘦身"讲清并一键完成:
///   ① 这些可以放心清理 (A/B, 一键回收) → ② 最占地但不能删的 (可迁移到其他盘) → ③ 官方手段再清一些。
/// 不持有任何删除能力, 全部委托给 <see cref="HomeViewModel"/> 已有命令 (安全闸门/确认弹窗不变)。
/// </summary>
public sealed class SpaceWizardViewModel : ViewModelBase
{
    private readonly HomeViewModel _home;

    public SpaceWizardViewModel(HomeViewModel home)
    {
        _home = home;
        NextCommand = new RelayCommand(() => Step++, () => Step < 3);
        BackCommand = new RelayCommand(() => Step--, () => Step > 1);
        OneClickCommand = home.OneClickCleanCommand;
        RunOfficialCommand = home.RunOfficialActionCommand;
        GoListCommand = new RelayCommand(() => { _home.ViewListCommand.Execute(null); RequestClose?.Invoke(); });
        Load();
    }

    /// <summary>请求关闭向导窗口 (由窗口订阅)。</summary>
    public event Action? RequestClose;

    private int _step = 1;
    public int Step
    {
        get => _step;
        set
        {
            var v = Math.Clamp(value, 1, 3);
            if (!SetField(ref _step, v)) return;
            OnPropertyChanged(nameof(IsStep1));
            OnPropertyChanged(nameof(IsStep2));
            OnPropertyChanged(nameof(IsStep3));
            OnPropertyChanged(nameof(StepText));
            NextCommand.RaiseCanExecuteChanged();
            BackCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsStep1 => _step == 1;
    public bool IsStep2 => _step == 2;
    public bool IsStep3 => _step == 3;
    public string StepText => $"第 {_step} / 3 步";

    public RelayCommand NextCommand { get; }
    public RelayCommand BackCommand { get; }
    public System.Windows.Input.ICommand OneClickCommand { get; }      // → 一键清理可放心清理项 (自带确认)
    public System.Windows.Input.ICommand RunOfficialCommand { get; }   // → 执行某条官方手段 (自带确认)
    public RelayCommand GoListCommand { get; }

    // —— 第 1 步: 可放心清理 ——
    public string ReclaimText { get; private set; } = "";
    public string CleanCountText { get; private set; } = "";
    public bool HasCleanable { get; private set; }

    // —— 第 2 步: 最占地但不能删 (迁移到其他盘) ——
    public bool HasBiggestKeep { get; private set; }
    public string KeepName { get; private set; } = "";
    public string KeepSize { get; private set; } = "";
    public string KeepPath { get; private set; } = "";

    // —— 第 3 步: 官方手段 ——
    public IReadOnlyList<OfficialActionViewModel> TopOfficial { get; private set; } = Array.Empty<OfficialActionViewModel>();
    public bool HasOfficial => TopOfficial.Count > 0;

    private void Load()
    {
        var s = _home.Session;
        if (s is not null)
        {
            var reclaim = s.RemainingReclaimable;
            HasCleanable = reclaim > 0;
            ReclaimText = Format.HumanSize(reclaim);
            CleanCountText = $"{Math.Max(0, s.TreeCleanableCount - s.RemovedCount)} 处";

            var keep = s.Report.Items
                .Where(i => !i.IsContainer && i.RiskLevel != RiskLevel.A && i.RiskLevel != RiskLevel.B
                            && i.ExclusiveSize > 0 && !s.IsRemoved(i.Path))
                .OrderByDescending(i => i.ExclusiveSize)
                .FirstOrDefault();
            if (keep is not null)
            {
                HasBiggestKeep = true;
                KeepPath = keep.Path;
                var name = System.IO.Path.GetFileName(keep.Path.TrimEnd('\\'));
                KeepName = string.IsNullOrWhiteSpace(name) ? keep.Path : name;
                KeepSize = Format.HumanSize(keep.ExclusiveSize);
            }
        }
        TopOfficial = _home.ApplicableOfficialActions.Take(3).ToList();
        OnPropertyChanged(nameof(HasOfficial));
    }
}
