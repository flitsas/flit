using Flit.Admin.Domain.Identity;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #11765 (ADR-0050) — traduce el resultado de <see cref="IdentityVigenciaPorDocumentoResolver"/>
/// (los CUATRO estados del ADR-0050: <see cref="IdentityVigenciaEstados"/>) al vocabulario histórico
/// que expone el contrato admin de representantes legales y mandatarios
/// (<see cref="AdminIdentityVigencia"/>: <c>valid</c>/<c>pending</c>/<c>expired</c>/<c>none</c>).
/// <para>
/// Punto ÚNICO de esta traducción: lo comparten <c>DbLegalRepresentativeReader</c> y
/// <c>DbMandateSignerReader</c> para no reimplementar la regla dos veces. NO reimplementa vigencia — la
/// clasificación ya vino resuelta por <see cref="IdentityVigenciaClassifier"/> (vía el resolver); esta
/// clase solo RENOMBRA el estado y decide cuándo informar <c>ValidUntil</c> (solo en <c>valid</c>,
/// igual que hacía <c>AdminIdentityVigencia.Resumir</c> antes de esta HU — retirado por quedar sin
/// consumidor de producción).
/// </para>
/// <para>
/// No se cambia el contrato de wire (<c>LegalRepresentativeResponse</c>/<c>MandateSignerResponse</c>):
/// el frontend (<c>identity-vigencia.ts</c>) sigue esperando estos cuatro nombres. Lo que cambia es DE
/// DÓNDE sale el dato, no cómo se llama en el DTO.
/// </para>
/// </summary>
internal static class IdentityVigenciaLegacyMapper
{
    /// <summary>
    /// Traduce un <see cref="IdentityVigenciaResult"/> (estado ADR-0050) a
    /// <see cref="AdminIdentityVigencia.Resultado"/> (estado histórico admin).
    /// </summary>
    public static AdminIdentityVigencia.Resultado ToLegacyResultado(IdentityVigenciaResult result) =>
        result.Status switch
        {
            IdentityVigenciaEstados.AprobadaVigente =>
                new AdminIdentityVigencia.Resultado(AdminIdentityVigencia.Valid, result.ValidUntil),
            IdentityVigenciaEstados.EnCurso =>
                new AdminIdentityVigencia.Resultado(AdminIdentityVigencia.Pending, null),
            IdentityVigenciaEstados.Vencida =>
                new AdminIdentityVigencia.Resultado(AdminIdentityVigencia.Expired, null),
            _ => new AdminIdentityVigencia.Resultado(AdminIdentityVigencia.None, null),
        };
}
