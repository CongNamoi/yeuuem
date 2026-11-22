using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using PhoneBridge.Wpf.Streaming;
using TouchPoint = PhoneBridge.Wpf.Streaming.TouchPoint;

namespace PhoneBridge.Wpf;

public partial class GameWindow
{
    private sealed record BindingPoint(double X, double Y)
    {
        public bool IsValid => X is >= 0 and <= 1 && Y is >= 0 and <= 1;
    }

    private static readonly string MappingPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhoneBridge", "keymapping.json");

    private readonly Dictionary<Key, BindingPoint> _bindings = [];
    private readonly Dictionary<Key, BindingPoint> _held = [];
    private RemoteTouch? _touch;
    private BindingPoint? _mouse;
    private BindingPoint? _editPoint;
    private long _nextMove;
    private bool _editing;

    private Rect VideoBounds()
    {
        if (Video.Source is not BitmapSource frame || Surface.ActualWidth <= 0 || Surface.ActualHeight <= 0)
            return Rect.Empty;
        var scale = Math.Min(Surface.ActualWidth / frame.Width, Surface.ActualHeight / frame.Height);
        var width = frame.Width * scale;
        var height = frame.Height * scale;
        return new Rect((Surface.ActualWidth - width) / 2, (Surface.ActualHeight - height) / 2, width, height);
    }

    private BindingPoint? MapMouse(MouseEventArgs e, bool clamp = false)
    {
        var bounds = VideoBounds();
        var p = e.GetPosition(Surface);
        if (bounds.IsEmpty || (!clamp && !bounds.Contains(p))) return null;
        return new BindingPoint(Math.Clamp((p.X - bounds.X) / bounds.Width, 0, 1),
            Math.Clamp((p.Y - bounds.Y) / bounds.Height, 0, 1));
    }

    private bool SendTouch(RemoteTouchAction action, long id, BindingPoint point)
    {
        if (Video.Source is not BitmapSource frame || _touch == null) return false;
        return _touch.Send(action, id, new TouchPoint(
            Math.Min((int)(point.X * frame.PixelWidth), frame.PixelWidth - 1),
            Math.Min((int)(point.Y * frame.PixelHeight), frame.PixelHeight - 1),
            (ushort)frame.PixelWidth, (ushort)frame.PixelHeight));
    }

    private void OnTouchDown(object sender, MouseButtonEventArgs e)
    {
        if (_closing || _streamStop is not { IsCancellationRequested: false } ||
            MapMouse(e) is not { } point) return;
        Surface.Focus();
        e.Handled = true;
        if (_editing)
        {
            _editPoint = point;
            MappingStatus.Text = "Press a letter or number for this spot.";
            RedrawMarkers();
            return;
        }

        // scrcpy 3.3.4 supports up to ten simultaneous pointer slots.
        if (_mouse != null || _held.Count >= 10 || !Surface.CaptureMouse()) return;
        if (SendTouch(RemoteTouchAction.Down, 0, point))
        {
            _mouse = point;
            _nextMove = 0;
        }
        else Surface.ReleaseMouseCapture();
    }

    private void OnTouchMove(object sender, MouseEventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        if (_mouse == null || now < _nextMove || MapMouse(e, clamp: true) is not { } point) return;
        _nextMove = now + Stopwatch.Frequency / 90;
        if (SendTouch(RemoteTouchAction.Move, 0, point)) _mouse = point;
    }

    private void OnTouchUp(object sender, MouseButtonEventArgs e)
    {
        if (_mouse == null) return;
        ReleaseMouseTouch(MapMouse(e, clamp: true));
        e.Handled = true;
    }

    private void OnTouchCaptureLost(object sender, MouseEventArgs e) => ReleaseMouseTouch();

    private void ReleaseMouseTouch(BindingPoint? end = null)
    {
        if (_mouse is not { } last) return;
        _mouse = null;
        SendTouch(RemoteTouchAction.Up, 0, end ?? last);
        if (Surface.IsMouseCaptured) Surface.ReleaseMouseCapture();
    }

    private void ReleaseContacts()
    {
        ReleaseMouseTouch();
        foreach (var (key, point) in _held)
            SendTouch(RemoteTouchAction.Up, (long)key, point);
        _held.Clear();
    }

