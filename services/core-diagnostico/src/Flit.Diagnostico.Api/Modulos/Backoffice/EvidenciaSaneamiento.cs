using System.Net.Http.Headers;
using System.Text;
using Flit.Diagnostico.Api.Infraestructura.Archivos;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Saneamiento;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Saneamiento;
using Microsoft.EntityFrameworkCore;
using SaneamientoEntidad = Flit.Diagnostico.Api.Modulos.Saneamiento.Saneamiento;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Evidencia en PDF de un renglón (plan §6.4, ADR-0087 D-6; HU #14576 AC45–AC47; SP-12, SP-16, SP-17, SP-18, SP-19). Subida y
/// reemplazo desde el backoffice:
/// <list type="number">
/// <item><c>Content-Length</c> desconocido o mayor que el máximo más el margen del multipart: 413 <see cref="CodigoMuyGrande"/> sin leer
/// el cuerpo (la ruta además lleva <c>RequestSizeLimit</c> y <c>FormOptions</c>).</item>
/// <item>Cadena empresa → saneamiento → renglón (404), EN_GESTION (409 <c>NO_EDITABLE</c>) y tope de <see cref="TopeReemplazos"/>
/// reemplazos (409 <see cref="CodigoDemasiadosReemplazos"/>), antes de leer el archivo.</item>
/// <item>Un solo archivo en el campo <c>archivo</c>, no vacío (400 <see cref="CodigoRequerido"/>), de hasta el máximo (413), con
/// <c>Content-Type</c> <c>application/pdf</c> sin parámetros, extensión <c>.pdf</c> y firma de <see cref="FirmasArchivo.EsPdf"/> (422
/// <see cref="CodigoNoEsPdf"/>). No se abre ni se analiza.</item>
/// <item>El archivo se guarda en el almacén de la empresa explícita, con un nombre generado por el servidor
/// (<c>evidencia-{renglon:N}.pdf</c>), FUERA de la transacción; luego, en la transacción, el renglón apunta al archivo nuevo (el anterior
/// queda huérfano, ADR-0087) y el historial suma <c>EVIDENCIA_CARGADA</c>.</item>
/// </list>
/// El nombre original solo va a <c>evidencia_nombre</c>, sin rutas ni caracteres de control ni marcas bidireccionales, recortado a 200.
/// Nunca se registra el nombre ni el contenido (C21): el log <c>EvidenciaReemplazada</c> lleva ids y tamaño.
/// </summary>
internal sealed partial class EvidenciaSaneamiento(
    DiagnosticoDb db, GestionSaneamientoBackoffice gestion, IServiceScopeFactory scopes, IConfiguration configuracion, UsuarioActual usuario,
    ILogger<EvidenciaSaneamiento> logger)
{
    public const string ClaveMaximo = "Diagnostico:Saneamiento:EvidenciaMaximoBytes";
    public const long MaximoPorDefecto = 10_485_760;

    /// <summary>Margen del multipart sobre el máximo del archivo (SP-16).</summary>
    public const long Margen = 64 * 1024;

    public const int TopeReemplazos = 20;
    public const string CodigoMuyGrande = "ARCHIVO_MUY_GRANDE";
    public const string CodigoRequerido = "ARCHIVO_REQUERIDO";
    public const string CodigoNoEsPdf = "ARCHIVO_NO_ES_PDF";
    public const string CodigoDemasiadosReemplazos = "DEMASIADOS_REEMPLAZOS";
    public const string Campo = "archivo";
    public const string ContentTypePdf = "application/pdf";

    /// <summary>Tipo del archivo en el almacén (<c>ck_archivo_local_tipo</c> admite PDF, XLSX y ZIP; D-84).</summary>
    public const string TipoAlmacen = "PDF";

    private const int LargoNombre = 200;

    public static long Maximo(IConfiguration configuracion) =>
        configuracion?.GetValue<long?>(ClaveMaximo) is { } valor && valor > 0 ? valor : MaximoPorDefecto;

    /// <summary>PUT: sube o reemplaza la evidencia; devuelve el renglón con su version nueva.</summary>
    public async Task<RenglonClienteDto> SubirAsync(Guid empresa, Guid sid, Guid rid, HttpRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var maximo = Maximo(configuracion);
        if (request.ContentLength is not { } largo || largo > maximo + Margen)
            throw MuyGrande(maximo);
        await ExigirSubibleAsync(empresa, sid, rid, ct).ConfigureAwait(false);
        var (nombre, contenido) = await LeerArchivoAsync(request, maximo, ct).ConfigureAwait(false);

        ArchivoGuardado guardado;
        await using (var ambito = AmbitoDeLaEmpresa.Abrir(scopes, empresa))
        {
            var dbEmpresa = ambito.ServiceProvider.GetRequiredService<DiagnosticoDb>();
            var almacen = ambito.ServiceProvider.GetRequiredService<IFabricaAlmacen>().Para(dbEmpresa);
            guardado = await almacen.GuardarAsync(empresa, TipoAlmacen, $"evidencia-{rid:N}.pdf", ContentTypePdf, contenido, ct).ConfigureAwait(false);
        }

        await gestion.EscribirAsync(empresa, sid, async (s, ahora) =>
        {
            var renglon = await gestion.RenglonParaEditarAsync(empresa, sid, rid, ct).ConfigureAwait(false);
            GestionSaneamientoBackoffice.ExigirEditable(s);
            var reemplazo = renglon.EvidenciaReferencia is not null || await YaTuvoEvidenciaAsync(empresa, sid, rid, ct).ConfigureAwait(false);
            if (reemplazo)
            {
                if (renglon.EvidenciaReemplazos >= TopeReemplazos)
                    throw DemasiadosReemplazos();
                renglon.EvidenciaReemplazos++;
            }

            renglon.EvidenciaReferencia = guardado.Referencia;
            renglon.EvidenciaSha256 = guardado.Sha256;
            renglon.EvidenciaTamano = guardado.Tamano;
            renglon.EvidenciaNombre = nombre;
            renglon.EvidenciaSubidaPor = usuario.Id;
            renglon.EvidenciaSubidaEn = ahora;
            renglon.ActualizadoPor = usuario.Id;
            renglon.ActualizadoEn = ahora;
            EscritorSaneamiento.RegistrarEdicion(db, s, TipoEventoSaneamiento.EvidenciaCargada, usuario.Id, ahora, renglon);
            if (reemplazo)
                LogEvidenciaReemplazada(logger, s.Id, renglon.Id, guardado.Tamano);
        }, ct).ConfigureAwait(false);
        return await gestion.RenglonDtoAsync(empresa, sid, rid, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// GET de FLIT: el PDF con el nombre <c>evidencia-{placa}-{fuente}.pdf</c>, en stream desde el almacén (AC47, CR-10); 404 si el
    /// renglón no tiene evidencia. El ámbito de la empresa se libera al terminar la respuesta, no al volver de aquí: el flujo del
    /// almacén puede depender de él mientras se envía.
    /// </summary>
    public async Task<IResult> DescargarAsync(Guid empresa, Guid sid, Guid rid, HttpResponse respuesta, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        var renglon = await db.DeLaEmpresa<RenglonSaneamiento>(empresa)
            .Where(r => r.Id == rid && r.SaneamientoId == sid && r.QuitadoEn == null && r.EvidenciaReferencia != null)
            .Select(r => new { r.EvidenciaReferencia, r.Fuente })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? throw GestionSaneamientoBackoffice.NoEncontrado();
        var placa = await (
                from s in db.DeLaEmpresa<SaneamientoEntidad>(empresa)
                join v in db.DeLaEmpresa<VehiculoDiagnostico>(empresa) on new { s.TenantId, Id = s.VehiculoId } equals new { v.TenantId, v.Id }
                where s.Id == sid
                select v.Placa ?? v.IdentificadorNorm)
            .SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? throw GestionSaneamientoBackoffice.NoEncontrado();
        var ambito = AmbitoDeLaEmpresa.Abrir(scopes, empresa);
        respuesta.RegisterForDisposeAsync(ambito);
        var dbEmpresa = ambito.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        var flujo = await ambito.ServiceProvider.GetRequiredService<IFabricaAlmacen>().Para(dbEmpresa)
            .AbrirAsync(renglon.EvidenciaReferencia!, ct).ConfigureAwait(false)
            ?? throw GestionSaneamientoBackoffice.NoEncontrado();
        return Results.Stream(flujo, ContentTypePdf, NombreDescarga(placa, renglon.Fuente));
    }

    /// <summary>
    /// <c>evidencia-{placa}-{fuente}.pdf</c>: la placa reducida a <c>[A-Z0-9]</c> (SP-18) y la fuente del enum; nunca el original. Sin
    /// placa ni identificador, <c>evidencia-sin-placa-{fuente}.pdf</c> (AP-3).
    /// </summary>
    public static string NombreDescarga(string? placa, FuenteSaneamiento fuente) =>
        $"evidencia-{PdfDeuda.PlacaSegura(placa ?? string.Empty)}-{CodigosDeEnum.Codigo(fuente)}.pdf";

    /// <summary>
    /// El nombre original para mostrar: solo el último segmento (sin rutas), sin caracteres de control ni marcas bidireccionales, sin
    /// espacios en los extremos y con hasta 200 puntos de código; si no queda nada, <c>evidencia.pdf</c>.
    /// </summary>
    internal static string NombreSeguro(string? original)
    {
        var nombre = (original ?? string.Empty).Replace('\\', '/');
        nombre = nombre[(nombre.LastIndexOf('/') + 1)..];
        var limpio = new StringBuilder(nombre.Length);
        var cuenta = 0;
        foreach (var rune in nombre.EnumerateRunes())
        {
            if (Rune.IsControl(rune) || rune.Value is (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069) or 0x200E or 0x200F or 0x061C)
                continue;
            if (cuenta++ == LargoNombre)
                break;
            limpio.Append(rune.ToString());
        }

        var resultado = limpio.ToString().Trim();
        return resultado.Length == 0 ? "evidencia.pdf" : resultado;
    }

    private async Task ExigirSubibleAsync(Guid empresa, Guid sid, Guid rid, CancellationToken ct)
    {
        var estado = await db.DeLaEmpresa<SaneamientoEntidad>(empresa).Where(s => s.Id == sid).Select(s => (EstadoSaneamiento?)s.Estado)
            .SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? throw GestionSaneamientoBackoffice.NoEncontrado();
        var renglon = await db.DeLaEmpresa<RenglonSaneamiento>(empresa)
            .Where(r => r.Id == rid && r.SaneamientoId == sid && r.QuitadoEn == null)
            .Select(r => new { ConEvidencia = r.EvidenciaReferencia != null, r.EvidenciaReemplazos })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? throw GestionSaneamientoBackoffice.NoEncontrado();
        if (estado != EstadoSaneamiento.EnGestion)
            throw ReglaException.Conflicto(GestionSaneamientoBackoffice.CodigoNoEditable, "El saneamiento solo se edita mientras está en gestión.");
        if (renglon.EvidenciaReemplazos >= TopeReemplazos
            && (renglon.ConEvidencia || await YaTuvoEvidenciaAsync(empresa, sid, rid, ct).ConfigureAwait(false)))
            throw DemasiadosReemplazos();
    }

    /// <summary>
    /// CR-3 (SP-19): si el renglón ya recibió una evidencia alguna vez (hay un <c>EVIDENCIA_CARGADA</c> suyo en el historial). Así toda
    /// carga después de la primera cuenta como reemplazo, también la que llega a un renglón sin evidencia porque se quitó: alternar
    /// DELETE y PUT no esquiva el tope ni deja huérfanos sin cota (ADR-0087).
    /// </summary>
    private Task<bool> YaTuvoEvidenciaAsync(Guid empresa, Guid sid, Guid rid, CancellationToken ct) =>
        db.DeLaEmpresa<EventoSaneamiento>(empresa)
            .AnyAsync(e => e.SaneamientoId == sid && e.RenglonId == rid && e.Evento == TipoEventoSaneamiento.EvidenciaCargada, ct);

    private static async Task<(string Nombre, byte[] Contenido)> LeerArchivoAsync(HttpRequest request, long maximo, CancellationToken ct)
    {
        if (!request.HasFormContentType)
            throw Requerido();
        IFormCollection formulario;
        try
        {
            formulario = await request.ReadFormAsync(ct).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            throw MuyGrande(maximo);
        }

        if (formulario.Files.Count != 1 || !string.Equals(formulario.Files[0].Name, Campo, StringComparison.Ordinal))
            throw Requerido();
        var archivo = formulario.Files[0];
        if (archivo.Length == 0)
            throw Requerido();
        if (archivo.Length > maximo)
            throw MuyGrande(maximo);
        var nombre = NombreSeguro(archivo.FileName);
        if (!MediaTypeHeaderValue.TryParse(archivo.ContentType, out var tipo)
            || !string.Equals(tipo.MediaType, ContentTypePdf, StringComparison.OrdinalIgnoreCase)
            || tipo.Parameters.Count > 0
            || !nombre.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw NoEsPdf();

        var contenido = new byte[archivo.Length];
        await using (var flujo = archivo.OpenReadStream())
            await flujo.ReadExactlyAsync(contenido, ct).ConfigureAwait(false);
        if (!FirmasArchivo.EsPdf(contenido))
            throw NoEsPdf();
        return (nombre, contenido);
    }

    private static ReglaException MuyGrande(long maximo) =>
        new(CodigoMuyGrande, $"La evidencia admite hasta {maximo / (1024 * 1024)} MB.", StatusCodes.Status413PayloadTooLarge) { Campo = Campo };

    private static ReglaException Requerido() =>
        new(CodigoRequerido, "Adjunta un solo archivo PDF en el campo archivo.", StatusCodes.Status400BadRequest) { Campo = Campo };

    private static ReglaException NoEsPdf() =>
        new(CodigoNoEsPdf, "La evidencia debe ser un archivo PDF.") { Campo = Campo };

    private static ReglaException DemasiadosReemplazos() =>
        ReglaException.Conflicto(CodigoDemasiadosReemplazos, $"La evidencia de este renglón ya se reemplazó {TopeReemplazos} veces.");

    [LoggerMessage(EventName = "EvidenciaReemplazada", Level = LogLevel.Information,
        Message = "Evidencia reemplazada en el saneamiento {SaneamientoId}, renglón {RenglonId} ({Tamano} bytes).")]
    private static partial void LogEvidenciaReemplazada(ILogger logger, Guid saneamientoId, Guid renglonId, long tamano);
}
