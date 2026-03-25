using System;
using Avalonia;
using Avalonia.Controls;

namespace CapacitorScanner
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static int Main(string[] args) 
        {
            var app = BuildAvaloniaApp();
            if (!Design.IsDesignMode)
            {
                CapacitorScanner.Api.Program.Main(args);
            }
            return app.StartWithClassicDesktopLifetime(args);
        }
        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
