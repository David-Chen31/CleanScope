using System.Collections.ObjectModel;
using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.Mvvm;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>
/// 资源管理器树视图 (P1): 把整盘当目录树浏览——可展开/折叠、显大小与占比、按大小排序、标来源/用途/可清理。
/// 回答用户最核心的诉求"整个 C 盘各文件占多大、各是什么", 不再是 200 项上限的扁平清单。
/// </summary>
public sealed class ExplorerViewModel : ViewModelBase
{
    public ObservableCollection<ExplorerNodeViewModel> Roots { get; } = new();

    private string _summary = "扫描后在此像目录树一样浏览整个磁盘。";
    public string Summary { get => _summary; private set => SetField(ref _summary, value); }

    public void Load(ScanSession session)
    {
        Roots.Clear();
        if (session.Tree is null)
        {
            Summary = "本次扫描未生成目录树。";
            return;
        }

        var root = new ExplorerNodeViewModel(session.Tree, session.Tree.Size) { IsExpanded = true };
        Roots.Add(root);
        Summary = $"{session.TargetPath} — 共 {Format.HumanSize(session.Tree.Size)}；" +
                  "点击 ▸ 展开目录，旁边是大小与占比，按大小排序。";
    }
}
