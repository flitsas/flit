using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Bug #13194 (P4-24/P4-25) — al aprobar una validación de identidad no se firmaba ningún pendiente de la
/// persona. <c>IdentityValidationOutboxProcessor</c> reclama el evento dentro de SU transacción y llama al
/// consumidor ahí dentro; el consumidor genera el FUR y <c>FurCommand</c> → <c>PersonalizedDocumentResolver</c>
/// → <c>CompanyPersonalizedDocumentRepository.GetActiveAsync</c> → <see cref="TenantRlsScope"/> intentaba
/// abrir OTRA transacción: <c>InvalidOperationException</c> ("The connection is already in a transaction…"),
/// cinco reintentos y dead-letter.
/// <para>Estas pruebas reproducen ese marco exacto con PostgreSQL real: una transacción de usuario abierta
/// dentro de la execution strategy (igual que el outbox) y la lectura del repositorio real dentro.</para>
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// await using var tx = await db.Database.BeginTransactionAsync(ct);   // como el outbox
/// await new CompanyPersonalizedDocumentRepository(db).GetActiveAsync(tenant, "mandato", ct); // ya no lanza
/// </code>
/// </remarks>
public sealed class Bug13194TenantRlsScopeTransaccionAmbienteTests(PostgresDatabaseFixture fixture)
    : PostgresTestBase(fixture)
{
    private static readonly Guid TenantA = new("5a5a5a5a-00a1-4000-8000-000000013194");
    private static readonly Guid TenantB = new("5a5a5a5a-00b1-4000-8000-000000013194");

    private static Task<string> TenantActualAsync(FlitDbContext db, CancellationToken ct) =>
        db.Database
            .SqlQuery<string>($"SELECT coalesce(current_setting('app.current_tenant_id', true), '') AS \"Value\"")
            .SingleAsync(ct);

    [PostgresFact]
    public async Task DentroDeLaTransaccionDelOutbox_ElRepositorioConRlsNoAbreOtra_YRestauraElTenant()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext();

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // El outbox podría venir con el contexto de otro tenant fijado en la transacción.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.current_tenant_id', {TenantB.ToString()}, true)", ct);

            var lectura = async () =>
                await new CompanyPersonalizedDocumentRepository(db).GetActiveAsync(TenantA, "mandato", ct);

            // Antes del fix: InvalidOperationException (la conexión ya está en una transacción).
            (await lectura.Should().NotThrowAsync()).Which.Should().BeNull("no hay documento personalizado activo");

            // El contexto RLS de A no se filtra a lo que sigue en la misma transacción.
            (await TenantActualAsync(db, ct)).Should().Be(TenantB.ToString());

            // La transacción ambiente sigue siendo de quien la abrió y se puede confirmar.
            await tx.CommitAsync(ct);
        });
    }

    [PostgresFact]
    public async Task SinTenantPrevio_AlSalirQuedaVacio()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext();

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            await new CompanyPersonalizedDocumentRepository(db).GetActiveAsync(TenantA, "tramite_virtual", ct);

            (await TenantActualAsync(db, ct)).Should().BeEmpty();
            await tx.CommitAsync(ct);
        });
    }

    [PostgresFact]
    public async Task SiLaOperacionFalla_RevierteAlSavepoint_YLaTransaccionAmbienteSigueUtilizable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext();

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.current_tenant_id', {TenantB.ToString()}, true)", ct);

            // Un error de PostgreSQL dentro del ámbito aborta la transacción hasta el savepoint.
            var fallo = async () => await TenantRlsScope.ExecuteAsync<int>(
                db, TenantA,
                async () => await db.Database.ExecuteSqlRawAsync("SELECT 1/0", ct),
                ct);
            await fallo.Should().ThrowAsync<Exception>();

            // Tras revertir al savepoint la transacción sigue viva y con el tenant previo.
            (await TenantActualAsync(db, ct)).Should().Be(TenantB.ToString());
            await tx.CommitAsync(ct);
        });
    }
}
