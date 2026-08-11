using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// KPIs de la grilla agrupada por persona: <see cref="ProcedureInstanceRepository.CountBiometricPersonsByEstadoAsync"/>
/// cuenta PERSONAS por el estado de su validación más reciente, no validaciones. Es lo que hace que los
/// contadores cuadren con las filas que se ven en pantalla (una por documento).
/// </summary>
public sealed class BiometricPersonStatsRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtroTenant = Guid.NewGuid();
    private static readonly DateTimeOffset Base = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static ProcedureInstanceBiometricValidation Validacion(
        Guid tenantId,
        string documento,
        string status,
        DateTimeOffset createdAt,
        string documentType = "CC") => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = $"Persona {documento}",
        DocumentType = documentType,
        DocumentNumber = documento,
        Email = "persona@correo.co",
        Status = status,
        CreatedAt = createdAt,
        ExpiresAt = createdAt.AddDays(1),
        Provider = BiometricProviders.Mock,
    };

    private static async Task<IProcedureInstanceRepository> RepoConAsync(
        string dbName,
        params ProcedureInstanceBiometricValidation[] validaciones)
    {
        var db = NewContext(dbName);
        db.Set<ProcedureInstanceBiometricValidation>().AddRange(validaciones);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new ProcedureInstanceRepository(db);
    }

    [Fact]
    public async Task Cuenta_personas_no_validaciones_y_usa_el_estado_de_la_mas_reciente()
    {
        // Ana: 3 validaciones (rechazada → rechazada → aprobada). Cuenta como UNA persona aprobada.
        // Luis: 2 validaciones, la última rechazada. Cuenta como UNA persona rechazada.
        var repo = await RepoConAsync(
            nameof(Cuenta_personas_no_validaciones_y_usa_el_estado_de_la_mas_reciente),
            Validacion(TenantId, "111", BiometricEstados.Rechazado, Base),
            Validacion(TenantId, "111", BiometricEstados.Rechazado, Base.AddHours(1)),
            Validacion(TenantId, "111", BiometricEstados.Aprobado, Base.AddHours(2)),
            Validacion(TenantId, "222", BiometricEstados.Enviado, Base),
            Validacion(TenantId, "222", BiometricEstados.Rechazado, Base.AddHours(3)));

        var counts = await repo.CountBiometricPersonsByEstadoAsync(
            TenantId, null, Base.AddDays(1), TestContext.Current.CancellationToken);

        counts.Values.Sum().Should().Be(2, "hay 5 validaciones pero solo 2 personas");
        counts.GetValueOrDefault(BiometricEstados.Aprobado).Should().Be(1);
        counts.GetValueOrDefault(BiometricEstados.Rechazado).Should().Be(1);
    }

    [Fact]
    public async Task El_documento_se_normaliza_igual_que_en_la_grilla()
    {
        // Mismo documento escrito distinto (espacios/minúsculas) = una sola persona, como en la grilla.
        var repo = await RepoConAsync(
            nameof(El_documento_se_normaliza_igual_que_en_la_grilla),
            Validacion(TenantId, " 333 ", BiometricEstados.Aprobado, Base, documentType: "cc"),
            Validacion(TenantId, "333", BiometricEstados.Aprobado, Base.AddHours(1), documentType: "CC"));

        var counts = await repo.CountBiometricPersonsByEstadoAsync(
            TenantId, null, Base.AddDays(1), TestContext.Current.CancellationToken);

        counts.Values.Sum().Should().Be(1);
    }

    [Fact]
    public async Task No_cuenta_personas_de_otro_tenant()
    {
        var repo = await RepoConAsync(
            nameof(No_cuenta_personas_de_otro_tenant),
            Validacion(TenantId, "444", BiometricEstados.Aprobado, Base),
            Validacion(OtroTenant, "555", BiometricEstados.Aprobado, Base));

        var counts = await repo.CountBiometricPersonsByEstadoAsync(
            TenantId, null, Base.AddDays(1), TestContext.Current.CancellationToken);

        counts.Values.Sum().Should().Be(1);
    }

    [Fact]
    public async Task Una_validacion_en_proceso_con_el_enlace_vencido_cuenta_como_expirada()
    {
        // El worker puede no haber marcado el estado todavía: en base sigue 'en_proceso' pero el enlace
        // ya venció. La fila se lee "Expirado", así que el KPI tiene que contarla ahí — si contara por
        // el estado crudo, el contador de "En proceso" diría uno más de los que se ven en la grilla.
        var db = NewContext(nameof(Una_validacion_en_proceso_con_el_enlace_vencido_cuenta_como_expirada));
        var vencida = Validacion(TenantId, "888", BiometricEstados.EnProceso, Base);
        vencida.ExpiresAt = Base.AddHours(1);
        var enVuelo = Validacion(TenantId, "999", BiometricEstados.EnProceso, Base);
        enVuelo.ExpiresAt = Base.AddDays(5);
        db.Set<ProcedureInstanceBiometricValidation>().AddRange(vencida, enVuelo);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repo = new ProcedureInstanceRepository(db);

        var counts = await repo.CountBiometricPersonsByEstadoAsync(
            TenantId, null, Base.AddDays(1), TestContext.Current.CancellationToken);

        counts.GetValueOrDefault(BiometricEstados.EnProceso).Should().Be(1);
        counts.GetValueOrDefault(BiometricEstados.Expirado).Should().Be(1);
    }

    [Fact]
    public async Task Con_el_mismo_created_at_el_desempate_es_estable_entre_la_pagina_y_el_conteo()
    {
        // Dos validaciones de la misma persona creadas en el mismo instante: sin desempate, la página y
        // el conteo (dos consultas distintas) podían quedarse con estados diferentes y descuadrar.
        var db = NewContext(nameof(Con_el_mismo_created_at_el_desempate_es_estable_entre_la_pagina_y_el_conteo));
        var a = Validacion(TenantId, "1010", BiometricEstados.Rechazado, Base);
        var b = Validacion(TenantId, "1010", BiometricEstados.Aprobado, Base);
        db.Set<ProcedureInstanceBiometricValidation>().AddRange(a, b);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repo = new ProcedureInstanceRepository(db);

        var (rows, _) = await repo.ListBiometricValidationsGroupedByPersonAsync(
            TenantId, 0, 20, null, Base.AddDays(1), TestContext.Current.CancellationToken);
        var counts = await repo.CountBiometricPersonsByEstadoAsync(
            TenantId, null, Base.AddDays(1), TestContext.Current.CancellationToken);

        rows.Should().HaveCount(1);
        counts.Values.Sum().Should().Be(1);
        counts.GetValueOrDefault(rows[0].Status).Should()
            .Be(1, "el contador y la fila describen la misma validación");
    }

    [Fact]
    public async Task Respeta_los_filtros_activos_de_la_grilla()
    {
        var repo = await RepoConAsync(
            nameof(Respeta_los_filtros_activos_de_la_grilla),
            Validacion(TenantId, "666", BiometricEstados.Aprobado, Base),
            Validacion(TenantId, "777", BiometricEstados.Rechazado, Base));

        var counts = await repo.CountBiometricPersonsByEstadoAsync(
            TenantId,
            new BiometricPersonGroupFilter { DocumentNumber = "666" },
            Base.AddDays(1),
            TestContext.Current.CancellationToken);

        counts.Values.Sum().Should().Be(1);
        counts.GetValueOrDefault(BiometricEstados.Aprobado).Should().Be(1);
    }
}
