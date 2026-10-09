using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Catalogos;
using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Modulos.Ingesta.Fuentes;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Identificadores;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Plazos;
using Flit.Comparendos.Domain.Textos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Ingesta;

/// <summary>La clave monitoreada de una empresa que se está reconciliando (un NIT con sus variantes, o una placa).</summary>
public sealed record ClaveEmpresa(Guid TenantId, CanalIngesta Canal, Guid? IdentificacionId, Guid? PlacaId, string? MunicipioCodigo, string Etiqueta);

/// <summary>Contadores de una clave para el log de migración.</summary>
public sealed class Contadores
{
    public int Obtenidas { get; set; }
    public int Nuevas { get; set; }
    public int Actualizadas { get; set; }
    public int SinCambios { get; set; }
    public int DuplicadasOtraFuente { get; set; }
    public int Inactivadas { get; set; }
    public int Reactivadas { get; set; }
    public int DpFavorableAutomatico { get; set; }
}

/// <summary>
/// Aplica a una empresa lo que una fuente devolvió para una de sus claves (§6.4.3). Reglas: la fuente sobrescribe todos
/// sus campos y cada cambio queda registrado [Excel C2]; solo se marcan ausentes los comparendos si la consulta fue
/// exitosa [Excel C3]; un comparendo se inactiva cuando no está en ninguna de sus fuentes y se reactiva si vuelve,
/// salvo que lo haya inactivado un operador [Excel C4 propuesta]; si tenía un DP enviado, el DP queda favorable
/// [Excel C14].
/// </summary>
public sealed class Reconciliador(ComparendosDb db, Timeline timeline, MapeadorOt mapeador, Capacidades capacidades, Reloj reloj)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Prioridad para elegir la fuente principal cuando la actual deja de traerlo (S-51).</summary>
    private static readonly CanalIngesta[] Prioridad = [CanalIngesta.SimitNit, CanalIngesta.SimitPlaca, CanalIngesta.MunicipalNit, CanalIngesta.MunicipalPlaca];

    public async Task<Contadores> AplicarAsync(ClaveEmpresa clave, Guid cicloId, IReadOnlyList<ItemFuente> items, bool reconciliar, MotorDePlazos motor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(clave);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(motor);
        var contadores = new Contadores();
        var config = await capacidades.DeAsync(clave.TenantId, ct).ConfigureAwait(false);
        var hoy = reloj.Hoy;

        // Ítems fusionados por número normalizado: gana el más completo (S-50).
        var porNumero = new Dictionary<string, ItemFuente>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (NumeroComparendo.Normalizar(item.Numero) is not { } n)
                continue;
            if (!porNumero.TryGetValue(n, out var previo) || item.CamposConValor > previo.CamposConValor)
                porNumero[n] = item;
        }

        contadores.Obtenidas = porNumero.Count;
        var numeros = porNumero.Keys.ToList();
        var existentes = await db.Comparendos.Include(c => c.Contraventor)
            .Where(c => c.TenantId == clave.TenantId && numeros.Contains(c.NumeroNormalizado))
            .ToDictionaryAsync(c => c.NumeroNormalizado, ct).ConfigureAwait(false);
        var fotos = await FotosDeLaClave(clave).ToListAsync(ct).ConfigureAwait(false);

        foreach (var (numero, item) in porNumero)
        {
            if (existentes.TryGetValue(numero, out var c))
                await ActualizarAsync(c, clave, cicloId, item, fotos.FirstOrDefault(f => f.ComparendoId == c.Id), motor, contadores, ct).ConfigureAwait(false);
            else
                existentes[numero] = await CrearAsync(clave, cicloId, numero, item, config, motor, contadores, ct).ConfigureAwait(false);
        }

        if (reconciliar)
            await MarcarAusentesAsync(clave, fotos, numeros.ToHashSet(StringComparer.Ordinal), hoy, motor, contadores, ct).ConfigureAwait(false);

        return contadores;
    }

    private IQueryable<ComparendoFuente> FotosDeLaClave(ClaveEmpresa clave) =>
        db.ComparendoFuentes.Where(f => f.TenantId == clave.TenantId && f.Canal == clave.Canal
            && f.IdentificacionMonitoreadaId == clave.IdentificacionId && f.PlacaMonitoreadaId == clave.PlacaId
            && f.MunicipioCodigo == clave.MunicipioCodigo);

    private async Task<Comparendo> CrearAsync(ClaveEmpresa clave, Guid cicloId, string numero, ItemFuente item, ConfiguracionEmpresa config, MotorDePlazos motor, Contadores contadores, CancellationToken ct)
    {
        var ahora = reloj.Ahora;
        var c = new Comparendo
        {
            Id = Guid.CreateVersion7(),
            TenantId = clave.TenantId,
            Numero = item.Numero.Trim(),
            NumeroNormalizado = numero,
            TipoIngreso = Codigo(clave.Canal),
            FuentePrincipal = clave.Canal,
            IdentificacionMonitoreadaId = clave.IdentificacionId,
            PlacaMonitoreadaId = clave.PlacaId,
            EstadoCambioContraventor = config.AllowsContravenerChange ? EstadoCambioContraventor.SinSolicitar : EstadoCambioContraventor.NoAplica,
            EstadoConsultaContraventor = config.TipoIntegracionConductor == TipoIntegracionConductor.ApiRenting && config.AllowsContravenerConsultation
                ? EstadoConsultaContraventor.Pendiente
                : EstadoConsultaContraventor.NoAplica,
            CreadoEn = ahora,
            UltimoEventoEn = ahora,
        };
        AplicarCampos(c, item);
        c.EnCobroCoactivo = EsCobroCoactivo(c.EstadoFuente);
        if (c.NombreInfractor is null && clave.IdentificacionId is { } idNit)
        {
            c.NombreInfractor = await db.IdentificacionesMonitoreadas.Where(n => n.Id == idNit).Select(n => n.NombreTitular).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        }

        c.OrganismoTransitoId = await mapeador.MapearAsync(item.Secretaria, ct).ConfigureAwait(false);
        Plazos.Recalcular(c, motor, ahora);
        db.Comparendos.Add(c);
        db.ComparendoFuentes.Add(NuevaFoto(c, clave, cicloId, item, ahora));
        timeline.Agregar(c, TipoEventoTimeline.Ingreso, $"Ingresó por {Etiqueta(clave.Canal)} ({clave.Etiqueta}).", Actor.Sistema,
            new { canal = clave.Canal.ToString(), clave = clave.Etiqueta, municipio = clave.MunicipioCodigo, ciclo = cicloId });
        timeline.Agregar(c, TipoEventoTimeline.PlazosCalculados, DescribirPlazos(c), Actor.Sistema, PlazosDatos(null, c));
        contadores.Nuevas++;
        return c;
    }

    private async Task ActualizarAsync(Comparendo c, ClaveEmpresa clave, Guid cicloId, ItemFuente item, ComparendoFuente? foto, MotorDePlazos motor, Contadores contadores, CancellationToken ct)
    {
        var ahora = reloj.Ahora;
        var reaparece = foto is null || !foto.Presente;
        var hash = Hash(item);
        if (foto is null)
        {
            foto = NuevaFoto(c, clave, cicloId, item, ahora);
            db.ComparendoFuentes.Add(foto);
            if (c.FuentePrincipal is { } principal && principal != clave.Canal)
            {
                timeline.Agregar(c, TipoEventoTimeline.FuenteAdicional, $"También lo reporta {Etiqueta(clave.Canal)} ({clave.Etiqueta}).", Actor.Sistema,
                    new { canal = clave.Canal.ToString(), clave = clave.Etiqueta });
            }
        }

        foto.Presente = true;
        foto.AusenteDesde = null;
        foto.UltimaVezVistaEn = ahora;
        foto.UltimoCicloId = cicloId;
        var fotoCambio = foto.HashDatos != hash;
        if (fotoCambio)
        {
            foto.DatosNormalizados = JsonSerializer.Serialize(item, Json);
            foto.HashDatos = hash;
        }

        // Un comparendo que solo existía por OCR pasa a tomar sus datos de la fuente automática (S-46).
        c.FuentePrincipal ??= clave.Canal;
        if (c.FuentePrincipal == clave.Canal)
        {
            var cambios = AplicarYRegistrar(c, clave, cicloId, item, true);
            if (cambios.Count > 0)
            {
                await DespuesDeCambiosAsync(c, cambios, motor, ct).ConfigureAwait(false);
                contadores.Actualizadas++;
            }
            else
            {
                contadores.SinCambios++;
            }
        }
        else
        {
            if (fotoCambio)
                AplicarYRegistrar(c, clave, cicloId, item, false);
            contadores.DuplicadasOtraFuente++;
        }

        if (!c.Activo && reaparece)
        {
            if (c.MotivoInactivacion == MotivoInactivacion.DesaparecidoFuente)
            {
                c.Activo = true;
                c.MotivoInactivacion = null;
                c.InactivadoEn = null;
                c.InactivadoPor = null;
                timeline.Agregar(c, TipoEventoTimeline.Reactivado, $"Volvió a aparecer en {Etiqueta(clave.Canal)}: se reactivó.", Actor.Sistema);
                contadores.Reactivadas++;
            }
            else
            {
                timeline.Agregar(c, TipoEventoTimeline.ReaparecioInactivoOperador,
                    $"Volvió a aparecer en {Etiqueta(clave.Canal)}, pero sigue inactivo porque lo inactivó un usuario.", Actor.Sistema);
            }
        }
    }

    /// <summary>Compara campo por campo; registra cada diferencia y, si el canal es la fuente principal, la escribe en el maestro.</summary>
    private List<(string Campo, string? Antes, string? Despues)> AplicarYRegistrar(Comparendo c, ClaveEmpresa clave, Guid cicloId, ItemFuente item, bool aplicar)
    {
        var copia = new Comparendo();
        CopiarCampos(c, copia);
        AplicarCampos(copia, item);
        var cambios = Diferencias(c, copia);
        var ahora = reloj.Ahora;
        foreach (var (campo, antes, despues) in cambios)
        {
            db.ComparendoCambios.Add(new ComparendoCambio
            {
                Id = Guid.CreateVersion7(),
                TenantId = c.TenantId,
                ComparendoId = c.Id,
                Canal = clave.Canal,
                CicloId = cicloId,
                Campo = campo,
                ValorAnterior = antes,
                ValorNuevo = despues,
                AplicadoAMaestro = aplicar,
                RegistradoEn = ahora,
            });
        }

        if (aplicar && cambios.Count > 0)
        {
            AplicarCampos(c, item);
            c.ActualizadoEn = ahora;
        }

        return aplicar ? cambios : [];
    }

    private async Task DespuesDeCambiosAsync(Comparendo c, List<(string Campo, string? Antes, string? Despues)> cambios, MotorDePlazos motor, CancellationToken ct)
    {
        var campos = cambios.Select(x => x.Campo).ToHashSet(StringComparer.Ordinal);
        timeline.Agregar(c, TipoEventoTimeline.DatosActualizados, $"La fuente actualizó: {string.Join(", ", campos)}.", Actor.Sistema,
            cambios.Select(x => new { campo = x.Campo, anterior = x.Antes, nuevo = x.Despues }));

        if (campos.Overlaps(["fecha_notificacion", "fecha_imposicion", "es_fotomulta"]))
        {
            var antes = Plazos.Recalcular(c, motor, reloj.Ahora);
            if (antes is not null)
                timeline.Agregar(c, TipoEventoTimeline.PlazosCalculados, "Se recalcularon los plazos. " + DescribirPlazos(c), Actor.Sistema, PlazosDatos(antes, c));
        }

        if (campos.Contains("secretaria_texto"))
            c.OrganismoTransitoId = await mapeador.MapearAsync(c.SecretariaTexto, ct).ConfigureAwait(false) ?? c.OrganismoTransitoId;

        if (campos.Contains("estado_fuente"))
        {
            var coactivo = EsCobroCoactivo(c.EstadoFuente);
            if (coactivo != c.EnCobroCoactivo)
            {
                c.EnCobroCoactivo = coactivo;
                timeline.Agregar(c, TipoEventoTimeline.CobroCoactivoDetectado,
                    coactivo ? "La fuente lo reporta en cobro coactivo." : "La fuente ya no lo reporta en cobro coactivo.", Actor.Sistema);
            }
        }
    }

    private async Task MarcarAusentesAsync(ClaveEmpresa clave, List<ComparendoFuente> fotos, HashSet<string> presentes, DateOnly hoy, MotorDePlazos motor, Contadores contadores, CancellationToken ct)
    {
        var ausentes = fotos.Where(f => f.Presente).ToList();
        if (ausentes.Count == 0)
            return;

        var ids = ausentes.Select(f => f.ComparendoId).ToList();
        var comparendos = await db.Comparendos.Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        var ahora = reloj.Ahora;
        foreach (var foto in ausentes)
        {
            var c = comparendos[foto.ComparendoId];
            if (presentes.Contains(c.NumeroNormalizado))
                continue;

            foto.Presente = false;
            foto.AusenteDesde = ahora;
            var otras = await db.ComparendoFuentes.Where(f => f.ComparendoId == c.Id && f.Id != foto.Id && f.Presente).ToListAsync(ct).ConfigureAwait(false);
            otras = [.. otras.Where(o => o.Presente)];
            if (otras.Count == 0)
            {
                if (c.Activo)
                {
                    c.Activo = false;
                    c.MotivoInactivacion = MotivoInactivacion.DesaparecidoFuente;
                    c.InactivadoEn = ahora;
                    await db.PortalEnlaces.Where(p => p.ComparendoId == c.Id && p.RevocadoEn == null)
                        .ExecuteUpdateAsync(u => u.SetProperty(p => p.RevocadoEn, ahora).SetProperty(p => p.MotivoRevocacion, "COMPARENDO_INACTIVO"), ct).ConfigureAwait(false);
                    timeline.Agregar(c, TipoEventoTimeline.InactivadoDesaparicion, $"Ya no aparece en ninguna fuente (último canal: {Etiqueta(clave.Canal)}). Se inactivó y se conserva su gestión.", Actor.Sistema);
                    contadores.Inactivadas++;
                }

                var dp = await db.DerechosPeticion.FirstOrDefaultAsync(d => d.ComparendoId == c.Id, ct).ConfigureAwait(false);
                if (dp is { Estado: EstadoDp.Enviado })
                {
                    dp.Estado = EstadoDp.Respondido;
                    dp.RespuestaSentido = SentidoRespuestaOt.Favorable;
                    dp.RespuestaOrigen = OrigenRespuestaOt.AutomaticaDesaparicion;
                    dp.RespuestaFecha = hoy;
                    dp.RespuestaRegistradaEn = ahora;
                    c.EstadoGestion = EstadoGestion.RespuestaOt;
                    timeline.Agregar(c, TipoEventoTimeline.DpFavorableAutomatico,
                        $"El comparendo desapareció de la fuente con el DP {dp.Radicado} enviado: se marcó FAVORABLE automáticamente.", Actor.Sistema,
                        relacionadoTipo: "derecho_peticion", relacionadoId: dp.Id);
                    contadores.DpFavorableAutomatico++;
                }
            }
            else if (c.FuentePrincipal == foto.Canal)
            {
                var nueva = otras.OrderBy(o => Array.IndexOf(Prioridad, o.Canal)).First();
                var anterior = c.FuentePrincipal;
                c.FuentePrincipal = nueva.Canal;
                if (JsonSerializer.Deserialize<ItemFuente>(nueva.DatosNormalizados, Json) is { } datos)
                {
                    var cambios = AplicarYRegistrar(c, clave with { Canal = nueva.Canal }, foto.UltimoCicloId, datos, true);
                    if (cambios.Count > 0)
                        await DespuesDeCambiosAsync(c, cambios, motor, ct).ConfigureAwait(false);
                }

                timeline.Agregar(c, TipoEventoTimeline.CambioFuentePrincipal,
                    $"{Etiqueta(anterior!.Value)} dejó de reportarlo; ahora sus datos vienen de {Etiqueta(nueva.Canal)}.", Actor.Sistema);
            }
        }
    }

    private static ComparendoFuente NuevaFoto(Comparendo c, ClaveEmpresa clave, Guid cicloId, ItemFuente item, DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = c.TenantId,
        ComparendoId = c.Id,
        Canal = clave.Canal,
        IdentificacionMonitoreadaId = clave.IdentificacionId,
        PlacaMonitoreadaId = clave.PlacaId,
        MunicipioCodigo = clave.MunicipioCodigo,
        Presente = true,
        DatosNormalizados = JsonSerializer.Serialize(item, Json),
        HashDatos = Hash(item),
        PrimeraVezEn = ahora,
        UltimaVezVistaEn = ahora,
        UltimoCicloId = cicloId,
    };

    public static TipoIngreso Codigo(CanalIngesta canal) => canal switch
    {
        CanalIngesta.SimitNit => TipoIngreso.SimitNit,
        CanalIngesta.SimitPlaca => TipoIngreso.SimitPlaca,
        CanalIngesta.MunicipalNit => TipoIngreso.MunicipalNit,
        _ => TipoIngreso.MunicipalPlaca,
    };

    public static string Etiqueta(CanalIngesta canal) => canal switch
    {
        CanalIngesta.SimitNit => "SIMIT por NIT",
        CanalIngesta.SimitPlaca => "SIMIT por placa",
        CanalIngesta.MunicipalNit => "la fuente municipal por NIT",
        _ => "la fuente municipal por placa",
    };

    /// <summary>Cobro coactivo: el estado de la fuente contiene «COACTIV» (S-52; el patrón se ajusta cuando se conozcan los valores reales).</summary>
    public static bool EsCobroCoactivo(string? estado) => TextoNormalizado.De(estado).Contains("COACTIV", StringComparison.Ordinal);

    private static string Hash(ItemFuente item) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(item, Json))));

    /// <summary>La fuente sobrescribe todos sus campos (RN-024); el número normalizado no cambia.</summary>
    public static void AplicarCampos(Comparendo c, ItemFuente i)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(i);
        c.Numero = i.Numero.Trim();
        c.Placa = Placa.Normalizar(i.Placa) ?? i.Placa?.Trim().ToUpperInvariant();
        c.TipoDocumentoInfractor = i.TipoDocumentoInfractor;
        c.NumeroDocumentoInfractor = i.NumeroDocumentoInfractor?.Trim();
        c.NombreInfractor = i.NombreInfractor?.Trim() ?? c.NombreInfractor;
        c.CodigoInfraccion = i.CodigoInfraccion is { Length: > 10 } largo ? largo[..10] : i.CodigoInfraccion?.Trim();
        c.DescripcionInfraccion = i.DescripcionInfraccion?.Trim();
        c.DireccionInfraccion = i.DireccionInfraccion?.Trim();
        c.FechaImposicion = i.FechaImposicion;
        c.HoraImposicion = i.HoraImposicion;
        c.FechaNotificacion = i.FechaNotificacion;
        c.EsFotomulta = i.EsFotomulta;
        c.SecretariaTexto = i.Secretaria?.Trim();
        c.TipoVehiculo = i.TipoVehiculo?.Trim();
        c.ServicioVehiculo = i.ServicioVehiculo?.Trim();
        c.ValorCapital = i.ValorCapital;
        c.ValorAdicional = i.ValorAdicional;
        c.ValorTotal = i.ValorTotal;
        c.EstadoFuente = i.EstadoFuente?.Trim();
        c.FotoEvidenciaUrl = i.FotoEvidenciaUrl;
    }

    private static void CopiarCampos(Comparendo de, Comparendo a)
    {
        a.Numero = de.Numero;
        a.Placa = de.Placa;
        a.TipoDocumentoInfractor = de.TipoDocumentoInfractor;
        a.NumeroDocumentoInfractor = de.NumeroDocumentoInfractor;
        a.NombreInfractor = de.NombreInfractor;
        a.CodigoInfraccion = de.CodigoInfraccion;
        a.DescripcionInfraccion = de.DescripcionInfraccion;
        a.DireccionInfraccion = de.DireccionInfraccion;
        a.FechaImposicion = de.FechaImposicion;
        a.HoraImposicion = de.HoraImposicion;
        a.FechaNotificacion = de.FechaNotificacion;
        a.EsFotomulta = de.EsFotomulta;
        a.SecretariaTexto = de.SecretariaTexto;
        a.TipoVehiculo = de.TipoVehiculo;
        a.ServicioVehiculo = de.ServicioVehiculo;
        a.ValorCapital = de.ValorCapital;
        a.ValorAdicional = de.ValorAdicional;
        a.ValorTotal = de.ValorTotal;
        a.EstadoFuente = de.EstadoFuente;
        a.FotoEvidenciaUrl = de.FotoEvidenciaUrl;
    }

    private static List<(string Campo, string? Antes, string? Despues)> Diferencias(Comparendo a, Comparendo b)
    {
        var lista = new List<(string, string?, string?)>();
        void Comparar(string campo, object? x, object? y)
        {
            var sx = Texto(x);
            var sy = Texto(y);
            if (!string.Equals(sx, sy, StringComparison.Ordinal))
                lista.Add((campo, sx, sy));
        }

        Comparar("numero", a.Numero, b.Numero);
        Comparar("placa", a.Placa, b.Placa);
        Comparar("tipo_documento_infractor", a.TipoDocumentoInfractor, b.TipoDocumentoInfractor);
        Comparar("numero_documento_infractor", a.NumeroDocumentoInfractor, b.NumeroDocumentoInfractor);
        Comparar("nombre_infractor", a.NombreInfractor, b.NombreInfractor);
        Comparar("codigo_infraccion", a.CodigoInfraccion, b.CodigoInfraccion);
        Comparar("descripcion_infraccion", a.DescripcionInfraccion, b.DescripcionInfraccion);
        Comparar("direccion_infraccion", a.DireccionInfraccion, b.DireccionInfraccion);
        Comparar("fecha_imposicion", a.FechaImposicion, b.FechaImposicion);
        Comparar("hora_imposicion", a.HoraImposicion, b.HoraImposicion);
        Comparar("fecha_notificacion", a.FechaNotificacion, b.FechaNotificacion);
        Comparar("es_fotomulta", a.EsFotomulta, b.EsFotomulta);
        Comparar("secretaria_texto", a.SecretariaTexto, b.SecretariaTexto);
        Comparar("tipo_vehiculo", a.TipoVehiculo, b.TipoVehiculo);
        Comparar("servicio_vehiculo", a.ServicioVehiculo, b.ServicioVehiculo);
        Comparar("valor_capital", a.ValorCapital, b.ValorCapital);
        Comparar("valor_adicional", a.ValorAdicional, b.ValorAdicional);
        Comparar("valor_total", a.ValorTotal, b.ValorTotal);
        Comparar("estado_fuente", a.EstadoFuente, b.EstadoFuente);
        Comparar("foto_evidencia_url", a.FotoEvidenciaUrl, b.FotoEvidenciaUrl);
        return lista;
    }

    private static string? Texto(object? v) => v switch
    {
        null => null,
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString("HH:mm", CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.00", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        _ => v.ToString(),
    };

    public static string DescribirPlazos(Comparendo c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (c.FechaLimiteGestion is null)
            return "Sin fecha de notificación: todavía no corre ningún plazo.";
        string F(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";
        return $"Límite de gestión {F(c.FechaLimiteGestion)}; 50% hasta {F(c.FechaFinDescuento50)}; 25% hasta {F(c.FechaFinDescuento25)}; caducidad {F(c.FechaCaducidad)}; prescripción {F(c.FechaPrescripcion)}.";
    }

    public static object PlazosDatos(FechasLegales? antes, Comparendo c) => new
    {
        anteriores = antes,
        nuevas = Plazos.Fechas(c),
    };
}
