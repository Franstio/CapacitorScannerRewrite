using CapacitorScanner.Core.Services;
using CapacitorScanner.Services;
using CapacitorScanner.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CapacitorScanner.Helpers
{
    public static class ServiceCollectionExtension
    {
        public static void AddCommonServices(this IServiceCollection services)
        {
            Type[] viewmodels = [typeof(MainViewModel),typeof(KeypadLoginViewModel), typeof(WasteControlViewModel), typeof(BinControlViewModel), typeof(SettingsViewModel), typeof(LoginViewModel)];
            foreach (var viewmodel in viewmodels)
                services.AddSingleton(viewmodel);
            Type[] service = [typeof(PIDSGService), typeof(BinLocalDbService)];
            foreach (var viewmodel in service)
                services.AddScoped(viewmodel);
            ConfigService configService = new ConfigService();
            configService.LoadAsync();
            services.AddTransient<DialogService>();
            services.AddSingleton(configService);
            services.AddSingleton<AppState>();

            Func<HttpClientHandler> f = () =>
            {
                var handler = new HttpClientHandler();
                handler.ClientCertificateOptions = ClientCertificateOption.Manual;
                handler.ServerCertificateCustomValidationCallback =
                    (httpRequestMessage, cert, cetChain, policyErrors) =>
                    {
                        return true;
                    };
                return handler;
            };
            services.AddHttpClient().ConfigureHttpClientDefaults(cfg =>
            {

                cfg.ConfigureHttpClient(c =>
                {
                    c.Timeout = TimeSpan.FromSeconds(7);
                });
                cfg.ConfigurePrimaryHttpMessageHandler(f);
            });
        }
    }
}
