using Flit.Ict.Domain.Entities;
using Flit.Ict.Infrastructure.ExternalClients;
using FluentAssertions;
using Xunit;

namespace Flit.Ict.Application.Tests.Clients;

/// <summary>
/// Bug #13590 (hallazgo 2) — la <c>description</c> de la transformación ICT 7 (color) / 6 (carrocería)
/// es el valor nuevo que eligió el gestor y viaja tal cual (solo Trim) como <c>vehicle_color</c> /
/// <c>vehicle_body_type</c>, junto a su bandera <c>cambio_*</c>.
/// <para>Uso de ejemplo:
/// <code>
/// IctDraftFieldValuesMapper.Map(master) // código 7 "NEGRO" → cambio_color="true" + vehicle_color="NEGRO"
/// </code></para>
/// </summary>
public sealed class IctDraftFieldValuesMapperColorCarroceriaTests
{
    private static ExternalIntegrationMaster Master(params (int Codigo, string Descripcion)[] transformaciones)
    {
        var master = new ExternalIntegrationMaster { Id = Guid.NewGuid(), TenantId = Guid.NewGuid() };
        foreach (var (codigo, descripcion) in transformaciones)
        {
            master.Transformations.Add(new ExternalIntegrationMasterTransformation
            {
                MasterId = master.Id,
                TenantId = master.TenantId,
                IdTransformationType = codigo,
                Description = descripcion,
            });
        }

        return master;
    }

    [Fact]
    public void Color7ConDescripcion_EmiteBanderaYVehicleColor()
    {
        var valores = IctDraftFieldValuesMapper.Map(Master((7, "NEGRO")))
            .ToDictionary(f => f.FieldKey, f => f.ValueText);

        valores.Should().Contain("cambio_color", "true");
        valores.Should().Contain("vehicle_color", "NEGRO");
    }

    [Fact]
    public void Carroceria6ConDescripcionConEspacios_EmiteVehicleBodyTypeRecortado()
    {
        var valores = IctDraftFieldValuesMapper.Map(Master((6, " VOLQUETA ")))
            .ToDictionary(f => f.FieldKey, f => f.ValueText);

        valores.Should().Contain("cambio_carroceria", "true");
        valores.Should().Contain("vehicle_body_type", "VOLQUETA");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DescripcionVacia_SoloEmiteLaBandera(string descripcion)
    {
        var claves = IctDraftFieldValuesMapper.Map(Master((7, descripcion), (6, descripcion)))
            .Select(f => f.FieldKey)
            .ToList();

        claves.Should().Contain(["cambio_color", "cambio_carroceria"]);
        claves.Should().NotContain(["vehicle_color", "vehicle_body_type"]);
    }

    [Fact]
    public void ColorCarroceriaYCombustibleJuntos_TodasLasClavesSinDuplicados()
    {
        var master = Master((7, "NEGRO"), (6, "VOLQUETA"), (9, ""));
        master.NewVehicleFuelType = 3;

        var campos = IctDraftFieldValuesMapper.Map(master);

        campos.Select(f => f.FieldKey).Should().OnlyHaveUniqueItems();
        campos.ToDictionary(f => f.FieldKey, f => f.ValueText).Should().BeEquivalentTo(
            new Dictionary<string, string>
            {
                ["cambio_carroceria"] = "true",
                ["vehicle_body_type"] = "VOLQUETA",
                ["cambio_color"] = "true",
                ["vehicle_color"] = "NEGRO",
                ["cambio_combustible"] = "true",
                ["vehicle_fuel"] = "DIESEL",
            });
    }

    [Fact]
    public void CodigoRepetido_UsaLaPrimeraDescripcionNoVacia()
    {
        var campos = IctDraftFieldValuesMapper.Map(Master((7, " "), (7, "ROJO"), (7, "AZUL")));

        campos.Select(f => f.FieldKey).Should().OnlyHaveUniqueItems();
        campos.Should().ContainSingle(f => f.FieldKey == "vehicle_color")
            .Which.ValueText.Should().Be("ROJO");
    }
}
