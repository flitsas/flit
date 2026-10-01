using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.SignatureVault;
using Flit.Queries.Domain.Time;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.MandateSigners.CompanyMandateSigners;

/// <summary>Cuerpo HTTP del alta/edición de un mandatario desde el configurador de la compañía.</summary>
public sealed record CompanyMandateSignerRequest(
    string? FullName,
    string? DocumentNumber,
    IReadOnlyList<Guid>? TransitOfficeIds,
    string? DocumentType = null,
    string? Email = null,
    /// <summary>
    /// HU #13131 (ADR-0061) — OBSOLETO E IGNORADO. La firma física ya no es una forma de firma: el campo se
    /// acepta por compatibilidad con clientes anteriores, pero no se valida ni se persiste como exención.
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
    IReadOnlyList<MandateSignerOfficeCompanies>? OfficeCompanies = null,
    /// <summary>
    /// HU #13129 (ADR-0061) — modelo: <c>natural</c> (por defecto) | <c>juridica</c> | <c>formato_blanco</c>.
    /// Persona jurídica y Formato en blanco no admiten forma de firma, fechas ni correo (422).
    /// </summary>
    string? SignerModel = null,
    /// <summary>HU #13129 — forma de firma de la Persona natural: <c>baul</c> | <c>biometria</c>.</summary>
    string? SignatureMethod = null,
    /// <summary>HU #13129 — vigencia propia: <c>fixed</c> (por defecto) | <c>range</c>.</summary>
    string? ValidityKind = null,
    /// <summary>HU #13129 — inicio del rango (date <c>yyyy-MM-dd</c>); solo con <c>range</c>.</summary>
    DateOnly? ValidFrom = null,
    /// <summary>HU #13129 — fin del rango (date); solo con <c>range</c>.</summary>
    DateOnly? ValidTo = null);

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
/// organismo ni la huella de integridad. Desde la HU #11757 (ADR-0050) el alta YA NO dispara
/// validación de identidad — eso solo lo origina el módulo Identidad.
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

    public Task<CreateMandateSignerResult> HandleAsync(
        Guid companyTenantId,
        CompanyMandateSignerRequest request,
        Guid? createdBy,
        CancellationToken cancellationToken = default) =>
        HandleAsync(companyTenantId, request, createdBy, "compania", cancellationToken);

    /// <param name="configuredByScope">HU #13195c — origen según el actor: <c>super_admin</c> o <c>compania</c>.</param>
    public async Task<CreateMandateSignerResult> HandleAsync(
        Guid companyTenantId,
        CompanyMandateSignerRequest request,
        Guid? createdBy,
        string configuredByScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (offices, error) = await ResolverOrganismosAsync(
            _reader, companyTenantId, request.TransitOfficeIds, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return CreateMandateSignerResult.Invalid([error]);
        }

        // HU #13129 — modelo, forma de firma y vigencia (422 por campo).
        var (profile, profileErrors) = MandateSignerModelRules.Evaluate(ToProfileInput(request));
        if (profileErrors.Count > 0)
        {
            return CreateMandateSignerResult.Invalid(profileErrors);
        }

        // Solo la Persona natural con baúl aporta y valida una firma del baúl; la biometría no la exige
        // aprobada al guardar (la origina y vigila el módulo Identidad).
        if (profile.SignatureMethod == MandateSignatureMethods.Baul)
        {
            var firmaError = await ValidarFirmaAsync(
                _vaultReader, companyTenantId, request, cancellationToken).ConfigureAwait(false);
            if (firmaError is not null)
            {
                return CreateMandateSignerResult.Invalid([firmaError]);
            }
        }

        // HU #11715 — no se habilita en un organismo a quien no puede firmar ante él.
        var sinFirmaError = profile.IsNatural
            ? MandateSignerSigningCapability.Validate(
                offices,
                request.SignatureVaultId,
                existente: null,
                profile.SignatureMethod)
            : null;
        if (sinFirmaError is not null)
        {
            return CreateMandateSignerResult.Invalid([sinFirmaError]);
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
                SignatureVaultId = request.SignatureVaultId,
                OfficeCompanies = request.OfficeCompanies,
                SignerModel = request.SignerModel,
                SignatureMethod = request.SignatureMethod,
                ValidityKind = request.ValidityKind,
                ValidFrom = request.ValidFrom,
                ValidTo = request.ValidTo,
                CreatedBy = createdBy,
                // HU #13195c — origen según el actor (Super Admin → super_admin; compañía → compania).
                ConfiguredByScope = configuredByScope,
                // La compañía configura sus propios mandatarios: ve toda la red (Bug #12912).
                CompanyVisibility = OtCompanyVisibility.WholeNetwork,
            },
            cancellationToken).ConfigureAwait(false);
    }

    internal static MandateSignerProfileInput ToProfileInput(CompanyMandateSignerRequest request) =>
        new(
            request.SignerModel,
            request.SignatureMethod,
            request.ValidityKind,
            request.ValidFrom,
            request.ValidTo,
            request.FullName,
            request.DocumentType,
            request.DocumentNumber,
            request.Email,
            request.SignatureVaultId);

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

        var hoy = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(ColombiaTime.Offset).Date);
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

    public Task<UpdateMandateSignerResult> HandleAsync(
        Guid companyTenantId,
        Guid mandateSignerId,
        CompanyMandateSignerRequest request,
        Guid? updatedBy,
        CancellationToken cancellationToken = default) =>
        HandleAsync(companyTenantId, mandateSignerId, request, updatedBy, "compania", cancellationToken);

    /// <param name="configuredByScope">HU #13195c — origen de los vínculos NUEVOS; los existentes conservan el suyo.</param>
    public async Task<UpdateMandateSignerResult> HandleAsync(
        Guid companyTenantId,
        Guid mandateSignerId,
        CompanyMandateSignerRequest request,
        Guid? updatedBy,
        string configuredByScope,
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
        // HU #13129 — modelo, forma de firma y vigencia; lo que no se manda se conserva (solo natural).
        var (profile, profileErrors) = MandateSignerModelRules.Evaluate(
            CreateCompanyMandateSignerHandler.ToProfileInput(request), signer);
        if (profileErrors.Count > 0)
        {
            return UpdateMandateSignerResult.Invalid(profileErrors);
        }

        if (profile.SignatureMethod == MandateSignatureMethods.Baul)
        {
            var firmaError = await CreateCompanyMandateSignerHandler.ValidarFirmaAsync(
                _vaultReader, companyTenantId, request, cancellationToken).ConfigureAwait(false);
            if (firmaError is not null)
            {
                return UpdateMandateSignerResult.Invalid([firmaError]);
            }
        }

        // HU #13122 AC4 + HU #13129 — se valida TODA la lista de organismos, no solo los nuevos. La forma
        // de firma baúl exige la firma elegida (este configurador gestiona la firma: su null la quita);
        // la biometría no exige validación aprobada al guardar. La firma física ya no exime (HU #13131).
        var sinFirmaError = profile.IsNatural
            ? MandateSignerSigningCapability.Validate(
                offices,
                request.SignatureVaultId,
                existente: null,
                profile.SignatureMethod)
            : null;
        if (sinFirmaError is not null)
        {
            return UpdateMandateSignerResult.Invalid([sinFirmaError]);
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
                SignatureVaultId = request.SignatureVaultId,
                OfficeCompanies = request.OfficeCompanies,
                SignerModel = request.SignerModel,
                SignatureMethod = request.SignatureMethod,
                ValidityKind = request.ValidityKind,
                ValidFrom = request.ValidFrom,
                ValidTo = request.ValidTo,
                // El configurador de la compañía SÍ gestiona la firma: su null significa "quítala".
                ActualizaFirma = true,
                UpdatedBy = updatedBy,
                ConfiguredByScope = configuredByScope,
                // La compañía configura sus propios mandatarios: ve toda la red (Bug #12912).
                CompanyVisibility = OtCompanyVisibility.WholeNetwork,
            },
            cancellationToken).ConfigureAwait(false);
    }
}
