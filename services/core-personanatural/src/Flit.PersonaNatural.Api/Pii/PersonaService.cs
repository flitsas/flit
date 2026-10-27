using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Sesiones;
using Microsoft.EntityFrameworkCore;

namespace Flit.PersonaNatural.Api.Pii;

/// <summary>Datos de una persona de la solicitud, en claro. Solo existe en memoria: en la base queda cifrado.</summary>
public sealed record DatosPersona(string Parte, short Ordinal, string TipoDocumento, string Documento, string? Nombre, string? Correo, string? Celular, string? Direccion);

/// <summary>
/// Única puerta de entrada y salida de los datos personales de la solicitud (HU #14037). Al guardar cifra cada campo y deja
/// la huella HMAC del documento para buscar sin descifrar; al leer, cada lectura queda en <c>acceso_pii_log</c> con quién,
/// qué y cuándo (nunca con el dato).
/// </summary>
public sealed class PersonaService(PersonaNaturalDb db, CifradoPii cifrado, PiiHasher hasher, TimeProvider reloj)
{
    public const string AccionLectura = "lectura_persona";

    private const string CtxDocumento = "persona.documento";
    private const string CtxNombre = "persona.nombre";
    private const string CtxCorreo = "persona.correo";
    private const string CtxCelular = "persona.celular";
    private const string CtxDireccion = "persona.direccion";

    /// <summary>Crea o reemplaza la persona de esa parte y posición en la solicitud.</summary>
    public async Task<Guid> GuardarAsync(Guid solicitudId, DatosPersona datos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var ahora = reloj.GetUtcNow();
        var persona = await db.Set<PersonaSolicitante>()
            .FirstOrDefaultAsync(p => p.SolicitudId == solicitudId && p.Parte == datos.Parte && p.Ordinal == datos.Ordinal, ct).ConfigureAwait(false);
        if (persona is null)
        {
            persona = new PersonaSolicitante { Id = Guid.CreateVersion7(), SolicitudId = solicitudId, Parte = datos.Parte, Ordinal = datos.Ordinal, CreatedAt = ahora };
            db.Add(persona);
        }

        // HU #14044: la validación de identidad es de ESA persona. Con otro documento deja de valer: se olvida la validación
        // (y sus reenvíos) para que hubiera que pedir una nueva; si no, cambiar la cédula de una parte ya aprobada la dejaría
        // «validada» sin haberlo sido.
        var hashNuevo = hasher.HashCedula(datos.Documento);
        if (persona.DocumentoHash.Length > 0 && !persona.DocumentoHash.AsSpan().SequenceEqual(hashNuevo))
        {
            persona.ValidacionId = null;
            persona.ValidacionEstado = "sin_solicitar";
            persona.ValidacionVigenteHasta = null;
            persona.Reenvios = 0;
            persona.UltimoReenvioEn = null;
        }

        persona.TipoDocumento = datos.TipoDocumento;
        persona.DocumentoCifrado = cifrado.Cifrar(datos.Documento, CtxDocumento)!;
        persona.DocumentoHash = hashNuevo;
        persona.NombreCifrado = cifrado.Cifrar(datos.Nombre, CtxNombre);
        persona.CorreoCifrado = cifrado.Cifrar(datos.Correo, CtxCorreo);
        persona.CelularCifrado = cifrado.Cifrar(datos.Celular, CtxCelular);
        persona.DireccionCifrada = cifrado.Cifrar(datos.Direccion, CtxDireccion);
        persona.UpdatedAt = ahora;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return persona.Id;
    }

    /// <summary>Lee y descifra las personas de la solicitud y deja constancia de quién las leyó.</summary>
    public async Task<IReadOnlyList<DatosPersona>> LeerAsync(Guid solicitudId, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var personas = await db.Set<PersonaSolicitante>().AsNoTracking().Where(p => p.SolicitudId == solicitudId)
            .OrderBy(p => p.Parte).ThenBy(p => p.Ordinal).ToListAsync(ct).ConfigureAwait(false);

        db.Add(new AccesoPiiLog { Actor = actor.Length > 120 ? actor[..120] : actor, Accion = AccionLectura, SolicitudId = solicitudId, OcurrioEn = reloj.GetUtcNow() });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return personas.Select(p => new DatosPersona(
            p.Parte, p.Ordinal, p.TipoDocumento,
            cifrado.Descifrar(p.DocumentoCifrado, CtxDocumento) ?? string.Empty,
            cifrado.Descifrar(p.NombreCifrado, CtxNombre),
            cifrado.Descifrar(p.CorreoCifrado, CtxCorreo),
            cifrado.Descifrar(p.CelularCifrado, CtxCelular),
            cifrado.Descifrar(p.DireccionCifrada, CtxDireccion))).ToList();
    }
}
