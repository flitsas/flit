using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Personas;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Ejecutor de las celdas SIMIT_PROPIETARIO y SIMIT_LOCATARIO (HU #13794, F05-C5, F05-C6, F05-C9, F05-C10, decisión Q1 y
/// decisión 4 del gate): consulta los comparendos de CADA persona del rol con el patrón de <see cref="EjecutorPorPersonas"/>
/// (fallo parcial <c>PERSONAS_CON_ERROR</c>, reintento y reproceso solo de las personas en error, reutilización del mismo día).
/// Una llamada <see cref="IFuenteSimit"/> por persona, por documento (<see cref="SolicitudSimit.PorDocumento"/>), a nombre de la
/// empresa de la celda: la persona con NIT va con <c>tipo_persona=JURIDICA</c> y el NIT sin dígito de verificación (igual que el
/// RUES, <see cref="EjecutorCeldaRuntPersona.NitSinDigito"/>); las demás con <c>NATURAL</c>. El resultado de cada persona es
/// <see cref="ResultadoSimit"/> (semáforo, cantidad y número, fecha, valor, estado y organismo de cada comparendo): nunca
/// nombres, infracción ni documento. Sin locatario la celda nace NA <c>SIN_DOCUMENTO_LOCATARIO</c> y no llega aquí. Se registra
/// una instancia por fuente en <c>Modulos.AddMotorConsultas</c>.
/// </summary>
/// <example>
/// <code>
/// services.AddScoped&lt;IEjecutorCelda&gt;(sp =&gt; ActivatorUtilities.CreateInstance&lt;EjecutorCeldaSimitPersona&gt;(sp, Fuente.SimitPropietario));
/// </code>
/// </example>
internal sealed class EjecutorCeldaSimitPersona : EjecutorPorPersonas
{
    /// <summary>Tipo de consulta de cada persona en el payload (<c>personas[].consulta</c>).</summary>
    public const string ConsultaSimit = "SIMIT";

    private readonly IFuenteSimit _simit;

    public EjecutorCeldaSimitPersona(
        Fuente fuente, IFuenteSimit simit, IProtectorDocumentoPersona protector, TimeProvider reloj, ILogger<EjecutorCeldaSimitPersona> log)
        : base(protector, reloj, log)
    {
        if (fuente is not (Fuente.SimitPropietario or Fuente.SimitLocatario))
            throw new ArgumentOutOfRangeException(nameof(fuente), fuente, "El ejecutor SIMIT por persona atiende SIMIT_PROPIETARIO o SIMIT_LOCATARIO.");
        Fuente = fuente;
        _simit = simit;
    }

    public override Fuente Fuente { get; }

    /// <summary>El documento tal como va a SIMIT: tipo en mayúsculas y, si es NIT, sin dígito de verificación.</summary>
    internal static DocumentoPersona Normalizar(DocumentoPersona documento)
    {
        ArgumentNullException.ThrowIfNull(documento);
        var tipo = documento.Tipo.Trim().ToUpperInvariant();
        return documento.EsNit
            ? new DocumentoPersona(tipo, EjecutorCeldaRuntPersona.NitSinDigito(documento.Numero))
            : new DocumentoPersona(tipo, documento.Numero.Trim());
    }

    protected override string ConsultaDe(DocumentoPersona documento) => ConsultaSimit;

    protected override async Task<ConsultaPersona> ConsultarAsync(Guid empresa, DocumentoPersona documento, CancellationToken ct) =>
        ConsultaPersona.De(ConsultaSimit, await _simit.ConsultarAsync(empresa, SolicitudSimit.PorDocumento(Normalizar(documento)), ct).ConfigureAwait(false));
}
