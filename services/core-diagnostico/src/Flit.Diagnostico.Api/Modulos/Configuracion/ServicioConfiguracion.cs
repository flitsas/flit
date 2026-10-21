using System.Globalization;
using System.Text.Json;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Configuracion;

/// <summary>
/// Reglas de la configuración en dos niveles (HU #13741, D-04 = b, plan §6.1). La aplicación valida antes que la base, en
/// este orden: 400 si el cuerpo no es un objeto; 403 <c>CAMPO_NO_EDITABLE</c> si llega un campo que la ruta no edita (en la
/// operativa, los contractuales y <c>redaccion_ia_suspendida</c>), sin guardar nada; 422 si un valor está fuera de su
/// conjunto, el límite no es positivo, una fuente por defecto no está habilitada o se habilita una fuente no disponible. Un
/// CHECK de la base que aun así falle (23514) lo traduce <see cref="ManejadorReglas"/> a 422 <c>VALOR_INVALIDO</c>.
/// </summary>
public sealed class ServicioConfiguracion(DiagnosticoDb db, UsuarioActual usuario, DisponibilidadFuentes disponibilidad, TimeProvider reloj)
{
    public const string CampoFuentesHabilitadas = "fuentes_habilitadas";
    public const string CampoLimiteVehiculosLote = "limite_vehiculos_lote";
    public const string CampoRedaccionIaSuspendida = "redaccion_ia_suspendida";
    public const string CampoVisibilidadUsuario = "visibilidad_usuario";
    public const string CampoFuentesPorDefecto = "fuentes_por_defecto";
    public const string CampoEstimadoConsultas = "estimado_consultas";

    public const string CodigoCampoNoEditable = "CAMPO_NO_EDITABLE";
    public const string CodigoFueraDeConjunto = "VALOR_FUERA_DE_CONJUNTO";
    public const string CodigoPorDefectoFueraDeHabilitadas = "POR_DEFECTO_FUERA_DE_HABILITADAS";
    public const string CodigoLimiteInvalido = "LIMITE_INVALIDO";
    public const string CodigoFuenteNoDisponible = "FUENTE_NO_DISPONIBLE";
    public const string CodigoCuerpoInvalido = "CUERPO_INVALIDO";

    private static readonly HashSet<string> Operativos = new(StringComparer.Ordinal) { CampoVisibilidadUsuario, CampoFuentesPorDefecto, CampoEstimadoConsultas };

    private static readonly HashSet<string> DelContrato = new(StringComparer.Ordinal) { CampoFuentesHabilitadas, CampoLimiteVehiculosLote, CampoRedaccionIaSuspendida };

    /// <summary>Configuración de la compañía del token; sin fila guardada, los valores por defecto (sin escribir).</summary>
    public async Task<ConfiguracionEmpresa> VigenteAsync(CancellationToken ct)
    {
        var empresa = usuario.EmpresaPropia();
        return await db.Set<ConfiguracionEmpresa>().AsNoTracking().SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? new ConfiguracionEmpresa { TenantId = empresa };
    }

    /// <summary>
    /// Edita el nivel operativo de la compañía del token: solo los campos que llegan, con usuario y fecha. Sin fila, la crea
    /// con los valores contractuales por defecto.
    /// </summary>
    public async Task<ConfiguracionEmpresa> EditarOperativaAsync(JsonElement cuerpo, CancellationToken ct)
    {
        usuario.EmpresaPropia();
        var autor = Autor();
        var campos = Campos(cuerpo, Operativos, "Aquí solo se editan visibilidad_usuario, fuentes_por_defecto y estimado_consultas.");
        VisibilidadUsuario? visibilidad = campos.TryGetValue(CampoVisibilidadUsuario, out var v) ? Codigo<VisibilidadUsuario>(v, CampoVisibilidadUsuario) : null;
        EstimadoConsultas? estimado = campos.TryGetValue(CampoEstimadoConsultas, out var e) ? Codigo<EstimadoConsultas>(e, CampoEstimadoConsultas) : null;
        var porDefecto = campos.TryGetValue(CampoFuentesPorDefecto, out var d) ? Fuentes(d, CampoFuentesPorDefecto) : null;

        var configuracion = await db.Set<ConfiguracionEmpresa>().SingleOrDefaultAsync(ct).ConfigureAwait(false);
        var nueva = configuracion is null;
        configuracion ??= new ConfiguracionEmpresa();
        if (porDefecto is not null && porDefecto.Except(configuracion.FuentesHabilitadas).Any())
            throw new ReglaException(CodigoPorDefectoFueraDeHabilitadas, "Las fuentes por defecto deben estar entre las habilitadas en el contrato.");

        if (visibilidad is { } nuevaVisibilidad)
            configuracion.VisibilidadUsuario = nuevaVisibilidad;
        if (estimado is { } nuevoEstimado)
            configuracion.EstimadoConsultas = nuevoEstimado;
        if (porDefecto is not null)
            configuracion.FuentesPorDefecto = porDefecto;
        configuracion.OperativaActualizadaPor = autor;
        configuracion.OperativaActualizadaEn = reloj.GetUtcNow();
        if (nueva)
            db.Add(configuracion);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return configuracion;
    }

