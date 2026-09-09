using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.GeneracionDocumental;

namespace Flit.Admin.Application.GeneracionDocumental.GenerateRues;

/// <summary>
/// Emite el Certificado RUES SIN trámite (CF-02/CF-04, Feature #12201,
/// ADR-0056-generacion-documental-standalone): consulta el RUES en vivo, congela el snapshot,
/// renderiza el PDF con el generador del expediente y lo persiste en storage. No crea ni referencia
/// ninguna <c>procedure_instance</c>.
///
/// <para><b>Orden de las operaciones (no es cosmético):</b> la idempotencia se resuelve ANTES de la
/// consulta (CF-16 / R4). Repetir la clave no puede gastar una consulta al proveedor ni escribir un
/// archivo nuevo, así que el replay se responde con la fila existente y se sale.</para>
///
/// <para><b>Por qué tres escrituras y no una:</b> la fila nace en <c>pending</c> antes de la
/// consulta para que un fallo del proveedor deje rastro auditable; luego se escribe el snapshot
/// (que el trigger congela en cuanto existe) y por último se cierra en <c>generated</c> con el
/// binario. Un único UPDATE final que reescribiera el snapshot sería rechazado por
/// <c>tr_standalone_documents_immutable</c>.</para>
///
/// <para>Esta clase no nombra ningún tipo de <c>Flit.Tramites.*</c> (restricción C6): storage,
/// renderizador y consulta entran por puertos declarados en Admin.</para>
/// </summary>
public sealed class GenerateRuesDocumentHandler
{
    /// <summary>Agrupación del artefacto en el storage. No es un tipo del catálogo documental del trámite.</summary>
    internal const string StorageTipo = "generacion_documental";

    internal const string ErrorInvalidRequest = "invalid_request";
    internal const string ErrorRuesNotFound = "rues_not_found";
    internal const string ErrorProviderUnavailable = "provider_unavailable";
    internal const string ErrorProviderNotFound = "provider_not_found";

    /// <summary>NIT: 5 a 20 caracteres, dígitos con separadores opcionales.</summary>
    private static readonly Regex NitPattern =
        new(@"^\d[\d.\-]{3,18}\d$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    private readonly IStandaloneDocumentRepository _repository;
    private readonly IStandaloneRuesCompanyLookup _lookup;
    private readonly IStandaloneRuesCertificateRenderer _renderer;
    private readonly IStandaloneDocumentStorage _storage;
    private readonly TimeProvider _timeProvider;

    public GenerateRuesDocumentHandler(
        IStandaloneDocumentRepository repository,
        IStandaloneRuesCompanyLookup lookup,
        IStandaloneRuesCertificateRenderer renderer,
        IStandaloneDocumentStorage storage,
        TimeProvider? timeProvider = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<GenerateRuesDocumentResult> HandleAsync(
        GenerateRuesDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var nit = command.Nit?.Trim();
        if (string.IsNullOrEmpty(nit) || !NitPattern.IsMatch(nit))
        {
            return new GenerateRuesDocumentResult(
                GenerateRuesDocumentOutcome.InvalidRequest, null, null, ErrorInvalidRequest);
        }

        // CF-16 / R4 — el replay se resuelve ANTES de gastar la consulta y antes de escribir nada.
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
                return new GenerateRuesDocumentResult(
                    GenerateRuesDocumentOutcome.Generated, existing.Id, existing.Status, existing.ErrorCode);
            }
        }

        var now = _timeProvider.GetUtcNow();
        var id = Guid.CreateVersion7();

        var document = new StandaloneDocument
        {
            Id = id,
            TenantId = command.TenantId,
            CreatedByUserId = command.UserId,
            DocumentType = StandaloneDocumentType.CertificadoRues,
            // El CHECK ck_standalone_documents_scenario_por_tipo prohíbe escenario en el RUES.
            Scenario = null,
            Status = StandaloneDocumentStatus.Pending,
            IdempotencyKey = idempotencyKey,
            // Resumen POBRE EN PII: NIT y tipo. Sin razón social, dirección ni correo.
            // JSON armado con JsonObject y no con JsonSerializer: el proyecto compila con
            // IsAotCompatible=true y la serialización por reflexión rompe el análisis (IL2026/IL3050).
            InputSummary = new JsonObject
            {
                ["nit"] = nit,
                ["documentType"] = StandaloneDocumentType.CertificadoRues,
            }.ToJsonString(),
            CreatedAt = now,
            // I3 — vínculo con el lote. En la generación individual ambos son nulos y el CHECK
            // ck_standalone_documents_batch_row exige justamente eso: o los dos o ninguno.
            BatchId = command.BatchId,
            RowNumber = command.RowNumber,
        };

        await _repository.InsertAsync(document, cancellationToken).ConfigureAwait(false);

        var lookup = await _lookup.ConsultAsync(command.TenantId, nit, cancellationToken).ConfigureAwait(false);

        if (lookup.Error is not null)
        {
            var code = lookup.Error == ErrorProviderNotFound
                ? ErrorProviderNotFound
                : ErrorProviderUnavailable;

            await _repository
                .MarkErrorAsync(command.TenantId, id, code, "nit", cancellationToken)
                .ConfigureAwait(false);

            return new GenerateRuesDocumentResult(
                code == ErrorProviderNotFound
                    ? GenerateRuesDocumentOutcome.ProviderNotFound
                    : GenerateRuesDocumentOutcome.ProviderUnavailable,
                id,
                StandaloneDocumentStatus.Error,
                code);
        }

        if (!lookup.Found)
        {
            // NIT sin coincidencia: fila en error con código identificable y NINGÚN archivo en storage.
            await _repository
                .MarkErrorAsync(command.TenantId, id, ErrorRuesNotFound, "nit", cancellationToken)
                .ConfigureAwait(false);

            return new GenerateRuesDocumentResult(
                GenerateRuesDocumentOutcome.RuesNotFound, id, StandaloneDocumentStatus.Error, ErrorRuesNotFound);
        }

        // Snapshot congelado { queriedAt, fields } — patrón ADR-0037. Se escribe ANTES de renderizar:
        // es la evidencia de qué datos produjeron este PDF.
        var fields = new JsonObject();
        foreach (var field in lookup.Fields)
        {
            fields[field.Key] = field.Value;
        }

        var snapshot = new JsonObject
        {
            ["queriedAt"] = lookup.QueriedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["fields"] = fields,
        }.ToJsonString();

        await _repository
            .SaveRuesSnapshotAsync(command.TenantId, id, snapshot, cancellationToken)
            .ConfigureAwait(false);

        var rendered = _renderer.Render(lookup.Fields, ReferenceNumberFor(id));

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

        return new GenerateRuesDocumentResult(
            GenerateRuesDocumentOutcome.Generated, id, StandaloneDocumentStatus.Generated, null);
    }

    /// <summary>
    /// Referencia sintética del documento standalone. El generador del expediente la usa SOLO para
    /// nombrar el archivo; aquí no hay radicado porque no hay trámite.
    /// </summary>
    public static string ReferenceNumberFor(Guid id) => $"GD-{id.ToString("N")[..8]}";
}
