using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using IdermaBaseDatos.Models;

namespace IdermaBaseDatos.Services;

public static class ImpresionAutorizaciones
{
    public static async Task AbrirAsync(
        IReadOnlyList<SolicitudAprobacion> solicitudes,
        string tituloRango,
        string tituloEstado)
    {
        var html = Documento(solicitudes, tituloRango, tituloEstado);
        var carpeta = Path.Combine(Path.GetTempPath(), "IdermaBaseDatos");
        Directory.CreateDirectory(carpeta);
        var ruta = Path.Combine(carpeta, $"autorizaciones-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        await File.WriteAllTextAsync(ruta, html, Encoding.UTF8);
        Process.Start(new ProcessStartInfo
        {
            FileName = ruta,
            UseShellExecute = true,
            WorkingDirectory = carpeta
        });
    }

    private static string Documento(
        IReadOnlyList<SolicitudAprobacion> solicitudes,
        string tituloRango,
        string tituloEstado)
    {
        var autorizadas = solicitudes.Count(s => s.Estado == EstadoAprobacion.Autorizada);
        var denegadas = solicitudes.Count(s => s.Estado == EstadoAprobacion.Denegada);
        var cuerpo = new StringBuilder();
        if (solicitudes.Count == 0)
        {
            cuerpo.Append("<p class=\"vacio\">No hay autorizaciones en ese rango.</p>");
        }
        else
        {
            foreach (var solicitud in solicitudes)
            {
                cuerpo.Append(Bloque(solicitud));
            }
        }

        return $$"""
            <!DOCTYPE html>
            <html lang="es">
            <head>
            <meta charset="utf-8" />
            <title>Autorizaciones · {{WebUtility.HtmlEncode(tituloRango)}}</title>
            <style>
            body { font-family: Segoe UI, sans-serif; color: #1b1b1b; margin: 24px; }
            h1 { font-size: 22px; margin: 0 0 4px; }
            .meta { color: #555; margin: 0 0 16px; }
            .acciones { margin: 0 0 16px; }
            button { font: inherit; padding: 8px 14px; }
            article { border: 1px solid #d0d0d0; border-radius: 8px; padding: 12px 14px; margin: 0 0 12px; break-inside: avoid; }
            .fila { display: flex; justify-content: space-between; gap: 12px; }
            .folio { font-weight: 650; }
            .estado { font-weight: 650; }
            .autorizada { color: #0f6b3a; }
            .denegada { color: #a4262c; }
            .sub { color: #555; font-size: 13px; margin-top: 2px; }
            ul { margin: 8px 0 0; padding-left: 18px; }
            .vacio { color: #555; }
            @media print { .acciones { display: none; } body { margin: 12px; } }
            </style>
            </head>
            <body>
            <h1>Autorizaciones de base de datos</h1>
            <p class="meta">{{WebUtility.HtmlEncode(tituloEstado)}} · {{WebUtility.HtmlEncode(tituloRango)}} · {{autorizadas}} autorizadas · {{denegadas}} denegadas · Impreso {{DateTime.Now:dd/MM/yyyy HH:mm}}</p>
            <p class="acciones"><button onclick="window.print()">Imprimir</button></p>
            {{cuerpo}}
            </body>
            </html>
            """;
    }

    private static string Bloque(SolicitudAprobacion solicitud)
    {
        var clase = solicitud.Estado == EstadoAprobacion.Denegada ? "denegada" : "autorizada";
        var cuando = (solicitud.FechaRespuesta ?? solicitud.Fecha).ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
        var quien = string.IsNullOrWhiteSpace(solicitud.RespondidoPor) ? "Base de datos" : solicitud.RespondidoPor;
        var origen = solicitud.Origen.Equals("logistica", StringComparison.OrdinalIgnoreCase) ? "Logística" : solicitud.Origen;
        var productos = new StringBuilder();
        foreach (var producto in solicitud.Productos)
        {
            var nombre = string.IsNullOrWhiteSpace(producto.Nombre) ? producto.Codigo : producto.Nombre;
            if (string.IsNullOrWhiteSpace(nombre))
            {
                continue;
            }

            var codigo = string.IsNullOrWhiteSpace(producto.Codigo) ? string.Empty : WebUtility.HtmlEncode(producto.Codigo) + " · ";
            productos.Append("<li>").Append(codigo).Append(WebUtility.HtmlEncode(nombre)).Append("</li>");
        }

        var motivo = solicitud.Estado == EstadoAprobacion.Denegada && !string.IsNullOrWhiteSpace(solicitud.MotivoDenegacion)
            ? $"<p>Motivo: {WebUtility.HtmlEncode(solicitud.MotivoDenegacion)}</p>"
            : string.Empty;
        var lista = productos.Length == 0 ? string.Empty : "<ul>" + productos + "</ul>";

        return $"""
            <article>
            <div class="fila"><span class="folio">{WebUtility.HtmlEncode(solicitud.Folio)}</span><span class="estado {clase}">{WebUtility.HtmlEncode(solicitud.EstadoTexto)}</span></div>
            <div class="sub">{WebUtility.HtmlEncode(solicitud.TituloAccion)} · {WebUtility.HtmlEncode(origen)} · {cuando}</div>
            <div class="sub">Pidió {WebUtility.HtmlEncode(solicitud.Solicitante)} · respondió {WebUtility.HtmlEncode(quien)} · {solicitud.Cantidad} de {solicitud.TotalInventario} productos ({solicitud.Porcentaje.ToString("0.#", CultureInfo.CurrentCulture)} %)</div>
            {motivo}
            {lista}
            </article>
            """;
    }
}
