using System.Globalization;
using System.Text.Json.Nodes;
using Flit.Admin.Application.GeneracionDocumental.GenerateRues;
using Flit.Admin.Application.GeneracionDocumental.GenerateTransferencia;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.GeneracionDocumental;

namespace Flit.Admin.Application.GeneracionDocumental.Batches;

/// <summary>
/// Procesa un lote XLSX fila por fila (CF-12/CF-13/CF-24 en lote, Feature #12201 I3). Vive en
/// Application —y no dentro del <c>BackgroundService</c>— para que el recorrido completo del lote
/// sea comprobable sin base de datos, sin hosting y sin OpenXml.
///
/// <para><b>El worker no genera documentos: los pide.</b> Cada fila se traduce a un comando y se
/// entrega a <see cref="GenerateRuesDocumentHandler"/> o a <see cref="GenerateTransferenciaHandler"/>,
/// que son los mismos de la generación individual. Reimplementar aquí las VB, el snapshot o el
/// render produciría dos verdades normativas: la del formulario y la del lote.</para>
///
/// <para><b>Una fila mala no cancela el lote</b> (CF-13). Cada fila se aísla: sus errores quedan en
/// <c>validation_errors</c> con código, campo y mensaje —nunca con el valor capturado— y el recorrido
/// sigue. El lote cierra en <c>partial_failure</c> si hubo de las dos clases, en <c>failed</c> si no
/// se generó ninguna y en <c>completed</c> solo si todas salieron.</para>
///
/// <para><b>Reproceso seguro (R5).</b> El reaper devuelve a la cola un lote cuyo <c>claimed_at</c>
/// venció. Antes de tocar nada se leen las filas ya materializadas y se saltan; el respaldo duro es
/// el índice único <c>uq_standalone_documents_batch_row</c>, que impide físicamente una segunda fila
/// con el mismo (lote, número de fila).</para>
/// </summary>
public sealed class StandaloneDocumentBatchRunner
{
    /// <summary>Fila del XLSX cuyo <c>document_type</c> no es ninguno de los dos conocidos.</summary>
    public const string ErrorUnknownDocumentType = "unknown_document_type";

    /// <summary>Fila de transferencia cuyo escenario no es A, B ni C (o falta).</summary>
    public const string ErrorInvalidScenario = "invalid_scenario";

    /// <summary>Celda de fecha con contenido que no es una fecha ISO AAAA-MM-DD.</summary>
    public const string ErrorInvalidDate = "invalid_date";

    /// <summary>Excepción no prevista al generar UNA fila. No cancela el lote.</summary>
    public const string ErrorRowFailed = "row_failed";

    private readonly IStandaloneDocumentBatchRepository _batches;
    private readonly IStandaloneDocumentRepository _documents;
    private readonly IStandaloneDocumentStorage _storage;
    private readonly IStandaloneDocumentXlsxParser _parser;
    private readonly GenerateRuesDocumentHandler _rues;
    private readonly GenerateTransferenciaHandler _transferencia;
    private readonly TimeProvider _timeProvider;

