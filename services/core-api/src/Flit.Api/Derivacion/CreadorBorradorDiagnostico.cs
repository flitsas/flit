using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Flit.Tramites.Grpc.V1;
using Grpc.Core;

namespace Flit.Api.Derivacion;

/// <summary>Códigos estables de rechazo del rpc (diseño §3). Ninguno es reintentable; Diagnóstico los traduce a motivos de omisión.</summary>
internal static class RechazosDerivacion
{
    public const string TipoNoDisponible = "TIPO_NO_DISPONIBLE";
    public const string FamiliaBloqueada = "FAMILIA_BLOQUEADA";
    public const string UsuarioSinPermiso = "USUARIO_SIN_PERMISO";
    public const string OtNoHabilitado = "OT_NO_HABILITADO";
    public const string ReglaCompania = "REGLA_COMPANIA";
    public const string TramiteActivoDuplicado = "TRAMITE_ACTIVO_DUPLICADO";
    public const string EmpresaInactiva = "EMPRESA_INACTIVA";
    public const string ReferenciaDeOtroOrigen = "REFERENCIA_DE_OTRO_ORIGEN";
    public const string DatosInvalidos = "DATOS_INVALIDOS";

    /// <summary>Motivo en lenguaje claro, sin datos personales ni códigos técnicos.</summary>
    public static string Motivo(string codigo) => codigo switch
    {
        TipoNoDisponible => "El tipo de trámite no existe, no está publicado o todavía no está habilitado para crearse.",
        FamiliaBloqueada => "La compañía tiene bloqueada la creación de trámites de esta familia.",
        UsuarioSinPermiso => "El usuario no está activo en la compañía o no tiene permiso para crear este tipo de trámite.",
        OtNoHabilitado => "El organismo de tránsito de matrícula no está habilitado para la compañía.",
        ReglaCompania => "El organismo de tránsito no cumple la regla de compañía del tipo de trámite.",
        TramiteActivoDuplicado => "Ya existe un trámite activo del mismo tipo para el vehículo.",
        EmpresaInactiva => "La compañía o su red no están activas.",
        ReferenciaDeOtroOrigen => "El vehículo ya tiene un trámite creado por otro canal.",
        _ => "La solicitud no trae los datos mínimos para crear el borrador.",
    };
}

/// <summary>
/// ADR-0084 / diseño §3 — crea el borrador de trámite de un vehículo con dictamen VIABLE de Diagnóstico, sin wizard.
/// Patrón de <c>PersonaNaturalCreador</c> e <c>IctOrchestrationService.CreateDraftFromIct</c>: reutiliza los casos de uso
/// de Trámites (crear + sembrar campos y organismo) y no reimplementa reglas. Orden:
/// <list type="number">
///   <item>Datos mínimos (<c>DATOS_INVALIDOS</c>), sin tocar la base.</item>
///   <item>Idempotencia por <c>(empresa, external_ref)</c>: el mismo trámite si es de Diagnóstico (completa lo que falte si
///   sigue en borrador); <c>REFERENCIA_DE_OTRO_ORIGEN</c> si es de otro canal. Va antes de las validaciones para que un
///   reintento siempre reciba el mismo trámite aunque el tipo o los permisos hayan cambiado después.</item>
///   <item>Tipo publicado, autor (usuario real de la empresa con <c>tramites.create</c> + Crear de la familia) y OT de la
///   lista efectiva (por nombre RUNT y, si no casa, por código). Nunca se crea sin OT.</item>
///   <item>Creación con <c>origin=diagnostico</c>, <c>external_ref</c>, canal <c>tramites</c> y el usuario como autor; la
///   carrera del índice único responde <c>ya_existia</c>.</item>
///   <item>Siembra: un fallo va a <c>advertencias</c> y no deshace el borrador.</item>
/// </list>
/// Habeas Data: este tipo no escribe logs; el rpc solo registra ids, código y duración.
/// </summary>
internal sealed class CreadorBorradorDiagnostico(IAlmacenBorradorDiagnostico almacen, IAutorDerivacion autor, ITransitOfficeResolver oficinas)
{
    /// <summary><c>procedure_instances.origin</c> de los borradores de Diagnóstico (varchar(20), sin CHECK).</summary>
    public const string Origen = "diagnostico";

    /// <summary>Tope de referencias por llamada a <c>ObtenerTramitesPorReferencia</c>.</summary>
    public const int MaxReferencias = 500;

