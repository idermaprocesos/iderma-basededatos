using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IdermaBaseDatos.Pages;

public sealed partial class InicioPage : Page
{
    public InicioPage()
    {
        InitializeComponent();
    }

    private void VerNotificaciones_Click(object sender, RoutedEventArgs e)
    {
        App.Instance.MainAppWindow?.IrANotificaciones();
    }
}
