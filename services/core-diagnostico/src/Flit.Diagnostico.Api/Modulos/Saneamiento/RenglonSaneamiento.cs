using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Domain.Saneamiento;

namespace Flit.Diagnostico.Api.Modulos.Saneamiento;

/// <summary>
/// Renglón de un saneamiento por fuente (<c>diagnostico.saneamiento_renglones</c>, HU #14576, ADR-0087, clase (a)): hallazgo,
/// resultado, nota y PDF opcional de evidencia. Se quita con borrado lógico (<see cref="QuitadoEn"/>, SP-2): las lecturas
/// filtran los quitados. <see cref="EvidenciaReemplazos"/> cuenta los reemplazos del PDF (tope de 20, SP-19).
/// </summary>
public sealed class RenglonSaneamiento : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }

    public Guid SaneamientoId { get; init; }

    public FuenteSaneamiento Fuente { get; init; }

    /// <summary>Foto del hallazgo al tomar; null en OTRO.</summary>
    public HallazgoSaneamiento? Hallazgo { get; init; }

    /// <summary>Descripción de un renglón OTRO (3..200).</summary>
    public string? Descripcion { get; init; }

    public bool Automatico { get; init; }

    public ResultadoRenglonSaneamiento Resultado { get; set; }

    public string? Nota { get; set; }

    /// <summary>Referencia del archivo en el almacén; nunca se expone.</summary>
    public string? EvidenciaReferencia { get; set; }

    public string? EvidenciaNombre { get; set; }

    /// <summary>SHA-256 hex del PDF; nunca se expone.</summary>
    public string? EvidenciaSha256 { get; set; }

    public long? EvidenciaTamano { get; set; }

    public Guid? EvidenciaSubidaPor { get; set; }

    public DateTimeOffset? EvidenciaSubidaEn { get; set; }

    /// <summary>Veces que se reemplazó el PDF (0..20).</summary>
    public short EvidenciaReemplazos { get; set; }

    public Guid CreadoPor { get; init; }

    public DateTimeOffset CreadoEn { get; init; }

    public Guid? ActualizadoPor { get; set; }

    public DateTimeOffset? ActualizadoEn { get; set; }

    /// <summary>Borrado lógico (SP-2): instante en que FLIT quitó el renglón; null si sigue.</summary>
    public DateTimeOffset? QuitadoEn { get; set; }

    /// <summary>Columna de sistema <c>xmin</c>: la <c>version</c> del renglón en su PUT.</summary>
    public uint Xmin { get; set; }
}
