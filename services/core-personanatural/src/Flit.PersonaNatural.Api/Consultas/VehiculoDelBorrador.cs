using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Pii;
using Microsoft.EntityFrameworkCore;

namespace Flit.PersonaNatural.Api.Consultas;

public enum EstadoVehiculoBorrador
{
    Ok,
    SolicitudNoEncontrada,

    /// <summary>Sin placa/VIN o, por placa, sin documento del propietario con el cual consultar el RUNT.</summary>
    SinVehiculo,
}

/// <summary>Gravamen del vehículo del borrador (HU #14257). <c>Aviso</c> = <c>sin_gravamen</c> cuando el RUNT respondió sin prenda.</summary>
public sealed record GravamenBorradorDto(bool TienePrenda, string? Acreedor, string? NitAcreedor, string? Aviso);

public sealed record ResultadoVehiculoBorrador(EstadoVehiculoBorrador Estado, string? Clase = null, GravamenBorradorDto? Gravamen = null);

/// <summary>
/// Consultas del RUNT sobre el vehículo del BORRADOR (HU #14255 y #14257): la clase para filtrar las carrocerías y el gravamen para
/// la prenda. El servidor resuelve el vehículo y el propietario desde el borrador: el navegador no manda ni placa ni documento.
/// Por placa se consulta con el documento que el diagnóstico dejó en los campos, o con el del titular de la solicitud.
/// Habeas Data (HU #14041): la clase y la bandera de prenda son dato mínimo; el acreedor (dato de un tercero) solo sale si algún actor
/// de ESTA solicitud tiene la identidad validada, con el mismo criterio de <see cref="ConsultaDetalleService"/>.
/// </summary>
public sealed class VehiculoDelBorradorService(PersonaNaturalDb db, PersonaService personas, IConsultasRunt runt, ConsultaDetalleService detalle, MedicionConsultasService medicion)
{
    public const string ActorAuditoria = "consulta_borrador";

    private static readonly string[] RolesTitular = ["vendedor", "comprador", "locatario"];

    private static readonly JsonSerializerOptions OpcionesJson = new(JsonSerializerDefaults.Web);

    public async Task<ResultadoVehiculoBorrador> ClaseAsync(Guid solicitudId, CancellationToken ct)
    {
        var (estado, resultado, placa) = await ConsultarAsync(solicitudId, ct).ConfigureAwait(false);
        return estado != EstadoVehiculoBorrador.Ok
            ? new ResultadoVehiculoBorrador(estado)
            : new ResultadoVehiculoBorrador(estado, ProyeccionMinima.Vehiculo(resultado!, placa).Clase);
    }

    public async Task<ResultadoVehiculoBorrador> GravamenAsync(Guid solicitudId, CancellationToken ct)
    {
        var (estado, resultado, _) = await ConsultarAsync(solicitudId, ct).ConfigureAwait(false);
        if (estado != EstadoVehiculoBorrador.Ok)
            return new ResultadoVehiculoBorrador(estado);

        if (!ProyeccionMinima.TienePrenda(resultado!))
            return new ResultadoVehiculoBorrador(estado, null, new GravamenBorradorDto(false, null, null, "sin_gravamen"));

        if (!await detalle.TieneIdentidadValidadaAsync(solicitudId, ct).ConfigureAwait(false))
            return new ResultadoVehiculoBorrador(estado, null, new GravamenBorradorDto(true, null, null, null));

        var (acreedor, nit) = Acreedor(resultado!);
        return new ResultadoVehiculoBorrador(estado, null, new GravamenBorradorDto(true, acreedor, nit, null));
    }

