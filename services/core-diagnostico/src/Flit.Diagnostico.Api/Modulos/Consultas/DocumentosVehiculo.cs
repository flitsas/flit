using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Documentos de persona que trae el RUNT del vehículo (HU #13730, F03-C5, D-02, R-3, plan §9.3): el del propietario y, si la
/// respuesta trae locatario, el del locatario, cifrados con <see cref="IProtectorDocumentoPersona"/> antes de tocar la entidad.
/// Solo se guardan tipo y número cifrado: el RUNT normalizado ya no trae nombres (<see cref="MapperIntempo"/>). La precedencia
/// del locatario es la de <see cref="ResolucionLocatario"/>: el del proveedor reemplaza al declarado con origen
/// <c>PROVEEDOR</c> y el declarado pasa, tal como estaba cifrado, a <c>locatario_declarado_doc_*</c> (respaldo, HU #13730b);
/// si no viene, se conserva el que había y el respaldo queda vacío. Con varios propietarios o locatarios se toma el primero que
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
        if (locatario is not null)
            ReemplazarLocatario(vehiculo, locatario.TipoDocumento, protector.Proteger(locatario.NumeroDocumento));
    }

    /// <summary>
    /// Copia al vehículo <paramref name="destino"/> los documentos que el RUNT dejó en <paramref name="origen"/> (HU #13739,
    /// reutilización del mismo día, D-10): el del propietario y, solo si en el origen el locatario es del proveedor, el del
    /// locatario, con la MISMA precedencia y el mismo respaldo del declarado que <see cref="Aplicar"/>. Copia los bytes cifrados
    /// sin descifrarlos: el propósito de la protección no depende del vehículo ni de la empresa, así que el cifrado vale tal cual.
    /// El declarado del origen nunca se copia: es un dato que capturó el cliente para ESE vehículo, no del RUNT.
    /// </summary>
    public static void Copiar(VehiculoDiagnostico destino, VehiculoDiagnostico origen)
    {
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(origen);

        if (origen.PropietarioDocCifrado is { } propietario)
        {
            destino.PropietarioDocTipo = origen.PropietarioDocTipo;
            destino.PropietarioDocCifrado = (byte[])propietario.Clone();
        }

        if (origen.LocatarioDocOrigen == OrigenDocumento.Proveedor && origen.LocatarioDocTipo is { } tipo && origen.LocatarioDocCifrado is { } locatario)
            ReemplazarLocatario(destino, tipo, (byte[])locatario.Clone());
    }

    /// <summary>El locatario que trae el proveedor reemplaza al guardado (<see cref="ResolucionLocatario"/>); el declarado pasa al respaldo.</summary>
    private static void ReemplazarLocatario(VehiculoDiagnostico vehiculo, string tipo, byte[] cifrado)
    {
        var decision = ResolucionLocatario.Resolver(vehiculo.LocatarioDocOrigen, proveedorLoTrae: true);
        if (!decision.Reemplazar)
            return;

        // El declarado no se pierde (HU #13730b): sus bytes ya cifrados pasan al respaldo antes de reemplazarlo.
        if (ResolucionLocatario.RespaldarDeclarado(vehiculo.LocatarioDocOrigen, proveedorLoTrae: true))
        {
            vehiculo.LocatarioDeclaradoDocTipo = vehiculo.LocatarioDocTipo;
            vehiculo.LocatarioDeclaradoDocCifrado = vehiculo.LocatarioDocCifrado;
        }

        vehiculo.LocatarioDocTipo = tipo;
        vehiculo.LocatarioDocCifrado = cifrado;
        vehiculo.LocatarioDocOrigen = decision.Origen;
    }

    /// <summary>Verdadero si los tipos de documento caben en sus columnas (un código desconocido del proveedor puede no caber).</summary>
    public static bool TiposValidos(VehiculoRunt runt)
    {
        ArgumentNullException.ThrowIfNull(runt);
        return runt.Propietarios.Take(1).All(p => p.TipoDocumento.Length <= LargoTipo)
            && runt.Locatarios.Take(1).All(l => l.TipoDocumento.Length <= LargoTipo);
    }
}
