using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Vehiculos;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Una persona del vehículo (<c>diagnostico.personas_vehiculo</c>, HU #13790, F05-C3, decisión Q1): cada propietario y cada
/// locatario que trae el RUNT, con el tipo de documento y el número cifrado (<c>@pii:high</c>, protector de HU #13751), su
/// rol, su orden dentro del rol (1, 2, 3… en el orden del proveedor) y de dónde salió. Nunca el nombre. Se escriben una sola
/// vez, en la transacción que cierra la celda RUNT (<see cref="DocumentosVehiculo.Personas"/>), o se copian tal cual del
/// vehículo de origen al reutilizar el RUNT del mismo día (<see cref="ReutilizacionMismoDia"/>). Las celdas de persona
/// (F05-H3/H5) consultan una vez por fila y ligan cada intento con <c>intentos_consulta.persona_id</c>. Las columnas
/// <c>propietario_doc_*</c>/<c>locatario_doc_*</c> de <c>vehiculos</c> se conservan (D2 y respaldo del declarado).
/// </summary>
/// <example>
/// <code>
/// var propietarios = await db.Set&lt;PersonaVehiculo&gt;().AsNoTracking()
///     .Where(p =&gt; p.VehiculoId == vehiculoId &amp;&amp; p.Rol == RolPersona.Propietario)
///     .OrderBy(p =&gt; p.Orden).ToListAsync(ct);
/// var documento = protector.Desproteger(propietarios[0].DocCifrado);
/// </code>
/// </example>
public sealed class PersonaVehiculo : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Empresa dueña del vehículo (lo exige la FK compuesta).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Vehículo al que pertenece la persona.</summary>
    public Guid VehiculoId { get; init; }

    public RolPersona Rol { get; init; }

    /// <summary>Posición dentro del rol, desde 1, en el orden en que la informa el proveedor.</summary>
    public int Orden { get; init; }

    /// <summary>Tipo de documento (CC, CE, NIT, PAS, TI…).</summary>
    public required string DocTipo { get; init; }

    /// <summary>Número de documento cifrado con <c>IProtectorDocumentoPersona</c>; nunca en claro.</summary>
    public required byte[] DocCifrado { get; init; }

    /// <summary><c>PROVEEDOR</c> (RUNT) o <c>DECLARADO_CLIENTE</c> (locatario declarado sin locatario del proveedor).</summary>
    public OrigenDocumento Origen { get; init; }

    public DateTimeOffset CreadoEn { get; init; }

    /// <summary>Concurrencia optimista con la columna de sistema <c>xmin</c>.</summary>
    public uint Version { get; set; }
}
