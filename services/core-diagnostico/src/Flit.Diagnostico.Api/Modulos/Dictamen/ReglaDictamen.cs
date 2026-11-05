namespace Flit.Diagnostico.Api.Modulos.Dictamen;

/// <summary>
/// Fila de <c>diagnostico.reglas_dictamen</c> (corte 4, ADR-0077). Catálogo global de FLIT, sin empresa (excepción de
/// ADR-0072): no implementa <c>IEntidadDeEmpresa</c> y no lleva <c>tenant_id</c>, actor ni borrado. El identificador lo
/// calcula el proyector (UUID v5). <see cref="Xmin"/> es el token de concurrencia; esta entidad no tiene columna
/// <c>version</c>. <see cref="Condicion"/> repite el código de la fila y no es un motor de reglas.
/// </summary>
public sealed class ReglaDictamen
{
    /// <summary>UUID v5 estable de la clave del catálogo (<c>ValueGeneratedNever</c>).</summary>
    public Guid Id { get; init; }

    /// <summary>Versión del catálogo. La v1 es 1.</summary>
    public int CatalogoVersion { get; init; }

    /// <summary>Código de trámite al que aplica la regla.</summary>
    public required string TipoTramiteCodigo { get; init; }

    /// <summary>Código de la regla. La condición guarda el mismo valor.</summary>
    public required string Codigo { get; init; }

    /// <summary>JSON <c>{"codigo":"&lt;codigo&gt;"}</c>. No describe la lógica: la evalúa el código.</summary>
    public required string Condicion { get; init; }

    /// <summary><c>BLOQUEA</c> o <c>ADVIERTE</c>.</summary>
    public required string Efecto { get; init; }

    /// <summary>Texto de <c>CatalogoReglasDictamen</c> mientras la redacción automática no está lista.</summary>
    public required string PlantillaTexto { get; init; }

    /// <summary>La v1 nace vigente. El único update previsto es pasarla a false cuando exista otra versión.</summary>
    public bool Vigente { get; set; }

    /// <summary>Token de concurrencia (columna de sistema <c>xmin</c>).</summary>
    public uint Xmin { get; set; }
}
