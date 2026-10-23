using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #13246/#13247 (Feature #13245) — la validación de identidad PROPIA del mandatario (party_role <c>mandatario</c> +
/// <c>mandate_signer_id</c>) es EXCLUSIVA: solo la leen las consultas por ficha y NO entra en ninguna consulta por documento
/// del trámite, la prevalidación o el módulo Identidad (su resultado no cambia). <para>Uso:
/// <c>repo.ListMandatarioValidationsAsync([fichaId])</c> devuelve solo las filas de esa ficha, de la más reciente a la más
/// antigua.</para>
/// </summary>
public sealed class ValidacionPropiaDelMandatarioRepositoryTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Signer = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private const string Documento = "1020304050";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FlitDbContext NewContext(string db) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(db).Options);

    private static ProcedureInstanceBiometricValidation Fila(
        string estado, Guid? signer, string? rol = null, DateTimeOffset? creada = null, Guid? personId = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Tenant,
        PartyRole = signer is null ? rol : BiometricRules.ParteMandatario,
        MandateSignerId = signer,
        PersonId = personId,
        DocumentType = "CC",
        DocumentNumber = Documento,
        Status = estado,
        Provider = BiometricProviders.Kyverum,
        ValidatedAt = estado == BiometricEstados.Aprobado ? Now.AddDays(-1) : null,
        ValidUntil = estado == BiometricEstados.Aprobado ? Now.AddDays(29) : null,
        TokenHash = Guid.NewGuid().ToString("N"),
        ExpiresAt = Now.AddHours(1),
        CreatedAt = creada ?? Now.AddDays(-1),
    };

    private static async Task<string> SembrarAsync(params ProcedureInstanceBiometricValidation[] filas)
    {
        var db = $"flit-validacion-mandatario-{Guid.NewGuid()}";
        await using var ctx = NewContext(db);
        ctx.Set<ProcedureInstanceBiometricValidation>().AddRange(filas);
        await ctx.SaveChangesAsync(Ct);
        return db;
    }

    [Fact]
    public async Task ListMandatarioValidations_SoloDevuelveLasDeLaFicha_DeLaMasRecienteALaMasAntigua()
    {
        var otraFicha = Guid.NewGuid();
        var vieja = Fila(BiometricEstados.Aprobado, Signer, creada: Now.AddDays(-60));
        var nueva = Fila(BiometricEstados.EnProceso, Signer, creada: Now.AddDays(-1));
        var db = await SembrarAsync(
            vieja, nueva,
            Fila(BiometricEstados.Aprobado, otraFicha),
            Fila(BiometricEstados.Aprobado, null, rol: "comprador"),
            Fila(BiometricEstados.Aprobado, null)); // prevalidación standalone
        await using var ctx = NewContext(db);

        var filas = await new ProcedureInstanceRepository(ctx).ListMandatarioValidationsAsync([Signer], Ct);

        filas.Select(f => f.Id).Should().Equal(nueva.Id, vieja.Id);
    }

    [Fact]
    public async Task ListMandatarioValidations_SinFichas_DevuelveVacio()
    {
        var db = await SembrarAsync(Fila(BiometricEstados.Aprobado, Signer));
        await using var ctx = NewContext(db);

        (await new ProcedureInstanceRepository(ctx).ListMandatarioValidationsAsync([], Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task LaValidacionDelMandatario_NoEntraEnLasConsultasPorDocumentoDelTramiteYLaPrevalidacion()
    {
        // La aprobada del mandatario con el mismo documento NO es identidad vigente del comprador ni cuenta en vuelo.
        var db = await SembrarAsync(
            Fila(BiometricEstados.Aprobado, Signer),
            Fila(BiometricEstados.EnProceso, Signer, creada: Now));
        await using var ctx = NewContext(db);
        var repo = new ProcedureInstanceRepository(ctx);

        (await repo.FindVigenteApprovedByDocumentAsync(Tenant, "CC", Documento, Now, Ct)).Should().BeNull();
        (await repo.ListInFlightByDocumentAsync(Tenant, "CC", Documento, Ct)).Should().BeEmpty();
        (await repo.ListVigenteApprovedIdentityKeysAsync([Tenant], Now, Ct)).Should().BeEmpty();
        var (porPersona, total, _) = await repo.ListBiometricValidationsByPersonAsync(Tenant, "CC", Documento, 0, 10, Ct);
        porPersona.Should().BeEmpty();
        total.Should().Be(0);
        (await repo.ListLatestBiometricValidationsByPersonsAsync(Tenant, [("CC", Documento)], Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task LasConsultasPorDocumento_SiguenViendoLasValidacionesDeCompradorVendedorYPrevalidacion()
    {
        // AC7 de la HU #13247: comprador, vendedor y prevalidación no cambian de resultado.
        var comprador = Fila(BiometricEstados.Aprobado, null, rol: "comprador");
        var db = await SembrarAsync(comprador, Fila(BiometricEstados.Aprobado, Signer));
        await using var ctx = NewContext(db);
        var repo = new ProcedureInstanceRepository(ctx);

        (await repo.FindVigenteApprovedByDocumentAsync(Tenant, "CC", Documento, Now, Ct))!.Id.Should().Be(comprador.Id);
        var (filas, total, _) = await repo.ListBiometricValidationsByPersonAsync(Tenant, "CC", Documento, 0, 10, Ct);
        total.Should().Be(1);
        filas.Single().Id.Should().Be(comprador.Id);
    }

    [Fact]
    public async Task SupersedeMandatarioInFlight_CierraSoloLasEnVueloDeEsaFicha_YConservaElHistorial()
    {
        var enVuelo = Fila(BiometricEstados.EnProceso, Signer, creada: Now.AddHours(-2));
        var pendiente = Fila(BiometricEstados.PendienteEnvio, Signer, creada: Now.AddHours(-1));
        var aprobada = Fila(BiometricEstados.Aprobado, Signer, creada: Now.AddDays(-30));
        var deOtraFicha = Fila(BiometricEstados.EnProceso, Guid.NewGuid());
        var db = await SembrarAsync(enVuelo, pendiente, aprobada, deOtraFicha);
        await using var ctx = NewContext(db);

        var cerradas = await new ProcedureInstanceRepository(ctx).SupersedeMandatarioInFlightAsync(Signer, Now, Ct);

        cerradas.Should().Be(2);
        await using var verif = NewContext(db);
        var estados = await verif.Set<ProcedureInstanceBiometricValidation>().AsNoTracking()
            .ToDictionaryAsync(v => v.Id, v => v.Status, Ct);
        estados[enVuelo.Id].Should().Be(BiometricEstados.Expirado);
        estados[pendiente.Id].Should().Be(BiometricEstados.Expirado);
        estados[aprobada.Id].Should().Be(BiometricEstados.Aprobado);
        estados[deOtraFicha.Id].Should().Be(BiometricEstados.EnProceso);
    }
}
