using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Catalogos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Personas;
using Flit.Platform.Grpc.V1;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Lo que el impuesto puede pedir, o el cierre visible si falta un dato. El constructor publica solo cuando hay solicitud.
/// <see cref="EjecutorCeldaImpuesto.SinConsultaAsync"/> usa el mismo cierre: una lista vacía ya no significa siempre N/A.
/// El nombre del propietario se lee en memoria y no se guarda.
/// </summary>
internal sealed record PreparacionSolicitudImpuesto(ConsultarImpuestoVehicularRequest? Solicitud, ResultadoCelda? Cierre)
{
    public static Task<PreparacionSolicitudImpuesto> PrepararAsync(
        Persistence.DiagnosticoDb db, CeldaConsulta celda, IProtectorDocumentoPersona documentos, IProtectorRespuestaFuente respuestas, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(celda);
        return PrepararAsync(db, celda.TenantId, celda.VehiculoId, documentos, respuestas, ct);
    }

    public static async Task<PreparacionSolicitudImpuesto> PrepararAsync(
        Persistence.DiagnosticoDb db, Guid tenantId, Guid vehiculoId, IProtectorDocumentoPersona documentos, IProtectorRespuestaFuente respuestas, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var celda = new CeldaConsulta { TenantId = tenantId, VehiculoId = vehiculoId, Fuente = Fuente.ImpuestoDepartamental };
        var vehiculo = await db.Set<VehiculoDiagnostico>().SingleOrDefaultAsync(v => v.TenantId == tenantId && v.Id == vehiculoId, ct).ConfigureAwait(false);
        var placa = Limpio(vehiculo?.Placa);
        if (placa is null)
            return Cerrar(EjecutorCeldaImpuesto.SinPlaca, "El vehículo no tiene placa definitiva tras el RUNT.");

        var departamento = Limpio(vehiculo!.DepartamentoCodigo);
        if (departamento is null || !await DerivacionDepartamento.SoportadoAsync(db, departamento, ct).ConfigureAwait(false))
            return new(null, ResultadoCelda.Na(MotivoNa.DepartamentoNoSoportado));

        var propietario = (await EjecutorPorPersonas.PersonasDelRolAsync(db, celda, RolPersona.Propietario, ct).ConfigureAwait(false)).FirstOrDefault();
        if (propietario is null)
            return Cerrar(EjecutorCeldaImpuesto.SinDocumento, "El vehículo no tiene propietario con documento.");
        var documento = EjecutorCeldaSimitPersona.Normalizar(new DocumentoPersona(propietario.DocTipo, documentos.Desproteger(propietario.DocCifrado)));
        if (documento.Numero.Length == 0)
            return Cerrar(EjecutorCeldaImpuesto.SinDocumento, "El vehículo no tiene propietario con documento.");

        if (Modelo(vehiculo.DatosConsolidados) is not { } modelo)
            return Cerrar(EjecutorCeldaImpuesto.SinModelo, "El RUNT no informó el modelo.");

        var organismo = await OrganismoAsync(db, vehiculo.OrganismoNombre, departamento, ct).ConfigureAwait(false);
        if (organismo is null || Limpio(organismo.Municipio) is not { } municipio || Limpio(organismo.DepartamentoNombre) is not { } departamentoNombre)
            return Cerrar(EjecutorCeldaImpuesto.OrganismoIncompleto, "El organismo no tiene municipio o nombre de departamento.");

        var jsonRunt = await JsonRuntAsync(db, celda, respuestas, ct).ConfigureAwait(false);
        if (LectorNombrePropietario.Leer(jsonRunt, documento) is not { } nombre)
            return Cerrar(EjecutorCeldaImpuesto.SinNombrePropietario, "La respuesta del RUNT no trae el nombre del propietario.");

        var contacto = LectorContactoPropietario.Leer(jsonRunt, documento);
        var vehiculoRunt = LectorVehiculoImpuesto.Leer(jsonRunt);
        var solicitud = new ConsultarImpuestoVehicularRequest
        {
            Opciones = new OpcionesConsulta(),
            Placa = new Placa { Valor = placa },
            DepartamentoCodigo = departamento,
            Documento = new DocumentoIdentidad { Tipo = documento.Tipo, Numero = documento.Numero },
            OrganismoNombre = Limpio(vehiculo.OrganismoNombre) ?? string.Empty,
            Municipio = municipio,
            DepartamentoNombre = departamentoNombre,
            Modelo = modelo,
            PropietarioNombre = nombre,
        };
        if (contacto?.Telefono is { } telefono)
            solicitud.PropietarioTelefono = telefono;
        if (contacto?.Correo is { } correo)
            solicitud.PropietarioCorreo = correo;
        if (contacto?.Departamento is { } depto)
            solicitud.PropietarioDepartamento = depto;
        if (contacto?.Ciudad is { } ciudad)
            solicitud.PropietarioCiudad = ciudad;
        if (contacto?.Direccion is { } direccion)
            solicitud.PropietarioDireccion = direccion;
        if (vehiculoRunt?.Clase is { } clase)
            solicitud.VehiculoClase = clase;
        if (vehiculoRunt?.Marca is { } marca)
            solicitud.VehiculoMarca = marca;
        if (vehiculoRunt?.Servicio is { } servicio)
            solicitud.VehiculoServicio = servicio;
        if (vehiculoRunt?.Combustible is { } combustible)
            solicitud.VehiculoCombustible = combustible;

        return new(solicitud, null);
    }

