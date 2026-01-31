using System;
using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace ProtoLink.Windows.Messanger;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static ILoggerFactory LoggerFactory { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Configure console logging
        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder
                .AddConsole()
                .SetMinimumLevel(LogLevel.Debug);
        });

        var logger = LoggerFactory.CreateLogger<App>();
        logger.LogInformation("Application started");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var logger = LoggerFactory?.CreateLogger<App>();
        logger?.LogInformation("Application shutting down");
        
        LoggerFactory?.Dispose();
        base.OnExit(e);
    }
}

