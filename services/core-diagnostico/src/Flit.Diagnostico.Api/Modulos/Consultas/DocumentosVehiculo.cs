using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Documentos de persona que trae el RUNT del vehículo (HU #13730, F03-C5, D-02, R-3, plan §9.3): el del propietario y, si la
/// respuesta trae locatario, el del locatario, cifrados con <see cref="IProtectorDocumentoPersona"/> antes de tocar la entidad.
/// Solo se guardan tipo y número cifrado: el RUNT normalizado ya no trae nombres (<see cref="MapperIntempo"/>). La precedencia
/// del locatario es la de <see cref="ResolucionLocatario"/>: el del proveedor reemplaza al declarado con origen
/// <c>PROVEEDOR</c> y el declarado pasa, tal como estaba cifrado, a <c>locatario_declarado_doc_*</c> (respaldo, HU #13730b);
/// si no viene, se conserva el que había y el respaldo queda vacío. En estas columnas (D2) va el primero que informa el proveedor;
/// TODAS las personas van a <c>personas_vehiculo</c> (<see cref="Personas"/>, HU #13790). Nada de esto se loguea ni sale en
/// excepciones.
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

    /// <summary>
    /// Todas las personas del vehículo que trae el RUNT (HU #13790, F05-C3, decisión Q1), cifradas, para la transacción que cierra
    /// la celda: cada propietario y cada locatario del proveedor (<see cref="VehiculoRunt.Locatarios"/> ya es la unión de
    /// <c>lessee[]</c> y <c>locatarios[]</c>; aquí no se repite el mismo tipo y número), con orden 1, 2, 3… en el orden del
    /// proveedor. Si el proveedor no trae locatario y el vehículo tiene uno declarado por el cliente, ese entra como LOCATARIO 1
    /// con origen <c>DECLARADO_CLIENTE</c> y los mismos bytes cifrados de su columna. Se llama DESPUÉS de <see cref="Aplicar"/>.
    /// </summary>
    public static IReadOnlyList<PersonaVehiculo> Personas(VehiculoDiagnostico vehiculo, VehiculoRunt runt, IProtectorDocumentoPersona protector, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(vehiculo);
        ArgumentNullException.ThrowIfNull(runt);
        ArgumentNullException.ThrowIfNull(protector);

        var personas = new List<PersonaVehiculo>();
        var propietarios = SinRepetir(runt.Propietarios.Select(p => (p.TipoDocumento, p.NumeroDocumento)));
        personas.AddRange(propietarios.Select((p, i) =>
            Nueva(vehiculo, RolPersona.Propietario, i + 1, p.Tipo, protector.Proteger(p.Numero), OrigenDocumento.Proveedor, ahora)));

        var locatarios = SinRepetir(runt.Locatarios.Select(l => (l.TipoDocumento, l.NumeroDocumento)));
        personas.AddRange(locatarios.Select((l, i) =>
            Nueva(vehiculo, RolPersona.Locatario, i + 1, l.Tipo, protector.Proteger(l.Numero), OrigenDocumento.Proveedor, ahora)));

        if (locatarios.Count == 0 && Declarado(vehiculo, ahora) is { } declarado)
            personas.Add(declarado);
        return personas;
    }

    /// <summary>
    /// Las personas del vehículo <paramref name="destino"/> copiadas de las de <paramref name="origen"/> al reutilizar su RUNT
    /// del mismo día (HU #13790, AC3, F05-C11): mismo rol, orden, tipo, origen y bytes cifrados, sin descifrar. Como en
    /// <see cref="Copiar"/>, el locatario declarado del origen nunca se copia: si el origen no tiene locatario del proveedor, entra
    /// el declarado del propio destino. Si el origen se cerró antes de existir la tabla (D2, sin filas), se toman sus columnas
    /// <c>propietario_doc_*</c> y el locatario del proveedor. Se llama DESPUÉS de <see cref="Copiar"/>.
    /// </summary>
    public static IReadOnlyList<PersonaVehiculo> CopiarPersonas(
        VehiculoDiagnostico destino, VehiculoDiagnostico origen, IReadOnlyList<PersonaVehiculo> deOrigen, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(origen);
        ArgumentNullException.ThrowIfNull(deOrigen);

        var personas = new List<PersonaVehiculo>();
        if (deOrigen.Count > 0)
        {
            personas.AddRange(deOrigen
                .Where(p => p.Origen == OrigenDocumento.Proveedor)
                .OrderBy(p => p.Rol).ThenBy(p => p.Orden)
                .Select(p => Nueva(destino, p.Rol, p.Orden, p.DocTipo, (byte[])p.DocCifrado.Clone(), p.Origen, ahora)));
        }
        else
        {
            if (origen.PropietarioDocTipo is { } tipo && origen.PropietarioDocCifrado is { } cifrado)
                personas.Add(Nueva(destino, RolPersona.Propietario, 1, tipo, (byte[])cifrado.Clone(), OrigenDocumento.Proveedor, ahora));
            if (origen.LocatarioDocOrigen == OrigenDocumento.Proveedor && origen.LocatarioDocTipo is { } tipoLocatario
                && origen.LocatarioDocCifrado is { } cifradoLocatario)
            {
                personas.Add(Nueva(destino, RolPersona.Locatario, 1, tipoLocatario, (byte[])cifradoLocatario.Clone(), OrigenDocumento.Proveedor, ahora));
            }
        }

        if (!personas.Exists(p => p.Rol == RolPersona.Locatario) && Declarado(destino, ahora) is { } declarado)
            personas.Add(declarado);
        return personas;
    }

    /// <summary>
    /// Las personas del vehículo ordenadas por rol y orden (HU #13790), con la empresa EXPLÍCITA además del filtro global (el
    /// procesador corre fuera de una petición). Es la lectura de las celdas por persona (F05-H3/H5) y del estimado (F05-H7); con
    /// <paramref name="rol"/> devuelve solo ese rol. Los documentos vienen cifrados: se descifran con
    /// <see cref="IProtectorDocumentoPersona.Desproteger"/> justo antes de consultar y nunca se loguean.
    /// </summary>
    public static Task<List<PersonaVehiculo>> PersonasDelVehiculoAsync(
        DiagnosticoDb db, Guid empresa, Guid vehiculoId, CancellationToken ct, RolPersona? rol = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Set<PersonaVehiculo>().AsNoTracking()
            .Where(p => p.TenantId == empresa && p.VehiculoId == vehiculoId && (rol == null || p.Rol == rol))
            .OrderBy(p => p.Rol).ThenBy(p => p.Orden)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Agrega <paramref name="personas"/> al contexto de cierre solo si el vehículo aún no tiene ninguna: se escriben una sola vez
    /// (la RUNT solo vuelve a correr desde ERROR, y en ERROR no se escriben).
    /// </summary>
    public static async Task RegistrarPersonasAsync(DiagnosticoDb db, VehiculoDiagnostico vehiculo, IReadOnlyList<PersonaVehiculo> personas, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(vehiculo);
        ArgumentNullException.ThrowIfNull(personas);
        if (personas.Count == 0
            || await db.Set<PersonaVehiculo>().AnyAsync(p => p.TenantId == vehiculo.TenantId && p.VehiculoId == vehiculo.Id, ct).ConfigureAwait(false))
        {
            return;
        }

        db.AddRange(personas);
    }

    private static List<(string Tipo, string Numero)> SinRepetir(IEnumerable<(string Tipo, string Numero)> documentos) =>
        documentos.Select(d => (Tipo: d.Tipo.Trim(), Numero: d.Numero.Trim())).Distinct().ToList();

    /// <summary>El locatario declarado vigente del vehículo como persona (mismos bytes cifrados); null si no hay o no es el declarado.</summary>
    private static PersonaVehiculo? Declarado(VehiculoDiagnostico vehiculo, DateTimeOffset ahora) =>
        vehiculo.LocatarioDocOrigen == OrigenDocumento.DeclaradoCliente && vehiculo.LocatarioDocTipo is { } tipo && vehiculo.LocatarioDocCifrado is { } cifrado
            ? Nueva(vehiculo, RolPersona.Locatario, 1, tipo, (byte[])cifrado.Clone(), OrigenDocumento.DeclaradoCliente, ahora)
            : null;

    private static PersonaVehiculo Nueva(
        VehiculoDiagnostico vehiculo, RolPersona rol, int orden, string tipo, byte[] cifrado, OrigenDocumento origen, DateTimeOffset ahora) => new()
        {
            TenantId = vehiculo.TenantId,
            VehiculoId = vehiculo.Id,
            Rol = rol,
            Orden = orden,
            DocTipo = tipo,
            DocCifrado = cifrado,
            Origen = origen,
            CreadoEn = ahora,
        };

    /// <summary>Verdadero si los tipos de documento caben en sus columnas (un código desconocido del proveedor puede no caber).</summary>
    public static bool TiposValidos(VehiculoRunt runt)
    {
        ArgumentNullException.ThrowIfNull(runt);
        // HU #13790: se guardan todas las personas (personas_vehiculo.doc_tipo tiene el mismo largo), no solo la primera.
        return runt.Propietarios.All(p => p.TipoDocumento.Trim().Length <= LargoTipo)
            && runt.Locatarios.All(l => l.TipoDocumento.Trim().Length <= LargoTipo);
    }
}