    public async Task<CrearBorradorDesdeDiagnosticoResponse> CrearAsync(Guid tenantId, CrearBorradorDesdeDiagnosticoRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var externalRef = Texto(request.ExternalRef);
        var tipoCodigo = Texto(request.TipoTramiteCodigo);
        if (externalRef is null || !Guid.TryParse(externalRef, out var vehiculo) || vehiculo == Guid.Empty
            || !Guid.TryParse(Texto(request.UsuarioId), out var usuario) || usuario == Guid.Empty
            || tipoCodigo is null
            || (Texto(request.Placa) is null && Texto(request.Vin) is null))
            return Rechazo(RechazosDerivacion.DatosInvalidos);
        externalRef = vehiculo.ToString();

        var existente = await almacen.BuscarPorReferenciaAsync(tenantId, externalRef, ct).ConfigureAwait(false);
        if (existente is not null)
            return await RetomarAsync(tenantId, usuario, existente, request, ct).ConfigureAwait(false);

        var tipo = await almacen.TipoPublicadoAsync(tipoCodigo, ct).ConfigureAwait(false);
        if (tipo is null)
            return Rechazo(RechazosDerivacion.TipoNoDisponible);

        if (!await autor.PuedeCrearAsync(tenantId, usuario, tipo.Familia, ct).ConfigureAwait(false))
            return Rechazo(RechazosDerivacion.UsuarioSinPermiso);

        var oficina = await ResolverOficinaAsync(tenantId, request, ct).ConfigureAwait(false);
        if (oficina is null)
            return Rechazo(RechazosDerivacion.OtNoHabilitado);

        var creacion = await almacen.CrearAsync(
            new CreateProcedureInstanceRequest(
                TenantId: tenantId, ProcedureTypeId: null, CreatedByUserId: usuario, TransitOfficeId: oficina.Id,
                ProcedureTypeCode: tipo.Codigo, Origin: Origen, ExternalRef: externalRef,
                // Decisión del diseño (ADR-0084 «Pendientes»): canal tramites; origin=diagnostico ya lo distingue.
                Channel: TramiteCanal.Tramites),
            ct).ConfigureAwait(false);

        if (creacion.ReferenciaDuplicada)
        {
            var ganador = await almacen.BuscarPorReferenciaAsync(tenantId, externalRef, ct).ConfigureAwait(false);
            return ganador is null
                ? throw new RpcException(new Status(StatusCode.Unavailable, "La referencia quedó en conflicto; reintente."))
                : await RetomarAsync(tenantId, usuario, ganador, request, ct).ConfigureAwait(false);
        }

        if (creacion.Error is not null || creacion.Resumen is null)
            return TraducirError(creacion.Error) is { } codigo
                ? Rechazo(codigo)
                : throw ErrorTecnico(creacion.Error);

        var creado = new BorradorCreado
        {
            ProcedureInstanceId = creacion.Resumen.Id.ToString(),
            Numero = creacion.Resumen.ReferenceNumber,
            Estado = creacion.Resumen.Status,
            YaExistia = false,
        };
        await SembrarAsync(creado, creacion.Resumen.Id, tenantId, usuario, tipo.Codigo, oficina, request, soloFaltantes: false, ct).ConfigureAwait(false);
        return new CrearBorradorDesdeDiagnosticoResponse { Creado = creado };
    }

    public async Task<ObtenerTramitesPorReferenciaResponse> PorReferenciasAsync(Guid tenantId, IReadOnlyCollection<string> externalRefs, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(externalRefs);
        var refs = externalRefs.Select(Texto).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var respuesta = new ObtenerTramitesPorReferenciaResponse();
        if (refs.Count == 0)
            return respuesta;

        var encontrados = await almacen.BuscarPorReferenciasAsync(tenantId, refs, ct).ConfigureAwait(false);
        respuesta.Tramites.AddRange(encontrados
            .Where(t => string.Equals(t.Origen, Origen, StringComparison.Ordinal))
            .Select(t => new TramitePorReferencia
            {
                ExternalRef = t.ExternalRef,
                ProcedureInstanceId = t.Id.ToString(),
                Numero = t.Numero,
                Estado = t.Estado,
            }));
        return respuesta;
    }

