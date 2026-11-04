using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Pii;
using Microsoft.EntityFrameworkCore;

namespace Flit.PersonaNatural.Api.Consultas;

public enum EstadoDetalle
{
    Ok,
    SolicitudNoEncontrada,

    /// <summary>La solicitud todavía no tiene placa ni VIN: no hay vehículo del cual dar detalle.</summary>
    SinVehiculo,

    /// <summary>Ningún actor del trámite (o el pedido) tiene la identidad validada en esta solicitud (el portal no reutiliza validaciones ni usa vigencia en días). NO se llamó a FLIT 2.</summary>
    IdentidadRequerida,
}

public sealed record ResultadoDetalle(EstadoDetalle Estado, VehiculoDetalle? Detalle = null);

/// <summary>
/// Detalle ampliado del vehículo del trámite (HU #14041, AC3 y AC4; nivel N2 del diseño §8). Solo se sirve si un actor de la
/// solicitud tiene la identidad validada: <c>persona_solicitante.validacion_estado</c> la espeja de FLIT 2 (HU C2) y aquí se
/// lee tal cual. El orden importa: primero la identidad y solo después, si pasa, la llamada a core-consultas; sin identidad
/// no se gasta ni una consulta.
/// </summary>
public sealed class ConsultaDetalleService(PersonaNaturalDb db, PersonaService personas, IConsultasRunt runt, MedicionConsultasService medicion)
{
    /// <summary>Estados que cuentan como identidad validada: «validada» es el espejo del BFF y «aprobada» el estado de FLIT 2 (diseño §5).</summary>
    private static readonly string[] EstadosValidados = [Identidad.EstadosIdentidad.Aprobada];

    private static readonly string[] PartesDelVehiculo = ["vendedor", "propietario", "titular"];

    public const string ActorAuditoria = "consulta_detalle";

    /// <summary>
    /// <paramref name="parte"/> es el rol del actor que respalda la consulta; sin él vale cualquiera con identidad validada,
    /// con preferencia por quien es dueño del vehículo (vendedor, propietario o titular).
    /// </summary>
    /// <exception cref="ProveedorNoDisponibleException">core-consultas no respondió o el proveedor del RUNT estaba caído.</exception>
    public async Task<ResultadoDetalle> ConsultarAsync(Guid solicitudId, string? parte, CancellationToken ct)
    {
        var solicitud = await db.Set<SolicitudTramite>().AsNoTracking().Where(s => s.Id == solicitudId)
            .Select(s => new { s.Placa, s.Vin }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (solicitud is null)
            return new ResultadoDetalle(EstadoDetalle.SolicitudNoEncontrada);

        var parteNormalizada = string.IsNullOrWhiteSpace(parte) ? null : parte.Trim().ToLowerInvariant();
        var candidatos = await db.Set<PersonaSolicitante>().AsNoTracking()
            .Where(p => p.SolicitudId == solicitudId && EstadosValidados.Contains(p.ValidacionEstado))
            .Select(p => new { p.Parte, p.Ordinal }).ToListAsync(ct).ConfigureAwait(false);
        var actor = candidatos.Where(c => parteNormalizada is null || string.Equals(c.Parte, parteNormalizada, StringComparison.OrdinalIgnoreCase)).OrderBy(c => PartesDelVehiculo.Contains(c.Parte.ToLowerInvariant()) ? 0 : 1).ThenBy(c => c.Parte, StringComparer.Ordinal).ThenBy(c => c.Ordinal).FirstOrDefault();
        if (actor is null)
            return new ResultadoDetalle(EstadoDetalle.IdentidadRequerida);

        var vin = EntradaConsulta.Vin(solicitud.Vin);
        var placa = EntradaConsulta.Placa(solicitud.Placa);
        if (vin is null && placa is null)
            return new ResultadoDetalle(EstadoDetalle.SinVehiculo);

        // Por VIN no hace falta el propietario; por placa, el documento del actor validado (core-consultas lo exige). La
        // lectura descifra y queda en acceso_pii_log. Si ese actor no es el dueño, el RUNT no halla el vehículo y responde
        // existe = false: no se filtra nada de nadie.
        DocumentoConsulta? propietario = null;
        if (vin is null)
        {
            var datos = (await personas.LeerAsync(solicitudId, ActorAuditoria, ct).ConfigureAwait(false))
                .FirstOrDefault(d => d.Parte == actor.Parte && d.Ordinal == actor.Ordinal);
            propietario = datos is null ? null : EntradaConsulta.Documento(datos.TipoDocumento, datos.Documento);
            if (propietario is null)
                return new ResultadoDetalle(EstadoDetalle.IdentidadRequerida);
        }

        // La medición se cuenta en cuanto se va a llamar a core-consultas, salga bien o mal.
        try
        {
            var resultado = await runt.ConsultarVehiculoAsync(vin is null ? placa : null, vin, propietario, solicitudId.ToString(), ct).ConfigureAwait(false);
            if (ProyeccionMinima.ProveedorCaido(resultado))
                throw new ProveedorNoDisponibleException("El proveedor del RUNT no respondió.");

            return new ResultadoDetalle(EstadoDetalle.Ok, ProyeccionMinima.Detalle(resultado, placa));
        }
        finally
        {
            await medicion.RegistrarAsync(MedicionConsultasService.TipoVehiculoDetalle, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