    /// <summary>Primer gravamen con datos de <c>runt_gravamenes</c> (mismas llaves que <c>parseRuntGravamenesJson</c> de FLIT 2) o, en su defecto, el acreedor plano.</summary>
    internal static (string? Acreedor, string? Nit) Acreedor(ResultadoConsulta resultado)
    {
        string? nombre = null, documento = null;
        var json = resultado.Campos.FirstOrDefault(c => string.Equals(c.Clave, ProyeccionMinima.CampoGarantias, StringComparison.OrdinalIgnoreCase))?.ValorJson;
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var fila in doc.RootElement.EnumerateArray().Where(f => f.ValueKind == JsonValueKind.Object))
                    {
                        nombre = Primero(fila, "nombreAcreedor", "NombreAcreedor", "acreedor", "Acreedor", "entidad", "Entidad");
                        documento = Primero(fila, "numeroDocumentoAcreedor", "NumeroDocumentoAcreedor", "documentoAcreedor", "numeroDocumentoEntidad", "NumeroDocumentoEntidad");
                        if (nombre is not null || documento is not null)
                            break;
                    }
                }
            }
            catch (JsonException)
            {
                // JSON ilegible: se cae al acreedor plano.
            }
        }

        nombre ??= ProyeccionMinima.Texto(resultado, ProyeccionMinima.CampoAcreedor);
        var nit = documento is null ? null : new string(documento.Where(char.IsDigit).ToArray());
        return (nombre, string.IsNullOrEmpty(nit) ? null : nit);
    }

    private static string? Primero(JsonElement fila, params string[] claves)
    {
        foreach (var clave in claves)
        {
            if (fila.TryGetProperty(clave, out var v) && v.ValueKind != JsonValueKind.Null)
            {
                var texto = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
                if (!string.IsNullOrWhiteSpace(texto))
                    return texto.Trim();
            }
        }

        return null;
    }

    private async Task<(EstadoVehiculoBorrador Estado, ResultadoConsulta? Resultado, string? Placa)> ConsultarAsync(Guid solicitudId, CancellationToken ct)
    {
        var s = await db.Set<SolicitudTramite>().AsNoTracking().Where(x => x.Id == solicitudId)
            .Select(x => new { x.Placa, x.Vin, x.FieldValues }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (s is null)
            return (EstadoVehiculoBorrador.SolicitudNoEncontrada, null, null);

        var vin = EntradaConsulta.Vin(s.Vin);
        var placa = EntradaConsulta.Placa(s.Placa);
        if (vin is null && placa is null)
            return (EstadoVehiculoBorrador.SinVehiculo, null, null);

        DocumentoConsulta? propietario = null;
        if (vin is null)
        {
            propietario = await PropietarioAsync(solicitudId, s.FieldValues, ct).ConfigureAwait(false);
            if (propietario is null)
                return (EstadoVehiculoBorrador.SinVehiculo, null, null);
        }

        try
        {
            var resultado = await runt.ConsultarVehiculoAsync(vin is null ? placa : null, vin, propietario, solicitudId.ToString(), ct).ConfigureAwait(false);
            if (ProyeccionMinima.ProveedorCaido(resultado))
                throw new ProveedorNoDisponibleException("El proveedor del RUNT no respondió.");
            return (EstadoVehiculoBorrador.Ok, resultado, placa);
        }
        finally
        {
            await medicion.RegistrarAsync(MedicionConsultasService.TipoVehiculo, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<DocumentoConsulta?> PropietarioAsync(Guid solicitudId, string camposJson, CancellationToken ct)
    {
        try
        {
            var campos = JsonSerializer.Deserialize<Dictionary<string, string>>(camposJson, OpcionesJson) ?? [];
            if (campos.TryGetValue("owner_document_type", out var tipo) && campos.TryGetValue("owner_document_number", out var numero)
                && EntradaConsulta.DocumentoPropietario(tipo, numero) is { } deCampos)
                return deCampos;
        }
        catch (JsonException)
        {
            // Sin campos legibles: se intenta con el titular.
        }

        var partes = await personas.LeerAsync(solicitudId, ActorAuditoria, ct).ConfigureAwait(false);
        var titular = RolesTitular.Select(rol => partes.FirstOrDefault(p => string.Equals(p.Parte, rol, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(p => p is not null);
        return titular is null ? null : EntradaConsulta.DocumentoPropietario(titular.TipoDocumento, titular.Documento);
    }
}
