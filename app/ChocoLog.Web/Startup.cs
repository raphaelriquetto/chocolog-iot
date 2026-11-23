using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using ChocoLog.Web.DAO;
using ChocoLog.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;

namespace ChocoLog.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration, IWebHostEnvironment env)
        {
            Configuration = configuration;
            // endereço do FIWARE: appsettings.json ou o que foi salvo na tela Configuração
            ConexaoFiware.Carrega(configuration, env.ContentRootPath);
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews();

            // monitor das médias x limites, roda em segundo plano mesmo sem nenhuma página aberta
            services.AddHostedService<MonitorAlertas>();

            // swagger para documentar as APIs usadas pelo dashboard
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "ChocoLog API",
                    Version = "v1",
                    Description = "APIs do dashboard do ChocoLog (dados do Orion e do STH-Comet)"
                });

                string xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
                if (File.Exists(xml))
                    c.IncludeXmlComments(xml);
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
                app.UseDeveloperExceptionPage();
            else
                app.UseExceptionHandler("/Home/Error");

            // cultura pt-BR (datas e decimais com vírgula)
            var cultura = new CultureInfo("pt-BR");
            app.UseRequestLocalization(new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture(cultura),
                SupportedCultures = new[] { cultura },
                SupportedUICultures = new[] { cultura }
            });

            app.UseStaticFiles();

            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "ChocoLog API v1"));

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
