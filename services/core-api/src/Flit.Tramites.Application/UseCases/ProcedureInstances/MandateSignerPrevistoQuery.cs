using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #13145 (ADR-0066) — un candidato que el OT puede elegir al aprobar, calculado por el backend. Solo
/// identificador, nombre y forma de firma (<c>baul</c> | <c>biometria</c>, nula en Persona jurídica y Formato en
/// blanco): nunca el documento ni la ruta de la firma (Ley 1581).
/// </summary>
public sealed record MandateSignerCandidatoDto(Guid Id, string Nombre, string? FormaFirma)
{
    public static MandateSignerCandidatoDto From(MandateSignerCandidate c) =>
        new(c.Id, c.Nombre, c.SignatureMethod);
}

/// <summary>
/// HU #13145 — el firmante previsto del trámite, de solo lectura. <c>Estado</c> es uno de
/// <c>valido</c>, <c>sin_mandatario</c>, <c>firma_invalida</c>, <c>no_aplica</c>, <c>pendiente_organismo</c> o
/// <c>pendiente_eleccion_ot</c>. <c>Nombre</c> y <c>FormaFirma</c> solo vienen con <c>valido</c>; <c>Motivo</c>
/// explica el resto (vocabulario estable de ADR-0066). <c>Modo</c> es el modo vigente de la validación
/// (<c>block</c> | <c>warn</c> | <c>off</c>) para que la UI decida entre alerta y advertencia.
/// </summary>
public sealed record MandateSignerPrevistoDto(
    string Estado,
    string? Nombre,
    string? FormaFirma,
    string? Motivo,
    string Modo,
    /// <summary>
    /// HU #13180 — nivel de la prelación en el que se resolvió el firmante (<c>explicita</c>, <c>ot_para_compania</c>,
    /// <c>propio_de_compania</c>, <c>asociado_de_otra_compania</c>, <c>default_del_ot</c>); solo viene con
    /// <c>valido</c>. El indicador «Firmará» lo usa para decir de dónde viene el firmante.
    /// </summary>
    string? Nivel = null);

/// <summary>
/// HU #13145 — consulta del firmante previsto. Reutiliza el evaluador único del gate (HU #13144) para que la
/// pantalla y la radicación nunca discrepen. El trámite se busca en el tenant de la petición: uno de otro
/// tenant responde <c>not_found</c> (404) y la respuesta jamás lleva documento ni ruta de firma.
/// </summary>
public sealed class GetMandateSignerPrevistoHandler(
    IProcedureInstanceRepository repo,
    MandateSignerEvaluator evaluator,
    TramiteValidationPolicy? validationPolicy = null)
{
    private readonly TramiteValidationPolicy _policy = validationPolicy ?? TramiteValidationPolicy.BlockAll;

    public async Task<(MandateSignerPrevistoDto? Result, string? Error)> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithDetailsAsync(instanceId, tenantId, ct).ConfigureAwait(false);
        if (instance is null)
        {
            return (null, "not_found");
        }

        var evaluacion = await evaluator.EvaluateAsync(instance, null, ct).ConfigureAwait(false);
        var valido = evaluacion.Estado == MandateSignerEstado.Valido;

        return (new MandateSignerPrevistoDto(
            MandateSignerEstados.ToCode(evaluacion.Estado),
            valido ? evaluacion.Signer?.Nombre : null,
            valido ? evaluacion.FormaFirma : null,
            evaluacion.Motivo,
            ModoCode(_policy.MandatarioRequerido),
            valido ? MandateSignerLevelCodes.ToCode(evaluacion.Nivel) : null), null);
    }

    private static string ModoCode(TramiteValidationMode modo) => modo switch
    {
        TramiteValidationMode.Warn => "warn",
        TramiteValidationMode.Off => "off",
        _ => "block",
    };
}
