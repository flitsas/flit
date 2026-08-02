using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.SignatureVault;

namespace Flit.Admin.Application.Companies.MandateSigners.CompanyMandateSigners;

/// <summary>Cuerpo HTTP del alta/edición de un mandatario desde el configurador de la compañía.</summary>
public sealed record CompanyMandateSignerRequest(
    string? FullName,
    string? DocumentNumber,
    IReadOnlyList<Guid>? TransitOfficeIds,
    string? DocumentType = null,
    string? Email = null,
    /// <summary>
    /// Organismos (subconjunto de los anteriores) en los que este mandatario firma A MANO: el contrato
    /// deja la línea de guiones bajos con sus datos debajo y no estampa firma del baúl ni sello de
    /// identidad. Va por organismo y no por persona porque la misma puede firmar a mano ante uno y
    /// electrónicamente ante otro.
    /// </summary>
    IReadOnlyList<Guid>? PhysicalSignatureOfficeIds = null,
    /// <summary>
    /// Firma del baúl elegida para el mandatario. <c>null</c> ⇒ el trámite la resuelve por documento,
    /// que es el comportamiento previo.
    /// </summary>
    Guid? SignatureVaultId = null,
    /// <summary>
    /// Empresas representadas para las que firma, POR ORGANISMO. Vacío o ausente ⇒ el mandatario aplica
    /// a todas las empresas de ese organismo, que es como se comportan los que ya existen.
    /// </summary>
    IReadOnlyList<MandateSignerOfficeCompanies>? OfficeCompanies = null);

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
    private readonly ISignatureVaultReader? _vaultReader;

    public CreateCompanyMandateSignerHandler(
        IMandateSignerReader reader,
        CreateMandateSignerHandler inner,
        ISignatureVaultReader? vaultReader = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _vaultReader = vaultReader;
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

        var firmaError = await ValidarFirmaAsync(
            _vaultReader, companyTenantId, request, cancellationToken).ConfigureAwait(false);
        if (firmaError is not null)
        {
            return CreateMandateSignerResult.Invalid([firmaError]);
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
                PhysicalSignatureOfficeIds = request.PhysicalSignatureOfficeIds,
                SignatureVaultId = request.SignatureVaultId,
                OfficeCompanies = request.OfficeCompanies,
                CreatedBy = createdBy,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Valida la firma del baúl elegida para el mandatario, con el mismo criterio que el representante
    /// legal (<c>LegalRepresentativeWriter</c>): que exista en el baúl de esta compañía, que sea de ESA
    /// persona y que esté activa y vigente hoy.
    ///
    /// <para>Sin lector inyectado no se valida — el comportamiento previo, en el que la firma ni
    /// siquiera se podía elegir. La vigencia se evalúa en día calendario de Colombia (UTC-5, sin horario
    /// de verano), igual que el resto del baúl.</para>
    /// </summary>
    internal static async Task<MandateSignerValidationError?> ValidarFirmaAsync(
        ISignatureVaultReader? vaultReader,
        Guid companyTenantId,
        CompanyMandateSignerRequest request,
        CancellationToken cancellationToken)
    {
        if (vaultReader is null || request.SignatureVaultId is not { } firmaId || firmaId == Guid.Empty)
        {
            return null;
        }

        var firma = await vaultReader
            .GetByIdAsync(companyTenantId, firmaId, cancellationToken).ConfigureAwait(false);

        if (firma is null)
        {
            return new MandateSignerValidationError(
                "signatureVaultId", "La firma indicada no existe en el baul de esta compania.", null);
        }

        var documento = request.DocumentNumber?.Trim() ?? string.Empty;
        var tipo = string.IsNullOrWhiteSpace(request.DocumentType) ? "CC" : request.DocumentType.Trim();
        if (!string.Equals(firma.DocumentType, tipo, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(firma.DocumentNumber, documento, StringComparison.Ordinal))
        {
            return new MandateSignerValidationError(
                "signatureVaultId", "La firma indicada no pertenece al mandatario.", null);
        }

        var hoy = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5)).Date);
        return firma.Estado != SignatureVaultEstado.Activa
            || hoy < firma.VigenciaDesde
            || hoy > firma.VigenciaHasta
            ? new MandateSignerValidationError(
                "signatureVaultId", "La firma indicada no esta activa o su vigencia ha expirado.", null)
            : null;
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
    private readonly ISignatureVaultReader? _vaultReader;

    public UpdateCompanyMandateSignerHandler(
        IMandateSignerReader reader,
        UpdateMandateSignerHandler inner,
        ISignatureVaultReader? vaultReader = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _vaultReader = vaultReader;
    }

    public async Task<UpdateMandateSignerResult> HandleAsync(
        Guid companyTenantId,
        Guid mandateSignerId,
        CompanyMandateSignerRequest request,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // El ámbito de esta ruta es la COMPAÑÍA, así que el mandatario se busca entre los suyos. Antes
        // no se comprobaba: bastaba con acertar el id y compartir organismo con su dueño para poder
        // editar el mandatario de otra empresa.
        var propios = await _reader.ListByCompanyAsync(companyTenantId, cancellationToken).ConfigureAwait(false);
        var signer = propios.FirstOrDefault(s => s.Id == mandateSignerId);
        if (signer is null)
        {
            return UpdateMandateSignerResult.NotFound();
        }

        var (offices, error) = await CreateCompanyMandateSignerHandler.ResolverOrganismosAsync(
            _reader, companyTenantId, request.TransitOfficeIds, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return UpdateMandateSignerResult.Invalid([error]);
        }

        // El organismo bajo el que se edita es el primario que el mandatario conservará. Se mantiene el
        // suyo mientras siga en la lista; solo si el gestor lo retira pasa a serlo el primero de los que
        // quedan. Tomar siempre `offices[0]` —el primero que mandó el formulario— era el origen del 404:
        // en cuanto no coincidía con el primario guardado, la búsqueda no encontraba al mandatario.
        var firmaError = await CreateCompanyMandateSignerHandler.ValidarFirmaAsync(
            _vaultReader, companyTenantId, request, cancellationToken).ConfigureAwait(false);
        if (firmaError is not null)
        {
            return UpdateMandateSignerResult.Invalid([firmaError]);
        }

        var primarioActual = signer.TransitOfficeId;
        var organismoDeEdicion = offices.Contains(primarioActual) ? primarioActual : offices[0];

        return await _inner.HandleAsync(
            new UpdateMandateSignerCommand
            {
                TransitOfficeId = organismoDeEdicion,
                OrganismoPrimarioActual = primarioActual,
                MandateSignerId = mandateSignerId,
                FullName = request.FullName ?? string.Empty,
                DocumentNumber = request.DocumentNumber ?? string.Empty,
                CompanyTenantIds = [companyTenantId],
                DocumentType = request.DocumentType ?? "CC",
                Email = request.Email,
                TransitOfficeIds = offices,
                PhysicalSignatureOfficeIds = request.PhysicalSignatureOfficeIds,
                SignatureVaultId = request.SignatureVaultId,
                OfficeCompanies = request.OfficeCompanies,
                // El configurador de la compañía SÍ gestiona la firma: su null significa "quítala".
                ActualizaFirma = true,
                UpdatedBy = updatedBy,
            },
            cancellationToken).ConfigureAwait(false);
    }
}
