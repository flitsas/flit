using System.Globalization;
using System.Text.Json;
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

/// <summary>Un documento generado (FUR / compraventa), referenciado al adjunto persistido.</summary>
public sealed record FurDocumentDto(Guid AttachmentId, string Tipo, string Filename, string Sha256);

public sealed record GenerarFurResult(IReadOnlyList<FurDocumentDto> Documents);

/// <summary>
/// Genera el FUR (y, en traspaso, el contrato de compraventa) con los datos reales de la instancia.
/// <para><b>Validación de identidad</b> (HU #10463): ya NO bloquea la generación. Se resuelve la identidad
/// vigente PER-PERSONA (documento del actor, HU #10350) de las partes requeridas (traspaso: comprador +
/// vendedor; matrícula: comprador); si no está aprobada+vigente, el FUR se genera con el sello "NO FIRMADO"
/// en el espacio de firma y NO se emite el certificado de identidad. La RADICACIÓN sí sigue exigiendo
/// identidad (SubmitGate/#10459).</para>
/// El FUR/compraventa se generan vía <c>IFurDocumentGenerator</c> y se persisten como ADJUNTO
/// (<c>IAttachmentStorage</c> + fila en procedure_instance_attachments, tipo 'fur' / 'compraventa').
/// El <b>certificado de identidad</b> es el PDF REAL de Kyverum, descargado <i>best-effort</i>: si la
/// descarga falla se registra un warning y se OMITE (no bloquea el FUR ni emite mock).
/// Idempotente: re-generar reemplaza los adjuntos previos. Registra un evento <c>fur_generado</c>.
/// </summary>
public sealed class GenerarFurHandler(
    IProcedureInstanceRepository repo,
    IFurDocumentGenerator generator,
    IKyverumCertificateClient certClient,
    IRuesCertificateGenerator ruesGenerator,
    IRnmcCertificateGenerator rnmcGenerator,
    IProcedureInstancePrendaRepository prendaRepo,
    IAttachmentStorage storage,
    ILogger<GenerarFurHandler> logger,
    ISignatureVaultPolicy? vaultPolicy = null,
    ISolicitudVirtualGenerator? solicitudVirtualGenerator = null,
    IMandatoGenerator? mandatoGenerator = null,
    IMandateRequirementPolicy? mandatePolicy = null,
    IMandatoFirmaPolicy? mandatoFirmaPolicy = null,
    IMandateSignerDirectory? mandateDirectory = null,
    ISoatRtmCertificateGenerator? soatRtmGenerator = null,
    GetSuggestedCommercialValueHandler? avaluoHandler = null,
    IFurTemplateResolver? templateResolver = null,
    IProcedureDeedResolver? deedResolver = null,
    IRepresentanteLegalDirectory? representanteDirectory = null,
    Certifications.ICertificationReader? certificationReader = null)
    : IExpedienteHotDocumentsRegenerator
{
    // HU #11198 (AC3) — respaldo del directorio para el nombre del representante cuando el trámite no lo
    // trajo. Default inerte (nunca responde) en los tests que no lo ejercitan: sin él, el hueco queda
    // como estaba, que es el comportamiento previo.
    private readonly IRepresentanteLegalDirectory _representanteDirectory =
        representanteDirectory ?? NullRepresentanteLegalDirectory.Instance;

    // HU #11305 (Feature #11301, ADR-0041) — lector documental de certificaciones. Sustituye al
    // IRuesActorDataResolver, que consultaba el RUES EN VIVO al generar el PDF: una llamada saliente,
    // cobrada, en cada regeneración, que además dejaba el documento a merced de que el proveedor
    // estuviera arriba. Ahora todo sale de base de datos y generar el expediente cuesta cero llamadas
    // externas (D4). Default nulo en los tests que no lo ejercitan: se cae al respaldo sobre
    // field_values, que es el comportamiento previo menos la consulta.
    private readonly Certifications.ICertificationReader? _certificationReader = certificationReader;

    // ADR-0025 §4 / HU #10645 — baúl de firmas: cubre la identidad de un actor NIT y alimenta la
    // IMAGEN real de la firma en el FUR. Default seguro (NUNCA resuelve) en tests que no lo ejercitan.
    private readonly ISignatureVaultPolicy _vaultPolicy = vaultPolicy ?? NullSignatureVaultPolicy.Instance;

    // ADR-0036 (HU #10914) — Solicitud de trámite virtual (siempre). Opcional: los tests que no lo
    // ejercitan construyen el handler sin él (no se genera el documento).
    private readonly ISolicitudVirtualGenerator? _solicitudVirtualGenerator = solicitudVirtualGenerator;

    // ADR-0036 (HU #10915) — Contrato de mandato (condicional). Opcional: sin el generador no se emite.
    private readonly IMandatoGenerator? _mandatoGenerator = mandatoGenerator;

    // ADR-0036 (HU #10912/#10915) — config de mandato por OT (plantilla / exige a PN / mandatario
    // institucional). Default seguro (NUNCA resuelve ⇒ plantilla genérica, solo PJ) si no se inyecta.
    private readonly IMandateRequirementPolicy _mandatePolicy = mandatePolicy ?? NullMandateRequirementPolicy.Instance;

    // Convenio compañía↔organismo y firma física del mandatario: deciden si el mandato lleva bloque de
    // firma del mandatario. Default seguro ⇒ lo lleva (es un actor obligatorio).
    private readonly IMandatoFirmaPolicy _mandatoFirmaPolicy = mandatoFirmaPolicy ?? NullMandatoFirmaPolicy.Instance;

    // ADR-0036 §D9 (HU #10916) — directorio de mandatarios: rellena el firmante del PDF del mandato desde
    // instance.MandateSignerId (resuelto al aprobar). Default seguro (NUNCA resuelve) si no se inyecta.
    private readonly IMandateSignerDirectory _mandateDirectory = mandateDirectory ?? NullMandateSignerDirectory.Instance;

    // HU #10920 (Feature #10918) — resuelve la plantilla de FUR según la clasificación del vehículo. Si no
    // se inyecta (tests), la plantilla es AUTOMOTOR (comportamiento previo intacto).
    private readonly IFurTemplateResolver? _templateResolver = templateResolver;

    // HU #10926 (ADR-0033) — resolutor de escrituras vigentes de las compañías (NIT) de los actores,
    // para adjuntarlas al consolidado. Default nulo (no resuelve) en tests que no lo ejercitan.
    private readonly IProcedureDeedResolver _deedResolver = deedResolver ?? NullProcedureDeedResolver.Instance;

    /// <summary>
    /// HU #10860 (cascada β) — regenera el FUR y sus documentos en caliente (con fecha vigente) para
    /// que el consolidado del wizard los incluya frescos. Devuelve el código de error o null si fue ok.
    /// </summary>
    public async Task<string?> RegenerateHotDocumentsAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var (_, error) = await HandleAsync(id, tenantId, ct);
        return error;
    }

    public async Task<(GenerarFurResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithFurGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        // Migración V1→V2 — el "modo foto" solo aplica a los migrados en estado FINAL. La generación
        // BORRA y re-inserta cada tipo (fur/compraventa/certificado), así que regenerar sobre un
        // aprobado o anulado destruiría los PDF históricos de V1 y los reemplazaría por mocks del
        // sistema.
        //
        // Un BORRADOR migrado sí debe poder generar: se trajo para seguir trabajándolo en V2, y sin FUR
        // no puede avanzar al consolidado ni radicarse. Ver el guard equivalente en ConsolidadoCommand,
        // que explica por qué no basta con el gate del gestor (HU #11051).
        if (instance.IsMigrated && TramiteEstado.EsFinal(instance.Status))
            return (null, "migrado_solo_lectura");

        var codigo = TipologiaResolver.ResolveCodigo(instance.TipologiaCodigo, instance.ModalidadEntrada);
        var esTraspaso = string.Equals(codigo, TramiteTipologiaCatalog.CodigoTraspasoStandard, StringComparison.OrdinalIgnoreCase);
        // HU #10856 — matrícula inicial no tiene revisión técnico-mecánica: se oculta la tabla RTM.
        var esMatricula = string.Equals(codigo, TramiteTipologiaCatalog.CodigoMatriculaInicial, StringComparison.OrdinalIgnoreCase);

        // HU #10463 — la validación de identidad ya NO bloquea la GENERACIÓN del FUR/consolidado.
        // Gating PER-PERSONA (HU #10350): se referencia la identidad vigente de la persona (documento del
        // actor), no una fila propia del trámite. Si falta, el FUR se genera con el sello "NO FIRMADO" y sin
        // certificado (no se declara "APROBADO" en falso). La RADICACIÓN sí sigue exigiendo identidad (#10459).
        var identidadAprobada = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(
            repo, instance, DateTimeOffset.UtcNow, ct, _vaultPolicy);
        var identidadValidada = BiometriaGateOk(identidadAprobada, esTraspaso);

        var fv = instance.FieldValues
            .ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);

        // Gating organismo de tránsito: requiere transit_office_code no vacío en field_values.
        if (string.IsNullOrWhiteSpace(Get(fv, "transit_office_code")))
            return (null, "organismo_requerido");

        // HU #11305 (Feature #11301, ADR-0041) — TODO lo certificado del expediente se resuelve aquí,
        // de una vez y CONTRA BASE DE DATOS. A partir de este punto generar el expediente no hace ni
        // una llamada saliente: la consulta en vivo al RUES que corría por cada regeneración —cobrada,
        // y capaz de dejar sin certificado un documento si el proveedor estaba caído— desaparece.
        var certs = _certificationReader is null
            ? Certifications.CertificationView.Empty
            : await _certificationReader.ForDocumentsAsync(instance.Id, tenantId, fv, ct);

        // HU #10488 — sello de identidad (texto) por parte para el espacio de firma del FUR. Solo cuando la
        // identidad está validada (si no, el mapper pinta "NO FIRMADO"). Se resuelve la validación aprobada+
        // vigente de cada parte (fila propia o identidad referenciada por documento, HU #10350).
        var sellosIdentidad = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (identidadValidada)
        {
            var roles = esTraspaso ? new[] { "comprador", "vendedor" } : new[] { "comprador" };
            foreach (var role in roles)
            {
                var val = await ResolveApprovedValidationAsync(instance, role, DateTimeOffset.UtcNow, ct);
                if (val is not null)
                    sellosIdentidad[role] = BuildIdentidadSello(val);
            }
        }

        // HU #10601, ampliado por HU #11257 (Feature #11254) — prenda vigente: resuelve YA en el
        // dominio (PrendaDecision.ToFurMarking) qué casilla marca el FUR: Constitucion (solicitar/
        // registrar) → 11, Levantamiento (levantar) → 12, Ninguna (omitir/sin_prenda/sin fila) →
        // ninguna. Antes `tienePrenda` (bool) colapsaba `levantar` al mismo `false` que "sin prenda":
        // el generador no podía distinguirlos.
        var prendaVigente = await prendaRepo.GetVigenteAsync(id, tenantId, ct);
        var prendaMarking = PrendaDecision.ToFurMarking(prendaVigente?.Decision);
        var acreedorPrenda = prendaMarking != FurPrendaMarking.Ninguna ? prendaVigente!.AcreedorNombre : null;
        // HU #10989, CF11 (HU #11257) — el documento del acreedor acompaña al nombre en el bloque de
        // observaciones, tanto en constitución como en levantamiento. Se lee solo cuando la marca no es
        // Ninguna: una fila previa 'registrar' que se reemplazó por 'levantar' no arrastra su acreedor
        // al FUR porque siempre se lee de la fila VIGENTE (prendaRepo.GetVigenteAsync), nunca de historia.
        var acreedorPrendaDocumento = prendaMarking != FurPrendaMarking.Ninguna ? prendaVigente!.AcreedorDocumento : null;

        // HU #10645 (ADR-0025 §4) — imagen REAL de la firma del baúl por parte NIT cubierta: se descarga el
        // artefacto (best-effort) y se alimenta FurDocumentData.FirmaImagenes; el mapper la estampa en el
        // espacio de firma en vez del sello de texto. Si la descarga falla, NO rompe el FUR (cae al sello).
        var (firmaImagenes, firmaBaulMetadatos) = await ResolveVaultSignaturesAsync(instance, esTraspaso, ct);

        // Bug #11146 — UNA parte firma de UNA sola manera. Quien firma por el baúl no lleva sello de
        // validación de identidad en ningún documento, aunque su identidad esté vigente; quien firma con
        // la validación de identidad no lleva imagen del baúl. Los dos juntos dejaban el documento como
        // si la parte hubiera firmado dos veces por vías distintas.
        //
        // Va aquí, en el único punto donde se ensamblan los datos de TODOS los documentos, y no en cada
        // generador: la compraventa pintaba ambos y el mandato y la solicitud virtual resolvían la
        // exclusividad cada uno por su cuenta.
        //
        // Hay DOS motivos distintos para retirar el sello, y confundirlos borra firmas legítimas:
        //
        //  · <b>Elección explícita del baúl.</b> Se retira SIEMPRE, resuelva o no la imagen: si el baúl
        //    es el elegido y su descarga falla, la firma queda en blanco —visible— en vez de rellenarse
        //    a escondidas con un sello que el negocio no eligió.
        //
        //  · <b>Sin elección</b> (el caso normal): manda la precedencia del baúl (HU #11031), pero solo
        //    si REALMENTE hay firma. Sin ella se cae al sello de identidad, que es el comportamiento de
        //    siempre. Retirarlo aquí por el mero hecho de ser persona jurídica dejaba sin firma a
        //    comprador y vendedor con identidad validada y sin baúl.
        foreach (var role in esTraspaso ? new[] { "comprador", "vendedor" } : ["comprador"])
        {
            var eligioBaul = EligioExplicitamenteElBaul(instance, role);
            var tieneFirmaDelBaul = firmaImagenes?.ContainsKey(role) == true;
            if (eligioBaul || tieneFirmaDelBaul)
                sellosIdentidad.Remove(role);
        }

        // HU #10920 — plantilla de FUR según la clasificación del vehículo (vehicle_class). Sin resolver → AUTOMOTOR.
        var templateFormat = _templateResolver is not null
            ? await _templateResolver.ResolveAsync(Get(fv, "vehicle_class"), ct)
            : FurTemplateFormat.Automotor;

        // HU #11198 (AC3) — el nombre del representante lo manda el trámite; solo si no lo trajo se pide
        // al directorio de la compañía. Se resuelve ANTES de ensamblar para que AssembleData siga siendo
        // una función pura y síncrona.
        var nombresRlDirectorio = await ResolverNombresDelDirectorioAsync(instance, esTraspaso, ct);

        var data = AssembleData(instance, codigo, esTraspaso, fv, identidadValidada, sellosIdentidad, prendaMarking, acreedorPrenda, acreedorPrendaDocumento, firmaImagenes, firmaBaulMetadatos, templateFormat, nombresRlDirectorio);

        var now = DateTimeOffset.UtcNow;
        var docs = new List<FurDocumentDto>(3);

        // FUR siempre. Compraventa SIEMPRE en traspaso, aunque el usuario haya cargado la suya (ADR-0035,
        // que supersede la condición de ADR-0031): la del sistema lleva la declaración con el formato
        // oficial y los sellos de identidad. La del usuario NO se pierde ni se sobrescribe — el borrado
        // idempotente de abajo solo alcanza Source="system", así que ambas coexisten en el expediente.
        var generated = new List<GeneratedDocument> { generator.GenerateFur(data) };
        if (esTraspaso)
            generated.Add(generator.GenerateCompraventa(data));

        // ADR-0036 (HU #10914) — Solicitud de trámite virtual: SIEMPRE (persona natural y jurídica).
        // Idempotente: el reemplazo por tipo (más abajo) sustituye el adjunto 'tramite_virtual' previo.
        if (_solicitudVirtualGenerator is not null)
            generated.Add(_solicitudVirtualGenerator.GenerateSolicitudVirtual(data));

        // ADR-0036 (HU #10915) — Contrato de mandato: CONDICIONAL (persona jurídica siempre; persona
        // natural solo si el OT lo exige). El firmante (mandatario) aún NO se resuelve en preparado: se
        // regenera al aprobar con el firmante elegido/filtrado (HU #10916). Generar-o-limpiar: si el
        // trámite dejó de exigir mandato en una regeneración, se retira el adjunto 'mandato' previo.
        var mandato = await TryGenerateMandatoAsync(
            data, Get(fv, "transit_office_code"), instance.MandateSignerId, TransformacionesActivas(fv), ct);
        if (mandato is not null)
        {
            generated.Add(mandato);
        }
        else
        {
            foreach (var prev in instance.Attachments
                         .Where(a => string.Equals(a.Tipo, "mandato", StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                storage.Delete(prev.StoragePath);
                instance.Attachments.Remove(prev);
                repo.RemoveAttachment(prev);
            }
        }

        if (identidadValidada)
        {
            // Certificado de identidad: PDF REAL de Kyverum (best-effort) POR PARTE. Traspaso emite el del
            // comprador y el del vendedor; matrícula solo el del comprador (mismo patrón que los sellos).
            // Si falla la descarga de una parte, warning + omitir esa parte (sin mock).
            var rolesCert = esTraspaso
                ? new[] { BiometricRules.ParteComprador, BiometricRules.ParteVendedor }
                : new[] { BiometricRules.ParteComprador };
            foreach (var role in rolesCert)
            {
                var certificado = await TryDownloadIdentityCertificateAsync(instance, role, ct);
                if (certificado is not null)
                    generated.Add(certificado);
            }
        }
        else
        {
            // Sin validación de identidad, retirar cualquier certificado previo (regeneración): el
            // consolidado no debe incluir un certificado de identidad obsoleto (#10463 AC5). StartsWith
            // cubre ambas variantes por parte (certificado_identidad y certificado_identidad_vendedor).
            foreach (var prev in instance.Attachments
                         .Where(a => a.Tipo.StartsWith("certificado_identidad", StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                storage.Delete(prev.StoragePath);
                instance.Attachments.Remove(prev);
                repo.RemoveAttachment(prev);
            }
        }

        // HU #10589 — Certificado RUES por cada actor persona jurídica (NIT), Source=system, para que
        // se fusione en el consolidado. Independiente de la biométrica (una persona jurídica no valida
        // identidad biométrica).
        // HU #10990 — deja de emitirse UNO por trámite desde las rues_* de instancia: se resuelve POR
        // ACTOR (ver TryGenerateRuesCertificates). Los tipos que ya no aplican se retiran abajo.
        var certificadosRues = TryGenerateRuesCertificates(instance, certs);
        generated.AddRange(certificadosRues);

        var tiposRues = certificadosRues
            .Select(d => d.Tipo)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var prev in instance.Attachments
                     .Where(a => a.Tipo.StartsWith("certificado_rues", StringComparison.OrdinalIgnoreCase)
                                 && !tiposRues.Contains(a.Tipo))
                     .ToList())
        {
            // Un actor que dejó de ser jurídico (o el trámite que se quedó sin PJ) no debe arrastrar su
            // certificado al consolidado. Los tipos que SÍ aplican los reemplaza el bucle de persistencia.
            storage.Delete(prev.StoragePath);
            instance.Attachments.Remove(prev);
            repo.RemoveAttachment(prev);
        }

        // HU #10762 — Certificado RNMC: si el preflight consultó el RNMC de alguna parte, emitir el
        // certificado (PDF, Source=system) con el resultado por parte para que se descargue del trámite y
        // se fusione en el consolidado. Best-effort: nunca bloquea el FUR.
        var (certificadoRnmc, aplicaRnmc) = TryGenerateRnmcCertificate(instance);
        if (certificadoRnmc is not null)
        {
            generated.Add(certificadoRnmc);
        }
        else if (!aplicaRnmc)
        {
            // Sin checks RNMC (o dejó de haberlos en una regeneración): retirar el certificado previo, para
            // que el consolidado no arrastre un resultado RNMC obsoleto. Solo cuando el RNMC NO aplica: si
            // aplicaba y la generación falló, se conserva el previo (borrarlo por un fallo transitorio
            // perdería un certificado válido).
            foreach (var prev in instance.Attachments
                         .Where(a => string.Equals(a.Tipo, "certificado_rnmc", StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                storage.Delete(prev.StoragePath);
                instance.Attachments.Remove(prev);
                repo.RemoveAttachment(prev);
            }
        }

        // HU #10856 — "Certificado de vigencia SOAT Y RTM" (PDF con membrete FLIT) desde el RUNT
        // (field_values); en traspaso incluye además el bloque de Avalúo (Feature #10707). Best-effort,
        // Source=system (tipo certificado_soat_rtm). Valores ausentes en la consulta → EN BLANCO.
        if (soatRtmGenerator is not null)
        {
            AvaluoInfo? avaluo = esTraspaso ? await BuildAvaluoAsync(instance.Id, tenantId, ct) : null;

            // D8 — se emite si hay AL MENOS UNA celda de SOAT o RTM con dato. El avalúo solo no basta:
            // ese bloque ya va en el FUR, y un certificado con las doce casillas en blanco no
            // certifica nada. Antes bastaba con tener una fecha de vencimiento.
            if (certs.HasSoatOrRtmData)
            {
                // HU #11136 — la RTM aplica solo en traspaso Y solo si el vehículo lleva matriculado
                // más que el periodo de gracia. Antes se pintaba en todo traspaso sin mirar antigüedad.
                var aplicaRtm = esTraspaso
                    && Domain.Certifications.RtmSelection.Applies(certs.Vehicle, HoyEnColombia())
                    && certs.Rtm is not null;

                var soatRtmData = new SoatRtmCertificateData(
                    instance.Id,
                    instance.ReferenceNumber,
                    Get(fv, "plate"),
                    Get(fv, "runt_consulta_fecha"),
                    Bloque(certs.Soat, EstadoSoatDisplay(Get(fv, SoatGate.FieldKey))),
                    aplicaRtm ? Bloque(certs.Rtm, null) : null,
                    avaluo,
                    FuenteSoat: Fuente(certs.SoatFrom),
                    FuenteRtm: aplicaRtm ? Fuente(certs.RtmFrom) : null);
                generated.Add(soatRtmGenerator.GenerateSoatRtmCertificate(soatRtmData));
            }
            else
            {
                // HU #11307 — limpieza del huérfano. Este documento era el ÚNICO de los seis
                // generados que no la tenía: si una regeneración dejaba de cumplir la condición de
                // emisión (p. ej. el trámite cambió de tipología, o el dato de SOAT/RTM desapareció),
                // el certificado anterior se quedaba adjunto y el consolidado seguía arrastrando una
                // vigencia que ya nadie estaba afirmando.
                foreach (var prev in instance.Attachments
                             .Where(a => string.Equals(a.Tipo, "certificado_soat_rtm", StringComparison.OrdinalIgnoreCase))
                             .ToList())
                {
                    storage.Delete(prev.StoragePath);
                    instance.Attachments.Remove(prev);
                    repo.RemoveAttachment(prev);
                }
            }
        }

        // HU #10926 — Escrituras: por cada actor persona jurídica (NIT) con una escritura activa y
        // vigente en el directorio del tenant (#10899), adjuntar su PDF (Source=system, tipo 'escritura'
        // para el vendedor/propietario y 'escritura_comprador' para el comprador) para que se fusione en
        // el consolidado.
        // HU #10936 — (1) selección: la más PRÓXIMA A VENCER por compañía (la decide el resolutor);
        // (2) persistencia: se guarda la referencia de la escritura usada (source_deed_id) en el
        // adjunto (deedIdPorTipo → bucle de persistencia); (3) CONGELADO tras entrega: una vez el
        // trámite fue ENTREGADO (o pasó a un estado posterior), NO se re-resuelve ni se reemplaza la
        // escritura — el adjunto vigente se conserva (no se trata como huérfano) para dejar fija la que
        // entró al registro. En estados previos a la entrega (borrador/preparado) se re-resuelve normal.
        // Si no hay escritura previa y el trámite ya está entregado, simplemente no se adjunta ninguna.
        // Congelado tras entrega: fuera de edición (borrador / rechazado+subsanación) y de
        // preparado no se re-resuelve la escritura — se conserva la que entró al registro.
        var tramiteYaEntregado =
            !TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva)
            && !string.Equals(instance.Status, TramiteEstado.Preparado, StringComparison.OrdinalIgnoreCase);
        var deedIdPorTipo = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        if (!tramiteYaEntregado)
        {
            var escrituras = await _deedResolver.ResolveForActorsAsync(tenantId, instance.Actors, ct);
            var tiposEscritura = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var esc in escrituras)
            {
                generated.Add(new GeneratedDocument(esc.Tipo, esc.Filename, "application/pdf", esc.Content));
                tiposEscritura.Add(esc.Tipo);
                deedIdPorTipo[esc.Tipo] = esc.DeedId;
            }
            // Retirar escrituras previas cuyo tipo ya no aplica (regeneración): un actor que dejó de tener
            // escritura vigente no debe arrastrar la anterior al consolidado. Los tipos que SÍ aplican los
            // reemplaza idempotentemente el bucle de persistencia de abajo.
            foreach (var prev in instance.Attachments
                         .Where(a => (string.Equals(a.Tipo, "escritura", StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(a.Tipo, "escritura_comprador", StringComparison.OrdinalIgnoreCase))
                                     && !tiposEscritura.Contains(a.Tipo))
                         .ToList())
            {
                storage.Delete(prev.StoragePath);
                instance.Attachments.Remove(prev);
                repo.RemoveAttachment(prev);
            }
        }

        // (Re)generar el FUR SIEMPRE reemplaza el adjunto 'fur' (y, en traspaso, la compraventa) y puede
        // cambiar certificados/escrituras del expediente. Como el consolidado maestro (#10701) cachea su
        // copia con este flag (se pone true al generarlo en ConsolidadoMaestroCommand), hay que invalidarlo
        // en CUALQUIER regeneración para que su próxima vista lo refunda con el FUR/escrituras vigentes; si
        // no, seguiría sirviendo el consolidado con el FUR viejo (el del wizard ya regenera siempre; solo el
        // maestro cachea). R1 (ADR-0033) cubría solo el cambio de escrituras; aquí se generaliza al FUR.
        instance.ConsolidadoMaestroVigente = false;

        foreach (var doc in generated)
        {
            // Idempotencia: re-generar reemplaza el adjunto previo del mismo tipo GENERADO por el sistema.
            // Nunca se sobrescribe un documento cargado por el usuario (Source="user"), p. ej. una
            // compraventa autenticada (HU #10859, ADR-0031 — corrige el clobber).
            foreach (var prev in instance.Attachments.Where(a =>
                         string.Equals(a.Tipo, doc.Tipo, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(a.Source, "system", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                storage.Delete(prev.StoragePath);
                instance.Attachments.Remove(prev);
                repo.RemoveAttachment(prev);
            }

            var stored = await storage.SaveAsync(id, doc.Tipo, doc.Filename, new MemoryStream(doc.Content), ct);
            var attachment = new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = id,
                Tipo = doc.Tipo,
                Filename = doc.Filename,
                Mimetype = doc.Mimetype,
                SizeBytes = stored.SizeBytes,
                Sha256 = stored.Sha256,
                StoragePath = stored.StoragePath,
                Source = "system",
                UploadedAt = now,
                // HU #10936 — traza la escritura usada en las escrituras de sistema; null en el resto.
                SourceDeedId = deedIdPorTipo.TryGetValue(doc.Tipo, out var deedId) ? deedId : null,
            };
            instance.Attachments.Add(attachment);
            repo.Add(attachment);

            docs.Add(new FurDocumentDto(attachment.Id, doc.Tipo, doc.Filename, stored.Sha256));
        }

        // Bitácora: evento append-only de generación del FUR.
        var evento = new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = "fur_generado",
            Payload = JsonSerializer.Serialize(new
            {
                documentos = docs.Select(d => new { d.Tipo, d.Filename, d.Sha256 }),
                generado_at = now,
            }),
            CreatedAt = now,
        };
        instance.Events.Add(evento);
        repo.Add(evento);

        // HU #10860 (ADR-0032) — el consolidado embebe el FUR y los certificados en caliente que se
        // acaban de (re)generar: el consolidado persistido queda stale, así que se invalida para que la
        // próxima petición lo regenere. Cierra la cascada en el sentido FUR→consolidado (el inverso lo
        // maneja ConsolidadoCommand). En la cascada consolidado→FUR es idempotente: ConsolidadoCommand
        // vuelve a subir el flag a true tras consolidar.
        instance.InvalidarConsolidados();

        await repo.SaveChangesAsync(ct);

        return (new GenerarFurResult(docs), null);
    }

    /// <summary>
    /// Gating biométrica PER-PERSONA (documento del actor): traspaso requiere comprador + vendedor con
    /// identidad vigente aprobada; matrícula requiere el comprador. Se referencia la validación vigente de
    /// la persona (HU #10350), no una fila propia del trámite; el set lo resuelve el handler con el repo.
    /// </summary>
    private static bool BiometriaGateOk(IReadOnlySet<string> identidadAprobadaPartes, bool esTraspaso) =>
        esTraspaso
            ? identidadAprobadaPartes.Contains("comprador") && identidadAprobadaPartes.Contains("vendedor")
            : identidadAprobadaPartes.Contains("comprador");

    private static FurDocumentData AssembleData(
        ProcedureInstance instance, string? codigo, bool esTraspaso, Dictionary<string, string?> fv,
        bool identidadValidada, IReadOnlyDictionary<string, string> sellosIdentidad,
        FurPrendaMarking prendaMarking, string? acreedorPrenda, string? acreedorPrendaDocumento,
        IReadOnlyDictionary<string, byte[]>? firmaImagenes,
        IReadOnlyDictionary<string, FirmaBaulMetadata>? firmaBaulMetadatos,
        FurTemplateFormat templateFormat,
        IReadOnlyDictionary<string, string>? nombresRlDirectorio = null)
    {
        var partes = new List<DocumentParte>(2);
        AddParte(partes, instance, "comprador", nombresRlDirectorio);
        if (esTraspaso)
            AddParte(partes, instance, "vendedor", nombresRlDirectorio);

        var sellos = instance.Signatures
            .Where(s => s.Estado == SignatureEstados.Firmada)
            .Select(s => $"{s.Parte}/{s.DocTipo}: {s.Sha256 ?? "-"} ({s.FirmadoAt:O})")
            .ToList();

        var vehiculo = new VehiculoDatos(
            Marca: Get(fv, "vehicle_brand"),
            Linea: Get(fv, "vehicle_line"),
            Modelo: Get(fv, "vehicle_year"),
            // A4/B4 (HU #10673, ADR-0029) — los CAMPOS del vehículo del FUR (color/combustible) llevan el
            // dato ORIGINAL del RUNT (snapshot *_runt); la transformación declarada viaja solo en las
            // observaciones. Fallback al efectivo si no hay snapshot (trámites previos a la feature).
            Color: RuntOrEffective(fv, "vehicle_color_runt", "vehicle_color"),
            Clase: Get(fv, "vehicle_class"),
            Combustible: RuntOrEffective(fv, "vehicle_fuel_runt", "vehicle_fuel"),
            Cilindraje: Get(fv, "vehicle_engine_displacement"),
            Vin: Get(fv, "vin"),
            Placa: Get(fv, "plate"),
            // HU #10256 — datos ampliados desde RUNT/Verifik (field_values)
            NumeroMotor: Get(fv, "vehicle_engine_number"),
            NumeroChasis: Get(fv, "vehicle_chassis"),
            NumeroSerie: Get(fv, "vehicle_series"),
            // A4/B4 (HU #10673) — carrocería: igual que color/combustible, el campo del FUR lleva el dato
            // original del RUNT; la transformación declarada viaja solo en observaciones.
            TipoCarroceria: RuntOrEffective(fv, "vehicle_body_type_runt", "vehicle_body_type"),
            TipoServicio: Get(fv, "vehicle_service"),
            Capacidad: Get(fv, "vehicle_passengers"),
            PesoBruto: Get(fv, "vehicle_weight"),
            NumeroEjes: Get(fv, "vehicle_axles"));

        var organismo = new OrganismoTransito(
            Codigo: Get(fv, "transit_office_code"),
            Nombre: Get(fv, "transit_office_name"),
            Ciudad: TransitOfficeCity.Legible(Get(fv, "transit_office_city")));

        return new FurDocumentData(
            ProcedureInstanceId: instance.Id,
            ReferenceNumber: instance.ReferenceNumber,
            Modalidad: instance.ModalidadEntrada,
            TipologiaCodigo: codigo,
            Vehiculo: vehiculo,
            Organismo: organismo,
            Partes: partes,
            ValorVenta: instance.Commercial?.ValorVenta,
            Causal: instance.Commercial?.Causal,
            SellosFirma: sellos,
            FechaTramite: ParseFechaTramite(Get(fv, "fur_processing_date")),
            // El recuadro OBSERVACIONES del FUR reúne tres cosas, en este orden:
            //   1. HU #10989, CF11 (HU #11257) — el beneficiario del gravamen, en constitución o en
            //      levantamiento.
            //   2. HU #10987 — las observaciones que escribe el gestor (fur_observations).
            //   3. A4/B4 (HU #10673, ADR-0029) — el texto automático de las transformaciones de
            //      color/combustible declaradas (diff snapshot RUNT vs efectivo).
            Observaciones: FurPrendaObservation.Join(
                FurPrendaObservation.Compose(prendaMarking, acreedorPrenda, acreedorPrendaDocumento),
                FurTransformationObservations.Compose(
                    Get(fv, "fur_observations"),
                    Get(fv, "vehicle_color_runt"), Get(fv, "vehicle_color"),
                    Get(fv, "vehicle_fuel_runt"), Get(fv, "vehicle_fuel"),
                    Get(fv, "vehicle_body_type_runt"), Get(fv, "vehicle_body_type"))),
            FirmaImagenes: firmaImagenes,
            FirmaBaulMetadatos: firmaBaulMetadatos,
            IdentidadValidada: identidadValidada,
            SellosIdentidad: sellosIdentidad,
            PrendaMarking: prendaMarking,
            AcreedorPrenda: acreedorPrenda,
            TemplateFormat: templateFormat)
        {
            // HU #11030 — tenant contra el que se resuelve el baúl del mandatario.
            TenantIdParaFirmas = instance.TenantId,
        };
    }

    /// <summary>
    /// HU #10645 (ADR-0025 §4) — resuelve la IMAGEN de la firma del baúl por parte con actor JURÍDICO (NIT)
    /// cubierto por una firma activa+vigente. Descarga el artefacto vía <see cref="IAttachmentStorage.OpenReadAsync"/>
    /// (best-effort) y lo mapea por rol ("comprador"/"vendedor") para <see cref="FurDocumentData.FirmaImagenes"/>;
    /// los metadatos del baúl (<see cref="FurDocumentData.FirmaBaulMetadatos"/>) se estampan a la derecha de la
    /// imagen. NUNCA rompe el FUR: cualquier fallo de lectura se registra como warning y la parte cae al sello
    /// de texto. Devuelve null en ambos diccionarios si ninguna parte tiene firma de baúl.
    /// </summary>
    /// <summary>
    /// Firma del MANDATARIO para el contrato de mandato (HU #11030): imagen del baúl si la tiene
    /// vigente, y si no el sello de su validación de identidad. Best-effort: cualquier fallo deja la
    /// línea de firma en blanco, nunca rompe la generación del mandato.
    /// </summary>
    private async Task<(byte[]? Firma, string? Sello, FirmaBaulMetadata? Metadatos)> ResolveMandatarioFirmaAsync(
        FurDocumentData data, MandateSignerCandidate signer, CancellationToken ct)
    {
        var tipoDoc = string.IsNullOrWhiteSpace(signer.TipoDocumento) ? "CC" : signer.TipoDocumento!.Trim();

        try
        {
            var match = await _vaultPolicy
                .ResolveAsync(data.TenantIdParaFirmas, tipoDoc, signer.Documento.Trim(), ct)
                .ConfigureAwait(false);

            if (match is not null && !string.IsNullOrWhiteSpace(match.StoragePath))
            {
                var stream = await storage.OpenReadAsync(match.StoragePath, ct).ConfigureAwait(false);
                if (stream is not null)
                {
                    await using (stream.ConfigureAwait(false))
                    {
                        using var ms = new MemoryStream();
                        await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
                        if (ms.Length > 0)
                        {
                            // HU #11170 — la imagen viaja con su trazabilidad (vigencia y hash), igual
                            // que la de las partes: una firma estampada que no se puede verificar leyendo
                            // el documento no sirve de nada.
                            var metadatos = new FirmaBaulMetadata(
                                match.DocumentNumber,
                                match.FullName,
                                match.VigenciaDesde,
                                match.VigenciaHasta,
                                match.SignatureVaultId,
                                match.CodigoHash);
                            return (ms.ToArray(), null, metadatos);
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            GenerarFurLog.FirmaBaulNoDisponible(logger, ex, data.ProcedureInstanceId);
        }

        // Sin firma del baúl: sello con el certificado de su identidad vigente, si lo hay.
        return signer.IdentityVigente && !string.IsNullOrWhiteSpace(signer.CertificadoIdentidad)
            ? (null, $"Validación de identidad\nFirma {signer.CertificadoIdentidad}", null)
            : (null, null, null);
    }

    private async Task<(IReadOnlyDictionary<string, byte[]>? Images, IReadOnlyDictionary<string, FirmaBaulMetadata>? Metadata)> ResolveVaultSignaturesAsync(
        ProcedureInstance instance, bool esTraspaso, CancellationToken ct)
    {
        var roles = esTraspaso ? new[] { "comprador", "vendedor" } : new[] { "comprador" };
        Dictionary<string, byte[]>? images = null;
        Dictionary<string, FirmaBaulMetadata>? metadata = null;

        foreach (var role in roles)
        {
            var actor = instance.Actors.FirstOrDefault(a =>
                string.Equals(a.ActorType, role, StringComparison.OrdinalIgnoreCase));

            // HU #11061 — si el gestor eligió EXPLÍCITAMENTE el sello de identidad, no se consume el
            // baúl aunque tenga firma vigente. Es el único punto donde se resuelve la imagen del baúl,
            // así que el guard aquí honra la elección en TODOS los documentos (FUR, mandato, solicitud
            // de trámite virtual y compraventa consumen `FirmaImagenes` de este mismo ensamblado).
            // Sin elección explícita se mantiene la precedencia del baúl (HU #11031).
            // Bug #11141 — la decisión vive en un único predicado, compartido con la consulta que
            // alimenta la interfaz: lo que se muestra debe ser lo que se plasma.
            // Bug #11146 — y es el MISMO predicado que decide si esa parte conserva su sello de
            // identidad, para que la imagen y el sello no puedan aparecer los dos ni faltar los dos.
            if (actor is null || !FirmaBaulCobertura.Aplica(actor))
                continue;

            // HU #10930/#10937 — la firma del baúl es de la PERSONA: se resuelve por el documento del
            // REPRESENTANTE LEGAL seleccionado (sujeto de identidad del actor jurídico), no por el NIT.
            var subject = IdentitySubjectResolver.For(actor);
            if (string.IsNullOrWhiteSpace(subject.TipoDocumento) || string.IsNullOrWhiteSpace(subject.NumeroDocumento))
                continue;

            var match = await _vaultPolicy.ResolveAsync(
                instance.TenantId, subject.TipoDocumento.Trim(), subject.NumeroDocumento.Trim(), ct);
            if (match is null || string.IsNullOrWhiteSpace(match.StoragePath))
                continue;

            try
            {
                var stream = await storage.OpenReadAsync(match.StoragePath, ct);
                if (stream is null)
                    continue;

                await using (stream.ConfigureAwait(false))
                {
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms, ct);
                    if (ms.Length == 0)
                        continue;
                    (images ??= new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase))[role] = ms.ToArray();
                    (metadata ??= new Dictionary<string, FirmaBaulMetadata>(StringComparer.OrdinalIgnoreCase))[role] =
                        new FirmaBaulMetadata(
                            match.DocumentNumber,
                            match.FullName,
                            match.VigenciaDesde,
                            match.VigenciaHasta,
                            match.SignatureVaultId,
                            match.CodigoHash);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best-effort (ADR-0025 §4): si el artefacto de firma no se puede leer, se omite la imagen y la
                // parte cae al sello de texto. No se bloquea la generación del FUR.
                GenerarFurLog.FirmaBaulNoDisponible(logger, ex, instance.Id);
            }
        }

        return (images, metadata);
    }

    /// <summary>¿El actor es persona JURÍDICA (NIT/N)? Solo estos consumen el baúl de firmas (ADR-0025 §4).</summary>
    // Bug #11141 — delega en el predicado compartido para que no queden dos definiciones de
    // "persona jurídica" que puedan separarse con el tiempo.
    private static bool EsActorJuridico(string? documentType) =>
        FirmaBaulCobertura.EsJuridico(documentType);

    /// <summary>
    /// Bug #11146 — ¿el gestor eligió el baúl <b>a propósito</b> para esta parte? Se apoya en el mismo
    /// predicado compartido que decide si procede bajar la imagen, para no reintroducir una segunda
    /// definición de la regla.
    /// </summary>
    private static bool EligioExplicitamenteElBaul(ProcedureInstance instance, string role)
    {
        var actor = instance.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, role, StringComparison.OrdinalIgnoreCase));
        return FirmaBaulCobertura.EligioBaulExplicitamente(actor);
    }

    /// <summary>Huso horario de Colombia (UTC-5) para presentar las fechas del sello de identidad.</summary>
    private static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);

    /// <summary>
    /// Resuelve la validación biométrica APROBADA+VIGENTE de una parte para el sello del FUR: primero la fila
    /// propia del trámite; si no, la identidad vigente REFERENCIADA por documento del actor (HU #10350, sin
    /// clonar). Devuelve null si la parte no tiene identidad aprobada vigente.
    /// </summary>
    private async Task<ProcedureInstanceBiometricValidation?> ResolveApprovedValidationAsync(
        ProcedureInstance instance, string role, DateTimeOffset now, CancellationToken ct)
    {
        var actor = instance.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, role, StringComparison.OrdinalIgnoreCase));
        // Documento del SUJETO de identidad (HU #10688): el RL en PJ, el actor en PN.
        var subject = actor is null ? null : IdentitySubjectResolver.For(actor);

        // Fila propia aprobada+vigente del rol Y del documento del sujeto actual. El filtro por documento es
        // PARIDAD EXACTA con el gate (IdentityApprovalResolver.HasLocalVigente): sin él, una fila propia con
        // documento desfasado (documento editado tras validar) haría que el gate aprobara por la identidad
        // referenciada mientras el sello estamparía otro documento/certificado — inconsistencia en el FUR.
        var own = instance.BiometricValidations.FirstOrDefault(v =>
            string.Equals(v.PartyRole, role, StringComparison.OrdinalIgnoreCase)
            && BiometricRules.EsAprobadaVigente(v, now)
            && BiometricRules.DocumentoCoincide(v, subject?.TipoDocumento, subject?.NumeroDocumento));
        if (own is not null)
            return own;

        // Identidad vigente REFERENCIADA por documento del sujeto (HU #10350, sin clonar).
        if (subject is not null && !string.IsNullOrWhiteSpace(subject.TipoDocumento) && !string.IsNullOrWhiteSpace(subject.NumeroDocumento))
            return await repo.FindVigenteApprovedByDocumentAsync(
                instance.TenantId, subject.TipoDocumento.Trim(), subject.NumeroDocumento.Trim(), now, ct);

        return null;
    }

    /// <summary>
    /// Sello de texto de la validación biométrica (HU #10488): documento, uuid de la validación, serie/hash del
    /// certificado (firmaSerie) y fechas de aprobación/vencimiento (día calendario Colombia). Multilínea: el
    /// overlay del FUR lo pinta línea a línea en el espacio de firma.
    /// </summary>
    private static string BuildIdentidadSello(ProcedureInstanceBiometricValidation v)
    {
        var doc = $"{v.DocumentType} {v.DocumentNumber}".Trim();
        var uuid = string.IsNullOrWhiteSpace(v.KyverumVerificationId) ? v.Id.ToString("D") : v.KyverumVerificationId!;
        // HU #11015 — un guion no dice nada: si el proveedor no entregó la serie del certificado se
        // declara explícitamente, para que quien lee el documento sepa que la firma no se pudo estampar
        // en vez de creer que el valor se perdió al imprimir.
        var firma = string.IsNullOrWhiteSpace(v.CertificateHash) ? "no disponible" : v.CertificateHash!;
        // HU #11018 — formato de negocio unico en documentos: AÑO/MES/DIA, sin hora.
        var aprob = v.ValidatedAt is { } va
            ? va.ToOffset(ColombiaOffset).ToString(FechaDocumento.Formato, CultureInfo.InvariantCulture) : "-";
        var vence = v.ValidUntil is { } vu
            ? vu.ToOffset(ColombiaOffset).ToString(FechaDocumento.Formato, CultureInfo.InvariantCulture) : "-";
        return $"Validación biométrica {doc}\nUUID {uuid}\nFirma {firma}\nAprob {aprob} · Vence {vence}";
    }

    /// <summary>
    /// ADR-0036 (HU #10915) — Genera el Contrato de Mandato siempre que haya generador y código de OT
    /// (persona natural y jurídica). Resuelve la config del OT por el <c>transit_office_code</c>;
    /// sin generador o sin código de OT devuelve <c>null</c> (el caller retira el mandato previo).
    /// El firmante (mandatario) va <c>null</c> en preparado: HU #10916 lo resuelve al aprobar y regenera.
    /// </summary>
    /// <summary>
    /// HU #11206 — transformaciones declaradas en el trámite (<c>field_values</c> con valor <c>true</c>).
    /// Se leen aquí y no en el generador para que el documento no dependa del formato de almacenamiento.
    /// </summary>
    private static IReadOnlyList<string> TransformacionesActivas(Dictionary<string, string?> fv)
    {
        string[] claves =
        [
            MandatoObjetoComposer.CambioColor,
            MandatoObjetoComposer.CambioCarroceria,
            MandatoObjetoComposer.CambioCombustible,
        ];

        return [.. claves.Where(clave =>
            string.Equals(Get(fv, clave)?.Trim(), "true", StringComparison.OrdinalIgnoreCase))];
    }

    private async Task<GeneratedDocument?> TryGenerateMandatoAsync(
        FurDocumentData data,
        string? transitOfficeCode,
        Guid? mandateSignerId,
        IReadOnlyList<string> transformaciones,
        CancellationToken ct)
    {
        if (_mandatoGenerator is null || string.IsNullOrWhiteSpace(transitOfficeCode))
            return null;

        var config = await _mandatePolicy.ResolveAsync(transitOfficeCode, ct);
        // Producto: el mandato se emite siempre (PN y PJ). La plantilla/familia vienen de la config del OT.

        // HU #10916 — firmante resuelto al aprobar (instance.MandateSignerId). En preparado va null ⇒ el
        // PDF pinta placeholders y se regenera al aprobar. Sabaneta (institucional) no lleva firmante persona.
        MandatarioFirmante? mandatario = null;
        if (mandateSignerId is { } signerId)
        {
            var signer = await _mandateDirectory.GetByIdAsync(signerId, ct).ConfigureAwait(false);
            if (signer is not null)
            {
                // HU #11030 — la firma del mandatario no se pintaba nunca: el contrato salía con la línea
                // en blanco aunque el mandatario tuviera firma en el baúl o identidad validada. Misma
                // precedencia que el resto de documentos: imagen del baúl > sello de identidad > línea.
                var (firma, sello, metadatos) = await ResolveMandatarioFirmaAsync(data, signer, ct).ConfigureAwait(false);
                mandatario = new MandatarioFirmante(signer.Nombre, signer.Documento, firma, sello, metadatos);
            }
        }

        // El convenio se llavea por (compañía del trámite, organismo). El organismo del trámite se
        // conoce aquí por su CÓDIGO; el id lo aporta la configuración del OT, que ya se resolvió arriba
        // por ese mismo código: sin fila de configuración no hay id y el mandato conserva el bloque, que
        // es el default seguro.
        var modoFirma = await _mandatoFirmaPolicy
            .ResolveAsync(data.TenantIdParaFirmas, config?.TransitOfficeId ?? Guid.Empty, mandateSignerId, ct)
            .ConfigureAwait(false);

        var mandatoData = new MandatoData(
            data,
            config?.TemplateCode ?? MandatoTemplateResolver.Generico,
            config?.InstitutionalMandataryName,
            config?.InstitutionalMandataryNit,
            mandatario,
            // HU #11204 — familia del mandatario y datos propios del OT. Sin configuración el generador
            // aplica los mismos valores de siempre, así que un OT sin fila sale como hasta ahora (AC5).
            MandatoFamiliaCodes.Resolve(config?.MandataryFamily),
            config?.ChamberCity,
            config?.MandatarySigla,
            // HU #11206 — las transformaciones entran DENTRO del objeto del contrato, sin cláusula nueva.
            transformaciones,
            // Con convenio, el recuadro de firmas solo lleva al MANDANTE; sin convenio el mandatario
            // firma, porque es un actor obligatorio. La firma física conserva el bloque pero deja la
            // línea para firmar a mano.
            modoFirma.TieneConvenio
                ? MandatarioFirmaModo.SinBloque
                : modoFirma.FirmaFisica
                    ? MandatarioFirmaModo.Manual
                    : MandatarioFirmaModo.Estampada);

        return _mandatoGenerator.GenerateMandato(mandatoData);
    }

    /// <summary>
    /// HU #10589 / HU #10990 — Genera UN certificado RUES por cada actor persona jurídica del trámite.
    ///
    /// <para><b>HU #11305 (Feature #11301, ADR-0041) — se retira la consulta en vivo (D4).</b> Todo sale
    /// del lector documental: tabla canónica → respaldo sobre <c>field_values</c> (snapshot congelado y,
    /// si no, las <c>rues_*</c> de instancia, que son de instancia y solo sirven a UNA compañía) → nada.
    /// Sin dato persistido <b>no se emite el certificado</b>, y esa es la contrapartida que el PO aceptó
    /// a sabiendas: las compañías precargadas del directorio de representantes legales dejan de tener
    /// este anexo, que hoy consiguen por una llamada saliente en cada regeneración.</para>
    ///
    /// <para><b>No se emite un certificado sin datos de registro.</b> Antes se emitía siempre que
    /// hubiera un actor NIT, aunque saliera con la razón social y 19 casillas en blanco. Un
    /// certificado en blanco no certifica nada.</para>
    ///
    /// <para><b>Tipos:</b> el comprador conserva <c>certificado_rues</c> (retrocompatible) y el resto
    /// de roles llevan sufijo (<c>certificado_rues_vendedor</c>), mismo patrón que
    /// <c>certificado_identidad</c>, de modo que ambos coexistan en el expediente.</para>
    /// </summary>
    /// <summary>Día calendario colombiano. No se usa UTC: un certificado imprime un día civil.</summary>
    private static DateOnly HoyEnColombia() =>
        DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5)).DateTime);

    /// <summary>
    /// Traduce una certificación canónica al bloque del documento. Solo formato: el parsing ya ocurrió
    /// una vez, al persistir. Valor ausente ⇒ celda en blanco (regla HU #10856).
    /// </summary>
    private static SoatRtmBlock Bloque(
        Domain.Certifications.SoatCertification? soat, string? estadoOverride) =>
        soat is null
            ? new SoatRtmBlock()
            : new SoatRtmBlock(
                Poliza: soat.PolicyNumber.ToDocumentText(),
                FechaVigencia: soat.ValidFrom.ToDocumentText(),
                FechaVencimiento: soat.ValidUntil.ToDocumentText(),
                FechaExpedicion: soat.IssuedOn.ToDocumentText(),
                Entidad: soat.Insurer.ToDocumentText(),
                // El estado del SOAT lo sigue mandando la llave del gate: es la que ve el OT y la que
                // el frontend compara estricto. No se deriva aquí para no abrir dos verdades.
                Estado: estadoOverride ?? soat.Status.ToDocumentText());

    private static SoatRtmBlock Bloque(
        Domain.Certifications.RtmCertification? rtm, string? estadoOverride) =>
        rtm is null
            ? new SoatRtmBlock()
            : new SoatRtmBlock(
                Poliza: rtm.CertificateNumber.ToDocumentText(),
                FechaVigencia: rtm.ValidFrom.ToDocumentText(),
                FechaVencimiento: rtm.ValidUntil.ToDocumentText(),
                FechaExpedicion: rtm.IssuedOn.ToDocumentText(),
                Entidad: rtm.Cda.ToDocumentText(),
                Estado: estadoOverride ?? rtm.Status.ToDocumentText());

    /// <summary>
    /// Pie de procedencia del bloque. El texto fijo del certificado afirma una consulta al RUNT que
    /// puede no haber ocurrido: hay celdas que salen del OCR de un PDF cargado por el operador.
    /// </summary>
    private static string? Fuente(Domain.Certifications.CertificationProvenance? provenance)
    {
        if (provenance is null || provenance.ObservedAt == DateTimeOffset.MinValue)
            return null;

        var etiqueta = provenance.Source switch
        {
            Domain.Certifications.CertificationSourceKind.Consultation => "RUNT 2.0",
            Domain.Certifications.CertificationSourceKind.Ocr => "documento cargado",
            Domain.Certifications.CertificationSourceKind.User => "registro manual",
            _ => "registro del trámite",
        };

        return provenance.ToDocumentFooter(etiqueta);
    }

    private List<GeneratedDocument> TryGenerateRuesCertificates(
        ProcedureInstance instance, Certifications.CertificationView certs)
    {
        var docs = new List<GeneratedDocument>(2);

        foreach (var actor in instance.Actors.Where(a => EsActorJuridico(a.DocumentType)))
        {
            var nit = actor.DocumentNumber?.Trim();
            if (string.IsNullOrEmpty(nit))
                continue;

            // El lector ya resolvió tabla → respaldo. Aquí no hay ninguna llamada saliente.
            var merchant = certs.Merchant(nit);
            if (merchant is null || !merchant.CanBeCertified)
            {
                GenerarFurLog.CertificadoRuesSinDatos(logger, instance.Id);
                continue;
            }

            var datos = merchant.Fields;
            var razonSocial = Val(datos, "rues_razon_social")!;   // CanBeCertified ya lo garantizó.

            var data = new RuesCertificateData(
                instance.Id,
                instance.ReferenceNumber,
                RazonSocial: razonSocial,
                Nit: Val(datos, "rues_nit") ?? nit,
                Estado: Val(datos, "rues_estado") ?? string.Empty,
                MatriculaMercantil: Val(datos, "rues_matricula_mercantil"),
                CamaraComercio: Val(datos, "rues_camara_comercio"),
                Sigla: Val(datos, "rues_sigla"),
                FechaMatricula: Val(datos, "rues_fecha_matricula"),
                UltimoAnoRenovado: Val(datos, "rues_ultimo_ano_renovado"),
                FechaRenovacion: Val(datos, "rues_fecha_renovacion"),
                Direccion: Val(datos, "rues_direccion"),
                Municipio: Val(datos, "rues_municipio"),
                Categoria: Val(datos, "rues_categoria"),
                ActividadEconomica: Val(datos, "rues_actividad_economica"),
                TipoOrganizacion: Val(datos, "rues_tipo_organizacion"),
                // HU #10589 (Feature #10852) — resto del REGISTRO COMERCIAL + representación legal + actividades.
                TipoCompania: Val(datos, "rues_tipo_compania"),
                Email: Val(datos, "rues_email"),
                IdRm: Val(datos, "rues_id_rm"),
                FechaActualizacion: Val(datos, "rues_fecha_actualizacion"),
                RazonCancelacion: Val(datos, "rues_razon_cancelacion"),
                RepresentacionLegal: Val(datos, "rues_representacion_legal"),
                Actividades: ParseActividades(Val(datos, "rues_actividades_json")),
                // HU #11132 — jurisdicción de la cámara de comercio.
                CamaraCiudad: Val(datos, "rues_camara_ciudad"),
                CamaraDepartamento: Val(datos, "rues_camara_departamento"));

            docs.Add(ConTipoDeRol(ruesGenerator.GenerateRuesCertificate(data), actor.ActorType));
        }

        return docs;
    }

    /// <summary>Tipo base del certificado RUES (el del comprador, retrocompatible).</summary>
    private const string TipoCertificadoRues = "certificado_rues";

    /// <summary>
    /// Reetiqueta el certificado según el rol del actor: el comprador conserva el tipo base y el resto
    /// llevan sufijo de rol. Renombra también el fichero para que el expediente no muestre dos
    /// adjuntos distintos con el mismo nombre.
    /// </summary>
    private static GeneratedDocument ConTipoDeRol(GeneratedDocument doc, string? actorType)
    {
        if (string.IsNullOrWhiteSpace(actorType)
            || string.Equals(actorType, BiometricRules.ParteComprador, StringComparison.OrdinalIgnoreCase))
        {
            return doc;
        }

        var tipo = $"{TipoCertificadoRues}_{actorType.Trim().ToLowerInvariant()}";
        return doc with
        {
            Tipo = tipo,
            Filename = doc.Filename.Replace(TipoCertificadoRues, tipo, StringComparison.Ordinal),
        };
    }

    /// <summary>
    /// Las <c>rues_*</c> de <c>field_values</c>, pero solo si pertenecen al NIT indicado. Devuelve
    /// <c>null</c> si no hay datos o si son de OTRA compañía — en ese caso el llamador consulta en vivo.
    /// </summary>
    private static Dictionary<string, string?>? DatosRuesDeLaInstancia(
        Dictionary<string, string?> fv, string nit)
    {
        var nitInstancia = Get(fv, "rues_nit")?.Trim();
        if (string.IsNullOrEmpty(nitInstancia)
            || !string.Equals(nitInstancia, nit, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(Get(fv, "rues_razon_social")))
        {
            return null;
        }

        return fv;
    }

    private static string? Val(IReadOnlyDictionary<string, string?>? datos, string key) =>
        datos is not null && datos.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    private static readonly JsonSerializerOptions RuesJsonOptions = new(JsonSerializerDefaults.Web);

    // HU #10589 — deserializa la lista de actividades económicas persistida como JSON compacto
    // (codigo/nombre/descripcion). Ausente o ilegible → null (la sección se pinta vacía).
    private static List<RuesActividad>? ParseActividades(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var items = JsonSerializer.Deserialize<List<RuesActividadDto>>(json, RuesJsonOptions);
            return items?.Select(a => new RuesActividad(a.Codigo, a.Nombre, a.Descripcion)).ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record RuesActividadDto(string? Codigo, string? Nombre, string? Descripcion);

    // HU #10856 — bloque de Avalúo del certificado SOAT/RTM (solo traspaso): una fila por fuente de
    // avalúo (Fasecolda/Comercial/...). Best-effort: sin handler o sin fuentes, filas en blanco.
    private async Task<AvaluoInfo?> BuildAvaluoAsync(Guid id, Guid tenantId, CancellationToken ct)
    {
        if (avaluoHandler is null)
            return new AvaluoInfo([]);

        var (result, _) = await avaluoHandler.HandleAsync(id, tenantId, ct);
        if (result is null)
            return new AvaluoInfo([]);

        var rows = result.Sources
            .Select(s => new AvaluoRow(AvaluoLabel(s.Source), FormatMoney(s.Value)))
            .ToList();
        return new AvaluoInfo(rows);
    }

    private static string AvaluoLabel(string? source) => source?.Trim().ToLowerInvariant() switch
    {
        "fasecolda" => "AVALÚO FASECOLDA",
        "mercado_libre" => "AVALÚO COMERCIAL",
        "base_gravable" => "AVALÚO BASE GRAVABLE",
        _ => string.IsNullOrWhiteSpace(source) ? "AVALÚO" : $"AVALÚO {source.Trim().ToUpperInvariant()}",
    };

    // Formato con la cultura invariante: el runtime corre en globalization-invariant mode (contenedor),
    // donde GetCultureInfo("es-CO") lanza CultureNotFoundException. Se antepone "$".
    private static string? FormatMoney(long? value) =>
        value is { } v ? "$ " + v.ToString("#,##0", CultureInfo.InvariantCulture) : null;

    /// <summary>
    /// HU #10762 — Genera el certificado RNMC desde el ÚLTIMO snapshot de preflight (los checks
    /// <c>rnmc_{rol}_*</c>), o <c>null</c> si no se ha corrido el preflight o no consultó el RNMC (p. ej.
    /// todas las partes son personas jurídicas: el RNMC solo aplica a naturales).
    /// <para>La fuente del dato es el SNAPSHOT, no field_values: la señal <c>rnmc_medida_pendiente</c> es un
    /// booleano sin el detalle por parte que exige el certificado.</para>
    /// <para>Best-effort: cualquier fallo se registra como warning y omite el certificado; nunca aborta el FUR.</para>
    /// </summary>
    /// <returns>
    /// <c>Aplica</c> distingue "el RNMC no aplica a este trámite" (⇒ el caller retira el certificado previo)
    /// de "aplicaba pero la generación falló" (⇒ se conserva el previo, que sigue siendo válido).
    /// </returns>
    private (GeneratedDocument? Doc, bool Aplica) TryGenerateRnmcCertificate(ProcedureInstance instance)
    {
        try
        {
            // FEATURE 05 — el resultado RNMC ya no vive en el snapshot del pre-vuelo, sino en el
            // field_value `rnmc_checks` que escribe la consulta RNMC dedicada del paso final.
            var fvRnmc = instance.FieldValues
                .FirstOrDefault(f => string.Equals(f.FieldKey, RunRnmcConsultHandler.FieldRnmcChecks, StringComparison.Ordinal));

            var checks = GetPreflightHandler.DeserializeChecks(fvRnmc?.ValueJson)
                .Where(c => c.Key.EndsWith("medidas_correctivas", StringComparison.Ordinal))
                .ToList();
            if (checks.Count == 0)
                return (null, false);

            var entradas = new List<RnmcCertificateEntry>(checks.Count);
            foreach (var check in checks)
            {
                var rol = RolFromCheckKey(check.Key);
                var actor = instance.Actors.FirstOrDefault(a =>
                    string.Equals(a.ActorType, rol, StringComparison.OrdinalIgnoreCase));
                entradas.Add(new RnmcCertificateEntry(
                    rol,
                    actor?.FullName ?? string.Empty,
                    actor?.DocumentNumber ?? string.Empty,
                    EstadoRnmc(check.Status),
                    check.Message));
            }

            var consultadoAt = fvRnmc?.UpdatedAt ?? fvRnmc?.CreatedAt ?? DateTimeOffset.UtcNow;
            var data = new RnmcCertificateData(
                instance.Id,
                instance.ReferenceNumber,
                consultadoAt,
                entradas);

            return (rnmcGenerator.GenerateRnmcCertificate(data), true);
        }
        catch (Exception ex)
        {
            GenerarFurLog.CertificadoRnmcFallo(logger, ex, instance.Id);
            return (null, true);
        }
    }

    /// <summary>
    /// Rol del segmento intermedio de la key del check: <c>rnmc_{rol}_{keyDelMapper}</c> (p. ej.
    /// <c>rnmc_comprador_medidas_correctivas</c>). Cubre también la key sin sufijo (<c>rnmc_{rol}</c>), que
    /// el preflight emite cuando el provider RNMC no está disponible.
    /// </summary>
    private static string RolFromCheckKey(string key)
    {
        var parts = key.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : string.Empty;
    }

    /// <summary>
    /// Estado del check → texto del certificado. Deliberadamente NO expone el estado crudo del preflight
    /// ni el proveedor: el certificado es un documento de cara al usuario / al OT.
    /// </summary>
    private static string EstadoRnmc(string? status) => status switch
    {
        "ok" => "SIN MEDIDAS CORRECTIVAS",
        "warn" => "CON MEDIDAS CORRECTIVAS",
        "unknown" => "SIN DATOS",
        _ => "NO VERIFICABLE",
    };

    /// <summary>
    /// Descarga best-effort el certificado (PDF) de la validación de identidad de una PARTE
    /// (<paramref name="role"/> = comprador | vendedor) desde Kyverum. El adjunto del comprador conserva
    /// el tipo <c>certificado_identidad</c> (retrocompatible); el del vendedor usa
    /// <c>certificado_identidad_vendedor</c>, de modo que ambos coexistan en el expediente.
    /// Devuelve null (sin bloquear el FUR) si no hay validación Kyverum con id, si Kyverum no tiene
    /// certificado, o si la descarga falla — en los dos últimos casos registra un warning.
    /// </summary>
    private async Task<GeneratedDocument?> TryDownloadIdentityCertificateAsync(
        ProcedureInstance instance, string role, CancellationToken ct)
    {
        static bool EsKyverumConId(ProcedureInstanceBiometricValidation v) =>
            v.Status == BiometricEstados.Aprobado
            && string.Equals(v.Provider, BiometricProviders.Kyverum, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(v.KyverumVerificationId);

        var bio = instance.BiometricValidations.FirstOrDefault(v =>
            string.Equals(v.PartyRole, role, StringComparison.OrdinalIgnoreCase) && EsKyverumConId(v));

        // Sin fila propia (identidad REFERENCIADA de otro trámite de la persona): se busca la validación
        // vigente de la parte por documento para tomar su certificado Kyverum (HU #10350, sin clonar).
        if (bio is null)
        {
            var actor = instance.Actors.FirstOrDefault(a =>
                string.Equals(a.ActorType, role, StringComparison.OrdinalIgnoreCase));
            // Documento del SUJETO de identidad (HU #10688): el RL en PJ, el actor en PN.
            var subject = actor is null ? null : IdentitySubjectResolver.For(actor);
            if (subject is not null && !string.IsNullOrWhiteSpace(subject.TipoDocumento) && !string.IsNullOrWhiteSpace(subject.NumeroDocumento))
            {
                var source = await repo.FindVigenteApprovedByDocumentAsync(
                    instance.TenantId, subject.TipoDocumento.Trim(), subject.NumeroDocumento.Trim(), DateTimeOffset.UtcNow, ct);
                if (source is not null && EsKyverumConId(source))
                    bio = source;
            }
        }

        if (bio is null)
            return null; // provider mock o sin id de Kyverum: no hay certificado externo que descargar.

        try
        {
            var cert = await certClient.DownloadCertificateAsync(bio.KyverumVerificationId!, ct);
            if (cert is null)
            {
                GenerarFurLog.CertificadoNoDisponible(logger, bio.Id, instance.Id);
                return null;
            }

            // Comprador: certificado_identidad (retrocompatible). Otras partes: sufijo de rol.
            var tipo = string.Equals(role, BiometricRules.ParteComprador, StringComparison.OrdinalIgnoreCase)
                ? "certificado_identidad"
                : $"certificado_identidad_{role}";
            var safeRef = instance.ReferenceNumber.Replace('/', '-');
            return new GeneratedDocument(
                tipo, $"{tipo}_{safeRef}.pdf", cert.ContentType, cert.Content);
        }
        catch (KyverumCertificateException ex)
        {
            GenerarFurLog.CertificadoDescargaFallo(logger, ex, instance.Id);
            return null;
        }
    }

    /// <summary>
    /// HU #11198 (AC3) — nombres de respaldo del directorio, por rol, SOLO para las partes jurídicas cuyo
    /// trámite no registró el nombre del representante. Si el trámite lo trae, no se consulta nada: el
    /// dato del trámite es el que manda (AC1/AC2) y una consulta de más solo abriría la puerta a que el
    /// directorio termine ganando por accidente.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>?> ResolverNombresDelDirectorioAsync(
        ProcedureInstance instance, bool esTraspaso, CancellationToken ct)
    {
        var roles = esTraspaso ? new[] { "comprador", "vendedor" } : ["comprador"];
        Dictionary<string, string>? nombres = null;

        foreach (var rol in roles)
        {
            var actor = instance.Actors.FirstOrDefault(a =>
                string.Equals(a.ActorType, rol, StringComparison.OrdinalIgnoreCase));
            if (actor is null)
                continue;

            var esJuridica = ActorPersonTypes.IsJuridical(actor.PersonType)
                || string.Equals(actor.DocumentType, "NIT", StringComparison.OrdinalIgnoreCase);
            if (!esJuridica)
                continue;

            var (_, _, rl) = ParseActorMetadata(actor.Metadata);
            if (!string.IsNullOrWhiteSpace(rl?.NombreCompleto))
                continue; // El trámite lo trae: no hay nada que respaldar.

            var nombre = await _representanteDirectory.BuscarNombreRepresentanteAsync(
                instance.TenantId, actor.DocumentNumber, rl?.TipoDocumento, rl?.NumeroDocumento, ct);
            if (string.IsNullOrWhiteSpace(nombre))
                continue;

            nombres ??= [];
            nombres[rol] = nombre;
        }

        return nombres;
    }

    private static void AddParte(
        List<DocumentParte> partes,
        ProcedureInstance instance,
        string rol,
        IReadOnlyDictionary<string, string>? nombresRlDirectorio = null)
    {
        var a = instance.Actors.FirstOrDefault(x =>
            string.Equals(x.ActorType, rol, StringComparison.OrdinalIgnoreCase));
        var (ciudad, direccion, rl) = ParseActorMetadata(a?.Metadata);
        // HU #10688 — persona jurídica (tipo juridical o documento NIT): la razón social no se trocea en el FUR.
        var esJuridica = ActorPersonTypes.IsJuridical(a?.PersonType)
            || string.Equals(a?.DocumentType, "NIT", StringComparison.OrdinalIgnoreCase);
        partes.Add(new DocumentParte(
            rol,
            a?.FullName,
            a?.DocumentNumber,
            a?.Email,
            string.IsNullOrWhiteSpace(a?.DocumentType) ? null : a.DocumentType.Trim(),
            string.IsNullOrWhiteSpace(a?.Phone) ? null : a.Phone.Trim(),
            direccion,
            ciudad,
            esJuridica,
            // ADR-0036 (HU #10914/#10915) — representante legal del mandante (solo persona jurídica).
            // HU #11198 — el nombre lo manda SIEMPRE el trámite; el directorio es solo respaldo para
            // cuando el trámite no lo trajo (AC3). Este es el punto ÚNICO donde se arma la parte, así que
            // el mandato, la compraventa, la solicitud y el FUR quedan consistentes por construcción (AC4).
            RepresentanteLegalNombre: Trim(rl?.NombreCompleto)
                ?? (nombresRlDirectorio is not null && nombresRlDirectorio.TryGetValue(rol, out var respaldo)
                    ? Trim(respaldo)
                    : null),
            RepresentanteLegalTipoDoc: Trim(rl?.TipoDocumento),
            RepresentanteLegalDocumento: Trim(rl?.NumeroDocumento)));
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static readonly JsonSerializerOptions ActorMetadataJson = new(JsonSerializerDefaults.Web);

    private static (string? Ciudad, string? Direccion, ActorMetadataRl? RepresentanteLegal) ParseActorMetadata(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata) || metadata == "{}")
            return (null, null, null);
        try
        {
            var m = JsonSerializer.Deserialize<ActorMetadataDto>(metadata, ActorMetadataJson);
            return (m?.Ciudad, m?.Direccion, m?.RepresentanteLegal);
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    private sealed record ActorMetadataDto(string? Ciudad, string? Direccion, ActorMetadataRl? RepresentanteLegal);

    /// <summary>Subconjunto del representante legal leído de <c>actor.metadata</c> (ADR-0036).</summary>
    private sealed record ActorMetadataRl(string? TipoDocumento, string? NumeroDocumento, string? NombreCompleto);

    private static DateTime? ParseFechaTramite(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
            ? dt
            : null;
    }

    private static string? Get(Dictionary<string, string?> fv, string key) =>
        fv.TryGetValue(key, out var v) ? v : null;

    /// <summary>
    /// HU #10973 — presentación del estado del SOAT en el certificado. La llave <c>soat_estado</c> es
    /// ante todo el GATE de aprobación del OT, así que se persiste en el vocabulario de
    /// <see cref="SoatGate"/> (minúscula); aquí se sube a mayúscula solo para imprimir.
    /// <para><c>unknown</c> significa "el RUNT no reportó estado": se imprime EN BLANCO en vez de la
    /// palabra "unknown", coherente con la regla de HU #10856 (valor ausente ⇒ celda vacía).</para>
    /// Cualquier otro valor se imprime tal cual en mayúscula, lo que conserva legibles los trámites
    /// previos a esta HU (que guardaron el crudo del RUNT, p. ej. "VIGENTE").
    /// </summary>
    private static string? EstadoSoatDisplay(string? estado)
    {
        var v = estado?.Trim();
        if (string.IsNullOrEmpty(v) || string.Equals(v, SoatGate.Unknown, StringComparison.OrdinalIgnoreCase))
            return null;

        return v.ToUpperInvariant();
    }

    // Devuelve el snapshot RUNT si existe (no vacío); si no, cae al valor efectivo. Se usa para imprimir
    // el dato original del vehículo en el FUR aunque exista una transformación declarada en el efectivo.
    private static string? RuntOrEffective(Dictionary<string, string?> fv, string runtKey, string effectiveKey)
    {
        var runt = Get(fv, runtKey);
        return string.IsNullOrWhiteSpace(runt) ? Get(fv, effectiveKey) : runt;
    }
}

/// <summary>Logging source-generated (CA1848) de la generación del FUR. No incluye PII ni secretos.</summary>
internal static partial class GenerarFurLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Kyverum no tiene certificado para la validación {ValidationId} (instancia {InstanceId}); se omite del expediente.")]
    public static partial void CertificadoNoDisponible(ILogger logger, Guid validationId, Guid instanceId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo descargar el certificado de identidad de Kyverum (instancia {InstanceId}); se omite del expediente sin bloquear el FUR.")]
    public static partial void CertificadoDescargaFallo(ILogger logger, Exception ex, Guid instanceId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo leer el artefacto de la firma del baúl (instancia {InstanceId}); la parte cae al sello de texto sin bloquear el FUR.")]
    public static partial void FirmaBaulNoDisponible(ILogger logger, Exception ex, Guid instanceId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo generar el certificado RNMC (instancia {InstanceId}); se omite del expediente sin bloquear el FUR.")]
    public static partial void CertificadoRnmcFallo(ILogger logger, Exception ex, Guid instanceId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Sin datos de registro del RUES para un actor jurídico (instancia {InstanceId}); se omite su certificado en vez de emitirlo en blanco.")]
    public static partial void CertificadoRuesSinDatos(ILogger logger, Guid instanceId);

    // HU #11305 — se retiró CertificadoRuesConsultaEnVivo junto con la consulta que registraba. Esa
    // traza existía para medir cuántos trámites obligaban a pagar una consulta al generar el PDF y
    // poder apagarla al llegar a cero; el PO decidió apagarla sin esperar (D4), porque el contador no
    // iba a bajar: lo alimentaba el caso de la precarga del directorio, que se conserva (D1).
}
