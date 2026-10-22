using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Bug #13194 (P4-24) — «Enviar al OT» de un trámite ASIGNADO y firmado respondía SIEMPRE 409
/// <c>conflicto_concurrencia</c> si el trámite ya tenía FUR, y cada intento subía <c>row_version</c> en 1.
/// <para>Causa: la fase 1 persiste los checks (<c>soat_pagado</c>, <c>impuesto_departamental_pagado</c>), que
/// son field_values y por tanto «dato del FUR». Con FUR existente, <c>ConsolidadoVigenciaTracker</c> (Bug
/// #13055) recarga la instancia y sella <c>expediente_actualizado_en</c> en un segundo UPDATE dentro del
/// mismo <c>SaveChanges</c>; el trigger BEFORE UPDATE sube <c>row_version</c> y EF no lo relee. La fase 2
/// (transición en el MISMO contexto) salía con el token viejo.</para>
/// <para>Se ejercita el pipeline real: <see cref="EnviarAlOtHandler"/> + <see cref="TramiteLifecycleService"/>
/// + <see cref="ProcedureInstanceRepository"/> sobre PostgreSQL con sus triggers. Solo se sustituyen los
/// puertos que no intervienen en la arista asignado→entregado (gates de radicación, historial/publicación).</para>
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var (r, error, _) = await new EnviarAlOtHandler(repo, lifecycle).HandleAsync(id, tenant, user, new(), ct);
/// // error == null y r.Status == "entregado" aunque el trámite tenga FUR.
/// </code>
/// Datos ficticios: documentos 9000000xxx, correos @example.test.
/// </remarks>
public sealed class Bug13194EnviarAlOtConFurTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Tenant = HierarchyScenario.C1;
    private static readonly Guid Instancia = new("5a5a5a5a-00e1-4000-8000-000000013194");

    [PostgresFact]
    public async Task Asignado_ConFurGenerado_EnviarAlOt_PasaAEntregado()
    {
        var ct = TestContext.Current.CancellationToken;
        var versionInicial = await SembrarAsignadoFirmadoAsync(conFur: true, ct);

        await using var ctx = NewContext();
        var (resultado, error, _) = await Handler(ctx).HandleAsync(
            Instancia, Tenant, HierarchyScenario.UserOf(Tenant),
            new EnviarAlOtRequest(SoatPagado: true, ImpuestoDepartamentalPagado: true), ct);

        // Antes del fix: error == conflicto_concurrencia y el trámite seguía en asignado.
        error.Should().BeNull();
        resultado!.Status.Should().Be(TramiteEstado.Entregado);

        var (estado, version) = await LeerAsync(ct);
        estado.Should().Be(TramiteEstado.Entregado);
        version.Should().BeGreaterThan(versionInicial);
    }

    [PostgresFact]
    public async Task Asignado_SinFur_EnviarAlOt_PasaAEntregado()
    {
        // Control: sin FUR el tracker no sella la marca y el camino ya funcionaba.
        var ct = TestContext.Current.CancellationToken;
        await SembrarAsignadoFirmadoAsync(conFur: false, ct);

        await using var ctx = NewContext();
        var (_, error, _) = await Handler(ctx).HandleAsync(
            Instancia, Tenant, HierarchyScenario.UserOf(Tenant), new EnviarAlOtRequest(SoatPagado: true), ct);

        error.Should().BeNull();
        (await LeerAsync(ct)).Estado.Should().Be(TramiteEstado.Entregado);
    }

    /// <summary>
    /// HU #13264 AC3 — FLITO marcó el impuesto como pagado (<c>source = flito</c>) y el gestor envía al OT con el check
    /// desmarcado: la marca sigue en <c>true</c>/<c>flito</c>. Una marca del gestor, en cambio, sí se actualiza.
    /// </summary>
    [PostgresTheory]
    [InlineData("flito", "true", "true", "flito")]
    [InlineData("user", "true", "false", "user")]
    public async Task AC3_EnviarAlOt_ConElCheckDesmarcado_RespetaLaMarcaDeFlito(
        string fuente, string valorPrevio, string valorEsperado, string fuenteEsperada)
    {
        var ct = TestContext.Current.CancellationToken;
        await SembrarAsignadoFirmadoAsync(conFur: false, ct);
        await using (var conn = await Fixture.OpenConnectionAsync())
        await using (var cmd = new NpgsqlCommand(
                         "INSERT INTO tramites.procedure_instance_field_values (tenant_id, procedure_instance_id, field_key, value_text, source) "
                         + "VALUES (@tenant, @id, 'impuesto_departamental_pagado', @valor, @fuente)", conn))
        {
            cmd.Parameters.AddWithValue("tenant", Tenant);
            cmd.Parameters.AddWithValue("id", Instancia);
            cmd.Parameters.AddWithValue("valor", valorPrevio);
            cmd.Parameters.AddWithValue("fuente", fuente);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using (var ctx = NewContext())
        {
            var (_, error, _) = await Handler(ctx).HandleAsync(
                Instancia, Tenant, HierarchyScenario.UserOf(Tenant),
                new EnviarAlOtRequest(SoatPagado: true, ImpuestoDepartamentalPagado: false), ct);
            error.Should().BeNull();
        }

        (await LeerAsync(ct)).Estado.Should().Be(TramiteEstado.Entregado);
        await using var verify = NewContext();
        var marca = await verify.Set<ProcedureInstanceFieldValue>().AsNoTracking()
            .SingleAsync(f => f.ProcedureInstanceId == Instancia && f.FieldKey == "impuesto_departamental_pagado", ct);
        marca.ValueText.Should().Be(valorEsperado);
        marca.Source.Should().Be(fuenteEsperada);
    }

    private static EnviarAlOtHandler Handler(FlitDbContext ctx)
    {
        var repo = new ProcedureInstanceRepository(ctx);
        var lifecycle = new TramiteLifecycleService(
            repo,
            Substitute.For<IProcedureTypeRepository>(),
            Substitute.For<ITransitOfficeGrantGate>(),
            Substitute.For<IOtOperabilityGate>(),
            NullOtRuleGate.Instance,
            Substitute.For<ITramiteTransitionRecorder>(),
            Substitute.For<ITramiteTransitionPublisher>());
        return new EnviarAlOtHandler(repo, lifecycle);
    }

    private async Task<long> SembrarAsignadoFirmadoAsync(bool conFur, CancellationToken ct)
    {
        var tipo = await HierarchyScenario.SeedAsync(Fixture);
        await using (var ctx = NewContext())
        {
            // Una entidad distinta por parte (la unicidad es instancia + entidad + ordinal).
            var entidades = await ctx.Database
                .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM tramites.procedure_entities ORDER BY id LIMIT 2")
                .ToListAsync(ct);

            var instancia = new ProcedureInstance
            {
                Id = Instancia,
                TenantId = Tenant,
                ProcedureTypeId = tipo.Id,
                ReferenceNumber = "IT13194E",
                Status = TramiteEstado.Borrador,
                Plate = "ITE194",
                CreatedByUserId = HierarchyScenario.UserOf(Tenant),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            ctx.ProcedureInstances.Add(instancia);

            // Firmado: cada parte con su validación de identidad aprobada y vigente del MISMO documento.
            foreach (var (parte, documento, entidad) in new[]
                     {
                         ("comprador", "9000000701", entidades[0]),
                         ("vendedor", "9000000702", entidades[^1]),
                     })
            {
                ctx.Set<ProcedureInstanceActor>().Add(new ProcedureInstanceActor
                {
                    Id = Guid.NewGuid(),
                    TenantId = Tenant,
                    ProcedureInstanceId = Instancia,
                    ProcedureEntityId = entidad,
                    ActorType = parte,
                    PersonType = "natural",
                    DocumentType = "CC",
                    DocumentNumber = documento,
                    FullName = $"Persona {parte}",
                    Email = $"{parte}@example.test",
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                ctx.Set<ProcedureInstanceBiometricValidation>().Add(new ProcedureInstanceBiometricValidation
                {
                    Id = Guid.NewGuid(),
                    TenantId = Tenant,
                    ProcedureInstanceId = Instancia,
                    PartyRole = parte,
                    Status = BiometricEstados.Aprobado,
                    Name = $"Persona {parte}",
                    DocumentType = "CC",
                    DocumentNumber = documento,
                    Email = $"{parte}@example.test",
                    TokenHash = Guid.NewGuid().ToString("N"),
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                    ValidatedAt = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            }

            if (conFur)
            {
                ctx.Set<ProcedureInstanceAttachment>().Add(new ProcedureInstanceAttachment
                {
                    Id = Guid.NewGuid(),
                    TenantId = Tenant,
                    ProcedureInstanceId = Instancia,
                    Tipo = "fur",
                    Filename = "fur.pdf",
                    Mimetype = "application/pdf",
                    StoragePath = "it/13194/fur.pdf",
                    Sha256 = "sha-fur",
                    Source = "system",
                    UploadedAt = DateTimeOffset.UtcNow,
                });
            }

            await ctx.SaveChangesAsync(ct);
        }

        // Borrador → asignado por SQL (la ruta de placa no es lo que se prueba), con su historial.
        await using var conn = await Fixture.OpenConnectionAsync();
        foreach (var sql in new[]
                 {
                     "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) VALUES (@tenant, @id, 'asignado')",
                     "UPDATE tramites.procedure_instances SET status = 'asignado' WHERE id = @id",
                 })
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", Instancia);
            cmd.Parameters.AddWithValue("tenant", Tenant);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        return (await LeerAsync(ct)).Version;
    }

    private async Task<(string Estado, long Version)> LeerAsync(CancellationToken ct)
    {
        await using var ctx = NewContext();
        var fila = await ctx.ProcedureInstances.AsNoTracking()
            .Where(p => p.Id == Instancia)
            .Select(p => new { p.Status, p.RowVersion })
            .SingleAsync(ct);
        return (fila.Status, fila.RowVersion);
    }
}
