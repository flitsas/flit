using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;
using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Celdas RUNT_PROPIETARIO y RUNT_LOCATARIO (HU #13792, F05-C4, F05-C9, F05-C10, decisión Q2; desde ADR-0075 por el bus): cada
/// persona del rol es un trabajo con el patrón de <see cref="EjecutorPorPersonas"/>. La persona natural va al RUNT persona
/// (<c>ConsultarConductor</c>, fuente del bus <c>runt_propietario</c>/<c>runt_locatario</c>) con su tipo y número; la persona con NIT
/// va al RUES (<c>ConsultarRues</c>, fuente <see cref="FuentesBus.Rues"/>, proveedor <c>verifik_rues</c>) con el NIT sin dígito de
/// verificación (<see cref="ConstructorSolicitudRuntPersona"/>). La respuesta de cada persona se lee con
/// <see cref="MapeoRuntPersona"/> (<see cref="ResultadoPersonaRunt"/>: inscripción y licencias) o <see cref="MapeoRues"/>
/// (<see cref="ResultadoRues"/>: estado de la matrícula y última renovación): nunca nombres, razón social ni documento. Se registra
/// una instancia por fuente en <c>Modulos.AddMotorConsultas</c>.
/// </summary>
/// <example>
/// <code>
/// services.AddScoped&lt;IEjecutorCelda&gt;(sp =&gt; ActivatorUtilities.CreateInstance&lt;EjecutorCeldaRuntPersona&gt;(sp, Fuente.RuntPropietario));
/// </code>
/// </example>
internal sealed class EjecutorCeldaRuntPersona : EjecutorPorPersonas
{
    public const string ConsultaRuntPersona = "RUNT_PERSONA";
    public const string ConsultaRues = "RUES";

    public EjecutorCeldaRuntPersona(Fuente fuente, TimeProvider reloj, ILogger<EjecutorCeldaRuntPersona> log)
        : base(reloj, log)
    {
        if (fuente is not (Fuente.RuntPropietario or Fuente.RuntLocatario))
            throw new ArgumentOutOfRangeException(nameof(fuente), fuente, "El ejecutor RUNT persona atiende RUNT_PROPIETARIO o RUNT_LOCATARIO.");
        Fuente = fuente;
    }

    public override Fuente Fuente { get; }

    protected override string ConsultaPorDefecto => ConsultaRuntPersona;

    /// <summary>NIT sin dígito de verificación: lo anterior al guion, solo dígitos.</summary>
    internal static string NitSinDigito(string numero)
    {
        ArgumentNullException.ThrowIfNull(numero);
        var guion = numero.IndexOf('-', StringComparison.Ordinal);
        var cuerpo = guion >= 0 ? numero[..guion] : numero;
        return new string([.. cuerpo.Where(char.IsAsciiDigit)]);
    }

    protected override ConsultaPersona Mapear(RespuestaConsulta respuesta)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        return respuesta.Fuente == FuentesBus.Rues
            ? ConsultaPersona.De(ConsultaRues, respuesta.Mapear<ConsultarRuesResponse, ResultadoRues>(MapeoRues.Mapear, MapeoRues.Error))
            : ConsultaPersona.De(ConsultaRuntPersona, respuesta.Mapear<ConsultarConductorResponse, ResultadoPersonaRunt>(MapeoRuntPersona.Mapear, MapeoRuntPersona.Error));
    }
}
