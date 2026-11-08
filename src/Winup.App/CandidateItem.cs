using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Winup.Core;

namespace Winup.App;

/// <summary>One row of the list: a candidate from the check, plus its checkbox.</summary>
public sealed class CandidateItem(Candidate candidate, Action changed) : INotifyPropertyChanged
{
    private bool _isChecked;

    public Candidate Candidate { get; } = candidate;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            var v = value && IsEnabled;
            if (_isChecked == v) return;
            _isChecked = v;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            changed();
        }
    }

    public bool IsEnabled => Candidate.Selectable;
    public double RowOpacity => IsEnabled ? 1.0 : 0.6;
    public string Label => Candidate.Rule.LocalizedLabel;
    public string Note => Candidate.Rule.LocalizedNote ?? "";
    public string Size => Candidate.Bytes > 0 ? Format.Bytes(Candidate.Bytes) : "—";
    public string LevelText => Candidate.Level.Word();

    public string Blocked =>
        Candidate.NeedsAdmin ? Loc.T("needs administrator rights") :
        Candidate.OpenApps.Count > 0 ? Loc.F("Close {0} first", string.Join(", ", Candidate.OpenApps)) :
        Candidate.Rule.Manual ? Loc.T("only if you pick it") : "";

    public Visibility BlockedVisibility => Blocked.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Brush LevelForeground => Res(Candidate.Level switch
    {
        Level.Safe => "SystemFillColorSuccessBrush",
        Level.Standard => "SystemFillColorCautionBrush",
        _ => "SystemFillColorCriticalBrush",
    });

    public Brush LevelBackground => Res(Candidate.Level switch
    {
        Level.Safe => "SystemFillColorSuccessBackgroundBrush",
        Level.Standard => "SystemFillColorCautionBackgroundBrush",
        _ => "SystemFillColorCriticalBackgroundBrush",
    });

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}
