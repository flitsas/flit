using System.Globalization;
using System.Text.Json;
using Flit.Queries.Domain.Time;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.Avaluos;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #14547 — cómo resuelve <see cref="MandatoEmisor"/> el mandatario que firma el contrato de mandato.
/// </summary>
public enum ModoFirmanteMandato
{
    /// <summary>
    /// Camino del FUR, sin cambios: en borrador/subsanación se recalcula con la prelación de ADR-0066; fuera de
    /// borrador manda el <c>MandateSignerId</c> guardado aunque ya no sea válido (HU #13142 AC4).
    /// </summary>
    ConservarGuardadoFueraDeBorrador,

    /// <summary>
    /// Acción «Regenerar mandato y consolidado» del SuperAdmin (R1): la prelación contra la configuración vigente
    /// en CUALQUIER estado, y el resultado sobrescribe <c>MandateSignerId</c> (también a <c>null</c> si hoy no
    /// resuelve a nadie). Excepción explícita a HU #13142 AC4, solo para esa acción.
    /// </summary>
    RecalcularSiempre,
}

/// <summary>
/// HU #14547 — resultado de regenerar solo el mandato. <c>PersonalizadoConservado</c>: manda el documento personalizado
/// de la compañía (HU #11313), que no se reemplaza.
/// </summary>
public sealed record MandatoRegenerado(
    Guid? AttachmentId,
    Guid? FirmanteAnteriorId,
    Guid? FirmanteNuevoId,
    bool SinFirmar,
    bool PersonalizadoConservado)
{
    /// <summary>Sin generador de mandato en la composición: no hay nada que emitir.</summary>
    public const string NoDisponible = "mandato_no_disponible";
}

/// <summary>
/// HU #14547 — puerto para volver a emitir SOLO el contrato de mandato con el mandatario recalculado
/// (<see cref="ModoFirmanteMandato.RecalcularSiempre"/>), sin regenerar el FUR. Lo implementa
/// <c>GenerarFurHandler</c>, el único que arma el <see cref="FurDocumentData"/> que el mandato necesita.
/// </summary>
public interface IMandatoRegenerador
{
    Task<(MandatoRegenerado? Result, string? Error)> RegenerarMandatoAsync(Guid id, Guid tenantId, CancellationToken ct = default);
}

/// <summary>Contrato de mandato generado, el formato/versión de plantilla con que salió (HU #13172) y su firmante.</summary>
/// <param name="RequiereFirmantePersona">El modo de asignación del OT pide un mandatario persona (no institucional ni abierto).</param>
/// <param name="Firmante">Mandatario que quedó en el documento; <c>null</c> = recuadro «Sin firmar» o sin firmante persona.</param>
internal sealed record MandatoEmitido(
    GeneratedDocument Document,
    string? FormatCode,
    int? FormatVersion,
    bool RequiereFirmantePersona,
    MandatarioFirmante? Firmante);

