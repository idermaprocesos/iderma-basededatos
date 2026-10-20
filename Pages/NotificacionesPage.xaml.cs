using IdermaBaseDatos.Models;
using IdermaBaseDatos.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IdermaBaseDatos.Pages;

public sealed partial class NotificacionesPage : Page
{
    private readonly List<string> _estados = ["Todas", "Pendiente", "Autorizada", "Denegada"];

    public NotificacionesPage()
    {
        InitializeComponent();
        ComboEstado.ItemsSource = _estados;
        ComboEstado.SelectedIndex = 0;
        AprobacionesNube.Cambiado += Refrescar;
        Loaded += (_, _) =>
        {
            AprobacionesNube.Iniciar(DispatcherQueue);
            Refrescar();
        };
        Unloaded += (_, _) => AprobacionesNube.Cambiado -= Refrescar;
    }

    private void Refrescar()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(Refrescar);
            return;
        }

        var filtro = CajaBusqueda.Text?.Trim() ?? string.Empty;
        var estado = ComboEstado.SelectedItem as string ?? "Todas";
        var vista = AprobacionesNube.Items
            .Where(s => estado == "Todas" || s.EstadoTexto.Equals(estado, StringComparison.OrdinalIgnoreCase))
            .Where(s => string.IsNullOrWhiteSpace(filtro) ||
                        Contiene(s.Folio, filtro) ||
                        Contiene(s.Solicitante, filtro) ||
                        Contiene(s.Usuario, filtro) ||
                        Contiene(s.Detalle, filtro) ||
                        Contiene(s.MotivoDenegacion, filtro))
            .ToList();

        ListaNotificaciones.ItemsSource = null;
        ListaNotificaciones.ItemsSource = vista;
        TextoVacio.Visibility = vista.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var pendientes = AprobacionesNube.Pendientes;
        TextoConteo.Text = pendientes == 0
            ? $"{AprobacionesNube.Items.Count} notificaciones · {AprobacionesNube.Estado}"
            : $"{AprobacionesNube.Items.Count} notificaciones · {pendientes} pendientes · {AprobacionesNube.Estado}";
    }

    private async void Imprimir_Click(object sender, RoutedEventArgs e)
    {
        var eleccion = await PedirImpresionAsync();
        if (eleccion is null)
        {
            return;
        }

        var (inicio, fin, etiquetaRango, estados, etiquetaEstado) = eleccion.Value;
        var lista = AprobacionesNube.Items
            .Where(s => estados.Contains(s.Estado))
            .Where(s =>
            {
                var fecha = (s.FechaRespuesta ?? s.Fecha).LocalDateTime;
                return fecha >= inicio && fecha < fin;
            })
            .OrderByDescending(s => s.FechaRespuesta ?? s.Fecha)
            .ToList();
        if (lista.Count == 0)
        {
            Mostrar("No hay autorizaciones " + etiquetaEstado.ToLowerInvariant() + " en " + etiquetaRango.ToLowerInvariant() + ".", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            await ImpresionAutorizaciones.AbrirAsync(lista, etiquetaRango, etiquetaEstado);
        }
        catch (Exception ex)
        {
            Mostrar("No se pudo preparar la impresión: " + ex.Message, InfoBarSeverity.Error);
        }
    }

    private async Task<(DateTime Inicio, DateTime Fin, string Rango, HashSet<EstadoAprobacion> Estados, string Estado)?> PedirImpresionAsync()
    {
        var estados = new ComboBox { Header = "Autorizaciones", HorizontalAlignment = HorizontalAlignment.Stretch };
        estados.Items.Add("Autorizadas y denegadas");
        estados.Items.Add("Solo autorizadas");
        estados.Items.Add("Solo denegadas");
        estados.SelectedIndex = 0;

        var periodos = new ComboBox { Header = "Rango de tiempo", HorizontalAlignment = HorizontalAlignment.Stretch };
        periodos.Items.Add("Hoy");
        periodos.Items.Add("Últimos 7 días");
        periodos.Items.Add("Este mes");
        periodos.Items.Add("Este año");
        periodos.Items.Add("Personalizado");
        periodos.SelectedIndex = 2;

        var hoy = DateTimeOffset.Now;
        var desde = new CalendarDatePicker
        {
            Header = "Desde",
            Date = new DateTimeOffset(new DateTime(hoy.Year, hoy.Month, 1)),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var hasta = new CalendarDatePicker
        {
            Header = "Hasta",
            Date = hoy,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var fechas = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        fechas.Children.Add(desde);
        fechas.Children.Add(hasta);
        periodos.SelectionChanged += (_, _) =>
            fechas.Visibility = periodos.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;

        var panel = new StackPanel { Spacing = 12, MinWidth = 320 };
        panel.Children.Add(estados);
        panel.Children.Add(periodos);
        panel.Children.Add(fechas);
        var dialogo = new ContentDialog
        {
            Title = "Imprimir autorizaciones",
            Content = panel,
            PrimaryButtonText = "Imprimir",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialogo.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        var inicioDia = DateTime.Today;
        DateTime inicio;
        DateTime fin;
        string etiqueta;
        if (periodos.SelectedIndex == 4)
        {
            if (desde.Date is null || hasta.Date is null)
            {
                Mostrar("Elige la fecha de inicio y la de fin.", InfoBarSeverity.Warning);
                return null;
            }

            inicio = desde.Date.Value.LocalDateTime.Date;
            var finDia = hasta.Date.Value.LocalDateTime.Date;
            if (finDia < inicio)
            {
                (inicio, finDia) = (finDia, inicio);
            }

            fin = finDia.AddDays(1);
            etiqueta = inicio.ToString("dd/MM/yyyy") + " – " + finDia.ToString("dd/MM/yyyy");
        }
        else
        {
            (inicio, fin, etiqueta) = periodos.SelectedIndex switch
            {
                0 => (inicioDia, inicioDia.AddDays(1), "Hoy"),
                1 => (inicioDia.AddDays(-6), inicioDia.AddDays(1), "Últimos 7 días"),
                3 => (new DateTime(inicioDia.Year, 1, 1), new DateTime(inicioDia.Year + 1, 1, 1), "Este año"),
                _ => (new DateTime(inicioDia.Year, inicioDia.Month, 1), new DateTime(inicioDia.Year, inicioDia.Month, 1).AddMonths(1), "Este mes")
            };
        }

        HashSet<EstadoAprobacion> filtro = estados.SelectedIndex switch
        {
            1 => [EstadoAprobacion.Autorizada],
            2 => [EstadoAprobacion.Denegada],
            _ => [EstadoAprobacion.Autorizada, EstadoAprobacion.Denegada]
        };
        var nombreEstado = estados.SelectedItem as string ?? "Autorizadas y denegadas";
        return (inicio, fin, etiqueta, filtro, nombreEstado);
    }

    private void CajaBusqueda_TextChanged(object sender, TextChangedEventArgs e) => Refrescar();

    private void Filtro_Changed(object sender, SelectionChangedEventArgs e) => Refrescar();

    private async void Autorizar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SolicitudAprobacion solicitud })
        {
            await ResponderAsync(solicitud, autorizar: true);
        }
    }

    private async void Denegar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SolicitudAprobacion solicitud })
        {
            await ResponderAsync(solicitud, autorizar: false);
        }
    }

    private async Task ResponderAsync(SolicitudAprobacion solicitud, bool autorizar)
    {
        try
        {
            if (autorizar)
            {
                if (!await DialogoAprobacion.ConfirmarAutorizarAsync(XamlRoot, solicitud))
                {
                    return;
                }

                await AprobacionesNube.AutorizarAsync(solicitud);
                Mostrar("Se autorizó. Logística aplica el borrado cuando reciba la respuesta.", InfoBarSeverity.Success);
            }
            else
            {
                var motivo = await DialogoAprobacion.PedirDenegacionAsync(XamlRoot, solicitud);
                if (motivo is null)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(motivo))
                {
                    Mostrar("Escribe el motivo de la denegación.", InfoBarSeverity.Warning);
                    return;
                }

                await AprobacionesNube.DenegarAsync(solicitud, motivo);
                Mostrar("Se denegó la solicitud. Los productos siguen en el inventario.", InfoBarSeverity.Informational);
            }

            Refrescar();
        }
        catch (Exception ex)
        {
            Mostrar(ex.Message, InfoBarSeverity.Error);
        }
    }

    private static bool Contiene(string? texto, string filtro) =>
        !string.IsNullOrWhiteSpace(texto) &&
        texto.Contains(filtro, StringComparison.CurrentCultureIgnoreCase);

    private void Mostrar(string mensaje, InfoBarSeverity severidad)
    {
        BarraEstado.Title = severidad == InfoBarSeverity.Error ? "Error" : "Notificaciones";
        BarraEstado.Message = mensaje;
        BarraEstado.Severity = severidad;
        BarraEstado.IsOpen = true;
    }
}
