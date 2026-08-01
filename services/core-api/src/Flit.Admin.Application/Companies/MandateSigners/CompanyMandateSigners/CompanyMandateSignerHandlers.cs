using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;
using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Admin.Application.Companies.MandateSigners.CompanyMandateSigners;

/// <summary>Cuerpo HTTP del alta/edición de un mandatario desde el configurador de la compañía.</summary>
public sealed record CompanyMandateSignerRequest(
    string? FullName,
    string? DocumentNumber,
    IReadOnlyList<Guid>? TransitOfficeIds,
    string? DocumentType = null,
    string? Email = null);

/// <summary>
/// HU #11202 — alta de un mandatario desde el configurador de la COMPAÑÍA. La empresa captura los datos
/// de la persona y marca en cuáles de SUS organismos aplica; antes era el organismo el que elegía
/// compañías.
///
/// <para>La compañía solo puede elegir organismos que tenga habilitados (AC2). Se comprueba en el
/// servidor y no solo en la lista: registrar un mandatario en un organismo donde la compañía no puede
/// radicar dejaría un dato inservible que además nadie vería fallar hasta el momento de firmar.</para>
///
/// <para>Delega en <see cref="CreateMandateSignerHandler"/> para no duplicar la operabilidad del
/// organismo, la huella de integridad ni el envío de la validación de identidad (HU #10911/#11000).
/// El organismo PRIMARIO es el primero de la lista; los demás viajan en <c>TransitOfficeIds</c>.</para>
/// </summary>
public sealed class CreateCompanyMandateSignerHandler
{
    private readonly IMandateSignerReader _reader;
    private readonly CreateMandateSignerHandler _inner;

    public CreateCompanyMandateSignerHandler(IMandateSignerReader reader, CreateMandateSignerHandler inner)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public async Task<CreateMandateSignerResult> HandleAsync(
        Guid companyTenantId,
        CompanyMandateSignerRequest request,
        Guid? createdBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (offices, error) = await ResolverOrganismosAsync(
            _reader, companyTenantId, request.TransitOfficeIds, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return CreateMandateSignerResult.Invalid([error]);
        }

        return await _inner.HandleAsync(
            new CreateMandateSignerCommand
            {
                TransitOfficeId = offices[0],
                FullName = request.FullName ?? string.Empty,
                DocumentNumber = request.DocumentNumber ?? string.Empty,
                CompanyTenantIds = [companyTenantId],
                DocumentType = request.DocumentType ?? "CC",
                Email = request.Email,
                TransitOfficeIds = offices,
                CreatedBy = createdBy,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deja la lista de organismos lista para usar, o el error 422 que corresponda. Compartida con la
    /// edición: el criterio de "qué organismos puede elegir esta compañía" tiene que ser el mismo.
    /// </summary>
    internal static async Task<(IReadOnlyList<Guid> Offices, MandateSignerValidationError? Error)>
        ResolverOrganismosAsync(
            IMandateSignerReader reader,
            Guid companyTenantId,
            IReadOnlyList<Guid>? solicitados,
            CancellationToken cancellationToken)
    {
        var offices = solicitados is null ? [] : solicitados.Distinct().ToList();
        if (offices.Count == 0)
        {
            return ([], new MandateSignerValidationError(
                "transitOfficeIds",
                "Debe indicar al menos un organismo de tránsito donde aplique el mandatario.",
                null));
        }

        var disponibles = await reader
            .ListCompanyTransitOfficesAsync(companyTenantId, cancellationToken).ConfigureAwait(false);
        var permitidos = disponibles.Select(o => o.TransitOfficeId).ToHashSet();

        var ajeno = offices.FirstOrDefault(id => !permitidos.Contains(id));
        return ajeno == Guid.Empty
            ? (offices, null)
            : ([], new MandateSignerValidationError(
                "transitOfficeIds",
                "Solo puede elegir organismos de tránsito habilitados para la compañía.",
                ajeno.ToString()));
    }
}

/// <summary>
/// HU #11202 (AC3) — edición desde el configurador de la compañía: datos personales y organismos. La
/// lista de organismos REEMPLAZA a la anterior, así que quitar uno lo retira (HU #11201, AC3).
/// </summary>
public sealed class UpdateCompanyMandateSignerHandler
{
    private readonly IMandateSignerReader _reader;
    private readonly UpdateMandateSignerHandler _inner;

    public UpdateCompanyMandateSignerHandler(IMandateSignerReader reader, UpdateMandateSignerHandler inner)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public async Task<UpdateMandateSignerResult> HandleAsync(
        Guid companyTenantId,
        Guid mandateSignerId,
        CompanyMandateSignerRequest request,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (offices, error) = await CreateCompanyMandateSignerHandler.ResolverOrganismosAsync(
            _reader, companyTenantId, request.TransitOfficeIds, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return UpdateMandateSignerResult.Invalid([error]);
        }

        return await _inner.HandleAsync(
            new UpdateMandateSignerCommand
            {
                TransitOfficeId = offices[0],
                MandateSignerId = mandateSignerId,
                FullName = request.FullName ?? string.Empty,
                DocumentNumber = request.DocumentNumber ?? string.Empty,
                CompanyTenantIds = [companyTenantId],
                DocumentType = request.DocumentType ?? "CC",
                Email = request.Email,
                TransitOfficeIds = offices,
                UpdatedBy = updatedBy,
            },
            cancellationToken).ConfigureAwait(false);
    }
}
