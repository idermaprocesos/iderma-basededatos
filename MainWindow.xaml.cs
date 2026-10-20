using IdermaBaseDatos.Pages;
using IdermaBaseDatos.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace IdermaBaseDatos;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Iderma.Shell.BotonLauncher.Colocar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        var icono = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icono))
        {
            AppWindow.SetIcon(icono);
        }

        if (AppWindow.Presenter is OverlappedPresenter presentador)
        {
            presentador.PreferredMinimumWidth = 1100;
            presentador.PreferredMinimumHeight = 700;
        }

        NavFrame.Navigate(typeof(InicioPage));
        AprobacionesNube.NuevaPendiente += solicitud =>
            DispatcherQueue.TryEnqueue(() =>
            {
                ActualizarBadge();
                AvisoWindowsAprobaciones.Mostrar(solicitud);
            });
        AprobacionesNube.Cambiado += () => DispatcherQueue.TryEnqueue(ActualizarBadge);
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            AvisoWindowsAprobaciones.Preparar(DispatcherQueue);
            AprobacionesNube.Iniciar(DispatcherQueue);
            ActualizarBadge();
        });
    }

    public void Maximize()
    {
        if (AppWindow.Presenter is OverlappedPresenter presentador)
        {
            presentador.Maximize();
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (AppWindow.Presenter is OverlappedPresenter presentador)
            {
                presentador.Maximize();
            }
        });
    }

    public void IrANotificaciones() => Seleccionar("notificaciones");

    private void ActualizarBadge()
    {
        var pendientes = AprobacionesNube.Pendientes;
        ItemNotificaciones.InfoBadge = pendientes > 0
            ? new InfoBadge { Value = pendientes }
            : null;
    }

    private void Seleccionar(string tag)
    {
        foreach (var item in NavView.MenuItems)
        {
            if (item is NavigationViewItem vista &&
                string.Equals(vista.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                NavView.SelectedItem = vista;
                return;
            }
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        if (NavFrame.CanGoBack)
        {
            NavFrame.GoBack();
        }
    }

    private void NavFrame_Navigated(object sender, NavigationEventArgs e)
    {
        AppTitleBar.IsBackButtonVisible = NavFrame.CanGoBack;
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        var pagina = (item.Tag as string) switch
        {
            "notificaciones" => typeof(NotificacionesPage),
            _ => typeof(InicioPage)
        };
        if (NavFrame.Content?.GetType() != pagina)
        {
            NavFrame.Navigate(pagina);
            NavFrame.BackStack.Clear();
        }
    }
}
