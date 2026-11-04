using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Consultas.Grpc.V1;
using Flit.PersonaNatural.Api.Consultas;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.PersonaNatural.Api.Solicitudes;
using Microsoft.EntityFrameworkCore;

namespace Flit.PersonaNatural.Api.Punto;

public abstract record ResultadoAbrirActores
{
    public sealed record Ok(SesionCreadaDto Sesion, bool PuedeSerRadicante, string? Message, IReadOnlyList<RolPuntoDto> Roles) : ResultadoAbrirActores;

    public sealed record ProveedorCaido : ResultadoAbrirActores;

    public sealed record DatosInvalidos(string Mensaje) : ResultadoAbrirActores;
}

/// <summary>
/// Abre el borrador PN desde el canal del punto y evalúa si el documento puede ser radicante (dueño RUNT).
/// HU #14236. No radica ni crea procedure instance en FLIT 2.0.
/// </summary>
public sealed class ActoresPuntoService(IConsultasRunt runt, IAbrirBorradorDesdePunto borrador, Definiciones.IFachadaDefiniciones definiciones, Cotizaciones.IFachadaTramites? catalogo = null)
{
    public const string MensajeNoDueño = "No puedes realizar este trámite porque no eres el propietario del vehículo.";
    public const string CanalOrigen = "pn_punto";

    public async Task<ResultadoAbrirActores> AbrirAsync(
        OrganismoPunto sesion,
        string? placa,
        string? documento,
        string? tipoCodigo,
        string? tipoDocumento,
        byte[]? ipHash,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sesion);
        var codigo = (tipoCodigo ?? string.Empty).Trim();
        if (codigo.Length == 0)
            return new ResultadoAbrirActores.DatosInvalidos("Indique el tipo de trámite.");

        var placaNorm = EntradaConsulta.Placa(placa);
        var propietario = EntradaConsulta.DocumentoPropietario(
            string.IsNullOrWhiteSpace(tipoDocumento) ? "CC" : tipoDocumento, documento);
        if (placaNorm is null || propietario is null)
            return new ResultadoAbrirActores.DatosInvalidos("Indique la placa y el documento del propietario.");

        // Los roles salen de la definición del tipo (en su orden): el primero radica en el punto, los demás se invitan.
        var def = await definiciones.ObtenerDefinicionAsync(codigo, sesion.OrganismoId.ToString(), ct).ConfigureAwait(false);
        var roles = RolesPunto.Desde(def);
        if (roles.Count == 0)
            return new ResultadoAbrirActores.DatosInvalidos("Este tipo de trámite no tiene partes definidas.");

        ResultadoConsulta resultado;
        try
        {
            resultado = await runt.ConsultarVehiculoAsync(placaNorm, null, propietario, CanalOrigen, ct).ConfigureAwait(false);
        }
        catch (ProveedorNoDisponibleException)
        {
            return new ResultadoAbrirActores.ProveedorCaido();
        }

        if (ProyeccionMinima.ProveedorCaido(resultado))
            return new ResultadoAbrirActores.ProveedorCaido();

        // /arranque se puede saltar: el organismo del vehículo se vuelve a comprobar aquí, con la misma consulta, antes de crear el
        // borrador. Si el tipo deja el organismo al operador no se compara. Un vehículo que el RUNT no halla no decide nada.
        var verificado = false;
        if (!def.Flags.OrganismoLoEligeOperador && ProyeccionMinima.Vehiculo(resultado, placaNorm).Existe)
        {
            var organismoRunt = ProyeccionMinima.Texto(resultado, TiposDelPunto.CampoOrganismoRunt);
            if (string.IsNullOrWhiteSpace(organismoRunt))
                throw new ReglaDeNegocioException(StatusCodes.Status422UnprocessableEntity, CoincidenciaOrganismo.CodigoNoCorresponde, "Organismo no corresponde", TiposDelPunto.OrganismoDesconocido);
            if (!await TiposDelPunto.OrganismoDeLaSesionAsync(organismoRunt, sesion, catalogo, ct).ConfigureAwait(false))
                throw new ReglaDeNegocioException(StatusCodes.Status422UnprocessableEntity, CoincidenciaOrganismo.CodigoNoCorresponde, "Organismo no corresponde", TiposDelPunto.MensajeOtroOrganismo(organismoRunt, sesion));
            verificado = true;
        }