    /// <summary>
    /// Traduce el código de <see cref="CreateProcedureInstanceHandler"/> al código estable del rpc; null si no es de negocio
    /// (lo técnico sale como status gRPC).
    /// </summary>
    internal static string? TraducirError(string? error) => error switch
    {
        "not_found" or "not_published" or "procedure_type_not_enabled" or "modalidad_not_available" => RechazosDerivacion.TipoNoDisponible,
        "procedure_family_blocked" => RechazosDerivacion.FamiliaBloqueada,
        "family_permission_denied" => RechazosDerivacion.UsuarioSinPermiso,
        "ot_not_permitted" or "OT_NOT_AUTHORIZED_FOR_TYPE" => RechazosDerivacion.OtNoHabilitado,
        "COMPANY_RULE_VIOLATION" => RechazosDerivacion.ReglaCompania,
        "DUPLICATE_ACTIVE_PROCEDURE" => RechazosDerivacion.TramiteActivoDuplicado,
        "tenant_inactive" or "network_inactive" => RechazosDerivacion.EmpresaInactiva,
        "invalid_reference" or "invalid_request" => RechazosDerivacion.DatosInvalidos,
        _ => null,
    };

    private async Task<CrearBorradorDesdeDiagnosticoResponse> RetomarAsync(
        Guid tenantId, Guid usuario, TramiteExistente existente, CrearBorradorDesdeDiagnosticoRequest request, CancellationToken ct)
    {
        if (!string.Equals(existente.Origen, Origen, StringComparison.Ordinal))
            return Rechazo(RechazosDerivacion.ReferenciaDeOtroOrigen);

        var creado = new BorradorCreado
        {
            ProcedureInstanceId = existente.Id.ToString(),
            Numero = existente.Numero,
            Estado = existente.Estado,
            YaExistia = true,
        };

        // Un intento previo pudo caerse entre crear y sembrar: en borrador se completa SOLO lo que falte (lo demás pudo
        // editarlo el gestor). Fuera de borrador no se toca nada. Antes de sembrar se revalida al autor (revisión de seguridad
        // 2026-11-11): sin tipo publicado o sin permiso se devuelve el mismo trámite (idempotencia) sin escribir a su nombre.
        if (string.Equals(existente.Estado, TramiteEstado.Borrador, StringComparison.Ordinal)
            && Texto(request.TipoTramiteCodigo) is { } tipoCodigo
            && await almacen.TipoPublicadoAsync(tipoCodigo, ct).ConfigureAwait(false) is { } tipo
            && await autor.PuedeCrearAsync(tenantId, usuario, tipo.Familia, ct).ConfigureAwait(false))
        {
            var oficina = await ResolverOficinaAsync(tenantId, request, ct).ConfigureAwait(false);
            await SembrarAsync(creado, existente.Id, tenantId, usuario, Texto(request.TipoTramiteCodigo), oficina, request, soloFaltantes: true, ct).ConfigureAwait(false);
        }

        return new CrearBorradorDesdeDiagnosticoResponse { Creado = creado };
    }

    private async Task<ResolvedTransitOffice?> ResolverOficinaAsync(Guid tenantId, CrearBorradorDesdeDiagnosticoRequest request, CancellationToken ct)
    {
        if (Texto(request.OrganismoNombreRunt) is { } nombre
            && await oficinas.ResolveEnabledByNameAsync(tenantId, nombre, ct).ConfigureAwait(false) is { } porNombre)
            return porNombre;

        return Texto(request.OrganismoCodigo) is { } codigo
            ? await oficinas.ResolveEnabledByCodeAsync(tenantId, codigo, ct).ConfigureAwait(false)
            : null;
    }

    private async Task SembrarAsync(
        BorradorCreado creado, Guid id, Guid tenantId, Guid usuario, string? tipoCodigo, ResolvedTransitOffice? oficina,
        CrearBorradorDesdeDiagnosticoRequest request, bool soloFaltantes, CancellationToken ct)
    {
        await Paso(creado, almacen.SembrarCamposAsync(id, tenantId, usuario, CamposEditables(request, tipoCodigo), soloFaltantes, ct), null).ConfigureAwait(false);
        if (oficina is not null)
            await Paso(creado, almacen.SembrarSistemaAsync(id, tenantId, CamposOrganismo(oficina), soloFaltantes, ct), "transit_office").ConfigureAwait(false);
        var vehiculo = CamposVehiculo(request.Vehiculo);
        if (vehiculo.Count > 0)
            await Paso(creado, almacen.SembrarSistemaAsync(id, tenantId, vehiculo, soloFaltantes, ct), "vehicle").ConfigureAwait(false);
    }

    private static async Task Paso(BorradorCreado creado, Task<string?> siembra, string? bloque)
    {
        string? error;
        try
        {
            error = await siembra.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = "exception";
        }

        if (error is not null)
            creado.Advertencias.Add(bloque is null ? $"seed_warning:{error}" : $"seed_warning:{bloque}:{error}");
    }

