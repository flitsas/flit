using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Cola de correos de cambio de estado (<c>tramites.procedure_state_change_email_dispatches</c>) con DOS procesadores a la
/// vez, como en PDN con core-api en 2 réplicas. Un cambio de estado deja una fila por destinatario con el mismo
/// <c>outbox_id</c> y el procesador envía el grupo entero. Antes solo bloqueaba la fila que reclamaba: la otra réplica
/// reclamaba una hermana libre, cargaba el mismo grupo y volvía a enviar los mismos correos (y al marcarlas, las dos
/// chocaban en un deadlock 40P01). Cada destinatario debe recibir su correo una sola vez.
/// </summary>
public sealed class CorreoEstadosConcurrenciaPostgresTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Tenant = new("c0e0e0e0-0001-4000-8000-000000000001");
    private static readonly Guid Instancia = new("c0e0e0e0-0002-4000-8000-000000000001");
    private static readonly Guid Gestor = new("c0e0e0e0-0003-4000-8000-000000000001");

    private static readonly string[] Destinatarios =
        ["comprador@flit.test", "vendedor@flit.test", "radicador@flit.test", "extra@flit.test"];

    [PostgresFact]
    public async Task DosProcesadoresALaVez_CadaDestinatarioRecibeUnSoloCorreo()
    {
        var outboxes = await SembrarAsync(cambiosDeEstado: 6);
        var sender = new EnvioLento(TimeSpan.FromMilliseconds(150));
        var a = Procesador(sender);
        var b = Procesador(sender);

        // Varias rondas simultáneas: cada ronda los dos sondean la cola en el mismo instante, que es justo
        // la ventana en la que se duplicaba en PDN.
        for (var ronda = 0; ronda < 3; ronda++)
        {
            var ct = TestContext.Current.CancellationToken;
            await Task.WhenAll(Task.Run(() => a.ProcessPendingAsync(ct), ct), Task.Run(() => b.ProcessPendingAsync(ct), ct));
        }

        // Un correo por (cambio de estado, parte): comprador y vendedor reciben el suyo; radicador y extra van
        // en copia oculta del comprador. Con N cambios, cada uno recibe exactamente N, nunca más.
        var enviados = sender.Enviados;
        enviados.Count(e => e.Para == "comprador@flit.test").Should().Be(outboxes.Count, "un correo por cambio, sin repetir");
        enviados.Count(e => e.Para == "vendedor@flit.test").Should().Be(outboxes.Count, "un correo por cambio, sin repetir");
        enviados.Where(e => e.Para == "comprador@flit.test").SelectMany(e => e.Bcc).Count(b => b == "radicador@flit.test")
            .Should().Be(outboxes.Count, "el radicador va en copia oculta una vez por cambio");
        enviados.Should().HaveCount(outboxes.Count * 2);

        await using var ctx = NewContext();
        var filas = await ctx.ProcedureStateChangeEmailDispatches.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        filas.Should().HaveCount(outboxes.Count * Destinatarios.Length);
        filas.Should().OnlyContain(f => f.Status == EstadoDespachoCorreo.Enviado, "todas quedan enviadas");
        filas.Should().OnlyContain(f => f.Attempts == 1, "nadie las procesó dos veces");
    }

    private ProcedureStateChangeEmailDispatchProcessor Procesador(IEmailSender sender)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => Fixture.CreateDbContext());
        services.AddScoped(_ => sender);
        services.AddScoped<INotificationChannelResolver>(_ => new CanalFijo());
        services.AddScoped<IEmailThemeResolver>(_ => NullEmailThemeResolver.Instance);
        services.AddSingleton(Options.Create(new NotificationEmailAssetsOptions { BaseUrl = "https://cdn.flit.test/email-assets" }));
        var provider = services.BuildServiceProvider();
        return new ProcedureStateChangeEmailDispatchProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ProcedureStateChangeEmailDispatchProcessor>.Instance);
    }

    /// <summary>Una instancia de trámite y <paramref name="cambiosDeEstado"/> cambios, cada uno con sus 4 destinatarios.</summary>
    private async Task<List<Guid>> SembrarAsync(int cambiosDeEstado)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.New(Tenant, "IT-CORREO-ESTADOS", isGroupParent: false, parentId: null));
        await ctx.SaveChangesAsync(ct);
        ctx.Users.Add(new User
        {
            Id = Gestor,
            Email = "it-correo-estados@flit.test",
            DisplayName = "Gestor correo estados",
            Status = "active",
            HomeTenantId = Tenant,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        await ctx.SaveChangesAsync(ct);

        var tipo = await ctx.ProcedureTypes.AsNoTracking().OrderBy(t => t.Code).Select(t => t.Id).FirstAsync(ct);
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = Instancia,
            TenantId = Tenant,
            ProcedureTypeId = tipo,
            ReferenceNumber = "IT-CORREO-1",
            Status = "aprobado",
            Plate = "ABC123",
            CreatedByUserId = Gestor,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(ct);

        var outboxes = new List<Guid>();
        var ahora = DateTimeOffset.UtcNow;
        for (var i = 0; i < cambiosDeEstado; i++)
        {
            var outbox = Guid.CreateVersion7();
            outboxes.Add(outbox);
            ctx.ProcedureStateChangeOutbox.Add(new ProcedureStateChangeOutbox
            {
                Id = outbox,
                TenantId = Tenant,
                ProcedureInstanceId = Instancia,
                FromStatus = "entregado",
                ToStatus = "aprobado",
                OccurredAt = ahora,
                CreatedAt = ahora,
            });
            // El modelo EF no relaciona la cola con el outbox: sin guardar antes, el orden de los INSERT rompe la FK.
            await ctx.SaveChangesAsync(ct);
            var roles = new[] { "comprador", "vendedor", "radicador", "extra" };
            for (var d = 0; d < Destinatarios.Length; d++)
            {
                ctx.ProcedureStateChangeEmailDispatches.Add(new ProcedureStateChangeEmailDispatch
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = Tenant,
                    OutboxId = outbox,
                    ProcedureInstanceId = Instancia,
                    Recipient = Destinatarios[d],
                    RecipientName = $"Destinatario {d}",
                    RecipientRole = roles[d],
                    RecipientKind = "persona",
                    TemplateKey = "tramites.aprobado",
                    Status = "pendiente",
                    // Mismo instante para todo el cambio: el orden por queued_at no separa a las hermanas.
                    QueuedAt = ahora.AddSeconds(i),
                    CreatedAt = ahora,
                });
            }
        }

        await ctx.SaveChangesAsync(ct);
        return outboxes;
    }

    private sealed class CanalFijo : INotificationChannelResolver
    {
        public Task<NotificationChannel> ResolveAsync(Guid? tenantId, CancellationToken cancellationToken) =>
            Task.FromResult(NotificationChannel.FlitSmtp);
    }

    /// <summary>Envío que tarda: abre la ventana en la que la otra réplica podía tomar el mismo grupo.</summary>
    private sealed class EnvioLento(TimeSpan demora) : IEmailSender
    {
        private readonly List<(string Para, string[] Bcc)> _enviados = [];

        public IReadOnlyList<(string Para, string[] Bcc)> Enviados
        {
            get { lock (_enviados) return [.. _enviados]; }
        }

        public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            await Task.Delay(demora, cancellationToken);
            lock (_enviados)
                _enviados.Add((message.ToEmail, [.. message.BccEmails]));
            return EmailSendResult.Sent;
        }
    }
}
