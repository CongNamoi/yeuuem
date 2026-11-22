using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.Logging;
using PhoneBridge.Wpf.Streaming;
using PhoneBridge.Wpf.ViewModels;

namespace PhoneBridge.Wpf;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ScrcpyMirror _mirror;
    private readonly ILogger<GameWindow> _gameLog;
    private GameWindow? _game;
    private bool _closeAfterGame;

    public MainWindow(MainViewModel viewModel, ScrcpyMirror mirror, ILogger<GameWindow> gameLog)
    {
        _viewModel = viewModel;
        _mirror = mirror;
        _gameLog = gameLog;
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OpenGameClick(object sender, RoutedEventArgs e)
    {
        if (_game != null)
        {
            if (_game.WindowState == WindowState.Minimized) _game.WindowState = WindowState.Normal;
            _game.Activate();
            return;
        }
        if (_viewModel.Session.ConnectedDevice is not { } device) return;
        if (device.IsWireless)
        {
            _viewModel.Session.Message = "Connect the phone using USB for this mirror.";
            return;
        }

        _game = new GameWindow(device, _mirror, _viewModel.Session, _gameLog);
        _game.Closed += (_, _) =>
        {
            _game = null;
            if (_closeAfterGame) _ = Dispatcher.InvokeAsync(Close);
            else
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
            }
        };
        _game.Show();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_game != null)
        {
            e.Cancel = true;
            _closeAfterGame = true;
            _game.Close();
        }
        base.OnClosing(e);
    }
}
