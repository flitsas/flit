using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Domain.Derivacion;

namespace Flit.Diagnostico.Api.Modulos.Derivacion;

/// <summary>
/// Una operación «Crear trámite» (<c>diagnostico.derivacion_operaciones</c>, Punto 4, diseño §4, ADR-0084): individual o masiva,
/// de un lote y un usuario (el autor de los borradores, 4.6), con el tipo de trámite resuelto y los contadores de sus ítems.
/// Clase (a) de ADR-0072: de la empresa, sin borrado, <c>xmin</c> como versión. Cuelga del lote con la FK compuesta. El incremento
/// 3 (masivo) la reutiliza tal cual con <see cref="TipoOperacionDerivacion.Masiva"/>.
/// </summary>
public sealed class OperacionDerivacion : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }

    /// <summary>Lote del que salen los vehículos.</summary>
    public Guid DiagnosticoId { get; init; }

    public TipoOperacionDerivacion Tipo { get; init; }

    /// <summary>Tipo de trámite de los borradores: el del lote (Con análisis) o el elegido (Simple).</summary>
    public required string TipoTramiteCodigo { get; init; }

    /// <summary>Usuario que lanzó la operación (claim <c>sub</c>); queda como autor de los borradores.</summary>
    public Guid UsuarioId { get; init; }

    public EstadoOperacionDerivacion Estado { get; set; }

    /// <summary>Ítems de la operación (mayor que 0).</summary>
    public int Total { get; set; }

    public int Creadas { get; set; }

    public int Omitidas { get; set; }

    public int Fallidas { get; set; }

    public DateTimeOffset CreadaEn { get; init; }

    /// <summary>Instante en que cerró su último ítem; null mientras está EN_CURSO.</summary>
    public DateTimeOffset? TerminadaEn { get; set; }

    /// <summary>Token de concurrencia (columna de sistema <c>xmin</c>).</summary>
    public uint Xmin { get; set; }

    /// <summary>Operación individual (una placa, síncrona) EN_CURSO con un ítem.</summary>
    public static OperacionDerivacion Individual(Guid tenant, Guid diagnostico, string tipoTramite, Guid usuario, DateTimeOffset ahora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tipoTramite);
        return new OperacionDerivacion
        {
            TenantId = tenant,
            DiagnosticoId = diagnostico,
            Tipo = TipoOperacionDerivacion.Individual,
            TipoTramiteCodigo = tipoTramite,
            UsuarioId = usuario,
            Estado = EstadoOperacionDerivacion.EnCurso,
            Total = 1,
            CreadaEn = ahora,
        };
    }

    /// <summary>Operación masiva (encolada) EN_CURSO con <paramref name="total"/> ítems.</summary>
    public static OperacionDerivacion Masiva(Guid tenant, Guid diagnostico, string tipoTramite, Guid usuario, int total, DateTimeOffset ahora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tipoTramite);
        ArgumentOutOfRangeException.ThrowIfLessThan(total, 1);
        return new OperacionDerivacion
        {
            TenantId = tenant,
            DiagnosticoId = diagnostico,
            Tipo = TipoOperacionDerivacion.Masiva,
            TipoTramiteCodigo = tipoTramite,
            UsuarioId = usuario,
            Estado = EstadoOperacionDerivacion.EnCurso,
            Total = total,
            CreadaEn = ahora,
        };
    }

    /// <summary>Ítems que aún no cerraron (PENDIENTE o ENVIADO).</summary>
    public int Pendientes => Math.Max(0, Total - Creadas - Omitidas - Fallidas);

    /// <summary>TERMINADA si ya cerraron todos sus ítems.</summary>
    public void TerminarSiCompleta(DateTimeOffset ahora)
    {
        if (Pendientes == 0 && Estado != EstadoOperacionDerivacion.Terminada)
        {
            Estado = EstadoOperacionDerivacion.Terminada;
            TerminadaEn = ahora;
        }
    }
}
