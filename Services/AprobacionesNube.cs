using System.Collections.ObjectModel;
using System.Globalization;
using Google.Cloud.Firestore;
using Iderma.Core.Services;
using IdermaBaseDatos.Models;
using Microsoft.UI.Dispatching;

namespace IdermaBaseDatos.Services;

public static class AprobacionesNube
{
    public const string Coleccion = "aprobaciones-datos";

    private static FirestoreDb? _base;
    private static FirestoreChangeListener? _escucha;
    private static int _iniciado;
    private static DispatcherQueue? _ui;
    private static readonly HashSet<string> Vistas = new(StringComparer.OrdinalIgnoreCase);
    private static string _estado = "Notificaciones de autorizaciones";
    private static bool _escuchaInicial = true;

    public static ObservableCollection<SolicitudAprobacion> Items { get; } = [];

    public static string Estado => _estado;

    public static int Pendientes => Items.Count(s => s.EstaPendiente);

    public static event Action? Cambiado;

    public static event Action<SolicitudAprobacion>? NuevaPendiente;

    public static void Iniciar(DispatcherQueue? ui = null)
    {
        if (ui is not null)
        {
            _ui = ui;
        }

        if (Interlocked.Exchange(ref _iniciado, 1) == 1)
        {
            return;
        }

        CargarLocal();
        _ = ConectarAsync();
    }

    public static async Task AutorizarAsync(SolicitudAprobacion solicitud)
    {
        await ResponderAsync(solicitud, EstadoAprobacion.Autorizada, string.Empty);
    }

    public static async Task DenegarAsync(SolicitudAprobacion solicitud, string motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new InvalidOperationException("Escribe el motivo de la denegación.");
        }

