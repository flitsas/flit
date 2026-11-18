using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Domain.Derivacion;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Derivacion;

/// <summary>
/// Punto 4, incremento 2 (diseño §5.1 fila 1) — respuesta HTTP del individual según el resultado: 201
/// <c>{ tramite_id, numero, url, ya_existia }</c> (también con <c>ya_existia=true</c>); 409 <c>{ code: YA_TIENE_TRAMITE, error,
/// tramite_id, numero }</c>; 422 <c>{ code: NO_ELEGIBLE, error, motivo_codigo, motivo }</c> para los no elegibles y los rechazos de
/// Trámites; 503 <c>{ code: TRAMITES_NO_DISPONIBLE, error }</c> con <c>Retry-After</c> solo si es transitorio. Sin base.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~RespuestaDerivacionTests
/// </code>
/// </example>
public sealed class RespuestaDerivacionTests
{
    private static readonly Guid Tramite = Guid.Parse("0199f3a1-7777-7000-8000-000000000077");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Creado_es_201_con_url_y_ya_existia(bool yaExistia)
    {
        var r = RespuestaDerivacion.Desde(new ResultadoBorrador.Creado(Tramite, "TR-1", yaExistia), "https://t/tramites/x?t=y");

        r.StatusCode.Should().Be(201);
        r.RetryAfterSegundos.Should().BeNull();
        var cuerpo = Json(r);
        cuerpo.GetProperty("tramite_id").GetGuid().Should().Be(Tramite);
        cuerpo.GetProperty("numero").GetString().Should().Be("TR-1");
        cuerpo.GetProperty("url").GetString().Should().Be("https://t/tramites/x?t=y");
        cuerpo.GetProperty("ya_existia").GetBoolean().Should().Be(yaExistia);
    }

    [Fact]
    public void Ya_tiene_trámite_es_409_con_el_trámite()
    {
        var r = RespuestaDerivacion.YaTieneTramite(Tramite, "TR-2");

        r.StatusCode.Should().Be(409);
        var cuerpo = Json(r);
        cuerpo.GetProperty("code").GetString().Should().Be(MotivoDerivacion.YaTieneTramite);
        cuerpo.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
        cuerpo.GetProperty("tramite_id").GetGuid().Should().Be(Tramite);
        cuerpo.GetProperty("numero").GetString().Should().Be("TR-2");
    }

    [Fact]
    public void Rechazo_de_Trámites_es_422_NO_ELEGIBLE_con_su_código_y_motivo()
    {
        var r = RespuestaDerivacion.Desde(new ResultadoBorrador.Rechazado(MotivoDerivacion.OtNoHabilitado, "Motivo de Trámites."), "u");

        r.StatusCode.Should().Be(422);
        var cuerpo = Json(r);
        cuerpo.GetProperty("code").GetString().Should().Be(RespuestaDerivacion.CodigoNoElegible);
        cuerpo.GetProperty("motivo_codigo").GetString().Should().Be(MotivoDerivacion.OtNoHabilitado);
        cuerpo.GetProperty("motivo").GetString().Should().Be("Motivo de Trámites.");
        cuerpo.GetProperty("error").GetString().Should().Be("Motivo de Trámites.");
    }

    [Fact]
    public void No_elegible_es_422_con_el_mensaje_del_motivo()
    {
        var r = RespuestaDerivacion.NoElegible(MotivoDerivacion.NoViable);

        r.StatusCode.Should().Be(422);
        var cuerpo = Json(r);
        cuerpo.GetProperty("code").GetString().Should().Be("NO_ELEGIBLE");
        cuerpo.GetProperty("motivo_codigo").GetString().Should().Be(MotivoDerivacion.NoViable);
        cuerpo.GetProperty("motivo").GetString().Should().Be(MotivoDerivacion.Mensaje(MotivoDerivacion.NoViable));
    }

    [Theory]
    [InlineData(true, 30)]
    [InlineData(false, null)]
    public void No_disponible_es_503_y_Retry_After_solo_si_es_transitorio(bool transitorio, int? retryAfter)
    {
        var r = RespuestaDerivacion.Desde(new ResultadoBorrador.NoDisponible(transitorio), "u");

        r.StatusCode.Should().Be(503);
        r.RetryAfterSegundos.Should().Be(retryAfter);
        Json(r).GetProperty("code").GetString().Should().Be(MotivoDerivacion.TramitesNoDisponible);
    }

    private static JsonElement Json(DerivacionHttpResult r) => JsonSerializer.SerializeToElement(r.Value);
}
