using IdermaBaseDatos.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IdermaBaseDatos.Services;

public static class DialogoAprobacion
{
    public static async Task<bool> ConfirmarAutorizarAsync(XamlRoot raiz, SolicitudAprobacion solicitud)
    {
        var dialogo = new ContentDialog
        {
            Title = "Autorizar",
            Content = $"{solicitud.Solicitante} pide {solicitud.TituloAccion.ToLowerInvariant()}: {solicitud.Cantidad} de {solicitud.TotalInventario} productos ({solicitud.Porcentaje:0.#} %).",
            PrimaryButtonText = "Autorizar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = raiz
        };
        return await dialogo.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task<string?> PedirDenegacionAsync(XamlRoot raiz, SolicitudAprobacion solicitud)
    {
        var motivo = new TextBox
        {
            Header = "Motivo",
            PlaceholderText = "Por qué no se autoriza",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 96
        };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"{solicitud.Folio} · {solicitud.Solicitante} · {solicitud.Cantidad} productos."
        });
        panel.Children.Add(motivo);
        var dialogo = new ContentDialog
        {
            Title = "Denegar",
            Content = panel,
            PrimaryButtonText = "Denegar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = raiz
        };
        if (await dialogo.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(motivo.Text) ? string.Empty : motivo.Text.Trim();
    }
}
