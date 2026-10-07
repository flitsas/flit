using Flit.Api.Telemetry;
using Flit.Notificaciones.Api;
using Flit.Notificaciones.Api.Persistence;
using Flit.Platform.Sdk.Authentication;
using Flit.Platform.Sdk.Grpc;
using Flit.Platform.Sdk.Messaging;
using Flit.Infrastructure.Email;
using Flit.Infrastructure.Notifications.Renting;
using Flit.Modules.Notificaciones;
using Flit.Modules.Security.Domain.Auth;
using Flit.Notificaciones.Api.Envio;
using Flit.Notificaciones.Api.Grpc;
using Flit.Notificaciones.Api.Webhooks;
using Flit.Modules.Notificaciones.Webhooks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

namespace Flit.Notificaciones.Api;

/// <summary>
/// core-notificaciones (notificaciones de servicio de plataforma, Epic #13316, HU #13340). REST en ASPNETCORE_URLS (solo salud por
/// ahora; lo que entra desde fuera de FLIT va por el gateway), gRPC en <see cref="ServicioSettings.GrpcPortKey"/> (red
/// interna, solo HTTP/2) y bus por la outbox/bandeja del SDK. Al arrancar aplica sus migraciones (solo su esquema).
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        var app = Build(args);
        await MigrateAsync(app).ConfigureAwait(false);
        await app.RunAsync().ConfigureAwait(false);
    }

    internal static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);
        ServicioSettings.Validate(builder.Configuration);
        var grpcPort = builder.Configuration.GetValue<int>(ServicioSettings.GrpcPortKey);

        builder.Host.UseDefaultServiceProvider(o =>
        {
            o.ValidateScopes = true;
            o.ValidateOnBuild = true;
        });
        builder.AddFlitTelemetry($"flit-core-{ServicioSettings.Codigo}");

        var connectionString = builder.Configuration.GetConnectionString("Servicio")!;
        builder.Services.AddDbContext<NotificacionesDb>(o => NotificacionesDb.Configure(o, connectionString));
        builder.Services.AddFlitPlatformAuthentication(builder.Configuration);
        builder.Services.AddAuthorization();
        builder.Services.AddFlitGrpcServer()
            .RequireServiceToken<NotificacionesGrpcService>(NotificacionesGrpcService.Scope, Flit.Api.Identity.ServiceAudiences.Notificaciones);
        builder.Services.AddFlitOutbox<NotificacionesDb>(builder.Configuration);

        // HU #13353: transportes de correo (los mismos de core-api, Flit.Modules.Notificaciones) y el envío con registro.
        AddTransportes(builder);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddScoped<EnvioDeCorreo>();
        // HU #13354: trabajos de correo desde el bus (notificaciones.email.send, con reintentos y .dlq).
        // HU #13356: webhooks salientes (notificaciones.webhook.send) con filtro de destinos internos, reintentos y .dlq.
        builder.Services.TryAddSingleton<IFiltroDestinosWebhook, FiltroDestinosPublicos>();
        builder.Services.AddHttpClient(TrabajoWebhookConsumer.ClienteHttp, c => c.Timeout = TimeSpan.FromSeconds(30));

        // Las esperas entre reintentos son las del ADR (10 s, 1 min, 10 min) salvo Notificaciones:Correo:EsperasReintento.
        var esperas = builder.Configuration.GetSection("Notificaciones:Correo:EsperasReintento").Get<TimeSpan[]>();
        builder.Services.AddFlitConsumer<NotificacionesDb, TrabajoCorreoConsumer, TrabajoCorreo>(
            builder.Configuration, TrabajoCorreoConsumer.Cola, producer: ServicioSettings.Codigo, [TrabajoCorreo.Tipo], o =>
            {
                if (esperas is not { Length: > 0 })
                    return;
                o.RetryDelays.Clear();
                foreach (var espera in esperas)
                    o.RetryDelays.Add(espera);
            });
        builder.Services.AddFlitConsumer<NotificacionesDb, TrabajoWebhookConsumer, TrabajoWebhook>(
            builder.Configuration, TrabajoWebhookConsumer.Cola, producer: ServicioSettings.Codigo, [TrabajoWebhook.Tipo], o =>
            {
                if (esperas is not { Length: > 0 })
                    return;
                o.RetryDelays.Clear();
                foreach (var espera in esperas)
                    o.RetryDelays.Add(espera);
            });

        // h2c necesita un endpoint solo HTTP/2: se vuelven a declarar las URLs del REST (Kestrel ignora ASPNETCORE_URLS
        // cuando se declaran endpoints por código) y se suma el del gRPC.
        builder.WebHost.ConfigureKestrel((context, options) =>
        {
            foreach (var raw in (context.Configuration[WebHostDefaults.ServerUrlsKey] ?? "http://localhost:5000")
                         .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var uri = new Uri(raw.Replace("://+", "://0.0.0.0", StringComparison.Ordinal).Replace("://*", "://0.0.0.0", StringComparison.Ordinal));
                if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                    options.ListenLocalhost(uri.Port, lo => lo.Protocols = HttpProtocols.Http1AndHttp2);
                else
                    options.ListenAnyIP(uri.Port, lo => lo.Protocols = HttpProtocols.Http1AndHttp2);
            }

            options.ListenAnyIP(grpcPort, lo => lo.Protocols = HttpProtocols.Http2);
        });

        var app = builder.Build();
        app.UseFlitCorrelationId();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/health", () => Results.Ok(new { status = "alive" })).AllowAnonymous();
        app.MapGet("/health/ready", async (NotificacionesDb db, CancellationToken ct) =>
            await db.Database.CanConnectAsync(ct).ConfigureAwait(false)
            && !(await db.Database.GetPendingMigrationsAsync(ct).ConfigureAwait(false)).Any()
                ? Results.Ok(new { status = "ready" })
                : Results.Json(new { status = "not_ready" }, statusCode: StatusCodes.Status503ServiceUnavailable)).AllowAnonymous();

        // gRPC solo en su puerto: los servicios gRPC del dominio se mapean aquí con .RequireHost(grpcHost).
        var grpcHost = $"*:{grpcPort}";
        app.MapGrpcService<NotificacionesGrpcService>().RequireHost(grpcHost);
        app.MapFlitGrpcPlatform(app.Environment, grpcHost);
        return app;
    }

    /// <summary>
    /// SMTP de FLIT (o consola cuando no hay host y <c>Smtp:UseConsoleWhenNoHost</c>, por defecto solo en Development),
    /// el canal Renting (apagado sin <c>RENTING_API_ENABLED=true</c>) y el envío por canal. Mismas variables que core-api.
    /// </summary>
    private static void AddTransportes(WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        var environment = builder.Environment;
        var emailSettings = configuration.GetSection(EmailSettings.SectionName).Get<EmailSettings>() ?? new EmailSettings();
        services.AddSingleton(emailSettings);
        var consola = configuration.GetValue("Smtp:UseConsoleWhenNoHost", environment.IsDevelopment())
            && string.IsNullOrWhiteSpace(emailSettings.Host);
        if (consola)
            services.AddScoped<IEmailSender, ConsoleEmailSender>();
        else
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddSingleton(new EmailTransportDescriptor(consola));

        services.AddRentingChannel(configuration);
        services.AddScoped(sp => new CorreoPorCanal(
            sp.GetRequiredService<IEmailSender>(),
            sp.GetService<IRentingEmailApiSender>(),
            sp.GetRequiredService<IOptions<RentingChannelOptions>>()));
    }

    /// <summary>Aplica las migraciones pendientes del esquema del servicio (EF las serializa entre réplicas).</summary>
    internal static async Task MigrateAsync(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!app.Configuration.GetValue("Servicio:MigrateOnStartup", true))
            return;

        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotificacionesDb>().Database.MigrateAsync().ConfigureAwait(false);
    }
}

/// <summary>Tipo de referencia del ensamblado para <c>WebApplicationFactory</c> (Program es estático).</summary>
public sealed class NotificacionesApiEntryPoint;
