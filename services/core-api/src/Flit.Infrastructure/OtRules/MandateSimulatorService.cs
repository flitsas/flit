using System.Globalization;
using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Documents;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Simulador de mandatos de Plataforma → Mandatos (Feature #11702, HU #11706).
///
/// <para><b>Fidelidad por construcción.</b> No reimplementa cómo se arma el mandato: resuelve la
/// configuración con <see cref="IMandateRequirementPolicy.ResolveByOfficeIdAsync"/> —el MISMO camino
/// que usa el trámite desde la corrección de la HU #11704— y la firma del mandatario con
/// <see cref="MandatarioFirmaResolver"/>, compartido con <c>FurCommand</c>. Si el simulador dijera
/// "así queda" con una lógica propia, dejaría de servir para lo único que se pide de él: comprobar la
/// parametrización antes de que un trámite real emita el documento.</para>
///
/// <para><b>No toca ningún expediente:</b> genera bytes en memoria y, si se pide, los adjunta a un
/// correo. Nada se persiste ni se asocia a un trámite.</para>
/// </summary>
internal sealed class MandateSimulatorService : IMandateSimulatorService
{
    /// <summary>Id ESTABLE de plantilla para la bitácora de envíos (mismo vocabulario del catálogo).</summary>
    private const string TemplateKey = "admin.mandato-simulacion";

    private static readonly HashSet<string> AssignmentModes = new(StringComparer.OrdinalIgnoreCase)
    {
        MandatoAssignmentModeCodes.Signer,
        MandatoAssignmentModeCodes.Institutional,
        MandatoAssignmentModeCodes.Open,
    };

    private readonly FlitDbContext _db;
    private readonly ITransitOfficeCatalog _catalog;
    private readonly IMandateRequirementPolicy _mandatePolicy;
    private readonly IMandateSignerDirectory _signerDirectory;
    private readonly IMandatoGenerator _generator;
    private readonly IMandateConfigAdminService _configService;
    private readonly ISignatureVaultPolicy _vaultPolicy;
    private readonly IAttachmentStorage _storage;
    private readonly IEmailSender _emailSender;

    public MandateSimulatorService(
        FlitDbContext db,
        ITransitOfficeCatalog catalog,
        IMandateRequirementPolicy mandatePolicy,
        IMandateSignerDirectory signerDirectory,
        IMandatoGenerator generator,
        IMandateConfigAdminService configService,
        ISignatureVaultPolicy vaultPolicy,
        IAttachmentStorage storage,
        IEmailSender emailSender)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _mandatePolicy = mandatePolicy ?? throw new ArgumentNullException(nameof(mandatePolicy));
        _signerDirectory = signerDirectory ?? throw new ArgumentNullException(nameof(signerDirectory));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _vaultPolicy = vaultPolicy ?? throw new ArgumentNullException(nameof(vaultPolicy));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
    }

    /// <summary>
    /// Mandatarios habilitados ANTE ESE ORGANISMO, con la compañía a la que están vinculados. El
    /// vínculo (mandatario × organismo × compañía) es lo que decide quién puede firmar, así que la
    /// lista sale de él y no del padrón de mandatarios.
    /// </summary>
    public async Task<IReadOnlyList<MandateSimulatorSignerOption>> ListSignersAsync(
        Guid officeId,
        CancellationToken ct = default)
    {
        if (officeId == Guid.Empty)
            return [];

        var vinculos = await (
            from s in _db.MandateSigners.AsNoTracking()
            join c in _db.MandateSignerCompanies.AsNoTracking() on s.Id equals c.MandateSignerId
            where c.TransitOfficeId == officeId && c.IsActive && s.IsActive
            select new { s.Id, s.FullName, s.DocumentNumber, c.CompanyTenantId, s.SignatureVaultId })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Un mandatario puede estar vinculado a varias compañías del mismo organismo; en la lista
        // aparece una sola vez (la firma que se estampa no depende de por cuál de ellas se le ofrezca).
        var candidatos = await _signerDirectory.GetCandidatesAsync(
            officeId, vinculos.Select(v => v.CompanyTenantId).FirstOrDefault(), null, ct)
            .ConfigureAwait(false);
        var vigencias = candidatos.ToDictionary(c => c.Id, c => c.IdentityVigente);

        return
        [
            .. vinculos
                .GroupBy(v => v.Id)
                .Select(g => g.First())
                .OrderBy(v => v.FullName, StringComparer.CurrentCultureIgnoreCase)
                .Select(v => new MandateSimulatorSignerOption(
                    v.Id,
                    v.FullName,
                    v.DocumentNumber,
                    vigencias.GetValueOrDefault(v.Id),
                    v.SignatureVaultId is not null)),
        ];
    }

    public async Task<MandateSimulationResult> PreviewAsync(
        MandateSimulationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await BuildAsync(
            request.OfficeId, request.PersonType, request.AssignmentMode, request.MandateSignerId,
            request.Tipologia, ct)
            .ConfigureAwait(false);
    }

    public async Task<MandateSimulationResult> SendAsync(
        MandateSimulationSendRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var to = request.ToEmail?.Trim();
        if (!EsCorreoValido(to))
        {
            return new MandateSimulationResult(
                MandateSimulationOutcome.InvalidRecipient,
                "Indica un correo de destino válido.");
        }

        var documento = await BuildAsync(
            request.OfficeId, request.PersonType, request.AssignmentMode, request.MandateSignerId,
            request.Tipologia, ct)
            .ConfigureAwait(false);
        if (!documento.Success)
            return documento;

        var office = _catalog.GetById(request.OfficeId);
        var asunto = $"Simulación de Contrato de Mandato · {office?.Name ?? "Organismo de tránsito"}";

        // El cuerpo dice explícitamente que es una simulación: el PDF adjunto lleva marcadores donde
        // van los datos del mandante, y sin ese aviso podría confundirse con un contrato emitido.
        var html =
            "<p>Adjunto encontrarás una <b>simulación</b> del Contrato Privado de Mandato "
            + $"para <b>{System.Net.WebUtility.HtmlEncode(office?.Name ?? "el organismo de tránsito")}</b>.</p>"
            + "<p>Es un documento de prueba generado desde Plataforma → Mandatos: "
            + "los datos del mandante, la placa y el trámite aparecen como marcadores y "
            + "<b>no corresponde a ningún trámite real</b>.</p>";

        var message = new EmailMessage(
            TenantId: null,
            TemplateKey: TemplateKey,
            ToEmail: to!,
            ToName: string.IsNullOrWhiteSpace(request.ToName) ? to! : request.ToName!.Trim(),
            Subject: asunto,
            HtmlBody: html)
        {
            Attachments =
            [
                new EmailAttachment(
                    documento.FileName ?? "mandato_simulado.pdf",
                    "application/pdf",
                    documento.Content ?? []),
            ],
        };

        var envio = await _emailSender.SendAsync(message, ct).ConfigureAwait(false);

        // El mensaje del transporte ya es genérico por contrato (nunca host ni credenciales).
        return envio.Success
            ? new MandateSimulationResult(
                MandateSimulationOutcome.Ok,
                "Simulación enviada con el PDF adjunto.",
                documento.Content,
                documento.FileName)
            : new MandateSimulationResult(MandateSimulationOutcome.SendFailed, envio.Message);
    }

    private async Task<MandateSimulationResult> BuildAsync(
        Guid officeId,
        string? personType,
        string? assignmentModeOverride,
        Guid? mandateSignerId,
        string? tipologia,
        CancellationToken ct)
    {
        var office = _catalog.GetById(officeId);
        if (office is null)
        {
            return new MandateSimulationResult(
                MandateSimulationOutcome.OfficeNotFound,
                "El organismo de tránsito no existe o no está dado de alta en FLIT.");
        }

        if (assignmentModeOverride is not null
            && !AssignmentModes.Contains(assignmentModeOverride.Trim()))
        {
            return new MandateSimulationResult(
                MandateSimulationOutcome.InvalidAssignmentMode,
                "El modo de asignación del mandatario no es válido.");
        }

        // El mandatario decide la compañía del escenario: su firma del baúl vive en ESE tenant, y la
        // regla compañía×OT (modo y mandatario por defecto) también. Sin mandatario elegido se simula
        // sin compañía y manda la configuración del organismo.
        var companyTenantId = mandateSignerId is { } elegido
            ? await ResolveCompanyTenantAsync(officeId, elegido, ct).ConfigureAwait(false)
            : null;

        if (mandateSignerId is not null && companyTenantId is null)
        {
            return new MandateSimulationResult(
                MandateSimulationOutcome.SignerNotFound,
                "El mandatario indicado no está habilitado para ese organismo.");
        }

        var config = await _mandatePolicy
            .ResolveByOfficeIdAsync(officeId, companyTenantId, ct)
            .ConfigureAwait(false);

        var assignmentMode = MandatoAssignmentModeCodes.Resolve(
            assignmentModeOverride ?? config?.AssignmentMode);

        var templateCode = config?.TemplateCode ?? MandatoTemplateResolver.Generico;
        var esJuridica = MandateSimulationPersonTypes.IsJuridica(personType);

        // Mismo orden que el trámite: elección explícita → default de la regla compañía×OT.
        MandatarioFirmante? mandatario = null;
        if (!MandatoAssignmentModeCodes.SkipsPersonSigner(assignmentMode))
        {
            var signerId = mandateSignerId ?? config?.DefaultMandateSignerId;
            if (signerId is { } id && id != Guid.Empty)
            {
                var signer = await _signerDirectory.GetByIdAsync(id, ct).ConfigureAwait(false);
                if (signer is null)
                {
                    return new MandateSimulationResult(
                        MandateSimulationOutcome.SignerNotFound,
                        "El mandatario indicado no existe o está inactivo.");
                }

                var firma = companyTenantId is { } tenantId
                    ? await MandatarioFirmaResolver
                        .ResolveAsync(_vaultPolicy, _storage, tenantId, signer, cancellationToken: ct)
                        .ConfigureAwait(false)
                    : default;

                mandatario = new MandatarioFirmante(
                    signer.Nombre, signer.Documento, firma.Firma, firma.Sello, firma.Metadatos);
            }
        }

        // Datos de muestra (no marcadores entre corchetes): el simulador existe para juzgar cómo se
        // LEE el contrato impreso. Son ficticios y lo dicen en el propio texto.
        var sample = MandatoPreviewSample.Build(
            templateCode,
            esJuridica,
            new OrganismoTransito(office.Code, office.Name, MandatoPreviewSample.PhCiudadOrganismo),
            mandatario,
            MandateSimulationTipologias.Resolve(tipologia),
            datosDeMuestra: true);

        byte[]? customPdf = null;
        if (MandatoCustomTemplateKindCodes.Resolve(config?.CustomTemplateKind)
            == MandatoCustomTemplateKindCodes.Pdf)
        {
            customPdf = await _configService.OpenCustomPdfAsync(officeId, ct).ConfigureAwait(false);
        }

        var data = sample with
        {
            InstitutionalMandataryName = config?.InstitutionalMandataryName ?? sample.InstitutionalMandataryName,
            InstitutionalMandataryNit = config?.InstitutionalMandataryNit ?? sample.InstitutionalMandataryNit,
            Familia = MandatoFamiliaCodes.Resolve(config?.MandataryFamily),
            ChamberCity = config?.ChamberCity ?? sample.ChamberCity,
            MandatarySigla = config?.MandatarySigla ?? sample.MandatarySigla,
            // Abierto: sin firmante, bloque con líneas. Institucional: sin bloque de mandatario.
            Mandatario = MandatoAssignmentModeCodes.IsOpen(assignmentMode) ? null : mandatario,
            ModoFirmaMandatario = MandatoAssignmentModeCodes.IsInstitutional(assignmentMode)
                ? MandatarioFirmaModo.SinBloque
                : MandatoAssignmentModeCodes.IsOpen(assignmentMode)
                    ? MandatarioFirmaModo.Manual
                    : MandatarioFirmaModo.Estampada,
            CustomTemplateKind = MandatoCustomTemplateKindCodes.Resolve(config?.CustomTemplateKind),
            CustomTemplateBody = config?.CustomTemplateBody,
            CustomTemplatePdf = customPdf,
        };

        var doc = _generator.GenerateMandato(data);
        var sufijoTramite = MandateSimulationTipologias.Resolve(tipologia)
            == MandateSimulationTipologias.MatriculaInicial ? "matricula" : "traspaso";
        var nombre = string.Create(
            CultureInfo.InvariantCulture,
            $"mandato_simulado_{office.Code}_{(esJuridica ? "pj" : "pn")}_{sufijoTramite}.pdf");

        return new MandateSimulationResult(
            MandateSimulationOutcome.Ok, "Simulación generada.", doc.Content, nombre);
    }

    /// <summary>Tenant de la compañía que habilita a ese mandatario en ese organismo, si lo hay.</summary>
    private async Task<Guid?> ResolveCompanyTenantAsync(
        Guid officeId, Guid mandateSignerId, CancellationToken ct)
    {
        var tenant = await (
            from s in _db.MandateSigners.AsNoTracking()
            join c in _db.MandateSignerCompanies.AsNoTracking() on s.Id equals c.MandateSignerId
            where c.TransitOfficeId == officeId
                && c.MandateSignerId == mandateSignerId
                && c.IsActive
                && s.IsActive
            select (Guid?)c.CompanyTenantId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return tenant;
    }

    /// <summary>
    /// Validación deliberadamente mínima: hay exactamente una arroba, con texto a ambos lados y un
    /// punto en el dominio. Quien decide de verdad si el buzón existe es el proveedor de correo.
    /// </summary>
    private static bool EsCorreoValido(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var partes = email.Split('@');
        return partes.Length == 2
            && partes[0].Length > 0
            && partes[1].Contains('.', StringComparison.Ordinal)
            && !partes[1].StartsWith('.')
            && !partes[1].EndsWith('.');
    }
}