    private void OnSurfaceFocusLost(object sender, KeyboardFocusChangedEventArgs e) => ReleaseContacts();

    private void OnDeactivated(object? sender, EventArgs e) => ReleaseContacts();

    private static bool IsMappingKey(Key key) => key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9;

    private void HandleMappingKey(KeyEventArgs e)
    {
        if (!Surface.IsKeyboardFocusWithin || !IsMappingKey(e.Key)) return;
        if (_editing)
        {
            if (_editPoint is not { } point || e.IsRepeat) return;
            _bindings[e.Key] = point;
            _editPoint = null;
            SaveMappings();
            UpdateMappingList(e.Key);
            RedrawMarkers();
            e.Handled = true;
            return;
        }

        if (_streamStop is not { IsCancellationRequested: false } ||
            !_bindings.TryGetValue(e.Key, out var binding)) return;
        e.Handled = true;
        if (e.IsRepeat || _held.ContainsKey(e.Key) || _held.Count + (_mouse == null ? 0 : 1) >= 10) return;
        if (SendTouch(RemoteTouchAction.Down, (long)e.Key, binding)) _held.Add(e.Key, binding);
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (!_held.Remove(e.Key, out var point)) return;
        SendTouch(RemoteTouchAction.Up, (long)e.Key, point);
        e.Handled = true;
    }

    private void MappingClick(object sender, RoutedEventArgs e)
    {
        SetMappingEditor(!_editing);
        Surface.Focus();
    }

    private void SetMappingEditor(bool open)
    {
        ReleaseContacts();
        _editing = open;
        _editPoint = null;
        MappingPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        MappingButton.Content = open ? "Done Editing" : "Keymapping Editor";
        MappingStatus.Text = "";
        RedrawMarkers();
    }

    private void RemoveMappingClick(object sender, RoutedEventArgs e)
    {
        if (MappingKeys.SelectedItem is not Key key || !_bindings.Remove(key)) return;
        SaveMappings();
        UpdateMappingList();
        RedrawMarkers();
        Surface.Focus();
    }

    private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs e) => RedrawMarkers();

    private void RedrawMarkers()
    {
        if (Markers == null) return;
        Markers.Children.Clear();
        if (!_editing || VideoBounds() is not { IsEmpty: false } bounds) return;
        foreach (var (key, point) in _bindings)
            DrawMarker(bounds, point, key is >= Key.D0 and <= Key.D9 ? ((int)key - (int)Key.D0).ToString() : key.ToString());
        if (_editPoint is { } preview) DrawMarker(bounds, preview, "+");
    }

    private void DrawMarker(Rect bounds, BindingPoint point, string text)
    {
        var marker = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = Brushes.RoyalBlue,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = text, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        Canvas.SetLeft(marker, bounds.X + point.X * bounds.Width - marker.Width / 2);
        Canvas.SetTop(marker, bounds.Y + point.Y * bounds.Height - marker.Height / 2);
        Markers.Children.Add(marker);
    }

    private void UpdateMappingList(Key? selected = null)
    {
        MappingKeys.ItemsSource = _bindings.Keys.OrderBy(key => key).ToArray();
        if (selected is { } key) MappingKeys.SelectedItem = key;
        else MappingKeys.SelectedIndex = 0;
    }

    private void LoadMappings()
    {
        try
        {
            if (File.Exists(MappingPath))
            {
                var bindings = JsonSerializer.Deserialize<Dictionary<Key, BindingPoint>>(File.ReadAllText(MappingPath));
                if (bindings != null)
                    foreach (var (key, point) in bindings)
                        if (IsMappingKey(key) && point is { IsValid: true }) _bindings.Add(key, point);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not load key mappings");
        }
        UpdateMappingList();
    }

    private void SaveMappings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MappingPath)!);
            var temporaryPath = MappingPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_bindings));
            File.Move(temporaryPath, MappingPath, overwrite: true);
            MappingStatus.Text = "Bindings saved.";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not save key mappings");
            MappingStatus.Text = "Could not save bindings. See Diagnostics.";
        }
    }
}