    private static PreparacionSolicitudImpuesto Cerrar(string tipo, string mensaje) =>
        new(null, ResultadoCelda.Error(tipo) with { Intento = new DatosIntento(null, null, null, mensaje) });

    private static async Task<Organismo?> OrganismoAsync(Persistence.DiagnosticoDb db, string? organismo, string departamento, CancellationToken ct)
    {
        var nombre = NombreNormalizado.Normalizar(organismo);
        if (nombre.Length == 0)
            return null;
        return await db.Set<Organismo>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.NombreNorm == nombre && o.DepartamentoCodigo == departamento, ct).ConfigureAwait(false);
    }

    private static async Task<string?> JsonRuntAsync(Persistence.DiagnosticoDb db, CeldaConsulta celda, IProtectorRespuestaFuente respuestas, CancellationToken ct)
    {
        var cifrada = db.ChangeTracker.Entries<CeldaConsulta>().Select(e => e.Entity)
            .Where(c => c.TenantId == celda.TenantId && c.VehiculoId == celda.VehiculoId && c.Fuente == Fuente.Runt && c.RespuestaFuenteCifrada is { Length: > 0 })
            .Select(c => c.RespuestaFuenteCifrada)
            .FirstOrDefault();
        cifrada ??= await db.Set<CeldaConsulta>().AsNoTracking()
            .Where(c => c.TenantId == celda.TenantId && c.VehiculoId == celda.VehiculoId && c.Fuente == Fuente.Runt)
            .Select(c => c.RespuestaFuenteCifrada)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return RespuestaFuenteCelda.TryLeerVehiculo(respuestas, cifrada, out var json) ? json?.GetRawText() : null;
    }

    internal static int? Modelo(string? datos)
    {
        if (string.IsNullOrWhiteSpace(datos))
            return null;
        try
        {
            using var documento = JsonDocument.Parse(datos);
            if (!documento.RootElement.TryGetProperty("vehiculo", out var vehiculo) || !vehiculo.TryGetProperty("modelo", out var modelo))
                return null;
            if (modelo.ValueKind == JsonValueKind.Number && modelo.TryGetInt32(out var numero))
                return numero;
            return modelo.ValueKind == JsonValueKind.String && int.TryParse(modelo.GetString(), out var texto) ? texto : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Limpio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
