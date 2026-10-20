using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Modulos.Configuracion;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Contraventor;

/// <summary>Datos del conductor que se asignan.</summary>
public sealed record DatosConductor(string TipoDocumento, string NumeroDocumento, string Nombres, string? Correo, string? Telefono, string? Direccion,
    string? Ciudad, string? Sucursal, string? TipoContrato);

/// <summary>
/// Asignación del conductor (HU #13478; ADO #12978, §4.7). Uno solo activo por comparendo: reemplazarlo exige motivo y
/// deja la traza completa (RN-092). La asignación manual exige la declaración de tratamiento de datos [Excel P12]. Al
/// cambiar de conductor se revocan los enlaces del portal y los avisos vuelven a empezar para el nuevo (S-33).
/// </summary>
public sealed class ServicioContraventor(ComparendosDb db, Timeline timeline, IApiContratos api, Capacidades capacidades, PermisosOt permisos, Reloj reloj)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Asignar(Comparendo c, DatosConductor d, OrigenContraventor origen, Actor actor, string? motivo, bool declaracion)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(actor);
        if (!Enum.TryParse<TipoDocumento>(d.TipoDocumento, true, out var tipo) || !Enum.IsDefined(tipo))
            throw new ReglaException("TIPO_DOCUMENTO_INVALIDO", "Tipo de documento del conductor inválido.");
        var numero = (d.NumeroDocumento ?? string.Empty).Trim();
        if (numero.Length is 0 or > 20 || string.IsNullOrWhiteSpace(d.Nombres))
            throw new ReglaException("CONDUCTOR_INCOMPLETO", "El documento (hasta 20 caracteres) y el nombre del conductor son obligatorios.");
        var correo = string.IsNullOrWhiteSpace(d.Correo) ? null : Catalogos.CatalogosEndpoints.NormalizarCorreos([d.Correo]).Single();
        if (origen == OrigenContraventor.Manual && !declaracion)
            throw new ReglaException("DECLARACION_REQUERIDA", "Para asignar a mano debes declarar que la empresa tiene la autorización del titular para el tratamiento de sus datos (Ley 1581 de 2012).");

        var anterior = c.Contraventor;
        if (anterior is not null && string.IsNullOrWhiteSpace(motivo))
            throw new ReglaException("MOTIVO_REQUERIDO", "Escribe el motivo del cambio de conductor.");

        var ahora = reloj.Ahora;
        var nuevo = new Persistence.Modelo.Contraventor
        {
            ComparendoId = c.Id,
            TenantId = c.TenantId,
            TipoDocumento = tipo,
            NumeroDocumento = numero,
            Nombres = d.Nombres.Trim(),
            Correo = correo,
            Telefono = d.Telefono?.Trim(),
            Direccion = d.Direccion?.Trim(),
            Ciudad = d.Ciudad?.Trim(),
            Sucursal = d.Sucursal?.Trim(),
            TipoContrato = d.TipoContrato?.Trim(),
            Origen = origen,
            DeclaracionTratamientoDatos = declaracion,
            CreadoEn = ahora,
            CreadoPor = actor.UsuarioId,
        };
        db.ContraventorHistorial.Add(new ContraventorHistorial
        {
            Id = Guid.CreateVersion7(),
            TenantId = c.TenantId,
            ComparendoId = c.Id,
            DatosAnteriores = anterior is null ? null : JsonSerializer.Serialize(Foto(anterior), Json),
            DatosNuevos = JsonSerializer.Serialize(Foto(nuevo), Json),
            Origen = origen,
            Motivo = motivo?.Trim(),
            UsuarioId = actor.UsuarioId,
            ActorNombre = actor.Nombre,
            RegistradoEn = ahora,
        });

        if (anterior is null)
        {
            db.Contraventores.Add(nuevo);
            c.Contraventor = nuevo;
            timeline.Agregar(c, TipoEventoTimeline.ContraventorAsignado,
                $"Conductor asignado ({(origen == OrigenContraventor.Api ? "API" : "manual")}): {nuevo.Nombres}, {tipo} {Comun.HtmlCorreo.Enmascarar(numero)}.", actor);
        }
        else
        {
            (anterior.TipoDocumento, anterior.NumeroDocumento, anterior.Nombres, anterior.Correo, anterior.Telefono) = (nuevo.TipoDocumento, nuevo.NumeroDocumento, nuevo.Nombres, nuevo.Correo, nuevo.Telefono);
            (anterior.Direccion, anterior.Ciudad, anterior.Sucursal, anterior.TipoContrato, anterior.Origen) = (nuevo.Direccion, nuevo.Ciudad, nuevo.Sucursal, nuevo.TipoContrato, origen);
            (anterior.DeclaracionTratamientoDatos, anterior.ActualizadoEn, anterior.ActualizadoPor) = (declaracion, ahora, actor.UsuarioId);
            c.EstadoNotificacionConductor = EstadoNotificacionConductor.SinNotificar;
            foreach (var enlace in db.PortalEnlaces.Where(p => p.ComparendoId == c.Id && p.RevocadoEn == null))
                (enlace.RevocadoEn, enlace.MotivoRevocacion) = (ahora, "CONDUCTOR_REEMPLAZADO");
            timeline.Agregar(c, TipoEventoTimeline.ContraventorReemplazado, $"Conductor reemplazado por {nuevo.Nombres}. Motivo: {motivo!.Trim()}", actor);
        }

        // Documentos del conductor anterior dejan de estar vigentes.
        if (anterior is not null)
        {
            foreach (var doc in db.DocumentosSoporte.Where(x => x.ComparendoId == c.Id && x.Vigente && (x.Tipo == TipoDocumentoSoporte.Cedula || x.Tipo == TipoDocumentoSoporte.Licencia)))
                doc.Vigente = false;
        }

        c.Gestionado = true;
    }

    /// <summary>
    /// Consulta la API de contratos (§4.7). Si encuentra conductor lo asigna con origen API; si ya había uno, solo lo
    /// reemplaza con <paramref name="confirmarReemplazo"/>. Cuenta los intentos fallidos (máximo 3 automáticos, S-35).
    /// </summary>
    public async Task<RespuestaContrato> ConsultarAsync(Comparendo c, Actor actor, bool confirmarReemplazo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        var config = await capacidades.ExigirAsync(c.TenantId, Capacidad.ConsultaContraventor, ct).ConfigureAwait(false);
        if (config.TipoIntegracionConductor != TipoIntegracionConductor.ApiRenting)
            throw new ReglaException("SIN_INTEGRACION", "Tu empresa asigna el conductor a mano: no tiene una API de contratos configurada.");
        if (!ConsultaPorApi.DeNitMarcado(c, await ConsultaPorApi.MarcadosAsync(db, c.TenantId, ct).ConfigureAwait(false)))
            throw new ReglaException("NIT_SIN_API", "La API de contratos no aplica para el propietario de este comparendo: asigna el conductor a mano.");
        if (c.OrganismoTransitoId is { } ot && !(await permisos.DeAsync(c.TenantId, ot, null, ct).ConfigureAwait(false)).Consulta)
            throw new ReglaException("OT_BLOQUEADO", "La consulta de contraventor está bloqueada para este organismo de tránsito.");
        if (c.Placa is null || (c.FechaImposicion ?? c.FechaNotificacion) is not { } fecha)
            throw new ReglaException("SIN_DATOS_CONSULTA", "El comparendo no tiene placa o fecha para consultar el contrato.");

        var r = await api.ConsultarAsync(c.TenantId, c.Placa, fecha, ct).ConfigureAwait(false);
        switch (r.Resultado)
        {
            case ResultadoContrato.Encontrado when r.Conductor is { } k:
                if (c.Contraventor is not null && !confirmarReemplazo)
                    return r;
                Asignar(c, new DatosConductor(k.TipoDocumento.ToString(), k.NumeroDocumento, k.Nombres, k.Correo, k.Telefono, k.Direccion, k.Ciudad, k.Sucursal, k.TipoContrato),
                    OrigenContraventor.Api, actor, c.Contraventor is null ? null : "Reemplazo confirmado con la consulta a la API de contratos.", declaracion: false);
                c.EstadoConsultaContraventor = EstadoConsultaContraventor.Encontrado;
                c.ErrorConsultaContraventor = null;
                break;
            case ResultadoContrato.NoEncontrado:
                c.EstadoConsultaContraventor = EstadoConsultaContraventor.NoEncontrado;
                c.ErrorConsultaContraventor = null;
                break;
            default:
                c.EstadoConsultaContraventor = EstadoConsultaContraventor.Error;
                c.IntentosConsultaContraventor++;
                var error = string.Join("\n", new[] { r.Mensaje, r.CuerpoCrudo }.Where(x => !string.IsNullOrWhiteSpace(x)));
                c.ErrorConsultaContraventor = error.Length > 4000 ? error[..4000] : error;
                break;
        }

        timeline.Agregar(c, TipoEventoTimeline.ContraventorConsultado, r.Resultado switch
        {
            ResultadoContrato.Encontrado => "La API de contratos encontró el conductor.",
            ResultadoContrato.NoEncontrado => "La API de contratos no encontró conductor para la placa y la fecha: hay que asignarlo a mano.",
            _ => $"La consulta a la API de contratos falló: {r.Mensaje}",
        }, actor);
        return r;
    }

    private static object Foto(Persistence.Modelo.Contraventor k) => new
    {
        tipoDocumento = k.TipoDocumento.ToString(), k.NumeroDocumento, k.Nombres, k.Correo, k.Telefono, k.Direccion, k.Ciudad, k.Sucursal, k.TipoContrato, origen = k.Origen.ToString(),
    };
}
