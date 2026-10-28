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

    /// <summary>Acción de la auditoría cuando core-api lee las partes por RPC para crear el trámite (HU #14049).</summary>
    public const string AccionLecturaParaRadicar = "lectura_para_radicar";

    private const string CtxDocumento = "persona.documento";
    private const string CtxNombre = "persona.nombre";
    private const string CtxCorreo = "persona.correo";
    private const string CtxCelular = "persona.celular";
    private const string CtxDireccion = "persona.direccion";

    /// <summary>AAD de cada columna: ligado a la solicitud y a la parte (<c>parte:ordinal</c>) de ESA fila.</summary>
    private static ContextoCifrado Ctx(string columna, Guid solicitudId, string parte, short ordinal) => new(columna, solicitudId, parte, ordinal);

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
        persona.DocumentoCifrado = cifrado.Cifrar(datos.Documento, Ctx(CtxDocumento, solicitudId, datos.Parte, datos.Ordinal))!;
        persona.DocumentoHash = hashNuevo;
        persona.NombreCifrado = cifrado.Cifrar(datos.Nombre, Ctx(CtxNombre, solicitudId, datos.Parte, datos.Ordinal));
        persona.CorreoCifrado = cifrado.Cifrar(datos.Correo, Ctx(CtxCorreo, solicitudId, datos.Parte, datos.Ordinal));
        persona.CelularCifrado = cifrado.Cifrar(datos.Celular, Ctx(CtxCelular, solicitudId, datos.Parte, datos.Ordinal));
        persona.DireccionCifrada = cifrado.Cifrar(datos.Direccion, Ctx(CtxDireccion, solicitudId, datos.Parte, datos.Ordinal));
        persona.UpdatedAt = ahora;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return persona.Id;
    }

    /// <summary>Lee y descifra las personas de la solicitud y deja constancia de quién las leyó.</summary>
    public async Task<IReadOnlyList<DatosPersona>> LeerAsync(Guid solicitudId, string actor, CancellationToken ct, string accion = AccionLectura)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(accion);
        var personas = await db.Set<PersonaSolicitante>().AsNoTracking().Where(p => p.SolicitudId == solicitudId)
            .OrderBy(p => p.Parte).ThenBy(p => p.Ordinal).ToListAsync(ct).ConfigureAwait(false);

        db.Add(new AccesoPiiLog { Actor = actor.Length > 120 ? actor[..120] : actor, Accion = accion, SolicitudId = solicitudId, OcurrioEn = reloj.GetUtcNow() });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return personas.Select(p => new DatosPersona(
            p.Parte, p.Ordinal, p.TipoDocumento,
            cifrado.Descifrar(p.DocumentoCifrado, Ctx(CtxDocumento, solicitudId, p.Parte, p.Ordinal)) ?? string.Empty,
            cifrado.Descifrar(p.NombreCifrado, Ctx(CtxNombre, solicitudId, p.Parte, p.Ordinal)),
            cifrado.Descifrar(p.CorreoCifrado, Ctx(CtxCorreo, solicitudId, p.Parte, p.Ordinal)),
            cifrado.Descifrar(p.CelularCifrado, Ctx(CtxCelular, solicitudId, p.Parte, p.Ordinal)),
            cifrado.Descifrar(p.DireccionCifrada, Ctx(CtxDireccion, solicitudId, p.Parte, p.Ordinal)))).ToList();
    }
}
