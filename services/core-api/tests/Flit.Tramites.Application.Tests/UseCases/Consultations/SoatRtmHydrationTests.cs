using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// HU #10973 (Feature #10972) — el "Certificado de vigencia SOAT y RTM" leía <c>soat_estado</c>,
/// <c>rtm_estado</c> y <c>rtm_entidad</c>, pero los mappers de vehículo descartaban esos tres campos
/// pese a traerlos ya deserializados: tres celdas del documento salían siempre en blanco.
/// <para>Cubre además la NORMALIZACIÓN de <c>soat_estado</c>: esa llave es también el gate de
/// aprobación del OT (HU #10804) y el frontend la compara de forma estricta contra <c>"vigente"</c>
/// en minúscula, así que persistir el crudo del RUNT (<c>"VIGENTE"</c>) bloquearía la aprobación en
/// trámites con el SOAT vigente.</para>
/// </summary>
public sealed class SoatRtmHydrationTests
{
    private static string? Value(ConsultationResult r, string fieldKey) =>
        r.HydratedFields.SingleOrDefault(f => f.FieldKey == fieldKey)?.ValueText;

    // ── Verifik (proveedor cableado en producción) ────────────────────────────

    private static VerifikVehicleResponse VerifikResponse(
        string? soatEstado = "VIGENTE",
        string? rtmEstado = "VIGENTE",
        string? cdaExpide = "CDA LA 80") =>
        new()
        {
            Data = new VerifikVehicleData
            {
                InformacionGeneral = new VerifikInformacionGeneral
                {
                    EstadoDelVehiculo = "ACTIVO",
                    NoPlaca = "ABC123",
                    TieneGravamenes = "NO",
                    Prendas = "NO",
                },
                Soat = soatEstado is null ? [] : [new VerifikSoat { Estado = soatEstado }],
                TecnoMecanica = rtmEstado is null && cdaExpide is null
                    ? []
                    : [new VerifikTecnomecanica { Vigente = "SI", Estado = rtmEstado, CdaExpide = cdaExpide }],
                GarantiasMobiliarias = [],
            },
        };

    [Fact]
    public void Verifik_HidrataElEstadoDelSoatNormalizado()
    {
        var result = VerifikResultMapper.MapVehicle(VerifikResponse(soatEstado: "VIGENTE"));

        Value(result, SoatGate.FieldKey).Should().Be("vigente");
    }

    [Fact]
    public void Verifik_ElEstadoPersistidoNoBloqueaLaAprobacionDelOt()
    {
        // Regresión crítica: con el crudo "VIGENTE" el frontend (comparación estricta contra
        // 'vigente') dejaba puedeAprobar=false y bloqueaba al OT con el SOAT vigente.
        var result = VerifikResultMapper.MapVehicle(VerifikResponse(soatEstado: "VIGENTE"));
        var estado = Value(result, SoatGate.FieldKey);

        SoatGate.IsSatisfied(estado).Should().BeTrue();
        SoatGate.BlocksApproval(estado).Should().BeFalse();
        estado.Should().Be(SoatGate.Vigente); // literal exacto que compara el frontend
    }

    [Fact]
    public void Verifik_SoatVencidoSeNormalizaAVencido()
    {
        var result = VerifikResultMapper.MapVehicle(VerifikResponse(soatEstado: "NO VIGENTE"));

        Value(result, SoatGate.FieldKey).Should().Be(SoatGate.Vencido);
        SoatGate.BlocksApproval(Value(result, SoatGate.FieldKey)).Should().BeTrue();
    }

    [Fact]
    public void Verifik_HidrataEstadoYEntidadDeLaRtm()
    {
        var result = VerifikResultMapper.MapVehicle(
            VerifikResponse(rtmEstado: "VIGENTE", cdaExpide: "CDA LA 80"));

        Value(result, "rtm_estado").Should().Be("VIGENTE");
        Value(result, "rtm_entidad").Should().Be("CDA LA 80");
    }

    [Fact]
    public void Verifik_SinSoatNiRtmNoEscribeLasLlaves()
    {
        // Valor ausente en la consulta ⇒ NO se escribe la llave ⇒ celda en blanco (regla HU #10856).
        var result = VerifikResultMapper.MapVehicle(
            VerifikResponse(soatEstado: null, rtmEstado: null, cdaExpide: null));

        result.HydratedFields.Should().NotContain(f => f.FieldKey == SoatGate.FieldKey);
        result.HydratedFields.Should().NotContain(f => f.FieldKey == "rtm_estado");
        result.HydratedFields.Should().NotContain(f => f.FieldKey == "rtm_entidad");
    }

    [Fact]
    public void Verifik_RtmSinCdaNoInventaEntidad()
    {
        var result = VerifikResultMapper.MapVehicle(VerifikResponse(rtmEstado: "VIGENTE", cdaExpide: null));

        Value(result, "rtm_estado").Should().Be("VIGENTE");
        result.HydratedFields.Should().NotContain(f => f.FieldKey == "rtm_entidad");
    }

    // ── Kyverum RUNT (mismo defecto latente en soat_estado) ───────────────────

    private static KyverumRuntVehicleResponse KyverumResponse(string? soatEstado) =>
        new()
        {
            Data = new KyverumRuntVehicleData
            {
                Vehiculo = new KyverumRuntVehiculo { Placa = "ABC123", EstadoAutomotor = "ACTIVO" },
                Soat = soatEstado is null ? [] : [new KyverumRuntSoat { Estado = soatEstado }],
                Rtm = [],
            },
        };

    [Fact]
    public void Kyverum_TambienNormalizaElEstadoDelSoat()
    {
        // Antes escribía el crudo "VIGENTE": mismo bloqueo del OT que en Verifik.
        var result = KyverumRuntVehicleResultMapper.MapVehicle(KyverumResponse("VIGENTE"));

        Value(result, SoatGate.FieldKey).Should().Be(SoatGate.Vigente);
        SoatGate.BlocksApproval(Value(result, SoatGate.FieldKey)).Should().BeFalse();
    }

    [Fact]
    public void Kyverum_SinSoatNoEscribeLaLlave()
    {
        var result = KyverumRuntVehicleResultMapper.MapVehicle(KyverumResponse(null));

        result.HydratedFields.Should().NotContain(f => f.FieldKey == SoatGate.FieldKey);
    }
}
