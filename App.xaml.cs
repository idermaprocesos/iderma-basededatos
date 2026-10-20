using IdermaBaseDatos.Services;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace IdermaBaseDatos;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    public static App Instance => (App)Current;

    public MainWindow? MainAppWindow => _window as MainWindow;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        _window = window;
        window.Activate();
        window.Maximize();
        try
        {
            var activacion = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activacion.Kind == ExtendedActivationKind.AppNotification)
            {
                AvisoWindowsAprobaciones.Recibir(activacion);
            }
        }
        catch
        {
        }
    }
}
