using Flit.Api.Grpc;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Xunit;

namespace Flit.Integration.Tests.Catalogs;

/// <summary>
/// HU #14602 — búsqueda exacta del color de la transformación 5 (ICT) en <c>catalogs.vehicle_colors</c> sobre
/// PostgreSQL real (el <c>ILIKE</c> no corre en EF InMemory). El catálogo real tiene ~20 000 colores: un término
/// común como «GRIS» aparece en 3 091 nombres, así que la búsqueda por «contiene» con máximo 100 por nombre dejaba
/// fuera el color exacto. <see cref="DbVehicleColorCatalog.FindByWordsAsync"/> trae los candidatos con las mismas
/// palabras sin límite y <see cref="IctPlacaCarroceria.CoincidenciaColor"/> decide la coincidencia exacta.
/// <para>
/// Uso de ejemplo: <c>var candidatos = await catalog.FindByWordsAsync("  azul   claro ")</c> →
/// <c>IctPlacaCarroceria.CoincidenciaColor("  azul   claro ", candidatos)?.Name</c> = «AZUL CLARO».
/// </para>
/// </summary>
public sealed class DbVehicleColorCatalogFindByWordsTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    // Prefijo propio para no depender del seed del catálogo real ni chocar con él.
    private const string Marca = "QHU14602";

    private static VehicleColor Color(string code, string name, bool activo = true) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        IsActive = activo,
        ExternalRefs = "{}",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task SembrarAsync(params VehicleColor[] colores)
    {
        await using var ctx = NewContext();
        ctx.VehicleColors.AddRange(colores);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string?> CoincidenciaAsync(string enviado)
    {
        await using var ctx = NewContext();
        var candidatos = await new DbVehicleColorCatalog(ctx)
            .FindByWordsAsync(enviado, TestContext.Current.CancellationToken);
        return IctPlacaCarroceria.CoincidenciaColor(enviado, candidatos)?.Name;
    }

    [PostgresFact]
    public async Task AC1_ColorExactoSeEncuentraAunqueHayaMasDeCienNombresQueLoContienenYOrdenanAntes()
    {
        // 150 nombres que CONTIENEN el término y ordenan alfabéticamente ANTES que él («A…» < «Q…»): con
        // SearchAsync (contiene + Take 100 por nombre) el exacto quedaba fuera del resultado.
        var ruido = Enumerable.Range(1, 150)
            .Select(i => Color($"QA{i:000}", $"A{i:000} {Marca}GRIS"))
            .ToArray();
        await SembrarAsync([.. ruido, Color("QG001", $"{Marca}GRIS")]);

        (await CoincidenciaAsync($"{Marca}GRIS")).Should().Be($"{Marca}GRIS");
    }

    [PostgresFact]
    public async Task AC3_IgnoraMayusculasYEspaciosDeMasEnElEnviadoYEnElCatalogo()
    {
        // El catálogo real tiene 57 nombres con espacios dobles internos.
        await SembrarAsync(
            Color("QD001", $"{Marca}AZUL CLARO"),
            Color("QD002", $"{Marca}VERDE  OLIVA"));

        (await CoincidenciaAsync($"  {Marca.ToLowerInvariant()}azul   claro ")).Should().Be($"{Marca}AZUL CLARO");
        (await CoincidenciaAsync($"{Marca}verde oliva")).Should().Be($"{Marca}VERDE  OLIVA");
    }

    [PostgresFact]
    public async Task AC2_ColorQueNoExisteOSoloComparteParteDelNombreNoCoincide()
    {
        await SembrarAsync(
            Color("QN001", $"{Marca}ROJO MUY OSCURO"),
            Color("QN002", $"{Marca}ROJO OSCURO METALICO"));

        (await CoincidenciaAsync($"{Marca}ROJO OSCURO")).Should().BeNull("solo hay nombres que lo contienen, no uno igual");
        (await CoincidenciaAsync($"{Marca}FUCSIA INVENTADO")).Should().BeNull();
    }

    [PostgresFact]
    public async Task ColorInactivoNoCoincide()
    {
        await SembrarAsync(Color("QI001", $"{Marca}AMBAR", activo: false));

        (await CoincidenciaAsync($"{Marca}AMBAR")).Should().BeNull();
    }

    [PostgresFact]
    public async Task LosComodinesDelTextoEnviadoSeTratanComoLiterales()
    {
        await SembrarAsync(Color("QW001", $"{Marca}PLATA"));

        await using var ctx = NewContext();
        var catalog = new DbVehicleColorCatalog(ctx);
        var ct = TestContext.Current.CancellationToken;

        (await catalog.FindByWordsAsync($"{Marca}%", ct)).Should().BeEmpty("«%» no debe actuar como comodín");
        (await catalog.FindByWordsAsync($"{Marca}PLAT_", ct)).Should().BeEmpty("«_» no debe actuar como comodín");
        (await catalog.FindByWordsAsync("   ", ct)).Should().BeEmpty();
    }
}
