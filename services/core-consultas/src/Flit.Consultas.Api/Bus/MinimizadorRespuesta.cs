using Flit.Consultas.Grpc.V1;
using Google.Protobuf;

namespace Flit.Consultas.Api.Bus;

/// <summary>
/// Minimización de la vuelta (ADR-0075, U1 = b, Q2): antes de publicar <c>consultas.resultado.diagnostico</c> se vacían, con una
/// lista CERRADA por rpc, los campos de nombre, dirección y contacto del <c>*Response</c>, y el detalle libre del proveedor
/// (mensaje de error, respuesta cruda) que puede traer un recorte del cuerpo. Diagnóstico no los usa: sus mapeos leen solo placa,
/// VIN, tipo y número de documento, propiedad, datos técnicos, licencias, estado RUES, comparendos, avalúos y vigencias.
/// <list type="bullet">
/// <item>RUNT por Intempo: <c>nombreCompleto</c> y <c>direcciones</c> de los propietarios, <c>nombreRazonSocial</c> y
/// <c>direcciones</c> de sus locatarios, <c>nombreCompletoPropietario</c> y <c>direccion</c> del histórico, los nombres de
/// <c>owner[]</c> y <c>lessee[]</c>, el <c>acreedor</c> de las garantías a favor de y la <c>entidad</c> de las mobiliarias, y
/// <c>mensajeError</c> y <c>errores[].description</c>.</item>
/// <item><see cref="ResultadoConsulta"/> (RUNT persona, SIMIT, RUES, impuesto): los <see cref="CamposPersonales"/>, la respuesta
/// cruda y, en las matrículas mercantiles, razón social, dirección, ciudad y el nombre de los representantes legales.</item>
/// <item>Avalúos: no trae datos personales; sale igual.</item>
/// </list>
/// No modifica el mensaje recibido (trabaja sobre una copia). Un rpc sin regla, o una respuesta que no es la de su rpc, lanza
/// <see cref="ArgumentException"/>: lo que no tiene regla no se publica.
/// </summary>
/// <example>
/// <code>
/// var publicable = MinimizadorRespuesta.Minimizar(RpcConsultas.PorPlaca, respuestaIntempo);
/// </code>
/// </example>
internal static class MinimizadorRespuesta
{
    /// <summary>Claves de <see cref="Campo"/> con nombre, dirección o contacto que hoy llenan los proveedores (lista cerrada).</summary>
    internal static IReadOnlySet<string> CamposPersonales { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "person_full_name", "person_first_name", "person_second_name", "person_last_name", "person_first_last_name", "person_second_last_name",
        "owner_full_name", "vehicle_owner_name", "vehicle_owner_first_last_name", "vehicle_owner_second_last_name", "vehicle_owner_phone",
        "vehicle_owner_city", "vehicle_owner_address", "lessee_full_name", "lessee_phone", "lessee_email", "lessee_city", "lessee_address",
        "company_name", "rues_razon_social", "rues_direccion", "rues_email", "rues_representacion_legal", "rues_snapshot",
    };

    /// <summary>La respuesta de <paramref name="rpc"/> lista para publicar: copia sin los campos de la lista cerrada.</summary>
    public static IMessage Minimizar(string rpc, IMessage respuesta)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        switch (rpc, respuesta)
        {
            case (RpcConsultas.PorPlaca or RpcConsultas.PorVin, ConsultarVehiculoIntempoResponse r):
                var intempo = r.Clone();
                Intempo(intempo);
                return intempo;
            case (RpcConsultas.Conductor, ConsultarConductorResponse r):
                var conductor = r.Clone();
                Resultado(conductor.Resultado);
                return conductor;
            case (RpcConsultas.Multas, ConsultarMultasResponse r):
                var multas = r.Clone();
                Resultado(multas.Resultado);
                return multas;
            case (RpcConsultas.Rues, ConsultarRuesResponse r):
                var rues = r.Clone();
                Resultado(rues.Resultado);
                return rues;
            case (RpcConsultas.Impuesto, ConsultarImpuestoVehicularResponse r):
                var impuesto = r.Clone();
                Resultado(impuesto.Resultado);
                return impuesto;
            case (RpcConsultas.Avaluos, ConsultarAvaluosResponse r):
                return r.Clone();
            default:
                throw new ArgumentException($"Sin regla de minimización para {rpc} con {respuesta.Descriptor.Name}.", nameof(rpc));
        }
    }

    /// <summary>El <c>*Response</c> vacío de <paramref name="rpc"/> (el tipo que su regla acepta).</summary>
    public static IMessage RespuestaVacia(string rpc) => rpc switch
    {
        RpcConsultas.PorPlaca or RpcConsultas.PorVin => new ConsultarVehiculoIntempoResponse(),
        RpcConsultas.Conductor => new ConsultarConductorResponse(),
        RpcConsultas.Multas => new ConsultarMultasResponse(),
        RpcConsultas.Rues => new ConsultarRuesResponse(),
        RpcConsultas.Impuesto => new ConsultarImpuestoVehicularResponse(),
        RpcConsultas.Avaluos => new ConsultarAvaluosResponse(),
        _ => throw new ArgumentException($"Rpc desconocido: {rpc}.", nameof(rpc)),
    };

    private static void Intempo(ConsultarVehiculoIntempoResponse r)
    {
        r.ClearMensajeError();
        foreach (var error in r.Errores)
            error.Description = string.Empty;
        if (r.Vehiculo is not { } v)
            return;

        foreach (var propietario in v.PropietariosActuales)
        {
            propietario.NombreCompleto = string.Empty;
            propietario.Direcciones.Clear();
            foreach (var locatario in propietario.Locatarios)
            {
                locatario.NombreRazonSocial = string.Empty;
                locatario.Direcciones.Clear();
            }
        }

        foreach (var historico in v.HistoricoPropietarios)
        {
            historico.NombreCompletoPropietario = string.Empty;
            historico.Direccion = null;
        }

        foreach (var persona in v.Owner.Concat(v.Lessee))
        {
            persona.Name = string.Empty;
            persona.FirstName = string.Empty;
            persona.SecondName = string.Empty;
            persona.FirstLastName = string.Empty;
            persona.SecondLastName = string.Empty;
        }

        foreach (var garantia in v.GarantiasFavorDe)
            garantia.Acreedor = string.Empty;
        foreach (var garantia in v.GarantiasMobiliarias)
            garantia.Entidad = string.Empty;
    }

    private static void Resultado(ResultadoConsulta? r)
    {
        if (r is null)
            return;

        foreach (var campo in r.Campos.Where(c => CamposPersonales.Contains(c.Clave)).ToList())
            r.Campos.Remove(campo);
        r.RespuestaCruda = null;
        foreach (var matricula in r.Certificaciones?.MatriculasMercantiles ?? [])
        {
            matricula.RazonSocial = null;
            matricula.Direccion = null;
            matricula.Ciudad = null;
            foreach (var representante in matricula.RepresentantesLegales)
                representante.Nombre = null;
        }
    }
}
