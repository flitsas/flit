using Flit.Admin.Application.Companies.LegalRepresentatives;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Adaptador del puerto <see cref="IRepresentanteLegalDirectory"/> (HU #11195, ADR-0033): consulta el
/// directorio de representantes legales de Admin desde el módulo de trámites.
///
/// <para><b>Qué queda aquí (HU #11663):</b> únicamente la <b>precarga</b> —el nombre del representante
/// activo de una compañía, como respaldo para los documentos cuando el trámite no lo trajo (HU #11198)—.
/// El directorio dejó de participar en decisiones: la de enviar la validación de identidad la toma la
/// precedencia única (ADR-0039) mirando a la persona del trámite, no al registro de la compañía.</para>
///
/// <para><b>Sobre el scope de RLS:</b> las tablas de Admin están protegidas por
/// <c>app.current_tenant_id</c>, así que la consulta va dentro de un <see cref="TenantRlsScope"/>. Si
/// alguna vez vuelve a haber colaboradores que abran el suyo, deben invocarse FUERA de este scope:
/// anidarlos abriría una transacción dentro de otra, que es lo que dejó muerta la regeneración
/// documental en el Feature #11004.</para>
/// </summary>
internal sealed class RepresentanteLegalDirectory : IRepresentanteLegalDirectory
{
    private readonly FlitDbContext _context;

    public RepresentanteLegalDirectory(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<string?> BuscarNombreRepresentanteAsync(
        Guid tenantId,
        string nitCompania,
        string? documentType,
        string? documentNumber,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(nitCompania))
        {
            return null;
        }

        var nit = nitCompania.Trim();
        var documento = string.IsNullOrWhiteSpace(documentNumber) ? null : documentNumber.Trim();
        var tipo = string.IsNullOrWhiteSpace(documentType) ? null : documentType.Trim();

        var candidatos = await TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            () => (
                    from vinculo in _context.LegalRepresentativeCompanies.AsNoTracking()
                    join compania in _context.RepresentedCompanies.AsNoTracking()
                        on vinculo.RepresentedCompanyId equals compania.Id
                    join representante in _context.CompanyLegalRepresentatives.AsNoTracking()
                        on vinculo.RepresentativeId equals representante.Id
                    where vinculo.TenantId == tenantId
                        && compania.TenantId == tenantId
                        && compania.DocumentNumber == nit
                        && representante.IsActive
                        && (documento == null
                            || (representante.DocumentNumber == documento
                                && (tipo == null || representante.DocumentType == tipo)))
                    select new Nombre(
                        representante.Id, representante.Name, representante.FirstLastName, representante.SecondLastName))
                .Distinct()
                .Take(2)
                .ToListAsync(cancellationToken),
            cancellationToken)
            .ConfigureAwait(false);

        // Con el documento del trámite se busca a ESA persona; sin él, solo se responde si la compañía
        // tiene exactamente un representante. Elegir entre varios imprimiría el nombre de alguien que no
        // es en un documento legal, que es peor que dejar el hueco.
        if (candidatos.Count != 1)
        {
            return null;
        }

        var elegido = candidatos[0];
        var partes = new[] { elegido.Name, elegido.FirstLastName, elegido.SecondLastName }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim());
        var completo = string.Join(' ', partes);
        return string.IsNullOrWhiteSpace(completo) ? null : completo;
    }

    /// <summary>Nombre del representante troceado como lo guarda el directorio.</summary>
    private sealed record Nombre(Guid Id, string Name, string FirstLastName, string? SecondLastName);
}