    /// <summary>Placa, VIN y documentos con las claves de ICT; el locatario como atributo solo fuera de traspaso.</summary>
    internal static List<FieldValueInput> CamposEditables(CrearBorradorDesdeDiagnosticoRequest request, string? tipoCodigo)
    {
        var items = new List<FieldValueInput>();
        void Add(string clave, string? valor)
        {
            if (Texto(valor) is { } v)
                items.Add(new FieldValueInput(null, clave, v, null));
        }

        Add(VehicleFieldKeys.Plate, Texto(request.Placa)?.ToUpperInvariant());
        Add(VehicleFieldKeys.Vin, Texto(request.Vin)?.ToUpperInvariant());
        if (request.Propietario is { } p && Texto(p.NumeroDocumento) is { } numero)
        {
            Add("owner_document_type", Texto(p.TipoDocumento)?.ToUpperInvariant() ?? "CC");
            Add("owner_document_number", numero);
        }

        var esTraspaso = tipoCodigo?.Contains("TRASPASO", StringComparison.OrdinalIgnoreCase) == true;
        if (!esTraspaso && request.Locatario is { } l && Texto(l.NumeroDocumento) is { } numeroLocatario)
        {
            Add("lessee_document_type", Texto(l.TipoDocumento)?.ToUpperInvariant() ?? "CC");
            Add("lessee_document_number", numeroLocatario);
        }

        return items;
    }

    /// <summary>Organismo en firme con las claves del paso 1 del wizard (patch de sistema aparte: B11 en traspaso).</summary>
    internal static List<FieldValueInput> CamposOrganismo(ResolvedTransitOffice oficina)
    {
        var items = new List<FieldValueInput>
        {
            new(null, TransitOfficeFieldKeys.Id, oficina.Id.ToString(), null),
            new(null, TransitOfficeFieldKeys.Code, oficina.Code, null),
        };
        if (!string.IsNullOrWhiteSpace(oficina.Name))
            items.Add(new FieldValueInput(null, TransitOfficeFieldKeys.Name, oficina.Name, null));
        if (!string.IsNullOrWhiteSpace(oficina.CityCode))
            items.Add(new FieldValueInput(null, TransitOfficeFieldKeys.City, oficina.CityCode, null));
        if (!string.IsNullOrWhiteSpace(oficina.CityName))
            items.Add(new FieldValueInput(null, TransitOfficeFieldKeys.CityName, oficina.CityName, null));
        items.Add(new FieldValueInput(null, TransitOfficeSelectionPolicy.OrigenFieldKey, TransitOfficeSelectionPolicy.OrigenPasoUno, null));
        return items;
    }

    /// <summary>
    /// Ficha del vehículo con las claves de <see cref="VehicleFieldKeys"/> (precarga; la hidratación RUNT de Trámites
    /// prevalece). Color y combustible son atributos transformables: se siembra también el snapshot <c>*_runt</c>, como
    /// <c>PersonaNaturalCreador.CamposDeLaConsulta</c>.
    /// </summary>
    internal static List<FieldValueInput> CamposVehiculo(VehiculoBorrador? vehiculo)
    {
        var items = new List<FieldValueInput>();
        if (vehiculo is null)
            return items;
        void Add(string clave, string? valor)
        {
            if (Texto(valor) is { } v)
                items.Add(new FieldValueInput(null, clave, v, null));
        }

        Add(VehicleFieldKeys.Class, vehiculo.Clase);
        Add(VehicleFieldKeys.Service, vehiculo.Servicio);
        Add(VehicleFieldKeys.Brand, vehiculo.Marca);
        Add(VehicleFieldKeys.Line, vehiculo.Linea);
        Add(VehicleFieldKeys.Year, vehiculo.Modelo);
        Add(VehicleFieldKeys.Fuel, vehiculo.Combustible);
        Add(VehicleFieldKeys.FuelRunt, vehiculo.Combustible);
        Add(VehicleFieldKeys.EngineDisplacement, vehiculo.Cilindraje);
        Add(VehicleFieldKeys.Color, vehiculo.Color);
        Add(VehicleFieldKeys.ColorRunt, vehiculo.Color);
        return items;
    }

    private static CrearBorradorDesdeDiagnosticoResponse Rechazo(string codigo) => new()
    {
        Rechazo = new RechazoBorrador { Codigo = codigo, Motivo = RechazosDerivacion.Motivo(codigo) },
    };

    private static RpcException ErrorTecnico(string? error) => error == "reference_conflict"
        ? new RpcException(new Status(StatusCode.Unavailable, "No se pudo asignar el número del trámite; reintente."))
        : new RpcException(new Status(StatusCode.Internal, "No se pudo crear el borrador."));

    private static string? Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
