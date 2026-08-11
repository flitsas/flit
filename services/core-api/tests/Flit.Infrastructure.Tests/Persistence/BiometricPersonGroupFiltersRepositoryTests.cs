using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// Filtros de la grilla de Identidad agrupada por persona. Cada filtro tiene que devolver exactamente
/// las filas que el gestor espera ver: por documento parcial (teclea los últimos dígitos), por estado
/// EFECTIVO (una validación no aprobada con el enlace vencido es "expirada" aunque en base siga
/// enviada o en proceso), por nombre parcial, por vigencia y por fechas.
/// </summary>
public sealed class BiometricPersonGroupFiltersRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    /// <summary>Ahora de referencia; las validaciones se siembran relativas a este instante.</summary>
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static ProcedureInstanceBiometricValidation Validacion(
        string documento,
        string status,
        string nombre = "Persona de prueba",
        DateTimeOffset? createdAt = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? validatedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        Name = nombre,
        DocumentType = "CC",
        DocumentNumber = documento,
        Email = "persona@correo.co",
        Status = status,
        CreatedAt = createdAt ?? Now.AddDays(-1),
        ExpiresAt = expiresAt ?? Now.AddDays(1),
        ValidatedAt = validatedAt,
        Provider = BiometricProviders.Mock,
    };

    private static async Task<IProcedureInstanceRepository> RepoAsync(
        string dbName,
        params ProcedureInstanceBiometricValidation[] validaciones)
    {
        var db = NewContext(dbName);
        db.Set<ProcedureInstanceBiometricValidation>().AddRange(validaciones);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new ProcedureInstanceRepository(db);
    }

    private static async Task<IReadOnlyList<string>> DocumentosAsync(
        IProcedureInstanceRepository repo,
        BiometricPersonGroupFilter filter)
    {
        var (rows, _) = await repo.ListBiometricValidationsGroupedByPersonAsync(
            TenantId, 0, 50, filter, Now, TestContext.Current.CancellationToken);
        return rows.Select(r => r.DocumentNumber).ToList();
    }

    [Fact]
    public async Task Documento_filtra_por_coincidencia_parcial()
    {
        var repo = await RepoAsync(
            nameof(Documento_filtra_por_coincidencia_parcial),
            Validacion("1037669356", BiometricEstados.Aprobado),
            Validacion("1035390008", BiometricEstados.Rechazado));

        // El gestor teclea los últimos dígitos, no la cédula entera.
        var soloUno = await DocumentosAsync(repo, new BiometricPersonGroupFilter { DocumentNumber = "9356" });
        soloUno.Should().ContainSingle().Which.Should().Be("1037669356");

        // Un prefijo común devuelve las dos.
        var ambas = await DocumentosAsync(repo, new BiometricPersonGroupFilter { DocumentNumber = "103" });
        ambas.Should().HaveCount(2);
    }

    [Fact]
    public async Task Documento_trata_los_comodines_como_texto_literal()
    {
        var repo = await RepoAsync(
            nameof(Documento_trata_los_comodines_como_texto_literal),
            Validacion("1037669356", BiometricEstados.Aprobado));

        // "%" no puede colarse como comodín y devolver todo.
        var conComodin = await DocumentosAsync(repo, new BiometricPersonGroupFilter { DocumentNumber = "%" });
        conComodin.Should().BeEmpty();
    }

    [Fact]
    public async Task Estado_expirado_incluye_las_vencidas_que_el_worker_no_ha_marcado()
    {
        var repo = await RepoAsync(
            nameof(Estado_expirado_incluye_las_vencidas_que_el_worker_no_ha_marcado),
            // Marcada por el worker.
            Validacion("111", BiometricEstados.Expirado),
            // Enlace vencido pero el estado persistido sigue en proceso: en pantalla se lee "Expirado".
            Validacion("222", BiometricEstados.EnProceso, expiresAt: Now.AddHours(-2)),
            // En vuelo de verdad.
            Validacion("333", BiometricEstados.EnProceso, expiresAt: Now.AddDays(2)));

        var expiradas = await DocumentosAsync(
            repo, new BiometricPersonGroupFilter { Status = BiometricEstados.Expirado });
        expiradas.Should().BeEquivalentTo(["111", "222"]);

        var enProceso = await DocumentosAsync(
            repo, new BiometricPersonGroupFilter { Status = BiometricEstados.EnProceso });
        enProceso.Should().ContainSingle().Which.Should().Be("333");
    }

    [Fact]
    public async Task Estado_filtra_por_la_validacion_mas_reciente_de_la_persona()
    {
        var repo = await RepoAsync(
            nameof(Estado_filtra_por_la_validacion_mas_reciente_de_la_persona),
            Validacion("444", BiometricEstados.Rechazado, createdAt: Now.AddDays(-5)),
            Validacion("444", BiometricEstados.Aprobado, createdAt: Now.AddDays(-1), validatedAt: Now.AddDays(-1)));

        var aprobadas = await DocumentosAsync(
            repo, new BiometricPersonGroupFilter { Status = BiometricEstados.Aprobado });
        aprobadas.Should().ContainSingle();

        // La rechazada es historia de esa persona: la grilla ya no la representa.
        var rechazadas = await DocumentosAsync(
            repo, new BiometricPersonGroupFilter { Status = BiometricEstados.Rechazado });
        rechazadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Nombre_filtra_parcial_y_sin_distinguir_mayusculas()
    {
        var repo = await RepoAsync(
            nameof(Nombre_filtra_parcial_y_sin_distinguir_mayusculas),
            Validacion("555", BiometricEstados.Aprobado, nombre: "WILLYN SMITH LONDOÑO"),
            Validacion("666", BiometricEstados.Aprobado, nombre: "Juan Motoya"));

        var encontrados = await DocumentosAsync(repo, new BiometricPersonGroupFilter { Name = "willyn" });
        encontrados.Should().ContainSingle().Which.Should().Be("555");
    }

    [Fact]
    public async Task Vigencia_separa_vigente_por_vencer_y_vencida()
    {
        var repo = await RepoAsync(
            nameof(Vigencia_separa_vigente_por_vencer_y_vencida),
            // Aprobada hace 2 días: le quedan 28 de los 30.
            Validacion("777", BiometricEstados.Aprobado, validatedAt: Now.AddDays(-2)),
            // Aprobada hace 27 días: le quedan 3 → por vencer.
            Validacion("888", BiometricEstados.Aprobado, validatedAt: Now.AddDays(-27)),
            // Aprobada hace 40 días: vigencia vencida.
            Validacion("999", BiometricEstados.Aprobado, validatedAt: Now.AddDays(-40)));

        (await DocumentosAsync(repo, new BiometricPersonGroupFilter { VigenciaEstado = "vigente" }))
            .Should().BeEquivalentTo(["777", "888"]);
        (await DocumentosAsync(repo, new BiometricPersonGroupFilter { VigenciaEstado = "por_vencer" }))
            .Should().ContainSingle().Which.Should().Be("888");
        (await DocumentosAsync(repo, new BiometricPersonGroupFilter { VigenciaEstado = "vencida" }))
            .Should().ContainSingle().Which.Should().Be("999");
    }

    [Fact]
    public async Task VenceEnDias_devuelve_solo_las_que_caducan_dentro_de_la_ventana()
    {
        var repo = await RepoAsync(
            nameof(VenceEnDias_devuelve_solo_las_que_caducan_dentro_de_la_ventana),
            Validacion("101", BiometricEstados.Aprobado, validatedAt: Now.AddDays(-28)), // quedan 2
            Validacion("102", BiometricEstados.Aprobado, validatedAt: Now.AddDays(-20))); // quedan 10

        var enTresDias = await DocumentosAsync(repo, new BiometricPersonGroupFilter { VenceEnDias = 3 });
        enTresDias.Should().ContainSingle().Which.Should().Be("101");
    }

    [Fact]
    public async Task Fechas_de_registro_acotan_por_la_validacion_mas_reciente()
    {
        var repo = await RepoAsync(
            nameof(Fechas_de_registro_acotan_por_la_validacion_mas_reciente),
            Validacion("201", BiometricEstados.Aprobado, createdAt: Now.AddDays(-10)),
            Validacion("202", BiometricEstados.Aprobado, createdAt: Now.AddDays(-2)));

        var recientes = await DocumentosAsync(
            repo, new BiometricPersonGroupFilter { CreatedFrom = Now.AddDays(-5) });
        recientes.Should().ContainSingle().Which.Should().Be("202");

        var antiguas = await DocumentosAsync(
            repo, new BiometricPersonGroupFilter { CreatedTo = Now.AddDays(-5) });
        antiguas.Should().ContainSingle().Which.Should().Be("201");
    }

    [Fact]
    public async Task Los_KPIs_cuentan_lo_mismo_que_devuelve_la_pagina_con_el_mismo_filtro()
    {
        var repo = await RepoAsync(
            nameof(Los_KPIs_cuentan_lo_mismo_que_devuelve_la_pagina_con_el_mismo_filtro),
            Validacion("301", BiometricEstados.EnProceso, expiresAt: Now.AddHours(-1)),
            Validacion("302", BiometricEstados.Expirado),
            Validacion("303", BiometricEstados.Aprobado, validatedAt: Now.AddDays(-1)));

        var filtro = new BiometricPersonGroupFilter { Status = BiometricEstados.Expirado };
        var documentos = await DocumentosAsync(repo, filtro);
        var counts = await repo.CountBiometricPersonsByEstadoAsync(
            TenantId, filtro, Now, TestContext.Current.CancellationToken);

        documentos.Should().HaveCount(2);
        counts.Values.Sum().Should().Be(documentos.Count);
        counts.GetValueOrDefault(BiometricEstados.Expirado).Should().Be(2);
    }
}
