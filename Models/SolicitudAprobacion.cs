using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace IdermaBaseDatos.Models;

public enum EstadoAprobacion
{
    Pendiente,
    Autorizada,
    Denegada
}

public sealed class LineaProductoAprobacion
{
    public string Id { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;

    [JsonIgnore]
    public string Texto => string.IsNullOrWhiteSpace(Codigo) ? Nombre : $"{Codigo}  {Nombre}";
}

public sealed class SolicitudAprobacion : INotifyPropertyChanged
{
    private EstadoAprobacion _estado = EstadoAprobacion.Pendiente;
    private string _motivoDenegacion = string.Empty;
    private string _respondidoPor = string.Empty;
    private DateTimeOffset? _fechaRespuesta;
    private bool _aplicada;

    public string Id { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string Origen { get; set; } = string.Empty;
    public string Folio { get; set; } = string.Empty;
    public string Solicitante { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public DateTimeOffset Fecha { get; set; } = DateTimeOffset.Now;
    public int TotalInventario { get; set; }
    public int Cantidad { get; set; }
    public double Porcentaje { get; set; }
    public List<LineaProductoAprobacion> Productos { get; set; } = [];

    public EstadoAprobacion Estado
    {
        get => _estado;
        set
        {
            if (_estado == value)
            {
                return;
            }

            _estado = value;
            Notificar(nameof(Estado));
            Notificar(nameof(EstadoTexto));
            Notificar(nameof(EstaPendiente));
            Notificar(nameof(EstaRespondida));
            Notificar(nameof(ResumenRespuesta));
        }
    }

    public string MotivoDenegacion
    {
        get => _motivoDenegacion;
        set
        {
            if (_motivoDenegacion == value)
            {
                return;
            }

            _motivoDenegacion = value;
            Notificar(nameof(MotivoDenegacion));
            Notificar(nameof(ResumenRespuesta));
        }
    }

    public string RespondidoPor
    {
        get => _respondidoPor;
        set
        {
            if (_respondidoPor == value)
            {
                return;
            }

            _respondidoPor = value;
            Notificar(nameof(RespondidoPor));
            Notificar(nameof(ResumenRespuesta));
        }
    }

    public DateTimeOffset? FechaRespuesta
    {
        get => _fechaRespuesta;
        set
        {
            if (_fechaRespuesta == value)
            {
                return;
            }

            _fechaRespuesta = value;
            Notificar(nameof(FechaRespuesta));
            Notificar(nameof(ResumenRespuesta));
        }
    }

    public bool Aplicada
    {
        get => _aplicada;
        set
        {
            if (_aplicada == value)
            {
                return;
            }

            _aplicada = value;
            Notificar(nameof(Aplicada));
            Notificar(nameof(ResumenRespuesta));
        }
    }

    [JsonIgnore]
    public string FechaTexto => Fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);

    [JsonIgnore]
    public string EstadoTexto => Estado switch
    {
        EstadoAprobacion.Autorizada => "Autorizada",
        EstadoAprobacion.Denegada => "Denegada",
        _ => "Pendiente"
    };

    [JsonIgnore]
    public bool EstaPendiente => Estado == EstadoAprobacion.Pendiente;

    [JsonIgnore]
    public bool EstaRespondida => !EstaPendiente;

    [JsonIgnore]
    public string TituloAccion => Tipo switch
    {
        "borrado-inventario" => "Borrado de inventario",
        _ => string.IsNullOrWhiteSpace(Tipo) ? "Autorización" : Tipo
    };

    [JsonIgnore]
    public string Resumen =>
        $"{Solicitante} · {OrigenVisible} · {Cantidad} de {TotalInventario} productos ({Porcentaje:0.#} %)";

    [JsonIgnore]
    public string Detalle
    {
        get
        {
            var nombres = Productos
                .Select(p => string.IsNullOrWhiteSpace(p.Nombre) ? p.Codigo : p.Nombre)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Take(8)
                .ToList();
            if (nombres.Count == 0)
            {
                return "Sin detalle de productos.";
            }

            var texto = string.Join(", ", nombres);
            var resto = Productos.Count - nombres.Count;
            return resto > 0 ? texto + $" y {resto} más." : texto;
        }
    }

    [JsonIgnore]
    public string ResumenRespuesta
    {
        get
        {
            if (EstaPendiente)
            {
                return string.Empty;
            }

            var quien = string.IsNullOrWhiteSpace(RespondidoPor) ? "Base de datos" : RespondidoPor;
            var cuando = FechaRespuesta?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
            var baseTexto = Estado == EstadoAprobacion.Denegada
                ? $"{quien} denegó" + (string.IsNullOrWhiteSpace(MotivoDenegacion) ? "." : $": {MotivoDenegacion}")
                : Aplicada
                    ? $"{quien} autorizó y logística ya aplicó el borrado."
                    : $"{quien} autorizó. Logística aplicará el borrado cuando esté abierta.";
            return string.IsNullOrWhiteSpace(cuando) ? baseTexto : $"{baseTexto} · {cuando}";
        }
    }

    [JsonIgnore]
    private string OrigenVisible => Origen.Equals("logistica", StringComparison.OrdinalIgnoreCase)
        ? "Logística"
        : (string.IsNullOrWhiteSpace(Origen) ? "Otra app" : Origen);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notificar([CallerMemberName] string? propiedad = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
}