/// <summary>
/// ADR-0036 (HU #10915) — emisión del Contrato de Mandato, extraída tal cual de <c>GenerarFurHandler</c>
/// (antes <c>TryGenerateMandatoAsync</c>) para que la acción del SuperAdmin (HU #14547) lo regenere sin pasar por
/// el FUR. El FUR lo usa con <see cref="ModoFirmanteMandato.ConservarGuardadoFueraDeBorrador"/> y se comporta
/// exactamente como antes. Lo construye <c>GenerarFurHandler</c> con sus mismas dependencias: el
/// <see cref="FurDocumentData"/> que necesita solo lo arma él.
/// </summary>
internal sealed class MandatoEmisor(
    IProcedureInstanceRepository repo,
    IKyverumCertificateClient certClient,
    IAttachmentStorage storage,
    ILogger logger,
    ISignatureVaultPolicy vaultPolicy,
    IMandatoGenerator? mandatoGenerator,
    IMandateRequirementPolicy mandatePolicy,
    IMandateSignerDirectory mandateDirectory,
    IMandateCustomTemplateBlobReader mandateTemplateBlob,
    IMandateFormatTemplateProvider? mandateFormatTemplates,
    IIdentitySignatureCapture? identitySignatureCapture,
    IMandatoFirmaPolicy mandatoFirmaPolicy)
{
    /// <summary>¿Hay generador de mandato? Sin él (tests/DI que no lo ejercitan) no se emite nada.</summary>
    public bool Disponible => mandatoGenerator is not null;

    /// <summary>
    /// Firma del MANDATARIO para el contrato de mandato (HU #11030): imagen del baúl si la tiene
    /// vigente, y si no el sello de su validación de identidad. Best-effort: cualquier fallo deja la
    /// línea de firma en blanco, nunca rompe la generación del mandato.
    /// </summary>
    private async Task<(byte[]? Firma, string? Sello, FirmaBaulMetadata? Metadatos, byte[]? FirmaIdentidad)> ResolveMandatarioFirmaAsync(
        FurDocumentData data, MandateSignerCandidate signer, CancellationToken ct)
    {
        // La precedencia vive en MandatarioFirmaResolver: el simulador de mandatos la comparte para
        // mostrar el documento tal como saldría del trámite (Feature #11702).
        var (firma, sello, metadatos, _, firmaIdentidad) = await MandatarioFirmaResolver
            .ResolveAsync(
                vaultPolicy,
                storage,
                data.TenantIdParaFirmas,
                signer,
                ex => GenerarFurLog.FirmaBaulNoDisponible(logger, ex, data.ProcedureInstanceId),
                ct)
            .ConfigureAwait(false);

        return (firma, sello, metadatos, firmaIdentidad);
    }

    /// <summary>
    /// Respaldo de la rúbrica del MANDATARIO, como <see cref="TryCaptureIdentitySignatureAsync"/> para las partes. La
    /// captura al terminar la validación puede no guardar la imagen (certificado aún no disponible, o un recorte que no
    /// la reconocía) y la outbox no reintenta. Solo actúa si firma con su validación propia vigente, sin baúl y sin
    /// rúbrica: toma su validación propia más reciente con el documento actual (la misma que decide su vigencia),
    /// descarga el certificado y captura. Devuelve el candidato con la ruta de la rúbrica si se capturó.
    /// </summary>
    private async Task<MandateSignerCandidate> TryCaptureMandatarioRubricaAsync(
        MandateSignerCandidate signer, Guid instanceId, CancellationToken ct)
    {
        if (identitySignatureCapture is null
            || !signer.FirmaValida
            || !signer.IdentityVigente
            || !string.IsNullOrWhiteSpace(signer.RubricaIdentidadPath)
            || signer.SignatureVaultId is not null
            || string.IsNullOrWhiteSpace(signer.Documento))
            return signer;

        try
        {
            var tipo = string.IsNullOrWhiteSpace(signer.TipoDocumento) ? "CC" : signer.TipoDocumento.Trim();
            var propias = await repo.ListMandatarioValidationsAsync([signer.Id], ct).ConfigureAwait(false);
            var ultima = propias.FirstOrDefault(v => BiometricRules.DocumentoCoincide(v, tipo, signer.Documento.Trim()));
            if (ultima is null || !GenerarFurHandler.EsKyverumConId(ultima) || !string.IsNullOrWhiteSpace(ultima.SignatureImagePath))
                return signer;

            // La lista es de solo lectura: la captura escribe sobre la validación rastreada.
            var bio = await repo.GetBiometricByIdAsync(ultima.Id, ct).ConfigureAwait(false);
            if (bio is null)
                return signer;

            var cert = await certClient.DownloadCertificateAsync(bio.KyverumVerificationId!, ct).ConfigureAwait(false);
            if (cert is null || cert.Content.Length == 0)
                return signer;

            var outcome = await identitySignatureCapture.EnsureFromPdfAsync(bio, cert.Content, ct).ConfigureAwait(false);
            return outcome == IdentitySignatureCaptureOutcome.Captured
                ? signer with { RubricaIdentidadPath = bio.SignatureImagePath }
                : signer;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            GenerarFurLog.CertificadoDescargaFallo(logger, ex, instanceId);
            return signer;
        }
    }

    /// <summary>
    /// Genera el contrato siempre que haya generador y código de OT (persona natural y jurídica); sin ellos devuelve
    /// <c>null</c> (el caller del FUR retira el mandato previo). <paramref name="modo"/> decide de dónde sale el
    /// mandatario (ver <see cref="ModoFirmanteMandato"/>); el FUR usa siempre
    /// <see cref="ModoFirmanteMandato.ConservarGuardadoFueraDeBorrador"/>.
    /// </summary>
    public async Task<MandatoEmitido?> EmitirAsync(
        ProcedureInstance instance,
        FurDocumentData data,
        string? transitOfficeCode,
        string? transitOfficeName,
        string? transitOfficeCity,
        IReadOnlyList<string> transformaciones,
        ModoFirmanteMandato modo,
        CancellationToken ct)
    {
        if (mandatoGenerator is null || string.IsNullOrWhiteSpace(transitOfficeCode))
            return null;

        // El FUR puede encabezar con el OT “actual” (RUNT). El mandato se emite para el OT canónico
        // al que va el trámite; si se usa el actual, Bogotá pintaba redacción de Bello.
        var nombreMandato = transitOfficeName ?? data.Organismo.Nombre;
        data = data with
        {
            Organismo = new OrganismoTransito(
                transitOfficeCode,
                nombreMandato,
                TransitOfficeCity.ForDocuments(transitOfficeCity, nombreMandato) ?? data.Organismo.Ciudad),
        };

        // El ORGANISMO se llavea por id cuando el trámite lo tiene, y solo si no, por el código. El
        // código de field_values no es una llave confiable —conviven RUNT de 7 dígitos con DIVIPOLA de
        // 5— y cuando no coteja exacto contra el catálogo NO se encuentra ni la fila de configuración ni
        // la plantilla de sistema: el mandato salía GENÉRICO y sin mandatario con el OT bien
        // parametrizado. Ver IMandateRequirementPolicy.ResolveByOfficeIdAsync.
        var transitOfficeId = MandateSignerSelectionResolver.ResolveTransitOfficeId(instance);
        var config = transitOfficeId is { } officeIdParaConfig
            ? await mandatePolicy.ResolveByOfficeIdAsync(officeIdParaConfig, data.TenantIdParaFirmas, ct)
                .ConfigureAwait(false)
            : null;
        config ??= await mandatePolicy.ResolveAsync(transitOfficeCode, data.TenantIdParaFirmas, ct)
            .ConfigureAwait(false);
        // Producto: el mandato se emite siempre (PN y PJ). La plantilla/familia vienen de la config del OT.

        // HU-L8 / HU #13142 — firmante por la prelación de ADR-0066 (OT para la compañía → propio de la compañía →
        // default del OT → vacío).
        // Sin esos, Mandatario queda null (cuerpo ___ / recuadro Sin firmar). Ya no se espera a aprobar
        // para pintar nombre y cédula cuando hay default.
        var assignmentMode = config?.AssignmentMode;
        // Plantilla del OT (o genérica de mandato cliente). Ya no se reescribe por PN/PJ ni por modo.
        var templateCode = config?.TemplateCode ?? MandatoTemplateResolver.Generico;

        MandatarioFirmante? mandatario = null;
        Guid? resolvedSignerId = null;
        if (!MandatoAssignmentModeCodes.SkipsPersonSigner(assignmentMode))
        {
            if (transitOfficeId is { } officeId)
            {
                // En borrador/subsanación el firmante se recalcula SIEMPRE con la prelación única de ADR-0066
                // (OT para la compañía → propio → asociado → default del OT) contra la config vigente.
                // Congelar instance.MandateSignerId en la primera generación dejaba el PDF con un firmante
                // viejo después de cambiar la configuración en Mandatos.
                // Fuera de borrador (expediente ya radicado) manda lo guardado aunque hoy ya no sea válido
                // (baja lógica, vencimiento): es documento legal y se conserva la referencia (HU #13142 AC4).
                var enBorrador = TramiteEstado.PermiteEdicionDatos(
                    instance.Status, instance.SubsanacionActiva);
                // HU #14547 (R1) — la acción del SuperAdmin recalcula también fuera de borrador.
                var recalcular = enBorrador || modo == ModoFirmanteMandato.RecalcularSiempre;

                MandateSignerCandidate? signer = null;
                if (!recalcular && instance.MandateSignerId is { } guardadoId)
                {
                    resolvedSignerId = guardadoId;
                    signer = await mandateDirectory
                        .GetByIdAsync(guardadoId, incluirEliminados: true, ct).ConfigureAwait(false);
                }
                else
                {
                    var (prelacion, _) = await MandateSignerPrelacionLoader
                        .ResolveAsync(
                            mandateDirectory, vaultPolicy, officeId, data.TenantIdParaFirmas,
                            MandateSignerSelectionResolver.ResolveNitMandante(instance), config,
                            eleccionOt: null, guardado: null, ct)
                        .ConfigureAwait(false);
                    // Un nivel ambiguo no sugiere a nadie (el OT elige al aprobar); quien firma a mano
                    // sigue saliendo en el PDF (P4).
                    signer = prelacion.Signer ?? prelacion.FirmaFisicaPendiente;
                    resolvedSignerId = signer?.Id;
                }

                if (resolvedSignerId is { } signerId)
                {
                    if (signer is not null)
                    {
                        // HU #11030 — la firma del mandatario no se pintaba nunca: el contrato salía con
                        // la línea en blanco aunque el mandatario tuviera firma en el baúl o identidad
                        // validada. Misma precedencia que el resto de documentos: imagen del baúl > sello
                        // de identidad > línea.
                        // Igual que las partes en el FUR: si su validación propia no guardó la rúbrica al terminar, se
                        // captura ahora del certificado de Kyverum (best-effort; nunca bloquea el mandato).
                        signer = await TryCaptureMandatarioRubricaAsync(signer, instance.Id, ct).ConfigureAwait(false);
                        var (firma, sello, metadatos, firmaIdentidad) =
                            await ResolveMandatarioFirmaAsync(data, signer, ct).ConfigureAwait(false);
                        mandatario = new MandatarioFirmante(
                            signer.Nombre, signer.Documento, firma, sello, metadatos, firmaIdentidad);

                        if (recalcular)
                            instance.MandateSignerId = signerId;
                        else if (instance.MandateSignerId is null)
                            instance.MandateSignerId = signerId;
                    }
                }
            }
        }

        // HU #14547 (R1) — recalcular SOBRESCRIBE: si hoy la prelación no resuelve a nadie (o el modo del OT no lleva
        // mandatario persona), la referencia anterior no sobrevive a un documento que ya no la nombra.
        if (modo == ModoFirmanteMandato.RecalcularSiempre && mandatario is null)
            instance.MandateSignerId = null;

        // El convenio se llavea por (compañía del trámite, organismo). El organismo del trámite se
        // conoce aquí por su CÓDIGO; el id lo aporta la configuración del OT, que ya se resolvió arriba
        // por ese mismo código: sin fila de configuración no hay id y el mandato conserva el bloque, que
        // es el default seguro.
        var modoFirma = await mandatoFirmaPolicy
            .ResolveAsync(data.TenantIdParaFirmas, config?.TransitOfficeId ?? Guid.Empty, resolvedSignerId, ct)
            .ConfigureAwait(false);

        var customKind = MandatoCustomTemplateKindCodes.Resolve(config?.CustomTemplateKind);
        byte[]? customPdf = null;
        if (customKind == MandatoCustomTemplateKindCodes.Pdf
            && !string.IsNullOrWhiteSpace(config?.CustomTemplateStoragePath))
        {
            customPdf = await mandateTemplateBlob
                .OpenPdfAsync(config!.CustomTemplateStoragePath!, ct)
                .ConfigureAwait(false);
        }

        // Institucional / convenio: sin bloque MANDATARIO.
        // Abierto o sin estampa: líneas. Si hay baúl o sello, se estampa aunque el modelo sea a mano.
        var modoFirmaMandatario = MandatoFirmaModoResolver.Resolve(
            assignmentMode,
            modoFirma.TieneConvenio,
            MandatoFirmaModoResolver.TieneEstampa(mandatario));

        var mandatoData = new MandatoData(
            data,
            templateCode,
            config?.InstitutionalMandataryName,
            config?.InstitutionalMandataryNit,
            mandatario,
            MandatoFamiliaCodes.Resolve(config?.MandataryFamily),
            config?.ChamberCity,
            config?.MandatarySigla,
            transformaciones,
            modoFirmaMandatario,
            customKind,
            config?.CustomTemplateBody,
            customPdf);

        // HU #13172 — plantilla publicada del formato. Regenerar un mandato ya emitido reproduce la versión registrada en su
        // adjunto (si es del mismo formato); un trámite sin mandato previo toma la vigente. Es el mismo camino del simulador.
        var previo = instance.Attachments.FirstOrDefault(a =>
            string.Equals(a.Tipo, "mandato", StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Source, "system", StringComparison.OrdinalIgnoreCase));
        int? versionRegistrada = previo?.MandateFormatVersion is { } v
            && (string.IsNullOrEmpty(previo.MandateFormatCode)
                || string.Equals(previo.MandateFormatCode, templateCode, StringComparison.OrdinalIgnoreCase))
            ? v
            : null;
        var aplicado = await MandatoFormatTemplateApplier
            .ApplyAsync(mandatoData, mandateFormatTemplates, versionRegistrada, ct)
            .ConfigureAwait(false);

        return new MandatoEmitido(
            mandatoGenerator.GenerateMandato(aplicado.Data), aplicado.FormatCode, aplicado.FormatVersion,
            RequiereFirmantePersona: !MandatoAssignmentModeCodes.SkipsPersonSigner(assignmentMode),
            Firmante: mandatario);
    }
}
