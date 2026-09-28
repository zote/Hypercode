using Avalonia.Controls;
using Avalonia.Interactivity;
using Hypertree.Services;
using Hypertree.ViewModels;

namespace Hypertree.Views;

/// <summary>
/// Escolha entre merge e rebase para trazer a base para dentro da branch. Devolve a
/// estratégia escolhida, ou null se cancelado.
/// </summary>
public partial class UpdateFromBaseWindow : Window
{
    public UpdateFromBaseWindow()
    {
        InitializeComponent();
    }

    public UpdateFromBaseWindow(WorktreeRow row, BaseDistance distance) : this()
    {
        var commits = distance.Behind == 1 ? "1 commit" : $"{distance.Behind} commits";

        SetText("HeadlineText", $"Trazer {distance.Ref} para dentro de {row.Branch}?");
        SetText("DetailText",
            $"base:   {distance.Ref}  ({commits} que a branch não tem)\n"
            + $"branch: {row.Branch}  ({distance.Ahead} commit(s) próprios)\n"
            + $"pasta:  {row.FullPath}");
        SetText("MergeText",
            $"git merge {distance.Ref} — preserva o histórico e não reescreve nada já pushado. Gera um commit de merge.");
        SetText("RebaseText",
            $"git rebase {distance.Ref} — histórico linear, mas reescreve os commits já publicados: o push depois exige --force-with-lease.");
    }

    private void SetText(string controlName, string value)
    {
        if (this.FindControl<TextBlock>(controlName) is { } textBlock) textBlock.Text = value;
    }

    private void OnMerge(object? sender, RoutedEventArgs e) => Close(BaseUpdateStrategy.Merge);

    private void OnRebase(object? sender, RoutedEventArgs e) => Close(BaseUpdateStrategy.Rebase);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
