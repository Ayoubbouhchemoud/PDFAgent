using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDFAgent.App.Services;
using PDFAgent.App.ViewModels;
using PDFAgent.App.Views;
using PDFAgent.Core.Services;
using PDFAgent.Core.Configuration;
using PDFAgent.Core.Interfaces;
using PDFAgent.PdfEngine;
using PDFAgent.PdfEngine.Ocr;
using PDFAgent.PdfEngine.Redaction;
using Serilog;

namespace PDFAgent.App;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        AppArguments.Initialize(e.Args);
        ConfigureSerilog();
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        ConfigureGlobalExceptionHandling();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureSerilog()
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PDFAgent", "logs", "app-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });

        // Configuration
        var config = AppConfiguration.Load(AppConfiguration.DefaultConfigPath);
        services.AddSingleton(config);

        // Core services — all Singleton so every consumer shares the same instance
        // (IPdfEngine holds document state; others are stateless but share Singleton lifetime)
        services.AddSingleton<IPdfEngine, PdfiumEngine>();
        services.AddSingleton<IPdfEditor, PdfiumEditor>();
        services.AddSingleton<IOcrEngine, TesseractOcrEngine>();
        services.AddSingleton<IRedactionEngine, PdfRedactionEngine>();

        // App services
        services.AddSingleton<FileDialogService>();
        services.AddSingleton<IBatchWorkflowService, BatchWorkflowService>();

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<ViewerViewModel>();
        services.AddTransient<BatchWorkflowViewModel>();
        services.AddTransient<OcrReviewViewModel>();

        // Views
        services.AddTransient<MainWindow>();
    }

    private static void ConfigureGlobalExceptionHandling()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Fatal((Exception)args.ExceptionObject, "Unhandled domain exception");
        };

        Current.DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            MessageBox.Show($"An unexpected error occurred:\n{args.Exception.Message}",
                "PDF Agent", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }
}
