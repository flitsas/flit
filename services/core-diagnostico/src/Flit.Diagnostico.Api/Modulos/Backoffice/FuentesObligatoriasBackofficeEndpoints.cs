using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Catalogos;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// <c>GET/PUT /backoffice/fuentes-obligatorias</c> (ADR-0078). Catálogo global de FLIT: la ruta va en un grupo
/// <c>/backoffice</c> con <see cref="PoliticaBackoffice"/> de configuración (SuperAdmin de cualquier compañía, o FLIT con
/// <c>diagnostico.backoffice.ver</c>; ajustes 2026-11-11, punto 1) y sin empresa explícita. No lee ni escribe datos de una
/// compañía, así que no usa <c>?empresa=</c>, no inserta en <c>lecturas_backoffice</c> y no abre
/// <c>AutorizarEscrituraDe</c>. El PUT crea la cabecera (uuid v7) si no existe y, si existe, la conserva y reemplaza
/// las líneas. <c>fuentes: []</c> deja la cabecera. Cada guardado deja el evento <see cref="AuditoriaConfiguracionBackoffice"/>
/// (usuario y compañía del actor). Los rechazos los decide <see cref="ResolucionFuentesObligatorias"/>.
/// </summary>
internal static class FuentesObligatoriasBackofficeEndpoints
{
    public const string Ruta = "/fuentes-obligatorias";
    public const string CodigoCuerpoInvalido = "CUERPO_INVALIDO";
    public const string CodigoFuenteDesconocida = "FUENTE_DESCONOCIDA";

    /// <summary>La ruta va en un grupo <c>/backoffice</c> con la política y sin el filtro de empresa explícita.</summary>
    public static void Map(RouteGroupBuilder backofficeSinEmpresa)
    {
        backofficeSinEmpresa.MapGet(Ruta, Listar).WithName("ListarFuentesObligatoriasBackoffice");
        backofficeSinEmpresa.MapPut(Ruta, Guardar).WithName("GuardarFuentesObligatoriasBackoffice");
    }

    private static async Task<IResult> Listar(DiagnosticoDb db, CancellationToken ct)
    {
        var cabeceras = await db.Set<DefinicionFuentesObligatorias>().AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var lineas = await db.Set<FuenteDefinicionObligatoria>().AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var porId = lineas.GroupBy(l => l.DefinicionId).ToDictionary(g => g.Key, g => g.Select(l => l.Fuente).OrderBy(f => f).ToArray());
        var definiciones = cabeceras
            .OrderBy(c => c.Familia is null)
            .ThenBy(c => c.Familia ?? c.TipoTramiteCodigo, StringComparer.Ordinal)
            .Select(c => Definicion(c.Familia, c.TipoTramiteCodigo, porId.GetValueOrDefault(c.Id) ?? []))
            .ToArray();
        return Results.Ok(new ListaDefinicionesDto(definiciones));
    }

    private static async Task<IResult> Guardar(
        JsonElement cuerpo, DiagnosticoDb db, UsuarioActual usuario, TimeProvider reloj, ILoggerFactory logs, CancellationToken ct)
    {
        if (cuerpo.ValueKind != JsonValueKind.Object)
            throw CuerpoInvalido("El cuerpo debe ser un objeto JSON.");

        var familia = LeerTexto(cuerpo, "familia");
        var tipo = LeerTexto(cuerpo, "tipo_tramite_codigo");

        // La forma del alcance va antes que las fuentes. Esta lista solo deja que ValidarGuardado distinga
        // DEFINICION_AMBIGUA; el guardado usa el arreglo del cuerpo.
        var forma = ResolucionFuentesObligatorias.ValidarGuardado(familia, tipo, [Fuente.SimitPropietario]);
        if (forma?.Codigo == RechazoGuardadoFuentes.DefinicionAmbigua)
            throw Rechazo(forma);

        if (!cuerpo.TryGetProperty("fuentes", out var fuentesEl) || fuentesEl.ValueKind != JsonValueKind.Array)
            throw CuerpoInvalido("El cuerpo debe incluir el arreglo fuentes.");

        var leidas = new List<Fuente>();
        foreach (var item in fuentesEl.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !CodigosDeEnum.TryLeer<Fuente>(item.GetString(), out var fuente))
            {
                throw new ReglaException(CodigoFuenteDesconocida, "Hay una fuente que no está en el catálogo.", StatusCodes.Status422UnprocessableEntity)
                {
                    Campo = RechazoGuardadoFuentes.CampoFuentes,
                };
            }

            leidas.Add(fuente);
        }

        var distintas = leidas.Distinct().OrderBy(f => f).ToList();
        var rechazo = ResolucionFuentesObligatorias.ValidarGuardado(familia, tipo, distintas);
        if (rechazo is not null)
            throw Rechazo(rechazo);

        if (usuario.Id == Guid.Empty)
            throw ReglaException.Prohibido("SIN_USUARIO", "El token no identifica al usuario: la definición no se puede registrar.");