    /// <summary>
    /// Aplica a <paramref name="configuracion"/> (de la empresa indicada por el backoffice) los campos contractuales que llegan
    /// y devuelve un <see cref="CambioContrato"/> por cada campo que cambió, con el usuario, la compañía, el valor anterior y el
    /// nuevo. Reducir las habilitadas recorta las por defecto en el mismo comando (y el recorte también se registra). Valida
    /// todo antes de cambiar nada. No guarda: lo hace el módulo Backoffice por su vía de escritura entre empresas.
    /// </summary>
    public IReadOnlyList<CambioContrato> AplicarContrato(ConfiguracionEmpresa configuracion, JsonElement cuerpo)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        var autor = Autor();
        var campos = Campos(cuerpo, DelContrato, "Aquí solo se editan fuentes_habilitadas, limite_vehiculos_lote y redaccion_ia_suspendida.");
        var habilitadas = campos.TryGetValue(CampoFuentesHabilitadas, out var h) ? Fuentes(h, CampoFuentesHabilitadas) : null;
        int? limite = campos.TryGetValue(CampoLimiteVehiculosLote, out var l) ? Limite(l) : null;
        bool? redaccion = campos.TryGetValue(CampoRedaccionIaSuspendida, out var r) ? Booleano(r, CampoRedaccionIaSuspendida) : null;
        var noDisponibles = habilitadas?.Except(configuracion.FuentesHabilitadas).Where(f => !disponibilidad.Disponible(f)).ToList() ?? [];
        if (noDisponibles.Count > 0)
            throw new ReglaException(CodigoFuenteNoDisponible, $"La fuente {string.Join(", ", noDisponibles.Select(CodigosDeEnum.Codigo))} todavía no se puede habilitar.");

        var cambios = new List<CambioContrato>();
        if (habilitadas is not null && !habilitadas.SequenceEqual(configuracion.FuentesHabilitadas))
        {
            cambios.Add(Cambio(configuracion, autor, CampoFuentesHabilitadas, Json(configuracion.FuentesHabilitadas), Json(habilitadas)));
            configuracion.FuentesHabilitadas = habilitadas;
            var recortadas = configuracion.FuentesPorDefecto.Where(habilitadas.Contains).ToList();
            if (recortadas.Count != configuracion.FuentesPorDefecto.Count)
            {
                cambios.Add(Cambio(configuracion, autor, CampoFuentesPorDefecto, Json(configuracion.FuentesPorDefecto), Json(recortadas)));
                configuracion.FuentesPorDefecto = recortadas;
            }
        }

        if (limite is { } nuevoLimite && nuevoLimite != configuracion.LimiteVehiculosLote)
        {
            cambios.Add(Cambio(configuracion, autor, CampoLimiteVehiculosLote,
                configuracion.LimiteVehiculosLote.ToString(CultureInfo.InvariantCulture), nuevoLimite.ToString(CultureInfo.InvariantCulture)));
            configuracion.LimiteVehiculosLote = nuevoLimite;
        }

        if (redaccion is { } suspendida && suspendida != configuracion.RedaccionIaSuspendida)
        {
            cambios.Add(Cambio(configuracion, autor, CampoRedaccionIaSuspendida, Json(configuracion.RedaccionIaSuspendida), Json(suspendida)));
            configuracion.RedaccionIaSuspendida = suspendida;
        }

        if (cambios.Count > 0)
        {
            configuracion.ContratoActualizadoPor = autor;
            configuracion.ContratoActualizadoEn = reloj.GetUtcNow();
        }

        return cambios;
    }

    private Guid Autor() =>
        usuario.Id != Guid.Empty ? usuario.Id : throw ReglaException.Prohibido("SIN_USUARIO", "El token no identifica al usuario: el cambio no se puede registrar.");

    private static Dictionary<string, JsonElement> Campos(JsonElement cuerpo, HashSet<string> permitidos, string ayuda)
    {
        if (cuerpo.ValueKind != JsonValueKind.Object)
            throw new ReglaException(CodigoCuerpoInvalido, "El cuerpo debe ser un objeto JSON.", StatusCodes.Status400BadRequest);

        var campos = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var propiedad in cuerpo.EnumerateObject())
        {
            if (!campos.TryAdd(propiedad.Name, propiedad.Value))
                throw new ReglaException(CodigoCuerpoInvalido, "El cuerpo repite un campo.", StatusCodes.Status400BadRequest);
        }

        if (campos.Keys.Any(k => !permitidos.Contains(k)))
            throw ReglaException.Prohibido(CodigoCampoNoEditable, ayuda);
        return campos;
    }

    private static T Codigo<T>(JsonElement valor, string campo)
        where T : struct, Enum =>
        valor.ValueKind == JsonValueKind.String && CodigosDeEnum.TryLeer<T>(valor.GetString(), out var leido)
            ? leido
            : throw FueraDeConjunto(campo);

    private static List<Fuente> Fuentes(JsonElement valor, string campo)
    {
        if (valor.ValueKind != JsonValueKind.Array)
            throw FueraDeConjunto(campo);
        return valor.EnumerateArray().Select(f => Codigo<Fuente>(f, campo)).Distinct().ToList();
    }

    private static int Limite(JsonElement valor) =>
        valor.ValueKind == JsonValueKind.Number && valor.TryGetInt32(out var limite) && limite > 0
            ? limite
            : throw new ReglaException(CodigoLimiteInvalido, "El límite de vehículos por lote debe ser un entero mayor que cero.");

    private static bool Booleano(JsonElement valor, string campo) => valor.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw FueraDeConjunto(campo),
    };

    private static ReglaException FueraDeConjunto(string campo) => new(CodigoFueraDeConjunto, $"El valor de {campo} no es válido.");

    private static CambioContrato Cambio(ConfiguracionEmpresa configuracion, Guid autor, string campo, string anterior, string nuevo) =>
        new() { TenantId = configuracion.TenantId, UsuarioId = autor, Campo = campo, ValorAnterior = anterior, ValorNuevo = nuevo };

    private static string Json(IEnumerable<Fuente> fuentes) => JsonSerializer.Serialize(Codigos.De(fuentes));

    private static string Json(bool valor) => valor ? "true" : "false";
}
