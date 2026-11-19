using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Domain.Saneamiento;

namespace Flit.Diagnostico.Api.Modulos.Saneamiento;

/// <summary>
/// Saneamiento de un vehículo (<c>diagnostico.saneamientos</c>, HU #14576, ADR-0087, clase (a) de ADR-0072): la solicitud de la
/// compañía y la gestión de FLIT, sin dinero. Un solo no anulado por vehículo (<c>ux_saneamiento_vigente</c>). Sin borrado:
/// <see cref="EstadoSaneamiento.Anulado"/> es un estado. Lo escribe solo <c>EscritorSaneamiento</c>.
/// </summary>
public sealed class Saneamiento : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }

    public Guid DiagnosticoId { get; init; }

    public Guid VehiculoId { get; init; }

    public EstadoSaneamiento Estado { get; set; }

    /// <summary>Usuario de la compañía que marcó (claim sub).</summary>
    public Guid SolicitadoPor { get; init; }

    public DateTimeOffset SolicitadoEn { get; init; }

    /// <summary>Usuario FLIT de la primera toma; no cambia al reabrir.</summary>
    public Guid? TomadoPor { get; set; }

    public DateTimeOffset? TomadoEn { get; set; }

    public Guid? CerradoPor { get; set; }

    public DateTimeOffset? CerradoEn { get; set; }

    /// <summary>Motivo de NO_PROCEDE; se limpia al reabrir (queda en el historial).</summary>
    public string? MotivoCierre { get; set; }

    public Guid? AnuladoPor { get; set; }

    public DateTimeOffset? AnuladoEn { get; set; }

    /// <summary>True si anuló FLIT (con motivo); false si desmarcó la compañía o no está anulado.</summary>
    public bool AnuladoPorFlit { get; set; }

    public string? MotivoAnulacion { get; set; }

    /// <summary>Observación general de FLIT, visible al cliente.</summary>
    public string? ObservacionFlit { get; set; }

    /// <summary>Columna de sistema <c>xmin</c>: la <c>version</c> del contrato.</summary>
    public uint Xmin { get; set; }
}
