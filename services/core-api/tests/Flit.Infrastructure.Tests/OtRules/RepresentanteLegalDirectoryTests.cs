using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.OtRules;

/// <summary>
/// HU #11198 / #11663 — <b>precarga</b> del nombre del representante legal desde el directorio de Admin,
/// ejercitada contra el adaptador REAL con EF InMemory. Es una regla de datos (tres tablas cruzadas), así
/// que probarla contra dobles no diría nada: lo que hay que verificar es que el cruce
/// representantes × puente × compañía sea el correcto.
///
/// <para>La HU #11663 retiró del puerto la pregunta "¿la compañía tiene un representante utilizable?" y
/// con ella las pruebas que la ejercitaban: esa decisión ya no la toma el directorio (ADR-0039). Lo que
/// queda —y lo que se prueba aquí— es el aporte de datos.</para>
///
/// <para>Uso de ejemplo:
/// <c>await directorio.BuscarNombreRepresentanteAsync(tenant, nit, "CC", "1090123456", ct)</c>
/// ⇒ <c>"Ana Representante"</c>.</para>
/// </summary>
public sealed class RepresentanteLegalDirectoryTests
{
    private const string Nit = "900123456";
    private const string DocType = "CC";
    private const string Doc = "1090123456";

    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public async Task ConElDocumentoDelTramite_DevuelveElNombreDeEsaPersona()
    {
        var ct = TestContext.Current.CancellationToken;
        using var context = NewContext();
        SeedRepresentante(context);
        await context.SaveChangesAsync(ct);

        var nombre = await Directorio(context)
            .BuscarNombreRepresentanteAsync(Tenant, Nit, DocType, Doc, ct);

        nombre.Should().Be("Ana Representante");
    }

    [Fact]
    public async Task SinDocumento_YConUnUnicoRepresentante_DevuelveSuNombre()
    {
        var ct = TestContext.Current.CancellationToken;
        using var context = NewContext();
        SeedRepresentante(context);
        await context.SaveChangesAsync(ct);

        var nombre = await Directorio(context)
            .BuscarNombreRepresentanteAsync(Tenant, Nit, null, null, ct);

        nombre.Should().Be("Ana Representante");
    }

    [Fact]
    public async Task SinDocumento_YConVariosRepresentantes_NoAdivina()
    {
        // Elegir entre varios imprimiría el nombre de alguien que no es en un documento legal, que es
        // peor que dejar el hueco.
        var ct = TestContext.Current.CancellationToken;
        using var context = NewContext();
        var (companiaId, _) = SeedRepresentante(context);
        SeedRepresentante(context, companiaId, documento: "1090999888", nombre: "Bruno");
        await context.SaveChangesAsync(ct);

        var nombre = await Directorio(context)
            .BuscarNombreRepresentanteAsync(Tenant, Nit, null, null, ct);

        nombre.Should().BeNull();
    }

    [Fact]
    public async Task RepresentanteInactivo_NoAportaNombre()
    {
        var ct = TestContext.Current.CancellationToken;
        using var context = NewContext();
        SeedRepresentante(context, activo: false);
        await context.SaveChangesAsync(ct);

        var nombre = await Directorio(context)
            .BuscarNombreRepresentanteAsync(Tenant, Nit, DocType, Doc, ct);

        nombre.Should().BeNull();
    }

    [Fact]
    public async Task SinNit_NoConsulta()
    {
        var ct = TestContext.Current.CancellationToken;
        using var context = NewContext();

        var nombre = await Directorio(context)
            .BuscarNombreRepresentanteAsync(Tenant, "  ", DocType, Doc, ct);

        nombre.Should().BeNull();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"rep-directory-{Guid.NewGuid()}")
            .Options);

    private static RepresentanteLegalDirectory Directorio(FlitDbContext context) => new(context);

    private static Guid SeedCompania(FlitDbContext context)
    {
        var id = Guid.NewGuid();
        context.RepresentedCompanies.Add(new RepresentedCompanyEntity
        {
            Id = id,
            TenantId = Tenant,
            DocumentType = "NIT",
            DocumentNumber = Nit,
            Name = "Empresa Compradora SAS",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return id;
    }

    private static (Guid CompaniaId, Guid RepresentativeId) SeedRepresentante(
        FlitDbContext context,
        Guid? companiaExistente = null,
        bool activo = true,
        string documento = Doc,
        string nombre = "Ana")
    {
        var companiaId = companiaExistente ?? SeedCompania(context);
        var representanteId = Guid.NewGuid();

        context.CompanyLegalRepresentatives.Add(new CompanyLegalRepresentativeEntity
        {
            Id = representanteId,
            TenantId = Tenant,
            RepresentedCompanyId = companiaId,
            DocumentType = DocType,
            DocumentNumber = documento,
            Name = nombre,
            FirstLastName = "Representante",
            Email = "rep@empresa.com",
            IsActive = activo,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        context.LegalRepresentativeCompanies.Add(new LegalRepresentativeCompanyEntity
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            RepresentativeId = representanteId,
            RepresentedCompanyId = companiaId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        return (companiaId, representanteId);
    }
}
