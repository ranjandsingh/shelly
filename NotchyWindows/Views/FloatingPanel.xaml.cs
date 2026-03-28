using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using NotchyWindows.Interop;

namespace NotchyWindows.Views;

public partial class FloatingPanel : Window
{
    public FloatingPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionTopCenter();
        WindowHelper.MakeNonActivating(this);
    }

    public void ShowPanel()
    {
        PositionTopCenter();
        Show();
        Activate();
    }

    public void HidePanel()
    {
        Hide();
    }

    private void PositionTopCenter()
    {
        var screen = SystemParameters.WorkArea;
        Left = (screen.Width - Width) / 2;
        Top = 0;
    }

    private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        // TODO: Auto-hide when not pinned
    }
}
