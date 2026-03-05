using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AirFreightRouter.ViewModels;

namespace AirFreightRouter.Avalonia;

public partial class MainWindow : Window
{
    private DispatcherTimer? _spinnerTimer;
    private double           _spinnerAngle;

    public MainWindow()
    {
        InitializeComponent();

        // DataContext is set after InitializeComponent so TopLevel is available.
        var vm = new MainViewModel(this);
        DataContext = vm;

        vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    // ------------------------------------------------------------------
    // Title bar interaction
    // ------------------------------------------------------------------

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void TitleBar_DoubleTapped(object? sender, TappedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    // ------------------------------------------------------------------
    // System buttons
    // ------------------------------------------------------------------

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AboutButton_Click(object? sender, RoutedEventArgs e)
    {
        var about = new Views.AboutWindow();
        about.ShowDialog(this);
    }

    // ------------------------------------------------------------------
    // Spinner animation (driven by IsComputing)
    // ------------------------------------------------------------------

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsComputing))
        {
            var isComputing = (DataContext as MainViewModel)?.IsComputing ?? false;
            if (isComputing)
                StartSpinner();
            else
                StopSpinner();
        }
    }

    private void StartSpinner()
    {
        _spinnerAngle = 0;
        // 360° / 0.9s ≈ 400°/s; at 60fps = 16ms per tick → 6.67°/tick
        _spinnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _spinnerTimer.Tick += (_, _) =>
        {
            _spinnerAngle = (_spinnerAngle + 6.67) % 360;
            if (SpinnerEllipse.RenderTransform is RotateTransform rt)
                rt.Angle = _spinnerAngle;
        };
        _spinnerTimer.Start();
    }

    private void StopSpinner()
    {
        _spinnerTimer?.Stop();
        _spinnerTimer = null;
    }
}
