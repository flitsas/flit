using Flit.Analytics.Application.CompanyQueries;
using Flit.Queries.Domain;
using Flit.Tramites.Application.UseCases.ProcedureInstances;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Traduce el resultado ÚNICO de <see cref="ITramiteFirmaEvaluator"/> a los DTO de Consultas (gestor y organismo).
/// No recalcula nada: solo da forma al resultado ya evaluado.
/// </summary>
internal static class FirmaDtoMapper
{
    public static IReadOnlyList<FirmaPersonaDto>? Personas(TramiteFirmaResultado? firma) =>
        firma?.Partes
            .Select(p => new FirmaPersonaDto(
                p.Rol, p.Ordinal, p.Nombre, p.TipoDocumento, p.Documento, p.Firmo, p.Estado, p.FechaFirma))
            .ToList();

    /// <summary>
    /// Copia el estado de firma a cada actor de la fila (mismo orden/ordinal que el bloque del Excel). El propietario de
    /// matrícula/«otros» es el «Comprador» del evaluador. Sin evaluación, los actores quedan intactos.
    /// </summary>
    public static IReadOnlyList<ActorValidacionDto> ConFirma(
        IReadOnlyList<ActorValidacionDto> actores, TramiteFirmaResultado? firma)
    {
        if (firma is null || actores.Count == 0)
            return actores;

        return actores.Select(a =>
        {
            var rol = a.Rol == ActorValidacionRoles.Vendedor ? "Vendedor" : "Comprador";
            var persona = firma.Partes.FirstOrDefault(p => p.Rol == rol && p.Ordinal == a.Ordinal);
            return persona is null ? a : a with { EstadoFirma = persona.Estado, FechaFirma = persona.FechaFirma };
        }).ToList();
    }
}
