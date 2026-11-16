using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #14305 (Feature #14304, Épica #14271) — DDL 170 contra PostgreSQL real: la columna
/// <c>manual_capture_token_encrypted</c> existe y se mapea, y el UPDATE atómico <c>TryPersistManualCaptureAsync</c> (que NO pasa
/// por el change tracker) también la deja en NULL al enviar la captura (AC5).
/// </summary>
public sealed class ManualCaptureTokenCifradoPostgresTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid UserId = new("b1430500-0000-4000-8000-0000000000aa");
    private static readonly Guid InstanceId = new("b1430500-0000-4000-8000-000000000001");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [PostgresFact]
    public async Task AC5_ElUpdateAtomicoDeLaCapturaBorraElTokenCifrado_YLaColumnaSeGuardaYSeLee()
    {
        await SeedInstanceAsync();
        var validationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var ctx = NewContext())
        {
            ctx.ProcedureInstanceBiometricValidations.Add(new ProcedureInstanceBiometricValidation
            {
                Id = validationId,
                TenantId = TenantSeed.LoneId,
                ProcedureInstanceId = InstanceId,
                PartyRole = "comprador",
                DocumentType = "CC",
                DocumentNumber = "1014305001",
                Name = "Persona captura manual",
                Email = "captura.manual.14305@flit.test",
                Provider = BiometricProviders.Manual,
                Status = BiometricEstados.ManualActivo,
                TokenHash = new string('a', 64),
                ManualCaptureTokenEncrypted = new string('c', 23),
                ExpiresAt = now.AddHours(20),
                ManualActivatedAt = now.AddMinutes(-30),
                ConsentAt = now.AddMinutes(-10),
                CreatedAt = now,
            });
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var read = NewContext())
        {
            var guardado = await read.ProcedureInstanceBiometricValidations.IgnoreQueryFilters().AsNoTracking()
                .Where(v => v.Id == validationId).Select(v => v.ManualCaptureTokenEncrypted).SingleAsync(Ct);
            guardado.Should().Be(new string('c', 23));
        }

        await using (var ctx = NewContext())
        {
            var repo = new ProcedureInstanceRepository(ctx);
            var v = await ctx.ProcedureInstanceBiometricValidations.IgnoreQueryFilters().SingleAsync(x => x.Id == validationId, Ct);
            v.RegistrarCapturaManual("fm-rostro", "fm-anverso", "fm-reverso", "fm-firma", new string('b', 64), now);

            (await repo.TryPersistManualCaptureAsync(v, Ct)).Should().BeTrue();
        }

        await using (var read = NewContext())
        {
            var fila = await read.ProcedureInstanceBiometricValidations.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(v => v.Id == validationId, Ct);
            fila.Status.Should().Be(BiometricEstados.PendienteRevisionManual);
            fila.ManualCaptureTokenEncrypted.Should().BeNull("al enviar la captura el token cifrado se borra");
            fila.TokenHash.Should().Be(new string('a', 64), "el hash se conserva");
        }
    }

    private async Task SeedInstanceAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.Lone());
        await ctx.SaveChangesAsync(Ct);
        ctx.Users.Add(new User
        {
            Id = UserId,
            Email = "it-14305@flit.test",
            DisplayName = "IT HU14305",
            Status = "active",
            HomeTenantId = TenantSeed.LoneId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(Ct);

        var typeId = await ctx.ProcedureTypes.AsNoTracking()
            .Where(t => t.Code == TramiteTipologiaCatalog.CodigoTraspasoStandard)
            .Select(t => t.Id)
            .SingleAsync(Ct);
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = InstanceId,
            TenantId = TenantSeed.LoneId,
            ProcedureTypeId = typeId,
            CreatedByUserId = UserId,
            Status = TramiteEstado.Borrador,
            Origin = "ict",
            ExternalRef = "ict-14305",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(Ct);
    }
}