        var puedeSerRadicante = EsPropietarioRunt(resultado, propietario);
        var creada = await borrador.AbrirAsync(sesion, codigo, placaNorm, propietario.Numero, ipHash, ct, verificado).ConfigureAwait(false);
        return new ResultadoAbrirActores.Ok(
            creada,
            puedeSerRadicante,
            puedeSerRadicante ? null : MensajeNoDueño,
            roles);
    }

    /// <summary>
    /// El documento consultado es dueño si RUNT devolvió el vehículo y el número (y tipo, si viene) del propietario coinciden.
    /// </summary>
    internal static bool EsPropietarioRunt(ResultadoConsulta resultado, DocumentoConsulta propietario)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        ArgumentNullException.ThrowIfNull(propietario);
        if (!ProyeccionMinima.Vehiculo(resultado, null).Existe)
            return false;

        var numeroRunt = ProyeccionMinima.Texto(resultado, "owner_document_number");
        // La consulta ya se hizo con ese documento como propietario. Si el proveedor no proyecta el campo,
        // el vehículo existente implica que el documento pasó la compuerta de dueño del RUNT.
        if (numeroRunt is null)
            return true;

        var numNorm = PiiHasher.NormalizarDocumento(numeroRunt);
        var docNorm = PiiHasher.NormalizarDocumento(propietario.Numero);
        if (!string.Equals(numNorm, docNorm, StringComparison.Ordinal))
            return false;

        var tipoRunt = ProyeccionMinima.Texto(resultado, "owner_document_type");
        return string.IsNullOrWhiteSpace(tipoRunt)
            || string.Equals(tipoRunt.Trim(), propietario.Tipo, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Crea el borrador PN con el organismo de la cookie del punto y marca origen <c>pn_punto</c> en Datos.</summary>
public interface IAbrirBorradorDesdePunto
{
    Task<SesionCreadaDto> AbrirAsync(
        OrganismoPunto organismo,
        string tipoCodigo,
        string placa,
        string documento,
        byte[]? ipHash,
        CancellationToken ct,
        bool organismoVerificado = false);
}

public sealed class AbrirBorradorDesdePunto(
    SolicitudesService solicitudes,
    PersonaNaturalDb db) : IAbrirBorradorDesdePunto
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SesionCreadaDto> AbrirAsync(
        OrganismoPunto organismo,
        string tipoCodigo,
        string placa,
        string documento,
        byte[]? ipHash,
        CancellationToken ct,
        bool organismoVerificado = false)
    {
        ArgumentNullException.ThrowIfNull(organismo);
        var creada = await solicitudes.CrearAsync(
            new CrearSolicitudRequest(
                tipoCodigo,
                organismo.OrganismoId.ToString(),
                documento,
                placa,
                null,
                Consentimiento: true,
                ConsentimientoVersion: null),
            ipHash,
            ct).ConfigureAwait(false);

        var solicitud = await db.Set<SolicitudTramite>()
            .FirstAsync(s => s.Id == creada.SolicitudId, ct)
            .ConfigureAwait(false);

        var nodo = string.IsNullOrWhiteSpace(solicitud.Datos)
            ? new JsonObject()
            : JsonNode.Parse(solicitud.Datos) as JsonObject ?? new JsonObject();
        nodo["canal"] = ActoresPuntoService.CanalOrigen;
        nodo["codigoOrganismo"] = organismo.Codigo;
        nodo["organismoId"] = organismo.OrganismoId.ToString();
        if (organismoVerificado)
            nodo["organismoVerificadoId"] = organismo.OrganismoId.ToString();
        solicitud.Datos = nodo.ToJsonString(Json);
        solicitud.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return creada;
    }
}
