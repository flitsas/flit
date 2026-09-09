using System.Globalization;
using System.Text.Json.Nodes;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.Common;
using Flit.Admin.Domain.GeneracionDocumental;

namespace Flit.Admin.Application.GeneracionDocumental.GenerateTransferencia;

/// <summary>
/// Emite el Documento de Transferencia de Dominio SIN trámite en los <b>tres escenarios</b> del
/// anexo —A traspaso ordinario (art. 5.3.2.1), B transferencia unilateral de leasing al locatario
/// (art. 5.3.2.2) y C entidad financiera a un tercero (art. 5.3.2.1 sin exenciones)—
/// CF-06/CF-07/CF-08/CF-09/CF-24/CF-26, <c>docs/plantilla-transferencia-dominio.md</c>.
///
/// <para><b>El escenario decide cuántas partes entran al modelo, y con eso cuántos bloques de firma
/// existen</b> (anexo §9.0.3). En el escenario B se construye una lista de <b>una</b> parte: el
/// locatario no es una parte con la firma apagada, sencillamente no está en la lista. Sus datos
/// viajan en <c>Leasing</c>, que alimenta las cláusulas declarativas y nunca el bloque de
/// firmas.</para>
///
/// <para><b>Una validación bloqueante no deja fila.</b> A diferencia del Certificado RUES —donde un
/// NIT sin coincidencia SÍ deja rastro en <c>error</c>, porque hubo consulta al proveedor y hay algo
/// que auditar—, aquí el rechazo ocurre antes de que exista documento: el cuerpo ni siquiera define
/// un escenario válido y el CHECK <c>ck_standalone_documents_scenario_por_tipo</c> impediría escribir
/// la fila. El intento se responde con la lista de códigos y no se persiste nada.</para>
///
/// <para><b>Las advisory viajan en el 200.</b> VB-01, VB-03, VB-04, VB-A-01..03, VB-A-05 y
/// VB-A-08..10 dependen de RUNT/RUES/SOAT/SIMIT y las verifica el Organismo de Tránsito: se emiten
/// como aviso y nunca impiden generar (anexo §6).</para>
///
/// <para><b>Sin baúl de firmas.</b> El modo de firma es <c>MANUSCRITA</c> fijo (§9.0): este handler
/// no recibe ni puede recibir un lector del baúl, así que no hay forma de que un sello se cuele en
/// el PDF.</para>
///
/// <para>Esta clase no nombra ningún tipo de <c>Flit.Tramites.*</c> (restricción C6 del ADR-0056):
/// storage y generador entran por puertos declarados en Admin.</para>
/// </summary>
public sealed class GenerateTransferenciaHandler
{
    /// <summary>Agrupación del artefacto en storage. Es la misma del Certificado RUES.</summary>
    internal const string StorageTipo = "generacion_documental";

    public const string ErrorInvalidRequest = "invalid_request";

    /// <summary>
    /// Escenario reconocido por el catálogo del dominio pero sin construcción de modelo aquí. Hoy no
    /// se produce —A, B y C están implementados—; se conserva para que un escenario nuevo del anexo
    /// falle con un 422 explicable y no con una excepción a mitad del render.
    /// </summary>
    public const string ErrorScenarioNotImplemented = "scenario_not_implemented";

    private readonly IStandaloneDocumentRepository _repository;
    private readonly IStandaloneTransferGenerator _generator;
    private readonly IStandaloneDocumentStorage _storage;
    private readonly TimeProvider _timeProvider;

