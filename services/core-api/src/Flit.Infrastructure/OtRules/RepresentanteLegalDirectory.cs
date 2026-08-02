using Flit.Admin.Application.Companies.LegalRepresentatives;
using Flit.Admin.Domain.Companies.SignatureVault;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Adaptador del puerto <see cref="IRepresentanteLegalDirectory"/> (HU #11195). Responde si la compañía
/// del NIT tiene un representante legal REGISTRADO Y UTILIZABLE, consultando el directorio de Admin
/// (ADR-0033).
///
/// <para>Las tres condiciones se evalúan con los MISMOS predicados que ya usa el resto del sistema, para
/// que la compuerta no pueda discrepar de lo que después hace el firmado:</para>
/// <list type="number">
///   <item>Representante ACTIVO asociado a la compañía por su NIT (puente
///     <c>admin.legal_representative_companies</c>, HU #10932: un representante puede tener varias).</item>
///   <item><b>Escritura vigente</b> que lo faculte: fila de <c>admin.company_deeds</c> suya y de esa
///     compañía, activa y con <c>hoy</c> dentro de su vigencia.</item>
///   <item><b>Con qué firmar</b>: firma del baúl activa y vigente de la PERSONA
///     (<see cref="ISignatureVaultReader.FindActiveByDocumentAsync"/> + <c>EstaVigente</c>) <b>o</b>
///     validación de identidad vigente por documento
///     (<see cref="IRepresentativeIdentityLookup.FindVigenteIdentityRefAsync"/>).</item>
/// </list>
///
/// <para>Basta con que UN representante de la compañía cumpla las tres para considerarla cubierta: el
/// trámite necesita a alguien que pueda firmar, no que puedan todos.</para>
///
/// <para><b>Sobre el scope de RLS:</b> las tablas de Admin están protegidas por
/// <c>app.current_tenant_id</c>, así que las consultas propias van dentro de un
/// <see cref="TenantRlsScope"/>. Los dos colaboradores (baúl e identidad) abren el suyo, por lo que se
/// invocan FUERA del scope: anidarlos abriría una transacción dentro de otra, que es exactamente lo que
/// dejó muerta la regeneración documental en el Feature #11004.</para>
/// </summary>
internal sealed class RepresentanteLegalDirectory : IRepresentanteLegalDirectory
{
    // Colombia no tiene horario de verano: UTC-5 fijo (coherente con SignatureVaultPolicy).
    private static readonly TimeSpan ColombiaUtcOffset = TimeSpan.FromHours(-5);

    private readonly FlitDbContext _context;
    private readonly ISignatureVaultReader _signatureVaultReader;
    private readonly IRepresentativeIdentityLookup _identityLookup;

    public RepresentanteLegalDirectory(
        FlitDbContext context,
        ISignatureVaultReader signatureVaultReader,
        IRepresentativeIdentityLookup identityLookup)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _signatureVaultReader = signatureVaultReader ?? throw new ArgumentNullException(nameof(signatureVaultReader));
        _identityLookup = identityLookup ?? throw new ArgumentNullException(nameof(identityLookup));
    }

    public async Task<bool> TieneRepresentanteUtilizableAsync(
        Guid tenantId,
        string nitCompania,
        DateOnly hoy,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(nitCompania))
        {
            // Sin NIT no hay nada que consultar. Se responde "sí lo tiene" para no disparar el envío
            // sobre un dato que no se pudo resolver.
            return true;
        }

        var nit = nitCompania.Trim();

        // (1) + (2) en un solo scope de RLS: representantes ACTIVOS de la compañía que ADEMÁS tienen una
        // escritura suya vigente para esa compañía. Las escrituras legadas (sin representante) no cuentan:
        // no acreditan a ESTA persona.
        var acreditados = await TenantRlsScope.ExecuteAsync(
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
                        && _context.CompanyDeeds.Any(escritura =>
                            escritura.TenantId == tenantId
                            && escritura.RepresentativeId == representante.Id
                            && escritura.IsActive
                            && escritura.VigenciaDesde <= hoy
                            && escritura.VigenciaHasta >= hoy
                            && _context.CompanyDeedCompanies.Any(puente =>
                                puente.DeedId == escritura.Id
                                && puente.RepresentedCompanyId == compania.Id))
                    select new Acreditado(representante.DocumentType, representante.DocumentNumber))
                .Distinct()
                .ToListAsync(cancellationToken),
            cancellationToken)
            .ConfigureAwait(false);

        // AC1 (sin representante registrado) y AC2 (registrado pero sin escritura vigente) caen aquí.
        if (acreditados.Count == 0)
        {
            return false;
        }

        var now = new DateTimeOffset(hoy.ToDateTime(TimeOnly.MinValue), ColombiaUtcOffset);

        // (3) Con qué firmar. Fuera del scope anterior: cada colaborador abre el suyo.
        foreach (var acreditado in acreditados)
        {
            var firma = await _signatureVaultReader
                .FindActiveByDocumentAsync(
                    tenantId, acreditado.DocumentType, acreditado.DocumentNumber, cancellationToken)
                .ConfigureAwait(false);

            if (firma is not null && firma.EstaVigente(hoy))
            {
                return true;
            }

            var identidad = await _identityLookup
                .FindVigenteIdentityRefAsync(
                    tenantId, acreditado.DocumentType, acreditado.DocumentNumber, now, cancellationToken)
                .ConfigureAwait(false);

            if (identidad is not null)
            {
                return true;
            }
        }

        // AC2: hay representantes acreditados por escritura, pero ninguno tiene con qué firmar.
        return false;
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

    /// <summary>Representante de la compañía con escritura vigente: solo su documento, que es la llave de la firma y de la identidad.</summary>
    private sealed record Acreditado(string DocumentType, string DocumentNumber);

    /// <summary>Nombre del representante troceado como lo guarda el directorio.</summary>
    private sealed record Nombre(Guid Id, string Name, string FirstLastName, string? SecondLastName);
}
