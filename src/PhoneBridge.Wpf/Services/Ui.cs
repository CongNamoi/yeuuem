using System.Windows;

namespace PhoneBridge.Wpf.Services;

public static class Ui
{
    public static void Post(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d == null) return;
        if (d.CheckAccess()) action(); else d.BeginInvoke(action);
    }
}
