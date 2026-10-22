using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Personas;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Ejecutor de las celdas RUNT_PROPIETARIO y RUNT_LOCATARIO (HU #13792, F05-C4, F05-C9, F05-C10, decisión Q2): consulta a cada
/// persona del rol con el patrón de <see cref="EjecutorPorPersonas"/>. La persona natural va al RUNT persona
/// (<see cref="IFuenteRuntPersona"/>, <c>ConsultarConductor</c>) con su tipo y número; la persona con NIT va al RUES
/// (<see cref="IFuenteRues"/>, <c>ConsultarRues</c>, proveedor <c>verifik_rues</c>) con el NIT sin dígito de verificación. El
/// resultado de cada persona es <see cref="ResultadoPersonaRunt"/> (inscripción y licencias) o <see cref="ResultadoRues"/>
/// (estado de la matrícula y última renovación): nunca nombres, razón social ni documento. Las llamadas van a nombre de la
/// empresa de la celda. Se registra una instancia por fuente en <c>Modulos.AddMotorConsultas</c>.
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

    private readonly IFuenteRuntPersona _runt;
    private readonly IFuenteRues _rues;

    public EjecutorCeldaRuntPersona(
        Fuente fuente, IFuenteRuntPersona runt, IFuenteRues rues, IProtectorDocumentoPersona protector, TimeProvider reloj,
        ILogger<EjecutorCeldaRuntPersona> log)
        : base(protector, reloj, log)
    {
        if (fuente is not (Fuente.RuntPropietario or Fuente.RuntLocatario))
            throw new ArgumentOutOfRangeException(nameof(fuente), fuente, "El ejecutor RUNT persona atiende RUNT_PROPIETARIO o RUNT_LOCATARIO.");
        Fuente = fuente;
        _runt = runt;
        _rues = rues;
    }

    public override Fuente Fuente { get; }

    /// <summary>NIT sin dígito de verificación: lo anterior al guion, solo dígitos.</summary>
    internal static string NitSinDigito(string numero)
    {
        ArgumentNullException.ThrowIfNull(numero);
        var guion = numero.IndexOf('-', StringComparison.Ordinal);
        var cuerpo = guion >= 0 ? numero[..guion] : numero;
        return new string([.. cuerpo.Where(char.IsAsciiDigit)]);
    }

    protected override string ConsultaDe(DocumentoPersona documento) => documento.EsNit ? ConsultaRues : ConsultaRuntPersona;

    protected override async Task<ConsultaPersona> ConsultarAsync(Guid empresa, DocumentoPersona documento, CancellationToken ct) =>
        documento.EsNit
            ? ConsultaPersona.De(ConsultaRues, await _rues.ConsultarAsync(empresa, NitSinDigito(documento.Numero), ct).ConfigureAwait(false))
            : ConsultaPersona.De(ConsultaRuntPersona, await _runt.ConsultarAsync(empresa, documento with { Tipo = documento.Tipo.Trim().ToUpperInvariant() }, ct).ConfigureAwait(false));
}
