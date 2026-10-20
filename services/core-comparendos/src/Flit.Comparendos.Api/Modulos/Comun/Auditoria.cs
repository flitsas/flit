using System.Text.Json;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;

namespace Flit.Comparendos.Api.Modulos.Comun;

/// <summary>Un campo de configuración que cambió.</summary>
public sealed record CambioCampo(string Campo, object? Anterior, object? Nuevo);

/// <summary>
/// Auditoría de configuración (RN-010): toda modificación queda con usuario, fecha y valor anterior y nuevo. Se agrega
/// al contexto; la guarda el mismo SaveChanges de la operación.
/// </summary>
public sealed class Auditoria(ComparendosDb db, UsuarioActual usuario, Reloj reloj)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Registrar(Guid? tenantId, string entidad, object entidadId, string accion, params CambioCampo[] cambios)
    {
        ArgumentNullException.ThrowIfNull(entidadId);
        db.AuditoriasConfiguracion.Add(new AuditoriaConfiguracion
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Entidad = entidad,
            EntidadId = entidadId.ToString() ?? string.Empty,
            Accion = accion,
            Cambios = JsonSerializer.Serialize(cambios.Select(c => new { campo = c.Campo, anterior = c.Anterior, nuevo = c.Nuevo }), Json),
            UsuarioId = usuario.Id,
            UsuarioNombre = usuario.Nombre,
            RegistradoEn = reloj.Ahora,
        });
    }

    /// <summary>Compara y devuelve solo lo que cambió.</summary>
    public static CambioCampo[] Diferencias(params (string Campo, object? Anterior, object? Nuevo)[] campos) =>
        campos.Where(c => !Equals(c.Anterior, c.Nuevo)).Select(c => new CambioCampo(c.Campo, c.Anterior, c.Nuevo)).ToArray();
}

/// <summary>Página de resultados con el total, para el pie estándar de las tablas.</summary>
public sealed record Pagina<T>(IReadOnlyList<T> Items, int Total, int PaginaActual, int TamanoPagina);

public static class Paginacion
{
    public static (int Pagina, int Tamano) Normalizar(int? pagina, int? tamano, int porDefecto = 25)
    {
        var p = pagina is > 0 ? pagina.Value : 1;
        var t = tamano is > 0 and <= 200 ? tamano.Value : porDefecto;
        return (p, t);
    }
}
