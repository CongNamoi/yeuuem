using CommunityToolkit.Mvvm.ComponentModel;
using PhoneBridge.Wpf.Services;

namespace PhoneBridge.Wpf.ViewModels;

public abstract class PlaceholderViewModel : ObservableObject
{
    protected PlaceholderViewModel(string title, string phase, string description)
    {
        Title = title; Phase = phase; Description = description;
    }
    public string Title { get; }
    public string Phase { get; }
    public string Description { get; }
}

public sealed class GamesViewModel : PlaceholderViewModel
{
    public GamesViewModel() : base("Games", "Planned: Phase 7",
        "Per-game profiles (FPS/TPS, MOBA, Action/RPG) and the profile library will live here.") { }
}

public sealed class KeymappingViewModel : PlaceholderViewModel
{
    public KeymappingViewModel() : base("Keymapping", "Planned: Phases 3-7",
        "Input visualizer, mapping engine and the transparent drag-and-drop overlay editor will live here.") { }
}

public sealed class HomeViewModel : ObservableObject
{
    public HomeViewModel(SessionService session)
    {
        Session = session;
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionService.IsConnected) or nameof(SessionService.SelectedDevice))
                OnPropertyChanged(nameof(ViewportMessage));
        };
    }

    public SessionService Session { get; }

    public string ViewportMessage => Session.IsConnected
        ? $"Connected to {Session.ConnectedDevice?.DisplayName}.\nOpen Game Window to mirror the phone."
        : "No device connected.\nOpen Devices, select your phone, then press Connect.";
}
