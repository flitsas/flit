using System.Reflection;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Queries.Domain.Documentos;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12791 (Épica #12760) — persistencia de la vigencia de los consolidados en los contratos:
/// la lectura lean del detalle (<see cref="ProcedureInstanceRepository.GetConsolidadoSourcesAsync"/>)
/// y la garantía de que el listado del gestor la deriva del grafo YA cargado (sin N+1).
/// <para>Uso de ejemplo: <c>await new ProcedureInstanceRepository(db).GetConsolidadoSourcesAsync(id, tenant, ct)</c>
/// ⇒ <c>{ "consolidado": "system" }</c>.</para>
/// </summary>
public sealed class ConsolidadoVigenciaRepositoryTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Base = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AC2_GetConsolidadoSources_DevuelveElSourceDelMasRecientePorTipo_YOmiteLosAusentes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(Guid.NewGuid().ToString());
        var instance = Instancia(Tenant, "R1");
        db.ProcedureInstances.Add(instance);
        db.ProcedureInstanceAttachments.AddRange(
            Adjunto(instance, "consolidado", "system", Base),
            Adjunto(instance, "consolidado", "user", Base.AddMinutes(5)),
            Adjunto(instance, "fur", "system", Base.AddMinutes(9)));
        await db.SaveChangesAsync(ct);

        var sources = await new ProcedureInstanceRepository(db).GetConsolidadoSourcesAsync(instance.Id, Tenant, ct);

        sources.Should().ContainKey("consolidado").WhoseValue.Should().Be("user");
        sources.Should().NotContainKey("consolidado_maestro", "sin adjunto del tipo ⇒ inexistente (AC2)");
        sources.Should().NotContainKey("fur");
        sources.ContainsKey("CONSOLIDADO").Should().BeTrue("la búsqueda por tipo no distingue mayúsculas");
    }

    [Fact]
    public async Task AC2_GetConsolidadoSources_NoLeeAdjuntosDeOtroTenant()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(Guid.NewGuid().ToString());
        var instance = Instancia(Tenant, "R1");
        db.ProcedureInstances.Add(instance);
        var ajeno = Adjunto(instance, "consolidado_maestro", "system", Base);
        ajeno.TenantId = Guid.NewGuid();
        db.ProcedureInstanceAttachments.Add(ajeno);
        await db.SaveChangesAsync(ct);

        var sources = await new ProcedureInstanceRepository(db).GetConsolidadoSourcesAsync(instance.Id, Tenant, ct);

        sources.Should().BeEmpty();
    }

    [Fact]
    public async Task AC3_ListadoFiltrado_CargaLosAdjuntosEnElGrafo_YLaVigenciaSeDerivaSinConsultaPorFila()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(Guid.NewGuid().ToString());
        var conPdf = Instancia(Tenant, "R1");
        conPdf.ConsolidadoWizardVigente = true;
        conPdf.ConsolidadoWizardGeneradoEn = Base;
        var sinPdf = Instancia(Tenant, "R2");
        db.ProcedureInstances.AddRange(conPdf, sinPdf);
        db.ProcedureInstanceAttachments.Add(Adjunto(conPdf, "consolidado", "system", Base));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var (items, _) = await new ProcedureInstanceRepository(db).ListWithSummaryGraphFilteredAsync(
            Tenant, 0, 20, new ProcedureInstanceListFilter(), ProcedureInstanceSortBy.Default, SortDirection.Descending, ct);

        // El grafo del listado ya trae Attachments (Include en la misma consulta dividida): la
        // proyección no necesita volver a la base por cada fila.
        ConsolidadoVigenciaProyeccion.Wizard(items.Single(i => i.Id == conPdf.Id)).Estado
            .Should().Be(ConsolidadoVigencia.EstadoVigente);
        ConsolidadoVigenciaProyeccion.Wizard(items.Single(i => i.Id == sinPdf.Id)).Estado
            .Should().Be(ConsolidadoVigencia.EstadoInexistente);
    }

    public static TheoryData<string> MetodosDelListado() =>
    [
        "ListWithSummaryGraphAsync",
        "ListWithSummaryGraphFilteredAsync",
    ];

    /// <summary>
    /// AC3 — verificación ESTÁTICA (la suite no levanta Postgres): los métodos del listado cargan
    /// <c>Attachments</c> con <c>Include</c> dentro de <c>AsSplitQuery</c>. Una consulta dividida emite
    /// un número FIJO de comandos (uno por colección incluida), independiente del número de filas; si
    /// alguien quita el Include, la vigencia saldría «inexistente» para todo o empujaría a una lectura
    /// por fila (N+1).
    /// </summary>
    [Theory]
    [MemberData(nameof(MetodosDelListado))]
    public void AC3_ElListadoIncluyeLosAdjuntosEnLaConsultaDividida(string metodo)
    {
        var fuente = Fuente();
        var inicio = 0;
        var cuerpos = new List<string>();
        while ((inicio = fuente.IndexOf(" " + metodo + "(", inicio, StringComparison.Ordinal)) > 0)
        {
            var fin = fuente.IndexOf("\n    public ", inicio + 1, StringComparison.Ordinal);
            cuerpos.Add(fin > 0 ? fuente[inicio..fin] : fuente[inicio..]);
            inicio++;
        }

        cuerpos.Should().NotBeEmpty($"el método {metodo} debe existir en el repositorio");
        foreach (var cuerpo in cuerpos.Where(c => c.Contains(".Include(", StringComparison.Ordinal)))
        {
            cuerpo.Should().Contain(".AsSplitQuery()");
            Regex.IsMatch(cuerpo, @"\.Include\(\w+ => \w+\.Attachments\)").Should().BeTrue(
                $"{metodo} debe incluir Attachments para derivar la vigencia sin N+1");
        }
    }

    [Fact]
    public void AC3_LaLecturaDelDetalleEsLean_SoloTipoSourceYFecha()
    {
        var fuente = Fuente();
        var inicio = fuente.IndexOf("GetConsolidadoSourcesAsync(", StringComparison.Ordinal);
        inicio.Should().BeGreaterThan(0);
        var fin = fuente.IndexOf("\n    public ", inicio, StringComparison.Ordinal);
        var cuerpo = fuente[inicio..fin];

        cuerpo.Should().Contain(".AsNoTracking()");
        cuerpo.Should().Contain(".Select(a => new { a.Tipo, a.Source, a.UploadedAt })");
        cuerpo.Should().NotContain(".Include(");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private static string Fuente()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return File.ReadAllText(Path.Combine(
            dir!.FullName, "src", "Flit.Infrastructure", "Persistence", "Repositories", "ProcedureInstanceRepository.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static ProcedureInstance Instancia(Guid tenantId, string reference) => new()
    {
        ProcedureType = ProcedureTypeFixture.For("traspaso"),
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = reference,
        Consecutivo = RadicadoFixture.ConsecutivoDe(reference),
        Status = TramiteEstado.Entregado,
        CreatedByUserId = Guid.NewGuid(),
        CreatedAt = Base,
    };

    private static ProcedureInstanceAttachment Adjunto(ProcedureInstance instance, string tipo, string source, DateTimeOffset uploadedAt) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = instance.TenantId,
        ProcedureInstanceId = instance.Id,
        Tipo = tipo,
        Filename = tipo + ".pdf",
        Mimetype = "application/pdf",
        Sha256 = new string('b', 64),
        StoragePath = "x/" + tipo + ".pdf",
        Source = source,
        UploadedAt = uploadedAt,
    };

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