    public StandaloneDocumentBatchRunner(
        IStandaloneDocumentBatchRepository batches,
        IStandaloneDocumentRepository documents,
        IStandaloneDocumentStorage storage,
        IStandaloneDocumentXlsxParser parser,
        GenerateRuesDocumentHandler rues,
        GenerateTransferenciaHandler transferencia,
        TimeProvider? timeProvider = null)
    {
        _batches = batches ?? throw new ArgumentNullException(nameof(batches));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _rues = rues ?? throw new ArgumentNullException(nameof(rues));
        _transferencia = transferencia ?? throw new ArgumentNullException(nameof(transferencia));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Reclama el siguiente lote procesable —encolado o atascado más allá de
    /// <paramref name="claimTimeout"/>— y lo procesa entero. Devuelve <c>false</c> si no había nada
    /// que hacer, que es la señal para que el worker se duerma.
    /// </summary>
    public async Task<bool> RunNextAsync(TimeSpan claimTimeout, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        var batch = await _batches
            .ClaimNextAsync(now, now - claimTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (batch is null)
        {
            return false;
        }

        await ProcessAsync(batch, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Procesa un lote ya reclamado y lo cierra. Público para poder probar el recorrido.</summary>
    public async Task ProcessAsync(StandaloneDocumentBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var generated = 0;
        var errors = 0;

        var source = await _storage
            .OpenReadAsync(batch.SourceStoragePath, cancellationToken)
            .ConfigureAwait(false);

        if (source is null)
        {
            // Sin archivo fuente no hay lote que reconstruir. Se cierra en 'failed' con todas las
            // filas contadas como error: dejarlo en 'processing' lo haría girar para siempre en el
            // reaper.
            await CompleteAsync(batch, 0, batch.TotalItems, cancellationToken).ConfigureAwait(false);
            return;
        }

        StandaloneBatchParseResult parsed;
        await using (source.ConfigureAwait(false))
        {
            parsed = _parser.Parse(source);
        }

        if (parsed.ErrorCode is not null)
        {
            await CompleteAsync(batch, 0, batch.TotalItems, cancellationToken).ConfigureAwait(false);
            return;
        }

        var yaHechas = await _documents
            .ListBatchRowStatesAsync(batch.TenantId, batch.Id, cancellationToken)
            .ConfigureAwait(false);

        var hechasPorFila = yaHechas.ToDictionary(r => r.RowNumber, r => r.Status);

        foreach (var row in parsed.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // R5 — reproceso: lo ya materializado no se vuelve a generar, solo se recuenta.
            if (hechasPorFila.TryGetValue(row.RowNumber, out var estadoPrevio))
            {
                if (string.Equals(estadoPrevio, StandaloneDocumentStatus.Generated, StringComparison.Ordinal))
                {
                    generated++;
                }
                else
                {
                    errors++;
                }

                continue;
            }

            bool ok;
            try
            {
                ok = await ProcessRowAsync(batch, row, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // Una fila que revienta no puede tumbar el lote (CF-13).
            catch (Exception)
#pragma warning restore CA1031
            {
                ok = false;
                await InsertErrorRowAsync(
                        batch,
                        row.RowNumber,
                        null,
                        null,
                        [new StandaloneDocumentValidationError(
                            ErrorRowFailed,
                            "fila",
                            "La fila no se pudo procesar. Vuelva a cargarla en un lote nuevo.")],
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (ok)
            {
                generated++;
            }
            else
            {
                errors++;
            }
        }

        await CompleteAsync(batch, generated, errors, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Procesa UNA fila. <c>true</c> si terminó en <c>generated</c>.</summary>
    private async Task<bool> ProcessRowAsync(
        StandaloneDocumentBatch batch,
        StandaloneBatchParsedRow row,
        CancellationToken cancellationToken)
    {
        // Errores de lectura (p. ej. serial numérico en columna de fecha): la fila ni se intenta.
        if (row.Errors.Count > 0)
        {
            await InsertErrorRowAsync(batch, row.RowNumber, null, null, row.Errors, cancellationToken)
                .ConfigureAwait(false);
            return false;
        }

        var documentType = Text(row, StandaloneBatchTemplate.DocumentType)?.ToLowerInvariant();

        if (string.Equals(documentType, StandaloneDocumentType.CertificadoRues, StringComparison.Ordinal))
        {
            return await ProcessRuesRowAsync(batch, row, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(
                documentType, StandaloneDocumentType.TransferenciaDominioGenerada, StringComparison.Ordinal))
        {
            return await ProcessTransferenciaRowAsync(batch, row, cancellationToken).ConfigureAwait(false);
        }

        // CF-12 — un tipo desconocido es UNA fila en error, nunca un aborto del lote.
        return await FailRowAsync(
                batch,
                row,
                ErrorUnknownDocumentType,
                StandaloneBatchTemplate.DocumentType,
                "El tipo de documento declarado no existe. Use uno de los valores de la plantilla v1.",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> ProcessRuesRowAsync(
        StandaloneDocumentBatch batch,
        StandaloneBatchParsedRow row,
        CancellationToken cancellationToken)
    {
        var result = await _rues
            .HandleAsync(
                new GenerateRuesDocumentCommand(
                    batch.TenantId,
                    batch.CreatedByUserId,
                    Text(row, StandaloneBatchTemplate.Nit),
                    IdempotencyKey: null,
                    BatchId: batch.Id,
                    RowNumber: row.RowNumber),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Outcome == GenerateRuesDocumentOutcome.Generated)
        {
            return true;
        }

        var error = new StandaloneDocumentValidationError(
            result.ErrorCode ?? ErrorRowFailed,
            StandaloneBatchTemplate.Nit,
            "El Certificado RUES no se pudo emitir para esta fila.");

        if (result.Id is { } id)
        {
            // El handler ya dejó la fila en 'error' (hubo consulta al proveedor y hay algo que
            // auditar): solo falta el detalle por fila. validation_errors está exenta del trigger de
            // inmutabilidad, igual que downloaded_at.
            await _documents
                .SaveValidationErrorsAsync(batch.TenantId, id, ToJson([error]), cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            // Rechazo previo a crear fila (NIT ausente o mal formado): la crea el worker, o el lote
            // quedaría con menos ítems de los que declara.
            await InsertErrorRowAsync(
                    batch,
                    row.RowNumber,
                    StandaloneDocumentType.CertificadoRues,
                    null,
                    [error],
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return false;
    }

    private async Task<bool> ProcessTransferenciaRowAsync(
        StandaloneDocumentBatch batch,
        StandaloneBatchParsedRow row,
        CancellationToken cancellationToken)
    {
        var escenario = Text(row, StandaloneBatchTemplate.Escenario)?.ToUpperInvariant();

        if (!TransferScenario.IsKnown(escenario))
        {
            return await FailRowAsync(
                    batch,
                    row,
                    ErrorInvalidScenario,
                    StandaloneBatchTemplate.Escenario,
                    "El escenario debe ser exactamente uno de A, B o C (VB-05).",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!TryDate(row, StandaloneBatchTemplate.FechaFirma, out var fechaFirma))
        {
            return await FailRowAsync(
                    batch, row, ErrorInvalidDate, StandaloneBatchTemplate.FechaFirma,
                    "La fecha debe venir como texto en formato AAAA-MM-DD.", cancellationToken)
                .ConfigureAwait(false);
        }

        if (!TryDate(row, StandaloneBatchTemplate.FechaTerminacion, out var fechaTerminacion))
        {
            return await FailRowAsync(
                    batch, row, ErrorInvalidDate, StandaloneBatchTemplate.FechaTerminacion,
                    "La fecha debe venir como texto en formato AAAA-MM-DD.", cancellationToken)
                .ConfigureAwait(false);
        }

        var command = new GenerateTransferenciaCommand(
            batch.TenantId,
            batch.CreatedByUserId,
            [escenario!],
            new TransferVehicleInput(
                Text(row, StandaloneBatchTemplate.Placa),
                Text(row, "marca"),
                Text(row, "linea"),
                Text(row, "modelo_anio"),
                Text(row, "clase_vehiculo"),
                Text(row, "tipo_carroceria"),
                Text(row, "color"),
                Text(row, "no_motor"),
                Text(row, "no_chasis"),
                Text(row, "no_serie"),
                Text(row, "servicio"),
                Text(row, "no_licencia_transito"),
                Text(row, "organismo_transito")),
            Parte(row, "transferente"),
            Parte(row, "adquirente"),
            new TransferBusinessInput(
                Text(row, "titulo_juridico"),
                Text(row, "descripcion_titulo"),
                Text(row, "precio_letras"),
                Text(row, "precio_numeros"),
                Text(row, "contraprestacion_descripcion"),
                Text(row, "forma_pago"),
                Text(row, "asume_retencion_fuente"),
                Text(row, "asume_derechos_tramite"),
                Text(row, "asume_impuesto_vehiculo"),
                Text(row, "ciudad_firma"),
                fechaFirma),
            new TransferEncumbranceInput(
                Flag(row, "gravamen_activo"),
                Flag(row, "tiene_levantamiento_o_autorizacion")),
            Regimen(row, _timeProvider.GetUtcNow()),
            new TransferLeasingInput(
                Flag(row, "transferente_es_entidad_financiera"),
                Text(row, "no_contrato_leasing"),
                Text(row, "tipo_opcion_compra"),
                fechaTerminacion,
                Text(row, "locatario_nombre"),
                Text(row, "locatario_tipo_doc"),
                Text(row, "locatario_no_doc")),
            IdempotencyKey: null,
            BatchId: batch.Id,
            RowNumber: row.RowNumber);

        var result = await _transferencia.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        if (result.Outcome == GenerateTransferenciaOutcome.Generated)
        {
            return true;
        }

        // Una VB bloqueante NO deja fila en la generación individual (el rechazo ocurre antes de que
        // exista documento). En lote sí tiene que dejarla: el usuario necesita ver qué falló en la
        // fila 4 sin abrir el XLSX. Aquí entra VB-07 con su artículo citado (CF-24 en lote).
        StandaloneDocumentValidationError[] errores = result.Errors.Count > 0
            ? [.. result.Errors.Select(e => new StandaloneDocumentValidationError(e.Code, e.Field, e.Message))]
            : [new StandaloneDocumentValidationError(
                GenerateTransferenciaHandler.ErrorInvalidRequest,
                result.ErrorField ?? "fila",
                "La fila no tiene los datos mínimos para emitir el documento.")];

        await InsertErrorRowAsync(
                batch,
                row.RowNumber,
                StandaloneDocumentType.TransferenciaDominioGenerada,
                escenario,
                errores,
                cancellationToken)
            .ConfigureAwait(false);

        return false;
    }

    private async Task<bool> FailRowAsync(
        StandaloneDocumentBatch batch,
        StandaloneBatchParsedRow row,
        string code,
        string field,
        string message,
        CancellationToken cancellationToken)
    {
        await InsertErrorRowAsync(
                batch,
                row.RowNumber,
                null,
                null,
                [new StandaloneDocumentValidationError(code, field, message)],
                cancellationToken)
            .ConfigureAwait(false);

        return false;
    }

    /// <summary>
    /// Materializa la fila fallida como documento en <c>error</c> (sub-decisión §3.c: no hay tabla
    /// de items).
    ///
    /// <para><b>Limitación conocida del esquema.</b> El CHECK
    /// <c>ck_standalone_documents_document_type</c> solo admite los dos literales conocidos y
    /// <c>ck_standalone_documents_scenario_por_tipo</c> exige escenario en la transferencia. Una fila
    /// cuyo tipo es desconocido —o cuya transferencia no declara escenario válido— no se puede
    /// tipificar: se persiste con la combinación mínima que los CHECK admiten
    /// (<c>certificado_rues</c> + escenario nulo) y el error real viaja en <c>validation_errors</c>,
    /// que es lo que la interfaz muestra. El tipo tecleado por el usuario NO se conserva en la
    /// columna: el esquema no tiene dónde ponerlo.</para>
    /// </summary>
    private Task InsertErrorRowAsync(
        StandaloneDocumentBatch batch,
        int rowNumber,
        string? documentType,
        string? scenario,
        IReadOnlyList<StandaloneDocumentValidationError> errors,
        CancellationToken cancellationToken)
    {
        var esTransferenciaTipificable =
            string.Equals(
                documentType, StandaloneDocumentType.TransferenciaDominioGenerada, StringComparison.Ordinal)
            && TransferScenario.IsKnown(scenario);

        var tipo = esTransferenciaTipificable
            ? StandaloneDocumentType.TransferenciaDominioGenerada
            : StandaloneDocumentType.CertificadoRues;

        var primero = errors[0];

        var document = new StandaloneDocument
        {
            Id = Guid.CreateVersion7(),
            TenantId = batch.TenantId,
            CreatedByUserId = batch.CreatedByUserId,
            DocumentType = tipo,
            Scenario = esTransferenciaTipificable ? scenario : null,
            Status = StandaloneDocumentStatus.Error,
            ErrorCode = primero.Code,
            ErrorField = primero.Field,
            // input_summary POBRE EN PII: lote y número de fila. Nada del contenido del usuario.
            InputSummary = new JsonObject
            {
                ["batchId"] = batch.Id.ToString(),
                ["rowNumber"] = rowNumber,
            }.ToJsonString(),
            ValidationErrors = ToJson(errors),
            BatchId = batch.Id,
            RowNumber = rowNumber,
            CreatedAt = _timeProvider.GetUtcNow(),
        };

        return _documents.InsertAsync(document, cancellationToken);
    }

    private Task CompleteAsync(
        StandaloneDocumentBatch batch,
        int generated,
        int errors,
        CancellationToken cancellationToken)
    {
        var status = errors == 0
            ? StandaloneDocumentBatchStatus.Completed
            : generated == 0
                ? StandaloneDocumentBatchStatus.Failed
                : StandaloneDocumentBatchStatus.PartialFailure;

        return _batches.CompleteAsync(
            batch.Id, status, generated, errors, _timeProvider.GetUtcNow(), cancellationToken);
    }

    /// <summary>
    /// JSON de <c>validation_errors</c> con <c>JsonObject</c> y NO con <c>JsonSerializer</c>: el
    /// proyecto compila con <c>IsAotCompatible</c> y serializar tipos anónimos por reflexión rompe
    /// el análisis (IL2026 / IL3050).
    /// </summary>
    internal static string ToJson(IReadOnlyList<StandaloneDocumentValidationError> errors)
    {
        var array = new JsonArray();
        foreach (var error in errors)
        {
            // Add((JsonNode?)...) y NO Add<T>(T): el generico exige codigo en runtime y el proyecto
            // compila con IsAotCompatible (IL2026 / IL3050).
            array.Add((JsonNode)new JsonObject
            {
                ["code"] = error.Code,
                ["field"] = error.Field,
                ["message"] = error.Message,
            });
        }

        return array.ToJsonString();
    }

    private static string? Text(StandaloneBatchParsedRow row, string header)
    {
        if (!row.Values.TryGetValue(header, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    /// <summary>Casilla booleana de la plantilla. Vacía = <c>false</c>: no responder no es afirmar.</summary>
    private static bool Flag(StandaloneBatchParsedRow row, string header) =>
        Text(row, header)?.ToUpperInvariant() is "SI" or "SÍ" or "S" or "X" or "TRUE" or "1";

    /// <summary>
    /// Fecha en texto ISO. <c>false</c> si hay contenido y NO es una fecha; la celda vacía sí es
    /// válida (no todas las filas llevan fecha de terminación).
    /// </summary>
    private static bool TryDate(StandaloneBatchParsedRow row, string header, out DateOnly? parsed)
    {
        parsed = null;
        var text = Text(row, header);
        if (text is null)
        {
            return true;
        }

        if (DateOnly.TryParseExact(
                text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            parsed = date;
            return true;
        }

        return false;
    }

    private static TransferPartyInput Parte(StandaloneBatchParsedRow row, string prefijo) => new(
        Text(row, $"{prefijo}_tipo_persona"),
        Text(row, $"{prefijo}_nombre"),
        Text(row, $"{prefijo}_tipo_doc"),
        Text(row, $"{prefijo}_no_doc"),
        null,
        Text(row, $"{prefijo}_domicilio"),
        Text(row, $"{prefijo}_representante_legal"),
        Text(row, $"{prefijo}_cc_representante_legal"));

    /// <summary>
    /// Declaración de régimen de la fila (CF-24 en lote). <c>NingunaAplica</c> queda en <c>null</c>
    /// si la casilla viene vacía: «no respondió» y «respondió que ninguna aplica» son estados
    /// distintos y solo el segundo permite generar. VB-07 bloquea los otros dos.
    /// </summary>
    private static RegimenDeclarationInput Regimen(StandaloneBatchParsedRow row, DateTimeOffset now)
    {
        var declarado = Text(row, StandaloneBatchTemplate.RegimenNingunaAplica)?.ToUpperInvariant();

        bool? ninguna = declarado switch
        {
            null => null,
            "SI" or "SÍ" or "S" or "X" or "TRUE" or "1" => true,
            _ => false,
        };

        var condiciones = Text(row, StandaloneBatchTemplate.RegimenCondiciones)
            ?.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new RegimenDeclarationInput(ninguna, condiciones, now);
    }
}
