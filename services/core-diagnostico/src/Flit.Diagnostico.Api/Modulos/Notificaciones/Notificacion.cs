using Flit.Diagnostico.Api.Modulos.Comun;

namespace Flit.Diagnostico.Api.Modulos.Notificaciones;

/// <summary>
/// Aviso de la campana de Diagnóstico para un usuario (<c>diagnostico.notificaciones</c>, HU #14563 B8, ADR-0086). Clase (a) de
/// ADR-0072 con excepción de retención: se purga a los 90 días. No guarda texto ni PII; título y mensaje se arman al leer.
/// </summary>
public sealed class Notificacion : IEntidadDeEmpresa
{
    public const string ZipEvidenciasListo = "ZIP_EVIDENCIAS_LISTO";
    public const string ZipEvidenciasFallido = "ZIP_EVIDENCIAS_FALLIDO";
    public const string DerivacionTerminada = "DERIVACION_TERMINADA";
    public const string LoteTerminado = "LOTE_TERMINADO";

    /// <summary>Tipos v1 (D8: sin ajuste del dictamen).</summary>
    public static readonly IReadOnlyList<string> Tipos = [ZipEvidenciasListo, ZipEvidenciasFallido, DerivacionTerminada, LoteTerminado];

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }

    /// <summary>Destinatario (claim sub).</summary>
    public Guid UsuarioId { get; init; }

    public string Tipo { get; init; } = string.Empty;

    public Guid DiagnosticoId { get; init; }

    /// <summary>Id del hecho según el tipo; polimórfico, sin FK.</summary>
    public Guid ReferenciaId { get; init; }

    /// <summary>Solo contadores y códigos en JSON plano.</summary>
    public string Datos { get; init; } = "{}";

    public DateTimeOffset CreadaEn { get; init; }

    public DateTimeOffset? LeidaEn { get; set; }

    public uint Xmin { get; set; }
}
