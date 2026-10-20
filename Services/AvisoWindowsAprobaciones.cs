using System.Runtime.InteropServices;
using Iderma.Core.Services;
using IdermaBaseDatos.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using WinRT.Interop;

namespace IdermaBaseDatos.Services;

public static class AvisoWindowsAprobaciones
{
    private const string Accion = "action";
    private const string IdSolicitud = "id";
    private static readonly object Puerta = new();
    private static bool _registrado;
    private static DispatcherQueue? _ui;
    private static (string Accion, string Id)? _pendiente;
    private static string? _ultimaClave;
    private static DateTime _ultimaUtc;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    public static void Recibir(AppActivationArguments args)
    {
        if (args.Kind == ExtendedActivationKind.AppNotification
            && args.Data is AppNotificationActivatedEventArgs datos)
        {
            var accion = datos.Arguments.TryGetValue(Accion, out var a) ? a : "ver";
            var id = datos.Arguments.TryGetValue(IdSolicitud, out var i) ? i : string.Empty;
            lock (Puerta)
            {
                _pendiente = (accion, id);
            }
        }

        AplicarPendiente();
    }

    public static void Preparar(DispatcherQueue? ui = null)
    {
        if (ui is not null)
        {
            _ui = ui;
        }

        AplicarPendiente();
        if (_registrado)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.NotificationInvoked += AlInvocar;
            AppNotificationManager.Default.Register();
            _registrado = true;
        }
        catch
        {
            _registrado = false;
        }
    }

    public static void Mostrar(SolicitudAprobacion solicitud)
    {
        if (!NotificationPrefs.AllowBaseDeDatos())
        {
            return;
        }

        try
        {
            Preparar();
            var aviso = new AppNotificationBuilder()
                .AddText(solicitud.TituloAccion)
                .AddText($"{solicitud.Folio} · {solicitud.Resumen}")
                .AddText(solicitud.Detalle)
                .AddArgument(Accion, "ver")
                .AddArgument(IdSolicitud, solicitud.Id)
                .AddButton(new AppNotificationButton("Autorizar")
                    .AddArgument(Accion, "autorizar")
                    .AddArgument(IdSolicitud, solicitud.Id))
                .AddButton(new AppNotificationButton("Denegar")
                    .AddArgument(Accion, "denegar")
                    .AddArgument(IdSolicitud, solicitud.Id))
                .BuildNotification();
            AppNotificationManager.Default.Show(aviso);
        }
        catch
        {
        }
    }

    private static void AlInvocar(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        var accion = args.Arguments.TryGetValue(Accion, out var a) ? a : "ver";
        var id = args.Arguments.TryGetValue(IdSolicitud, out var i) ? i : string.Empty;
        EnUi(() => _ = ProcesarAsync(accion, id));
    }

    private static void AplicarPendiente()
    {
        (string Accion, string Id)? pendiente;
        lock (Puerta)
        {
            pendiente = _pendiente;
            _pendiente = null;
        }

        if (pendiente is not { } pedido)
        {
            return;
        }

        var cola = _ui ?? App.Instance.MainAppWindow?.DispatcherQueue;
        if (cola is null)
        {
            lock (Puerta)
            {
                _pendiente = pedido;
            }

            return;
        }

        if (cola.HasThreadAccess)
        {
            _ = ProcesarAsync(pedido.Accion, pedido.Id);
            return;
        }

        if (!cola.TryEnqueue(() => _ = ProcesarAsync(pedido.Accion, pedido.Id)))
        {
            lock (Puerta)
            {
                _pendiente = pedido;
            }
        }
    }

    private static async Task ProcesarAsync(string accion, string id)
    {
        var clave = accion + "|" + id;
        var ahora = DateTime.UtcNow;
        if (clave == _ultimaClave && (ahora - _ultimaUtc).TotalSeconds < 2)
        {
            TraerAlFrente();
            return;
        }

        _ultimaClave = clave;
        _ultimaUtc = ahora;
        TraerAlFrente();
        App.Instance.MainAppWindow?.IrANotificaciones();

        var solicitud = AprobacionesNube.Items
            .FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (solicitud is not { EstaPendiente: true } || accion is not ("autorizar" or "denegar"))
        {
            return;
        }

        var raiz = App.Instance.MainAppWindow?.Content?.XamlRoot;
        if (raiz is null)
        {
            return;
        }

        try
        {
            if (accion == "autorizar")
            {
                if (await DialogoAprobacion.ConfirmarAutorizarAsync(raiz, solicitud))
                {
                    await AprobacionesNube.AutorizarAsync(solicitud);
                }
            }
            else
            {
                var motivo = await DialogoAprobacion.PedirDenegacionAsync(raiz, solicitud);
                if (string.IsNullOrWhiteSpace(motivo))
                {
                    return;
                }

                await AprobacionesNube.DenegarAsync(solicitud, motivo);
            }
        }
        catch
        {
        }
    }

    private static void TraerAlFrente()
    {
        var ventana = App.Instance.MainAppWindow;
        if (ventana is null)
        {
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(ventana);
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, 9);
        }

        SetForegroundWindow(hwnd);
    }

    private static void EnUi(Action accion)
    {
        var cola = _ui ?? App.Instance.MainAppWindow?.DispatcherQueue;
        if (cola is null || cola.HasThreadAccess)
        {
            accion();
            return;
        }

        cola.TryEnqueue(() => accion());
    }
}
