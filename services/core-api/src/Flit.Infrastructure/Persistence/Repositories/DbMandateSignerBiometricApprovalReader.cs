using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Ajuste HU #13123 — resuelve si la persona tiene identidad aprobada y vigente en el tenant de la
/// compañía destino, con la misma fuente que HU #13121 (<see cref="IdentityVigenciaPorDocumentoResolver"/>,
/// variante del mandatario: una aprobación cuenta sin ventana de 30 días, HU #13130b; en curso no cuenta). El tenant lo fija el llamador (la
/// compañía); no se consulta el del organismo.
/// </summary>
internal sealed class DbMandateSignerBiometricApprovalReader : IMandateSignerBiometricApprovalReader
{
    private readonly IdentityVigenciaPorDocumentoResolver _resolver;

    public DbMandateSignerBiometricApprovalReader(
        FlitDbContext context,
        IdentityVigenciaPorDocumentoResolver? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        _resolver = resolver ?? new IdentityVigenciaPorDocumentoResolver(new ProcedureInstanceRepository(context));
    }

    public async Task<bool> HasApprovedValidAsync(
        Guid companyTenantId,
        string documentType,
        string documentNumber,
        CancellationToken cancellationToken = default)
    {
        var result = await _resolver
            .ResolveMandatarioAsync(companyTenantId, documentType, documentNumber, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        return result.Status == IdentityVigenciaEstados.AprobadaVigente;
    }
}
