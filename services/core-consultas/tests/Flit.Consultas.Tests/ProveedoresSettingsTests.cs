using Flit.Consultas.Api;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Flit.Consultas.Tests;

/// <summary>HU #13347 AC2 — un proveedor en modo real sin su secreto no arranca y el mensaje nombra la variable.</summary>
public sealed class ProveedoresSettingsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Fact]
    public void VerifikRealSinToken_NoArranca_YNombraLaVariable()
    {
        var act = () => ProveedoresSettings.Validate(Config(("ImprontaRunt:ApiKey", "x")));
        act.Should().Throw<InvalidOperationException>().WithMessage("*VERIFIK_API_TOKEN*VERIFIK_VEHICLE_MODE=real*");
    }

    [Fact]
    public void FasecoldaRealSinCredenciales_NombraLasDos()
    {
        var act = () => ProveedoresSettings.Validate(Config(
            ("Consultations:VerifikVehicleMode", "mock"), ("ImprontaRunt:ApiKey", "x"), ("Consultations:FasecoldaMode", "real")));
        act.Should().Throw<InvalidOperationException>().WithMessage("*FASECOLDA_API_USERNAME*FASECOLDA_API_PASSWORD*");
    }

    [Fact]
    public void SinLlaveDeKyverumRunt_NoArranca()
    {
        var act = () => ProveedoresSettings.Validate(Config(("Consultations:VerifikVehicleMode", "mock")));
        act.Should().Throw<InvalidOperationException>().WithMessage("*KYVERUM_RUNT_API_KEY*");
    }

    [Fact]
    public void TodoEnMockConLlaveDeKyverum_Arranca()
    {
        var act = () => ProveedoresSettings.Validate(Config(("Consultations:VerifikVehicleMode", "mock"), ("ImprontaRunt:ApiKey", "x")));
        act.Should().NotThrow();
    }

    [Fact]
    public void ConTodosLosSecretos_EnModoReal_Arranca()
    {
        var act = () => ProveedoresSettings.Validate(Config(
            ("Verifik:BearerToken", "t"), ("ImprontaRunt:ApiKey", "x"),
            ("Consultations:KyverumFinesMode", "real"), ("KyverumFines:ApiKey", "k"),
            ("Consultations:FasecoldaMode", "real"), ("Fasecolda:Username", "u"), ("Fasecolda:Password", "p")));
        act.Should().NotThrow();
    }
}
