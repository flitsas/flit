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

    /// <summary>FLIT tomó saneamientos del solicitante (HU #14576, ADR-0087). <c>datos</c>: <c>{"cantidad": n}</c>.</summary>
    public const string SaneamientoTomado = "SANEAMIENTO_TOMADO";

    /// <summary>FLIT cerró saneamientos. <c>datos</c>: <c>{"cantidad": n, "resultado": "SANEADO" | "NO_PROCEDE"}</c>.</summary>
    public const string SaneamientoCerrado = "SANEAMIENTO_CERRADO";

    /// <summary>FLIT reabrió un saneamiento. <c>datos</c>: <c>{"cantidad": 1}</c>.</summary>
    public const string SaneamientoReabierto = "SANEAMIENTO_REABIERTO";

    /// <summary>FLIT anuló un saneamiento. <c>datos</c>: <c>{"cantidad": 1}</c>.</summary>
    public const string SaneamientoAnulado = "SANEAMIENTO_ANULADO";

    /// <summary>
    /// Tipos v1 (D8: sin ajuste del dictamen) y los cuatro del saneamiento al final (HU #14576): sin placa ni motivo (SP-23), y su
    /// <see cref="ReferenciaId"/> es el id del evento de <c>saneamiento_historial</c> de esa acción (C18, SP-22).
    /// </summary>
    public static readonly IReadOnlyList<string> Tipos =
        [ZipEvidenciasListo, ZipEvidenciasFallido, DerivacionTerminada, LoteTerminado, SaneamientoTomado, SaneamientoCerrado, SaneamientoReabierto, SaneamientoAnulado];

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
