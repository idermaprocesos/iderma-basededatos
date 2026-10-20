using System.Text.Json;
using System.Text.Json.Serialization;
using IdermaBaseDatos.Models;

namespace IdermaBaseDatos.Services;

internal static class AprobacionesLocales
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string Ruta() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IdermaCapilar",
            "aprobaciones-datos-local.json");

    public static IReadOnlyList<SolicitudAprobacion> Cargar()
    {
        var ruta = Ruta();
        if (!File.Exists(ruta))
        {
            return [];
        }

        try
        {
            var doc = JsonSerializer.Deserialize<Archivo>(File.ReadAllText(ruta), Json);
            return doc?.Solicitudes ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Guardar(IEnumerable<SolicitudAprobacion> solicitudes)
    {
        var ruta = Ruta();
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        var doc = new Archivo
        {
            Guardado = DateTimeOffset.Now,
            Solicitudes = solicitudes
                .Select(Clonar)
                .OrderByDescending(s => s.EstaPendiente)
                .ThenByDescending(s => s.Fecha)
                .ToList()
        };
        var temporal = ruta + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporal, JsonSerializer.Serialize(doc, Json));
        File.Copy(temporal, ruta, overwrite: true);
        File.Delete(temporal);
    }

    private static SolicitudAprobacion Clonar(SolicitudAprobacion s) => new()
    {
        Id = s.Id,
        Tipo = s.Tipo,
        Origen = s.Origen,
        Folio = s.Folio,
        Solicitante = s.Solicitante,
        Usuario = s.Usuario,
        Fecha = s.Fecha,
        TotalInventario = s.TotalInventario,
        Cantidad = s.Cantidad,
        Porcentaje = s.Porcentaje,
        Estado = s.Estado,
        MotivoDenegacion = s.MotivoDenegacion,
        RespondidoPor = s.RespondidoPor,
        FechaRespuesta = s.FechaRespuesta,
        Aplicada = s.Aplicada,
        Productos = s.Productos.Select(p => new LineaProductoAprobacion
        {
            Id = p.Id,
            Codigo = p.Codigo,
            Nombre = p.Nombre
        }).ToList()
    };

    private sealed class Archivo
    {
        public DateTimeOffset Guardado { get; set; }
        public List<SolicitudAprobacion> Solicitudes { get; set; } = [];
    }
}
