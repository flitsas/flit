using Flit.Infrastructure.OtClientProcedures;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.OtClientProcedures;

/// <summary>
/// HU9 (Caso C 2026-11-09) — el puerto <c>IVehiclePlateGroupResolver</c> del preview del paso 1 aplica la
/// MISMA regla que la bandeja OT (HU8): listas exactas de carro / moto / remolque y catálogo para remolque /
/// maquinaria. El AUTOMOTOR por defecto del catálogo no crea grupo.
/// <para>Uso de ejemplo: <c>new VehiclePlateGroupResolver(catalogo).ResolveContractValueAsync("CAMIÓN GRÚA")</c>
/// ⇒ <c>"MAQUINARIA"</c>.</para>
/// </summary>
public sealed class VehiclePlateGroupResolverTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("CAMIÓN GRÚA", "MAQUINARIA")]
    [InlineData("Retroexcavadora", "MAQUINARIA")]
    [InlineData("Camioneta", "CARRO")]
    [InlineData("MOTOCICLETA", "MOTO")]
    [InlineData("Motocarro", "MOTOCARRO")]
    [InlineData("TRICIMOTO", "MOTOCARRO")]
    [InlineData("SEMIRREMOLQUE", "REMOLQUE")]
    [InlineData("REMOLQUE", "REMOLQUE")]
    public async Task ClaseConGrupo_DevuelveElValorDeContrato(string clase, string esperado)
    {
        var grupo = await new VehiclePlateGroupResolver(new FakeFurCatalog()).ResolveContractValueAsync(clase, Ct);
        grupo.Should().Be(esperado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SIN CLASE")]
    [InlineData("NAVE ESPACIAL")]
    public async Task VaciaSinClaseODesconocida_DevuelveNull(string? clase)
    {
        var grupo = await new VehiclePlateGroupResolver(new FakeFurCatalog()).ResolveContractValueAsync(clase, Ct);
        grupo.Should().BeNull();
    }

    /// <summary>Subconjunto del seed <c>39-HU10919-vehicle-classification-fur.sql</c>; sin match ⇒ AUTOMOTOR.</summary>
    private sealed class FakeFurCatalog : IFurTemplateResolver
    {
        private static readonly IReadOnlyDictionary<string, FurClassificationMatch> Map =
            new (string Clase, FurTemplateFormat Formato)[]
            {
                ("CAMIONETA", FurTemplateFormat.Automotor),
                ("MOTOCICLETA", FurTemplateFormat.Automotor),
                ("SIN CLASE", FurTemplateFormat.Automotor),
                ("CAMIÓN GRÚA", FurTemplateFormat.Maquinaria),
                ("RETROEXCAVADORA", FurTemplateFormat.Maquinaria),
                ("REMOLQUE", FurTemplateFormat.Remolques),
                ("SEMIREMOLQUE", FurTemplateFormat.Remolques),
            }.ToDictionary(
                r => FurClassificationNormalizer.Normalize(r.Clase),
                r => new FurClassificationMatch(r.Formato, null),
                StringComparer.Ordinal);

        public async Task<FurTemplateFormat> ResolveAsync(string? vehicleClass, CancellationToken ct = default) =>
            (await ResolveMatchAsync(vehicleClass, ct)).Format;

        public Task<FurClassificationMatch> ResolveMatchAsync(string? vehicleClass, CancellationToken ct = default) =>
            Task.FromResult(FurTemplateResolution.ResolveMatch(vehicleClass, Map));

        public Task<IReadOnlyList<FurClassificationCatalogItem>> ListCatalogAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FurClassificationCatalogItem>>([]);
    }
}
