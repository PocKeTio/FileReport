using System.Configuration;
using System.Data;
using System.Windows;
using Application = System.Windows.Application;

namespace FileReport;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static bool AutoMode { get; private set; }
    public static string? ParametersFile { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Vérifier les arguments de ligne de commande
        if (e.Args.Length > 0)
        {
            AutoMode = true;
            ParametersFile = e.Args[0];
        }
    }
}

