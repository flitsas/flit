using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Personas;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Documentos de persona que trae el RUNT del vehículo (HU #13730, F03-C5, D-02, R-3, plan §9.3): el del propietario y, si la
/// respuesta trae locatario, el del locatario, cifrados con <see cref="IProtectorDocumentoPersona"/> antes de tocar la entidad.
/// Solo se guardan tipo y número cifrado: el RUNT normalizado ya no trae nombres (<see cref="MapperIntempo"/>). La precedencia
/// del locatario es la de <see cref="ResolucionLocatario"/>: el del proveedor reemplaza al declarado con origen
/// <c>PROVEEDOR</c>; si no viene, se conserva el que había. Con varios propietarios o locatarios se toma el primero que
/// informa el proveedor. Nada de esto se loguea ni sale en excepciones.
/// </summary>
/// <example>
/// <code>
/// DocumentosVehiculo.Aplicar(vehiculo, runt, protector);
/// </code>
/// </example>
internal static class DocumentosVehiculo
{
    /// <summary>Largo máximo de <c>propietario_doc_tipo</c> y <c>locatario_doc_tipo</c>.</summary>
    public const int LargoTipo = 8;

    public static void Aplicar(VehiculoDiagnostico vehiculo, VehiculoRunt runt, IProtectorDocumentoPersona protector)
    {
        ArgumentNullException.ThrowIfNull(vehiculo);
        ArgumentNullException.ThrowIfNull(runt);
        ArgumentNullException.ThrowIfNull(protector);

        if (runt.Propietarios.Count > 0 && runt.Propietarios[0] is var propietario)
        {
            vehiculo.PropietarioDocTipo = propietario.TipoDocumento;
            vehiculo.PropietarioDocCifrado = protector.Proteger(propietario.NumeroDocumento);
        }

        var locatario = runt.Locatarios.Count > 0 ? runt.Locatarios[0] : null;
        var decision = ResolucionLocatario.Resolver(vehiculo.LocatarioDocOrigen, proveedorLoTrae: locatario is not null);
        if (decision.Reemplazar && locatario is not null)
        {
            vehiculo.LocatarioDocTipo = locatario.TipoDocumento;
            vehiculo.LocatarioDocCifrado = protector.Proteger(locatario.NumeroDocumento);
            vehiculo.LocatarioDocOrigen = decision.Origen;
        }
    }

    /// <summary>Verdadero si los tipos de documento caben en sus columnas (un código desconocido del proveedor puede no caber).</summary>
    public static bool TiposValidos(VehiculoRunt runt)
    {
        ArgumentNullException.ThrowIfNull(runt);
        return runt.Propietarios.Take(1).All(p => p.TipoDocumento.Length <= LargoTipo)
            && runt.Locatarios.Take(1).All(l => l.TipoDocumento.Length <= LargoTipo);
    }
}