        var ahora = reloj.GetUtcNow();
        var existente = await BuscarAsync(db, familia, tipo, ct).ConfigureAwait(false);
        Guid id;
        if (existente is null)
        {
            id = Guid.CreateVersion7(ahora);
            db.Add(new DefinicionFuentesObligatorias
            {
                Id = id,
                Familia = familia,
                TipoTramiteCodigo = tipo,
                ActualizadoEn = ahora,
                ActualizadoPor = usuario.Id,
            });
        }
        else
        {
            id = existente.Id;
            existente.ActualizadoEn = ahora;
            existente.ActualizadoPor = usuario.Id;
        }

        await ReemplazarLineasAsync(db, id, distintas, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        AuditoriaConfiguracionBackoffice.Registrar(logs.CreateLogger(typeof(FuentesObligatoriasBackofficeEndpoints)), usuario,
            AuditoriaConfiguracionBackoffice.SeccionFuentesObligatorias, empresaObjetivo: null,
            [familia is not null ? "familia" : "tipo_tramite_codigo", "fuentes"]);
        return Results.Ok(Definicion(familia, tipo, distintas));
    }

    private static async Task<DefinicionFuentesObligatorias?> BuscarAsync(DiagnosticoDb db, string? familia, string? tipo, CancellationToken ct) =>
        familia is not null
            ? await db.Set<DefinicionFuentesObligatorias>().SingleOrDefaultAsync(d => d.Familia == familia, ct).ConfigureAwait(false)
            : await db.Set<DefinicionFuentesObligatorias>().SingleOrDefaultAsync(d => d.TipoTramiteCodigo == tipo, ct).ConfigureAwait(false);

    private static async Task ReemplazarLineasAsync(DiagnosticoDb db, Guid id, IReadOnlyList<Fuente> fuentes, CancellationToken ct)
    {
        var actuales = await db.Set<FuenteDefinicionObligatoria>().Where(l => l.DefinicionId == id).ToListAsync(ct).ConfigureAwait(false);
        var conservar = fuentes.ToHashSet();
        foreach (var linea in actuales)
        {
            if (!conservar.Contains(linea.Fuente))
                db.Remove(linea);
        }

        var presentes = actuales.Select(l => l.Fuente).ToHashSet();
        foreach (var fuente in fuentes)
        {
            if (!presentes.Contains(fuente))
                db.Add(new FuenteDefinicionObligatoria { DefinicionId = id, Fuente = fuente });
        }
    }

    /// <summary>Ausente o JSON null es null. Un valor que no es texto no cumple el patrón del alcance.</summary>
    private static string? LeerTexto(JsonElement cuerpo, string nombre)
    {
        if (!cuerpo.TryGetProperty(nombre, out var valor) || valor.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return valor.ValueKind == JsonValueKind.String ? valor.GetString() : string.Empty;
    }

    private static DefinicionFuentesObligatoriasDto Definicion(string? familia, string? tipo, IReadOnlyList<Fuente> fuentes) =>
        new(familia, tipo, [.. fuentes.Select(CodigosDeEnum.Codigo)]);

    private static ReglaException CuerpoInvalido(string mensaje) =>
        new(CodigoCuerpoInvalido, mensaje, StatusCodes.Status400BadRequest);

    private static ReglaException Rechazo(RechazoGuardadoFuentes rechazo) =>
        new(
            rechazo.Codigo,
            Mensaje(rechazo.Codigo),
            rechazo.Codigo == RechazoGuardadoFuentes.DefinicionAmbigua ? StatusCodes.Status400BadRequest : StatusCodes.Status422UnprocessableEntity)
        {
            Campo = rechazo.Campo,
        };

    private static string Mensaje(string codigo) => codigo switch
    {
        RechazoGuardadoFuentes.DefinicionAmbigua => "Indica una familia o un tipo de trámite, no los dos ni ninguno. El código solo usa mayúsculas, dígitos y guion bajo.",
        RechazoGuardadoFuentes.FuenteRuntNoConfigurable => "RUNT no se configura en esta lista: sigue siendo obligatorio en todo diagnóstico.",
        RechazoGuardadoFuentes.SimitProhibidoEnUnilateral => "TRASPASO_UNILATERAL no admite una fuente SIMIT.",
        RechazoGuardadoFuentes.PisoNoDispensable => "Esta definición no puede quitar SIMIT_PROPIETARIO.",
        _ => "La definición no se puede guardar.",
    };
}

/// <summary>Listado del catálogo: familias por código y después tipos por código.</summary>
internal sealed record ListaDefinicionesDto(
    [property: JsonPropertyName("definiciones")] IReadOnlyList<DefinicionFuentesObligatoriasDto> Definiciones);

/// <summary>Una definición guardada. La clave de alcance que no aplica es null. <c>fuentes</c> va en el orden del enum, sin RUNT.</summary>
internal sealed record DefinicionFuentesObligatoriasDto(
    [property: JsonPropertyName("familia")] string? Familia,
    [property: JsonPropertyName("tipo_tramite_codigo")] string? TipoTramiteCodigo,
    [property: JsonPropertyName("fuentes")] IReadOnlyList<string> Fuentes);