        await ResponderAsync(solicitud, EstadoAprobacion.Denegada, motivo.Trim());
    }

    private static void CargarLocal()
    {
        var locales = AprobacionesLocales.Cargar();
        Items.Clear();
        Vistas.Clear();
        foreach (var item in locales)
        {
            Items.Add(item);
            Vistas.Add(item.Id);
        }

        CambiarEstado(locales.Count == 0
            ? "Notificaciones locales"
            : $"{locales.Count} en caché local · conectando…");
        Cambiado?.Invoke();
    }

    private static async Task ResponderAsync(SolicitudAprobacion solicitud, EstadoAprobacion estado, string motivo)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        if (!solicitud.EstaPendiente)
        {
            throw new InvalidOperationException("Esa solicitud ya fue respondida.");
        }

        await AsegurarBaseAsync();
        if (_base is null)
        {
            throw new InvalidOperationException("No hay conexión con Firebase.");
        }

        var ahora = DateTimeOffset.Now;
        var quien = NombreDeSesion();
        var doc = _base.Collection(Coleccion).Document(solicitud.Id);
        await _base.RunTransactionAsync(async transaccion =>
        {
            var actual = await transaccion.GetSnapshotAsync(doc);
            var estadoActual = actual.Exists ? Texto(actual.ToDictionary(), "Estado") : string.Empty;
            if (!estadoActual.Equals("Pendiente", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Esa solicitud ya fue respondida.");
            }

            transaccion.Update(doc, new Dictionary<string, object>
            {
                ["Estado"] = estado.ToString(),
                ["Motivo denegación"] = motivo,
                ["Respondido por"] = quien,
                ["Fecha respuesta"] = ahora.ToString("o", CultureInfo.InvariantCulture),
                ["Actualizado"] = FieldValue.ServerTimestamp
            });
        });

        await EnUiAsync(() =>
        {
            solicitud.Estado = estado;
            solicitud.MotivoDenegacion = motivo;
            solicitud.RespondidoPor = quien;
            solicitud.FechaRespuesta = ahora;
            Ordenar();
            AprobacionesLocales.Guardar(Items);
            Cambiado?.Invoke();
        });
    }

    private static string NombreDeSesion()
    {
        try
        {
            using var sesiones = new SessionStore();
            var sesion = sesiones.Snapshot();
            if (!string.IsNullOrWhiteSpace(sesion.DisplayName))
            {
                return sesion.DisplayName;
            }

            if (!string.IsNullOrWhiteSpace(sesion.Username))
            {
                return sesion.Username;
            }
        }
        catch
        {
        }

        return "Base de datos";
    }

    private static async Task ConectarAsync()
    {
        try
        {
            CambiarEstado(Items.Count == 0
                ? "Conectando notificaciones…"
                : $"{Items.Count} en caché local · sincronizando…");
            await AsegurarBaseAsync();
            if (_base is null)
            {
                CambiarEstado(Items.Count == 0
                    ? "Notificaciones: sin credenciales de Firebase."
                    : $"{Items.Count} en caché local (sin Firebase).");
                return;
            }

            var guardado = CursorNube.Cargar(Coleccion);
            _escuchaInicial = !guardado.HasValue;
            var desde = guardado ?? CursorNube.Epoca;
            _escucha = _base.Collection(Coleccion)
                .WhereGreaterThan("Actualizado", desde)
                .OrderBy("Actualizado")
                .Listen(snapshot =>
                {
                    AlRecibir(snapshot);
                    CursorNube.GuardarSiAvanza(Coleccion, snapshot.ReadTime);
                });
        }
        catch (Exception ex)
        {
            CambiarEstado(Items.Count == 0
                ? "Notificaciones: " + ex.Message
                : $"{Items.Count} en caché local · {ex.Message}");
        }
    }

    private static void AlRecibir(QuerySnapshot snapshot)
    {
        try
        {
            var recibidas = new List<(SolicitudAprobacion Solicitud, bool EsNuevaDoc)>();
            var eliminadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cambio in snapshot.Changes)
            {
                if (cambio.ChangeType == DocumentChange.Type.Removed)
                {
                    eliminadas.Add(cambio.Document.Id);
                    continue;
                }

                var solicitud = Leer(cambio.Document);
                if (solicitud is null)
                {
                    continue;
                }

                recibidas.Add((solicitud, cambio.ChangeType == DocumentChange.Type.Added));
            }

            if (recibidas.Count == 0 && eliminadas.Count == 0)
            {
                _escuchaInicial = false;
                return;
            }

            EnUi(() =>
            {
                var inicial = _escuchaInicial;
                _escuchaInicial = false;
                foreach (var id in eliminadas)
                {
                    var existente = Items.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                    if (existente is not null)
                    {
                        Items.Remove(existente);
                        Vistas.Remove(id);
                    }
                }

                foreach (var (llegada, esNuevaDoc) in recibidas)
                {
                    var actual = Items.FirstOrDefault(s => s.Id.Equals(llegada.Id, StringComparison.OrdinalIgnoreCase));
                    var esNueva = !Vistas.Contains(llegada.Id);
                    if (actual is null)
                    {
                        Items.Insert(0, llegada);
                    }
                    else
                    {
                        actual.Tipo = llegada.Tipo;
                        actual.Origen = llegada.Origen;
                        actual.Folio = llegada.Folio;
                        actual.Solicitante = llegada.Solicitante;
                        actual.Usuario = llegada.Usuario;
                        actual.Fecha = llegada.Fecha;
                        actual.TotalInventario = llegada.TotalInventario;
                        actual.Cantidad = llegada.Cantidad;
                        actual.Porcentaje = llegada.Porcentaje;
                        actual.Productos = llegada.Productos;
                        actual.Estado = llegada.Estado;
                        actual.MotivoDenegacion = llegada.MotivoDenegacion;
                        actual.RespondidoPor = llegada.RespondidoPor;
                        actual.FechaRespuesta = llegada.FechaRespuesta;
                        actual.Aplicada = llegada.Aplicada;
                    }

                    Vistas.Add(llegada.Id);
                    if (!inicial && (esNueva || esNuevaDoc) && llegada.EstaPendiente)
                    {
                        NuevaPendiente?.Invoke(llegada);
                    }
                }

                Ordenar();
                AprobacionesLocales.Guardar(Items);
                CambiarEstado(Pendientes == 0
                    ? $"Notificaciones al día · {Items.Count} en local"
                    : $"{Pendientes} pendiente{(Pendientes == 1 ? "" : "s")} · {Items.Count} en local");
                Cambiado?.Invoke();
            });
        }
        catch
        {
        }
    }

    private static void Ordenar()
    {
        var ordenadas = Items
            .OrderByDescending(s => s.EstaPendiente)
            .ThenByDescending(s => s.Fecha)
            .ToList();
        Items.Clear();
        foreach (var item in ordenadas)
        {
            Items.Add(item);
        }
    }

    private static SolicitudAprobacion? Leer(DocumentSnapshot doc)
    {
        if (!doc.Exists)
        {
            return null;
        }

        var datos = doc.ToDictionary();
        var productos = new List<LineaProductoAprobacion>();
        if (datos.TryGetValue("Productos", out var raw) && raw is IEnumerable<object> lista)
        {
            foreach (var item in lista)
            {
                if (item is not IDictionary<string, object> mapa)
                {
                    continue;
                }

                productos.Add(new LineaProductoAprobacion
                {
                    Id = Texto(mapa, "Id"),
                    Codigo = Texto(mapa, "Código", "Codigo"),
                    Nombre = Texto(mapa, "Nombre")
                });
            }
        }

        return new SolicitudAprobacion
        {
            Id = doc.Id,
            Tipo = Texto(datos, "Tipo"),
            Origen = Texto(datos, "Origen"),
            Folio = Texto(datos, "Folio"),
            Solicitante = Texto(datos, "Solicitante"),
            Usuario = Texto(datos, "Usuario"),
            Fecha = Fecha(datos, "Fecha") ?? DateTimeOffset.Now,
            TotalInventario = Entero(datos, "Total inventario"),
            Cantidad = Entero(datos, "Cantidad"),
            Porcentaje = Doble(datos, "Porcentaje"),
            Estado = ParseEstado(Texto(datos, "Estado")),
            MotivoDenegacion = Texto(datos, "Motivo denegación", "MotivoDenegacion"),
            RespondidoPor = Texto(datos, "Respondido por"),
            FechaRespuesta = Fecha(datos, "Fecha respuesta", "FechaRespuesta"),
            Aplicada = Bool(datos, "Aplicada"),
            Productos = productos
        };
    }

    private static EstadoAprobacion ParseEstado(string texto)
    {
        if (texto.Equals("Autorizada", StringComparison.OrdinalIgnoreCase))
        {
            return EstadoAprobacion.Autorizada;
        }

        if (texto.Equals("Denegada", StringComparison.OrdinalIgnoreCase))
        {
            return EstadoAprobacion.Denegada;
        }

        return EstadoAprobacion.Pendiente;
    }

    private static async Task AsegurarBaseAsync()
    {
        if (_base is not null)
        {
            return;
        }

        _base = await ConexionFirebase.AbrirAsync();
    }

    private static Task EnUiAsync(Action accion)
    {
        var cola = _ui ?? App.Instance.MainAppWindow?.DispatcherQueue;
        if (cola is null || cola.HasThreadAccess)
        {
            accion();
            return Task.CompletedTask;
        }

        var listo = new TaskCompletionSource();
        if (!cola.TryEnqueue(() =>
            {
                try
                {
                    accion();
                    listo.SetResult();
                }
                catch (Exception ex)
                {
                    listo.SetException(ex);
                }
            }))
        {
            listo.SetException(new InvalidOperationException("No se pudo actualizar la pantalla."));
        }

        return listo.Task;
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

    private static void CambiarEstado(string texto)
    {
        _estado = texto;
        Cambiado?.Invoke();
    }

    private static string Texto(IDictionary<string, object> datos, params string[] claves)
    {
        foreach (var clave in claves)
        {
            if (datos.TryGetValue(clave, out var valor) && valor is not null)
            {
                return Convert.ToString(valor, CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static int Entero(IDictionary<string, object> datos, string clave)
    {
        if (!datos.TryGetValue(clave, out var valor) || valor is null)
        {
            return 0;
        }

        return valor switch
        {
            int n => n,
            long n => (int)n,
            double n => (int)n,
            _ => int.TryParse(Convert.ToString(valor, CultureInfo.InvariantCulture), out var n) ? n : 0
        };
    }

    private static double Doble(IDictionary<string, object> datos, string clave)
    {
        if (!datos.TryGetValue(clave, out var valor) || valor is null)
        {
            return 0;
        }

        return valor switch
        {
            double n => n,
            float n => n,
            int n => n,
            long n => n,
            _ => double.TryParse(Convert.ToString(valor, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : 0
        };
    }

    private static bool Bool(IDictionary<string, object> datos, string clave)
    {
        if (!datos.TryGetValue(clave, out var valor) || valor is null)
        {
            return false;
        }

        return valor switch
        {
            bool b => b,
            string s => s.Equals("true", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static DateTimeOffset? Fecha(IDictionary<string, object> datos, params string[] claves)
    {
        foreach (var clave in claves)
        {
            if (!datos.TryGetValue(clave, out var valor) || valor is null)
            {
                continue;
            }

            if (valor is Timestamp marca)
            {
                return marca.ToDateTimeOffset();
            }

            if (valor is string texto &&
                DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fecha))
            {
                return fecha;
            }
        }

        return null;
    }

}
