using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Catalogos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Catalogos;

/// <summary>
/// Organismo → departamento (HU #13733, F03-C4, plan §4 «Departamento»): busca el <c>transit_office_name</c> del RUNT en la
/// réplica de organismos por nombre normalizado (<see cref="NombreNormalizado"/>). Organismo no encontrado, o nombre que
/// corresponde a organismos de departamentos distintos: departamento nulo (el impuesto queda NA
/// <c>DEPARTAMENTO_NO_SOPORTADO</c>, nunca ERROR). Emparejar por nombre es frágil (supuesto del plan) hasta que C-PER hidrate
/// <c>transit_office_code</c>. Catálogos globales: se leen con el contexto de la celda sin filtro de empresa.
/// </summary>
/// <example>
/// <code>
/// var departamento = await DerivacionDepartamento.DerivarAsync(contexto.Db, vehiculo.OrganismoNombre, ct);
/// var soportado = await DerivacionDepartamento.SoportadoAsync(contexto.Db, departamento, ct);
/// </code>
/// </example>
internal static class DerivacionDepartamento
{
    /// <summary>El código del departamento del organismo, o null si no está en la réplica o es ambiguo.</summary>
    public static async Task<string?> DerivarAsync(DiagnosticoDb db, string? organismo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var nombre = NombreNormalizado.Normalizar(organismo);
        if (nombre.Length == 0)
            return null;
        var departamentos = await db.Set<Organismo>().AsNoTracking().Where(o => o.NombreNorm == nombre)
            .Select(o => o.DepartamentoCodigo).Distinct().Take(2).ToListAsync(ct).ConfigureAwait(false);
        return departamentos.Count == 1 ? departamentos[0] : null;
    }

    /// <summary>Verdadero si <c>departamentos_impuesto</c> tiene el departamento con <c>soportado=true</c> (sin C3 está vacía).</summary>
    public static async Task<bool> SoportadoAsync(DiagnosticoDb db, string? departamento, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return departamento is not null
            && await db.Set<DepartamentoImpuesto>().AsNoTracking().AnyAsync(d => d.DepartamentoCodigo == departamento && d.Soportado, ct).ConfigureAwait(false);
    }
}