    public GenerateTransferenciaHandler(
        IStandaloneDocumentRepository repository,
        IStandaloneTransferGenerator generator,
        IStandaloneDocumentStorage storage,
        TimeProvider? timeProvider = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<GenerateTransferenciaResult> HandleAsync(
        GenerateTransferenciaCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // CF-16 / R4 — el replay se resuelve antes de validar, renderizar y escribir.
        var idempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? null
            : command.IdempotencyKey.Trim();

        if (idempotencyKey is not null)
        {
            var existing = await _repository
                .FindByIdempotencyKeyAsync(command.TenantId, idempotencyKey, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                return new GenerateTransferenciaResult(
                    GenerateTransferenciaOutcome.Generated, existing.Id, existing.Status, [], []);
            }
        }

        // Normativa primero: el escenario y las VB se evalúan ANTES de exigir la completitud del
        // cuerpo, para que un payload de escenario B no se rechace por «falta el adquirente».
        var validation = TransferValidationPolicy.Evaluate(command);

        if (validation.IsBlocked)
        {
            return new GenerateTransferenciaResult(
                GenerateTransferenciaOutcome.ValidationFailed,
                null,
                null,
                validation.Blocking,
                validation.Advisories);
        }

        var scenario = command.SingleScenario!;

        if (!TransferScenario.IsKnown(scenario))
        {
            return new GenerateTransferenciaResult(
                GenerateTransferenciaOutcome.ScenarioNotImplemented, null, null, [], [], "escenario");
        }

        if (!TryBuildModel(command, out var model, out var invalidField))
        {
            return new GenerateTransferenciaResult(
                GenerateTransferenciaOutcome.InvalidRequest, null, null, [], [], invalidField);
        }

        var now = _timeProvider.GetUtcNow();
        var id = Guid.CreateVersion7();
        model = model! with { ReferenceNumber = ReferenceNumberFor(id) };

        var document = new StandaloneDocument
        {
            Id = id,
            TenantId = command.TenantId,
            CreatedByUserId = command.UserId,
            DocumentType = StandaloneDocumentType.TransferenciaDominioGenerada,
            // El CHECK ck_standalone_documents_scenario_por_tipo exige escenario en este tipo.
            Scenario = scenario,
            Status = StandaloneDocumentStatus.Pending,
            IdempotencyKey = idempotencyKey,
            InputSummary = BuildInputSummary(command, model, scenario, now),
            CreatedAt = now,
            // I3 — vínculo con el lote (nulos en la generación individual). Inmutables por trigger:
            // por eso viajan en el INSERT y no en un UPDATE posterior.
            BatchId = command.BatchId,
            RowNumber = command.RowNumber,
        };

        await _repository.InsertAsync(document, cancellationToken).ConfigureAwait(false);

        // CF-26 — el snapshot se escribe ANTES de renderizar: es la evidencia de qué datos
        // produjeron este PDF, no un resumen de lo que salió bien.
        await _repository
            .SaveDocumentSnapshotAsync(
                command.TenantId, id, BuildDocumentSnapshot(model, validation.Advisories, now), cancellationToken)
            .ConfigureAwait(false);

        var rendered = _generator.Render(model);

        using var content = new MemoryStream(rendered.Content, writable: false);
        var stored = await _storage
            .SaveAsync(command.TenantId, StorageTipo, rendered.Filename, content, cancellationToken)
            .ConfigureAwait(false);

        await _repository
            .MarkGeneratedAsync(
                command.TenantId,
                id,
                new StandaloneDocumentFile(stored.StoragePath, stored.Sha256, stored.SizeBytes, rendered.Filename),
                cancellationToken)
            .ConfigureAwait(false);

        return new GenerateTransferenciaResult(
            GenerateTransferenciaOutcome.Generated,
            id,
            StandaloneDocumentStatus.Generated,
            [],
            validation.Advisories);
    }

    /// <summary>
    /// Completa el modelo de renderización del escenario elegido. Lo que falla aquí NO es normativo:
    /// son campos del contrato (nombres, documentos, ciudad, catálogos fiscales) sin los cuales el
    /// PDF saldría con variables sin resolver, que es justamente lo que prohíbe el checklist §13.1.
    ///
    /// <para><b>El escenario B se arma con UNA parte.</b> No se construye el adquirente y luego se
    /// decide no pintarlo: no se construye. Es la garantía estructural de §9.2 y §10 regla #4.</para>
    /// </summary>
    private bool TryBuildModel(
        GenerateTransferenciaCommand command,
        out TransferDocumentModel? model,
        out string? invalidField)
    {
        model = null;
        invalidField = null;

        var scenario = command.SingleScenario!;

        if (!TryBuildParty(command.Transferente, TransferPartyRole.Transferente, out var transferente, out invalidField))
        {
            return false;
        }

        var ciudad = command.Negocio?.CiudadFirma?.Trim();
        if (string.IsNullOrEmpty(ciudad))
        {
            invalidField = "negocio.ciudadFirma";
            return false;
        }

        var vehiculo = BuildVehiculo(command);
        var fecha = ResolveFechaFirma(command);
        var gravamen = new TransferDocumentEncumbrance(
            command.Gravamen?.GravamenActivo ?? false,
            command.Gravamen?.TieneLevantamientoOAutorizacion ?? false);

        if (scenario == TransferScenario.UnilateralLeasing)
        {
            if (!TryBuildLeasing(command, out var leasing, out invalidField))
            {
                return false;
            }

            model = new TransferDocumentModel(
                scenario,
                TransferSignatureMode.Manuscrita,
                vehiculo,
                // UNA parte. El locatario no está en esta lista y por eso su bloque de firma no
                // puede existir: no hay elemento que recorrer (anexo §9.2, §10 regla #4).
                [transferente! with { RolEtiqueta = TransferPartyRole.EtiquetaEntidadFinanciera }],
                // Sin negocio: el acto unilateral no declara precio ni contraprestación (VB-B-05).
                null,
                gravamen,
                ciudad,
                fecha,
                string.Empty,
                leasing);

            return true;
        }

        if (!TryBuildParty(command.Adquirente, TransferPartyRole.Adquirente, out var adquirente, out invalidField))
        {
            return false;
        }

        if (!TryBuildNegocio(command, out var negocio, out invalidField))
        {
            return false;
        }

        var esTercero = scenario == TransferScenario.FinancieraATercero;

        model = new TransferDocumentModel(
            scenario,
            TransferSignatureMode.Manuscrita,
            vehiculo,
            // El orden importa para el bloque de firmas: transferente a la izquierda, adquirente a
            // la derecha (anexo §9.1 y §9.3).
            [
                esTercero
                    ? transferente! with { RolEtiqueta = TransferPartyRole.EtiquetaTransferenteFinanciero }
                    : transferente!,
                esTercero
                    ? adquirente! with { RolEtiqueta = TransferPartyRole.EtiquetaAdquirenteTercero }
                    : adquirente!,
            ],
            negocio,
            gravamen,
            ciudad,
            fecha,
            string.Empty,
            // En el escenario C el antecedente de leasing es opcional y solo sirvió para VB-C-01:
            // no se lleva al documento, que se rige íntegramente por el art. 5.3.2.1.
            null);

        return true;
    }

    /// <summary>Las 13 variables de vehículo del anexo §5.1, ya recortadas.</summary>
    private static TransferDocumentVehicle BuildVehiculo(GenerateTransferenciaCommand command) =>
        new(
            command.Vehiculo!.Placa!.Trim().ToUpperInvariant(),
            Trim(command.Vehiculo.Marca),
            Trim(command.Vehiculo.Linea),
            Trim(command.Vehiculo.ModeloAnio),
            Trim(command.Vehiculo.ClaseVehiculo),
            Trim(command.Vehiculo.TipoCarroceria),
            Trim(command.Vehiculo.Color),
            Trim(command.Vehiculo.NoMotor),
            Trim(command.Vehiculo.NoChasis),
            Trim(command.Vehiculo.NoSerie),
            Trim(command.Vehiculo.Servicio),
            Trim(command.Vehiculo.NoLicenciaTransito),
            Trim(command.Vehiculo.OrganismoTransito));

    /// <summary>
    /// Sin fecha declarada, el día de hoy en Colombia. Se calcula con offset fijo -05:00 y no con
    /// <c>TimeZoneInfo</c>: la API compila con <c>InvariantGlobalization</c> y «America/Bogota» no
    /// existe ahí.
    /// </summary>
    private DateOnly ResolveFechaFirma(GenerateTransferenciaCommand command) =>
        command.Negocio?.FechaFirma
        ?? DateOnly.FromDateTime(_timeProvider.GetUtcNow().ToOffset(TimeSpan.FromHours(-5)).DateTime);

    /// <summary>
    /// Variables del negocio de los escenarios A y C (anexo §5.4). El escenario B no pasa por aquí:
    /// no tiene negocio con precio.
    /// </summary>
    private static bool TryBuildNegocio(
        GenerateTransferenciaCommand command,
        out TransferDocumentBusiness? negocio,
        out string? invalidField)
    {
        negocio = null;
        invalidField = null;

        var entrada = command.Negocio;
        var esRemolque = TransferValidationPolicy.EsRemolque(command.Vehiculo?.ClaseVehiculo);

        var retencion = NormalizeCatalog(entrada?.AsumeRetencionFuente, TransferFiscalAssumption.ConLey);
        if (retencion is null)
        {
            invalidField = "negocio.asumeRetencionFuente";
            return false;
        }

        var derechos = NormalizeCatalog(entrada?.AsumeDerechosTramite, TransferFiscalAssumption.Derechos);
        if (derechos is null)
        {
            invalidField = "negocio.asumeDerechosTramite";
            return false;
        }

        // El impuesto sobre vehículos NO aplica a remolques ni semirremolques: están exentos
        // (Ley 488/1998, art. 5.3.2.1 num. 5.º inciso final). Pedirlo ahí sería pedir quién asume
        // un impuesto que nadie debe.
        string? impuesto = null;
        if (!esRemolque)
        {
            impuesto = NormalizeCatalog(entrada?.AsumeImpuestoVehiculo, TransferFiscalAssumption.ConLey);
            if (impuesto is null)
            {
                invalidField = "negocio.asumeImpuestoVehiculo";
                return false;
            }
        }

        var titulo = entrada!.TituloJuridico!.Trim().ToUpperInvariant();

        negocio = new TransferDocumentBusiness(
            titulo,
            TransferJuridicalTitle.Wording(titulo),
            Trim(entrada.DescripcionTitulo),
            Trim(entrada.PrecioLetras),
            Trim(entrada.PrecioNumeros),
            Trim(entrada.ContraprestacionDescripcion),
            Trim(entrada.FormaPago),
            retencion,
            derechos,
            impuesto);

        return true;
    }

    /// <summary>
    /// Antecedente de leasing del escenario B (anexo §5.5). Los datos del locatario alimentan las
    /// cláusulas segunda y tercera; <b>jamás</b> el bloque de firmas.
    /// </summary>
    private static bool TryBuildLeasing(
        GenerateTransferenciaCommand command,
        out TransferDocumentLeasing? leasing,
        out string? invalidField)
    {
        leasing = null;
        invalidField = null;

        var entrada = command.Leasing;

        var contrato = Trim(entrada?.NoContratoLeasing);
        if (contrato is null)
        {
            invalidField = "leasing.noContratoLeasing";
            return false;
        }

        var opcion = entrada?.TipoOpcionCompra?.Trim().ToUpperInvariant();
        if (!TransferPurchaseOption.IsKnown(opcion))
        {
            invalidField = "leasing.tipoOpcionCompra";
            return false;
        }

        var nombre = Trim(entrada?.LocatarioNombre);
        if (nombre is null)
        {
            invalidField = "leasing.locatarioNombre";
            return false;
        }

        var documento = Trim(entrada?.LocatarioNoDoc);
        if (documento is null)
        {
            invalidField = "leasing.locatarioNoDoc";
            return false;
        }

        leasing = new TransferDocumentLeasing(
            contrato,
            opcion!,
            entrada?.FechaTerminacion,
            nombre,
            Trim(entrada?.LocatarioTipoDoc)?.ToUpperInvariant(),
            documento);

        return true;
    }

    /// <summary>
    /// Construye una parte compareciente. <b>El dígito de verificación se recalcula</b> cuando el
    /// documento es un NIT: si el usuario tecleó uno distinto, el PDF no puede llevar un NIT y un DV
    /// que no se corresponden.
    /// </summary>
    private static bool TryBuildParty(
        TransferPartyInput? input,
        string rol,
        out TransferDocumentParty? party,
        out string? invalidField)
    {
        party = null;
        invalidField = null;
        var prefijo = rol == TransferPartyRole.Transferente ? "transferente" : "adquirente";

        var nombre = Trim(input?.NombreRazonSocial);
        if (nombre is null)
        {
            invalidField = $"{prefijo}.nombreRazonSocial";
            return false;
        }

        var numero = Trim(input?.NumeroDoc);
        if (numero is null)
        {
            invalidField = $"{prefijo}.numeroDoc";
            return false;
        }

        var tipoDoc = Trim(input?.TipoDoc);
        if (tipoDoc is null)
        {
            invalidField = $"{prefijo}.tipoDoc";
            return false;
        }

        string? dv = null;
        if (string.Equals(tipoDoc, "NIT", StringComparison.OrdinalIgnoreCase)
            && NitVerificationDigit.TryCompute(numero, out var digito))
        {
            dv = digito.ToString(CultureInfo.InvariantCulture);
        }

        party = new TransferDocumentParty(
            rol,
            nombre,
            tipoDoc.ToUpperInvariant(),
            numero,
            dv,
            Trim(input?.Domicilio),
            Trim(input?.RepresentanteLegal),
            Trim(input?.CcRepresentanteLegal));

        return true;
    }

    /// <summary>
    /// CF-26 — <c>document_snapshot</c>: TODO lo que se usó para renderizar (partes, vehículo,
    /// negocio, documentos de identidad y domicilios). Es PII alta y por eso no se expone en el
    /// listado: el repositorio no lo selecciona siquiera.
    /// <para>Se arma con <see cref="JsonObject"/> y no con <c>JsonSerializer</c>: este proyecto
    /// compila con <c>IsAotCompatible=true</c> y serializar por reflexión rompe el análisis
    /// (IL2026/IL3050).</para>
    /// </summary>
    private static string BuildDocumentSnapshot(
        TransferDocumentModel model,
        IReadOnlyList<TransferValidationIssue> advisories,
        DateTimeOffset generatedAt)
    {
        var partes = new JsonArray();
        foreach (var parte in model.Partes)
        {
            partes.Add((JsonNode)new JsonObject
            {
                ["rol"] = parte.Rol,
                ["nombreRazonSocial"] = parte.NombreRazonSocial,
                ["tipoDoc"] = parte.TipoDoc,
                ["numeroDoc"] = parte.NumeroDoc,
                ["digitoVerificacion"] = parte.DigitoVerificacion,
                ["domicilio"] = parte.Domicilio,
                ["representanteLegal"] = parte.RepresentanteLegal,
                ["ccRepresentanteLegal"] = parte.CcRepresentanteLegal,
            });
        }

        var avisos = new JsonArray();
        foreach (var advisory in advisories)
        {
            avisos.Add((JsonNode)new JsonObject
            {
                ["code"] = advisory.Code,
                ["field"] = advisory.Field,
            });
        }

        var vehiculo = new JsonObject
        {
            ["placa"] = model.Vehiculo.Placa,
            ["marca"] = model.Vehiculo.Marca,
            ["linea"] = model.Vehiculo.Linea,
            ["modeloAnio"] = model.Vehiculo.ModeloAnio,
            ["claseVehiculo"] = model.Vehiculo.ClaseVehiculo,
            ["tipoCarroceria"] = model.Vehiculo.TipoCarroceria,
            ["color"] = model.Vehiculo.Color,
            ["noMotor"] = model.Vehiculo.NoMotor,
            ["noChasis"] = model.Vehiculo.NoChasis,
            ["noSerie"] = model.Vehiculo.NoSerie,
            ["servicio"] = model.Vehiculo.Servicio,
            ["noLicenciaTransito"] = model.Vehiculo.NoLicenciaTransito,
            ["organismoTransito"] = model.Vehiculo.OrganismoTransito,
        };

        var negocio = model.Negocio is null ? null : new JsonObject
        {
            ["tituloJuridico"] = model.Negocio.TituloJuridico,
            ["descripcionTitulo"] = model.Negocio.DescripcionTitulo,
            ["precioLetras"] = model.Negocio.PrecioLetras,
            ["precioNumeros"] = model.Negocio.PrecioNumeros,
            ["contraprestacionDescripcion"] = model.Negocio.ContraprestacionDescripcion,
            ["formaPago"] = model.Negocio.FormaPago,
            ["asumeRetencionFuente"] = model.Negocio.AsumeRetencionFuente,
            ["asumeDerechosTramite"] = model.Negocio.AsumeDerechosTramite,
            ["asumeImpuestoVehiculo"] = model.Negocio.AsumeImpuestoVehiculo,
        };

        // El antecedente de leasing es PII alta (nombre y documento del locatario) y por eso vive
        // solo aquí: el snapshot no se expone en el listado ni en la respuesta de la generación.
        var leasing = model.Leasing is null ? null : new JsonObject
        {
            ["noContrato"] = model.Leasing.NoContrato,
            ["tipoOpcionCompra"] = model.Leasing.TipoOpcionCompra,
            ["fechaTerminacion"] = model.Leasing.FechaTerminacion?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["locatarioNombre"] = model.Leasing.LocatarioNombre,
            ["locatarioTipoDoc"] = model.Leasing.LocatarioTipoDoc,
            ["locatarioNoDoc"] = model.Leasing.LocatarioNoDoc,
        };

        return new JsonObject
        {
            ["generatedAt"] = generatedAt.ToString("O", CultureInfo.InvariantCulture),
            ["scenario"] = model.Scenario,
            ["signatureMode"] = model.SignatureMode,
            ["ciudadFirma"] = model.CiudadFirma,
            ["fechaFirma"] = model.FechaFirma.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["vehiculo"] = vehiculo,
            ["partes"] = partes,
            ["negocio"] = negocio,
            ["leasing"] = leasing,
            ["gravamen"] = new JsonObject
            {
                ["gravamenActivo"] = model.Gravamen.GravamenActivo,
                ["tieneLevantamientoOAutorizacion"] = model.Gravamen.TieneLevantamientoOAutorizacion,
            },
            ["advisories"] = avisos,
        }.ToJsonString();
    }

    /// <summary>
    /// CF-26 — <c>input_summary</c>: placa, NIT, escenario, título jurídico y la declaración de
    /// régimen. <b>Nada más.</b> Sin domicilios, sin correos y sin nombres completos: este JSON
    /// alimenta el listado del historial y viaja a la auditoría en cada UPDATE de la fila.
    /// <para>El «NIT» es el de la parte jurídica —un identificador de empresa, público en el RUES—.
    /// El documento de una persona natural NO entra aquí.</para>
    /// </summary>
    private static string BuildInputSummary(
        GenerateTransferenciaCommand command,
        TransferDocumentModel model,
        string scenario,
        DateTimeOffset generatedAt)
    {
        var nit = model.Partes.FirstOrDefault(
            p => string.Equals(p.TipoDoc, "NIT", StringComparison.OrdinalIgnoreCase))?.NumeroDoc;

        // CF-24 — la declaración de régimen y su fecha se conservan como evidencia de que el gate se
        // ofreció y el usuario lo respondió (anexo §4.0, nota de método). Los códigos son categorías
        // del catálogo normativo, no datos de la persona: aquí no entra PII por este motivo.
        JsonObject? regimen = null;
        if (command.RegimenAplicable is { } declaracion)
        {
            var condiciones = new JsonArray();
            foreach (var codigo in declaracion.CondicionesDeclaradas ?? [])
            {
                if (TransferSpecialRegime.Find(codigo) is { } condicion)
                {
                    condiciones.Add((JsonNode)new JsonObject
                    {
                        ["codigo"] = condicion.Codigo,
                        ["articulo"] = condicion.Articulo,
                    });
                }
            }

            regimen = new JsonObject
            {
                ["ningunaAplica"] = declaracion.NingunaAplica,
                ["condicionesDeclaradas"] = condiciones,
                ["declaredAt"] = (declaracion.DeclaredAt ?? generatedAt)
                    .ToString("O", CultureInfo.InvariantCulture),
            };
        }

        return new JsonObject
        {
            ["documentType"] = StandaloneDocumentType.TransferenciaDominioGenerada,
            ["placa"] = model.Vehiculo.Placa,
            ["nit"] = nit,
            ["escenario"] = scenario,
            ["tituloJuridico"] = model.Negocio?.TituloJuridico,
            ["regimenAplicable"] = regimen,
        }.ToJsonString();
    }

    private static string? NormalizeCatalog(string? value, IReadOnlyList<string> catalog)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return normalized is not null && catalog.Contains(normalized, StringComparer.Ordinal)
            ? normalized
            : null;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Referencia sintética del documento standalone: aquí no hay radicado, no hay trámite.</summary>
    public static string ReferenceNumberFor(Guid id) => $"GD-{id.ToString("N")[..8]}";
}
