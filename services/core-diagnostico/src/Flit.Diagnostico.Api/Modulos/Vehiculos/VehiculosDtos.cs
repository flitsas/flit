using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Carga;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Vehiculos;

namespace Flit.Diagnostico.Api.Modulos.Vehiculos;

/// <summary>
/// Nombres del propietario actual declarado en los cuerpos de entrada (HU #14582, contrato C1): los nuevos
/// <c>tipo_documento_propietario</c>/<c>documento_propietario</c> y, como alias obsoleto durante 1.6.x, los de locatario. Si llegan
/// los dos nombres de un campo es 400 <see cref="CodigoCamposExcluyentes"/> con el nombre nuevo en <c>campo</c>; el mensaje no repite
/// el valor.
/// </summary>
public static class AliasPropietario
{
    public const string CodigoCamposExcluyentes = "CAMPOS_EXCLUYENTES";
    public const string CampoTipo = "tipo_documento_propietario";
    public const string CampoDocumento = "documento_propietario";
    public const string CampoTipoObsoleto = "tipo_documento_locatario";
    public const string CampoDocumentoObsoleto = "documento_locatario";

    /// <summary>El valor del nombre nuevo o, si no vino, el del alias; 400 si vinieron los dos.</summary>
    public static string? Elegir(bool traeNuevo, string? nuevo, bool traeObsoleto, string? obsoleto, string campo, string campoObsoleto)
    {
        if (traeNuevo && traeObsoleto)
        {
            throw new ReglaException(CodigoCamposExcluyentes,
                $"Envíe «{campo}» o el nombre anterior «{campoObsoleto}», no los dos.", StatusCodes.Status400BadRequest)
            {
                Campo = campo,
            };
        }

        return traeNuevo ? nuevo : obsoleto;
    }
}

/// <summary>
/// Una fila capturada de validar-filas (HU #13751): el identificador y, opcional, el documento del propietario actual (HU #14582). La
/// confirmación ya no recibe filas en JSON: reenvía el archivo (<see cref="LoteDesdeArchivo"/>). <c>fila</c> es el número de fila del archivo para devolverlo en el resumen. Es una clase (no un record)
/// para que ningún <c>ToString</c> automático lleve el documento a un log.
/// </summary>
public sealed class FilaCapturadaDto
{
    [JsonPropertyName("fila")]
    public int? Fila { get; init; }

    [JsonPropertyName("identificador")]
    public string? Identificador { get; init; }

    private bool _traeTipo;
    private bool _traeDocumento;
    private bool _traeTipoObsoleto;
    private bool _traeDocumentoObsoleto;

    [JsonPropertyName(AliasPropietario.CampoTipo)]
    public string? TipoDocumentoPropietario { get; init { field = value; _traeTipo = true; } }

    [JsonPropertyName(AliasPropietario.CampoDocumento)]
    public string? DocumentoPropietario { get; init { field = value; _traeDocumento = true; } }

    /// <summary>Alias obsoleto (contrato C1, se retira en 1.7.0).</summary>
    [JsonPropertyName(AliasPropietario.CampoTipoObsoleto)]
    public string? TipoDocumentoLocatarioObsoleto { get; init { field = value; _traeTipoObsoleto = true; } }

    /// <summary>Alias obsoleto (contrato C1, se retira en 1.7.0).</summary>
    [JsonPropertyName(AliasPropietario.CampoDocumentoObsoleto)]
    public string? DocumentoLocatarioObsoleto { get; init { field = value; _traeDocumentoObsoleto = true; } }

    /// <summary>Tipo y número declarados, del nombre nuevo o del alias; 400 <c>CAMPOS_EXCLUYENTES</c> si vinieron los dos.</summary>
    public (string? Tipo, string? Numero) Declarado() => (
        AliasPropietario.Elegir(_traeTipo, TipoDocumentoPropietario, _traeTipoObsoleto, TipoDocumentoLocatarioObsoleto, AliasPropietario.CampoTipo, AliasPropietario.CampoTipoObsoleto),
        AliasPropietario.Elegir(_traeDocumento, DocumentoPropietario, _traeDocumentoObsoleto, DocumentoLocatarioObsoleto, AliasPropietario.CampoDocumento, AliasPropietario.CampoDocumentoObsoleto));
}

/// <summary>Cuerpo de <c>POST /diagnosticos/{id}/carga/validar-filas</c>.</summary>
public sealed class FilasAValidarDto
{
    [JsonPropertyName("filas")]
    public IReadOnlyList<FilaCapturadaDto>? Filas { get; init; }
}

/// <summary>
/// Respuesta de la confirmación: los vehículos guardados (documento enmascarado), lo descartado y, solo con
/// <c>estimado_consultas=ANTES_DE_LANZAR</c> en la copia de configuración, el estimado por fuente.
/// </summary>
public sealed record ResultadoConfirmacionDto(
    [property: JsonPropertyName("diagnostico_id")] Guid DiagnosticoId,
    [property: JsonPropertyName("confirmados")] int Confirmados,
    [property: JsonPropertyName("invalidos")] int Invalidos,
    [property: JsonPropertyName("duplicados")] int Duplicados,
    [property: JsonPropertyName("limite_vehiculos_lote")] int LimiteVehiculosLote,
    [property: JsonPropertyName("vehiculos")] IReadOnlyList<VehiculoConfirmadoDto> Vehiculos,
    [property: JsonPropertyName("estimado_consultas"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<EstimadoFuenteDto>? EstimadoConsultas);

/// <summary>Un vehículo guardado del lote, sin documentos (HU #14582 C5, SP-4: el eco enmascarado queda solo en la vista previa).</summary>
public sealed record VehiculoConfirmadoDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("orden")] int Orden,
    [property: JsonPropertyName("identificador")] string Identificador,
    [property: JsonPropertyName("identificador_norm")] string IdentificadorNorm,
    [property: JsonPropertyName("tipo_identificador")] string TipoIdentificador);
