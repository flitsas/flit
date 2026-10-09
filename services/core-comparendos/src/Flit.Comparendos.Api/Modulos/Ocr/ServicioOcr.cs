using System.Globalization;
using Flit.Comparendos.Api.Modulos.Calendario;
using Flit.Comparendos.Api.Modulos.Catalogos;
using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Modulos.Ingesta;
using Flit.Comparendos.Api.Modulos.Ingesta.Fuentes;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Identificadores;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Ocr;

/// <summary>Resultado de registrar un documento: el comparendo creado y la oferta de monitorear la placa (RN-062).</summary>
public sealed record Registro(Guid DocumentoId, Guid ComparendoId, string Numero, string? Placa, bool PlacaYaMonitoreada, TipoIngreso TipoIngreso);

/// <summary>
/// Carga por OCR (HUs #13469–#13471; §6.18, RN-050 a RN-064). Paquetes de 1 a 200 archivos que un LLM lee uno a uno;
/// el operador revisa, corrige o completa y registra. Todo lo que cambia el operador queda en el log de ediciones.
/// </summary>
public sealed class ServicioOcr(
    ComparendosDb db,
    RegistroArchivos archivos,
    IProveedorOcr proveedor,
    MapeadorOt mapeador,
    Timeline timeline,
    CalendarioService calendario,
    Capacidades capacidades,
    ServicioCorreo correo,
    Reloj reloj)
{
    public const int MaximoDocumentos = 200;
    public const long MaximoBytes = 20L * 1024 * 1024;
    public const int MaximoIntentos = 3;
    public static readonly string[] Formatos = ["application/pdf", "image/jpeg", "image/png", "image/tiff"];
    private static readonly TimeSpan[] Esperas = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45)];
    private static readonly EstadoDocumentoOcr[] Finales = [EstadoDocumentoOcr.Registrado, EstadoDocumentoOcr.Descartado, EstadoDocumentoOcr.Duplicado];

    public async Task<PaqueteOcr> CrearPaqueteAsync(Guid tenant, string? nombre, IReadOnlyList<IFormFile> lista, Guid usuario, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lista);
        await capacidades.ExigirAsync(tenant, Capacidad.Ocr, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Trim().Length > 150)
            throw new ReglaException("NOMBRE_INVALIDO", "El paquete necesita un nombre de hasta 150 caracteres.");
        if (lista.Count is 0 or > MaximoDocumentos)
            throw new ReglaException("CANTIDAD_INVALIDA", $"Un paquete lleva entre 1 y {MaximoDocumentos} archivos.");
        var invalidos = lista.Where(f => !Formatos.Contains(RegistroArchivos.TipoDe(f)) || f.Length is 0 or > MaximoBytes).Select(f => f.FileName).ToList();
        if (invalidos.Count > 0)
            throw new ReglaException("ARCHIVOS_INVALIDOS", $"Solo se aceptan JPG, PNG, TIFF o PDF de hasta 20 MB. Revisa: {string.Join(", ", invalidos.Take(10))}{(invalidos.Count > 10 ? "…" : string.Empty)}.");
        var prompt = await PromptPublicadoAsync(ct).ConfigureAwait(false);

        var paquete = new PaqueteOcr
        {
            Id = Guid.CreateVersion7(), TenantId = tenant, Nombre = nombre.Trim(), CantidadDocumentos = lista.Count, CantidadPendientes = lista.Count,
            PromptVersionId = prompt.Id, CreadoPor = usuario, CreadoEn = reloj.Ahora,
        };
        db.PaquetesOcr.Add(paquete);
        foreach (var f in lista)
        {
            var tipo = RegistroArchivos.TipoDe(f);
            var archivo = await archivos.SubirAsync(tenant, "ocr", f, Formatos, ct, usuario, MaximoBytes).ConfigureAwait(false);
            db.DocumentosOcr.Add(new DocumentoOcr
            {
                Id = Guid.CreateVersion7(), TenantId = tenant, PaqueteId = paquete.Id, ArchivoId = archivo,
                NombreArchivo = Path.GetFileName(f.FileName) is { Length: > 255 } largo ? largo[^255..] : Path.GetFileName(f.FileName),
                TipoArchivo = tipo == "application/pdf" ? "PDF" : "IMAGE",
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return paquete;
    }

    public async Task<PromptOcrVersion> PromptPublicadoAsync(CancellationToken ct) =>
        await db.PromptsOcr.AsNoTracking().FirstOrDefaultAsync(p => p.Publicada, ct).ConfigureAwait(false)
        ?? throw new ReglaException("SIN_PROMPT_OCR", "No hay una versión publicada del prompt de OCR.", StatusCodes.Status409Conflict);

    public async Task IniciarAsync(PaqueteOcr paquete, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paquete);
        if (paquete.Estado != EstadoPaqueteOcr.Pendiente)
            throw ReglaException.Conflicto("PAQUETE_YA_INICIADO", "El paquete ya se está procesando o ya se procesó.");
        paquete.PromptVersionId = (await PromptPublicadoAsync(ct).ConfigureAwait(false)).Id;
        paquete.Estado = EstadoPaqueteOcr.EnProcesamiento;
        paquete.ProcesamientoIniciadoEn = reloj.Ahora;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Procesa un documento ya reclamado por el procesador (§6.18 Procesar).</summary>
    public async Task ProcesarAsync(Guid documentoId, CancellationToken ct)
    {
        var doc = await db.DocumentosOcr.FirstAsync(d => d.Id == documentoId, ct).ConfigureAwait(false);
        var paquete = await db.PaquetesOcr.FirstAsync(p => p.Id == doc.PaqueteId, ct).ConfigureAwait(false);
        var ahora = reloj.Ahora;
        doc.Estado = EstadoDocumentoOcr.EnProcesamiento;

        var leido = await archivos.LeerAsync(doc.ArchivoId, ct).ConfigureAwait(false);
        if (leido is not { } a)
        {
            Fallar(doc, TipoFalloOcr.ArchivoInvalido, "No se encontró el archivo en el almacenamiento.");
            await CerrarProcesoAsync(paquete, ct).ConfigureAwait(false);
            return;
        }

        // Mismo archivo ya leído en la empresa (RN-056): solo cuenta contra un original con resultado útil, así la copia
        // que llega primero a la cola no marca como duplicado al original, y un fallido o descartado se puede volver a cargar.
        var mismoArchivo = await (from d in db.DocumentosOcr.AsNoTracking()
                                  join f in db.Archivos.AsNoTracking() on d.ArchivoId equals f.Id
                                  where d.TenantId == doc.TenantId && d.Id != doc.Id && f.Sha256 == a.Archivo.Sha256
                                      && (d.Estado == EstadoDocumentoOcr.Completo || d.Estado == EstadoDocumentoOcr.Incompleto || d.Estado == EstadoDocumentoOcr.Registrado)
                                  orderby d.Id
                                  select d.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (mismoArchivo != Guid.Empty)
        {
            (doc.Estado, doc.DuplicadoDeDocumentoId, doc.FuenteDuplicado, doc.ProcesadoEn) = (EstadoDocumentoOcr.Duplicado, mismoArchivo, "ARCHIVO", ahora);
            await CerrarProcesoAsync(paquete, ct).ConfigureAwait(false);
            return;
        }

        var prompt = await db.PromptsOcr.AsNoTracking().FirstAsync(p => p.Id == paquete.PromptVersionId, ct).ConfigureAwait(false);
        var r = await proveedor.ExtraerAsync(a.Contenido, a.Archivo.ContentType, prompt.Texto, prompt.Modelo, ct).ConfigureAwait(false);
        doc.Intentos++;
        doc.ProveedorModelo = $"{prompt.Proveedor}/{r.Modelo}";
        var intento = new DocumentoOcrIntento
        {
            Id = Guid.CreateVersion7(), DocumentoOcrId = doc.Id, Numero = doc.Intentos, IntentadoEn = ahora, TipoError = r.TipoFallo,
            MensajeError = r.Mensaje, Resultado = r.Exitoso ? "EXITO" : "ERROR",
        };
        db.DocumentoOcrIntentos.Add(intento);

        if (!r.Exitoso)
        {
            if (ResultadoOcr.EsTecnico(r.TipoFallo) && doc.Intentos < MaximoIntentos)
            {
                var espera = r.ReintentarEn is { } ra && ra > TimeSpan.Zero ? ra : Esperas[doc.Intentos - 1];
                (doc.Estado, doc.TipoFallo, doc.MensajeError, doc.ProximoIntentoEn) = (EstadoDocumentoOcr.Pendiente, r.TipoFallo, r.Mensaje, ahora + espera);
                intento.ProximoReintentoEn = doc.ProximoIntentoEn;
            }
            else
            {
                Fallar(doc, r.TipoFallo!.Value, r.Mensaje);
                doc.RespuestaLlm = r.Crudo is null ? null : JsonCrudo(r.Crudo);
                if (ResultadoOcr.EsTecnico(r.TipoFallo))
                    await AlertarAsync(paquete, doc, ct).ConfigureAwait(false);
            }

            await CerrarProcesoAsync(paquete, ct).ConfigureAwait(false);
            return;
        }

        doc.RespuestaLlm = JsonCrudo(r.Crudo);
        if (r.Campos.Values.All(c => c.Valor is null))
        {
            Fallar(doc, TipoFalloOcr.Ilegible, "El modelo no extrajo ningún campo del documento.");
            await CerrarProcesoAsync(paquete, ct).ConfigureAwait(false);
            return;
        }

        var campos = CamposOcr.Todos.Select(n => (Nombre: n, Campo: r.Campos.GetValueOrDefault(n))).Select(x => new DocumentoOcrCampo
        {
            Id = Guid.CreateVersion7(), DocumentoOcrId = doc.Id, NombreCampo = x.Nombre, ValorExtraido = x.Campo?.Valor, Confianza = x.Campo?.Confianza,
            EsNull = x.Campo?.Valor is null, ValorFinal = x.Campo?.Valor,
        }).ToList();
        await db.DocumentoOcrCampos.Where(c => c.DocumentoOcrId == doc.Id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        db.DocumentoOcrCampos.AddRange(campos);

        // RN-061: la descripción de la infracción sale del catálogo si el modelo no la leyó.
        var codigo = Valor(campos, CamposOcr.CodigoInfraccion)?.ToUpperInvariant();
        var descripcion = campos.First(c => c.NombreCampo == CamposOcr.DescripcionInfraccion);
        if (codigo is not null && descripcion.ValorFinal is null)
            descripcion.ValorFinal = await db.InfraccionesCatalogo.Where(i => i.Codigo == codigo).Select(i => i.Descripcion).FirstOrDefaultAsync(ct).ConfigureAwait(false);

        doc.ProcesadoEn = ahora;
        (doc.TipoFallo, doc.MensajeError, doc.ProximoIntentoEn) = (null, null, null);
        await EvaluarAsync(doc, campos, ct).ConfigureAwait(false);
        await CerrarProcesoAsync(paquete, ct).ConfigureAwait(false);
    }

    private static void Fallar(DocumentoOcr doc, TipoFalloOcr tipo, string? mensaje) =>
        (doc.Estado, doc.TipoFallo, doc.MensajeError, doc.ProximoIntentoEn) = (EstadoDocumentoOcr.Fallido, tipo, mensaje is { Length: > 1000 } m ? m[..1000] : mensaje, null);

    /// <summary>jsonb exige JSON válido: lo que no lo sea se guarda como cadena JSON.</summary>
    private static string? JsonCrudo(string? crudo)
    {
        if (crudo is null)
            return null;
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(crudo);
            return crudo;
        }
        catch (System.Text.Json.JsonException)
        {
            return System.Text.Json.JsonSerializer.Serialize(crudo);
        }
    }

    private static string? Valor(IEnumerable<DocumentoOcrCampo> campos, string nombre) => campos.FirstOrDefault(c => c.NombreCampo == nombre)?.ValorFinal;

    /// <summary>Obligatorios ausentes o con un formato que no se puede usar (RN-053).</summary>
    public static List<string> Faltantes(IReadOnlyCollection<DocumentoOcrCampo> campos)
    {
        ArgumentNullException.ThrowIfNull(campos);
        string? V(string n) => Valor(campos, n);
        return CamposOcr.Obligatorios.Where(n => n switch
        {
            CamposOcr.Numero => NumeroComparendo.Normalizar(V(n)) is null,
            CamposOcr.TipoDocumento => ValoresOcr.Documento(V(n)) is null,
            CamposOcr.FechaInfraccion => ValoresOcr.Fecha(V(n)) is null,
            CamposOcr.Placa => Placa.Normalizar(V(n)) is null,
            CamposOcr.Valor => Infraestructura.Excel.LectorExcel.Valor(new(V(n) ?? string.Empty, null)) is not > 0,
            _ => string.IsNullOrWhiteSpace(V(n)),
        }).ToList();
    }

    /// <summary>Número, duplicados y obligatorios → DUPLICADO, INCOMPLETO o COMPLETO.</summary>
    private async Task EvaluarAsync(DocumentoOcr doc, IReadOnlyCollection<DocumentoOcrCampo> campos, CancellationToken ct)
    {
        doc.NumeroComparendoExtraido = Valor(campos, CamposOcr.Numero) is { } n ? (n.Length > 40 ? n[..40] : n) : null;
        doc.NumeroNormalizado = NumeroComparendo.Normalizar(doc.NumeroComparendoExtraido);
        doc.CamposFaltantes = Faltantes(campos);
        (doc.DuplicadoDeComparendoId, doc.DuplicadoDeDocumentoId, doc.FuenteDuplicado) = (null, null, null);
        if (doc.NumeroNormalizado is { } numero)
        {
            var existente = await db.Comparendos.AsNoTracking().Where(c => c.TenantId == doc.TenantId && c.NumeroNormalizado == numero)
                .Select(c => new { c.Id, c.FuentePrincipal, c.TipoIngreso }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (existente is not null)
            {
                (doc.Estado, doc.DuplicadoDeComparendoId, doc.FuenteDuplicado) = (EstadoDocumentoOcr.Duplicado, existente.Id, Fuente(existente.FuentePrincipal));
                return;
            }

            var otro = await db.DocumentosOcr.AsNoTracking()
                .Where(d => d.TenantId == doc.TenantId && d.Id != doc.Id && d.NumeroNormalizado == numero
                    && d.Estado != EstadoDocumentoOcr.Descartado && d.Estado != EstadoDocumentoOcr.Duplicado && d.Estado != EstadoDocumentoOcr.Fallido)
                .OrderBy(d => d.Id).Select(d => d.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (otro != Guid.Empty)
            {
                (doc.Estado, doc.DuplicadoDeDocumentoId, doc.FuenteDuplicado) = (EstadoDocumentoOcr.Duplicado, otro, "OCR");
                return;
            }
        }

        doc.Estado = doc.CamposFaltantes.Count == 0 ? EstadoDocumentoOcr.Completo : EstadoDocumentoOcr.Incompleto;
    }

    private static string Fuente(CanalIngesta? canal) => canal switch
    {
        CanalIngesta.SimitNit or CanalIngesta.SimitPlaca => "SIMIT",
        CanalIngesta.MunicipalNit or CanalIngesta.MunicipalPlaca => "MUNICIPAL",
        _ => "OCR",
    };

    private async Task CerrarProcesoAsync(PaqueteOcr paquete, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await RecalcularAsync(paquete, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Contadores y estado del paquete (§4.8).</summary>
    public async Task RecalcularAsync(PaqueteOcr p, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(p);
        var cuenta = await db.DocumentosOcr.Where(d => d.PaqueteId == p.Id).GroupBy(d => d.Estado).Select(g => new { g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct).ConfigureAwait(false);
        int C(EstadoDocumentoOcr e) => cuenta.GetValueOrDefault(e);
        p.CantidadDocumentos = cuenta.Values.Sum();
        p.CantidadPendientes = C(EstadoDocumentoOcr.Pendiente) + C(EstadoDocumentoOcr.EnProcesamiento);
        p.CantidadCompletos = C(EstadoDocumentoOcr.Completo);
        p.CantidadIncompletos = C(EstadoDocumentoOcr.Incompleto);
        p.CantidadFallidos = C(EstadoDocumentoOcr.Fallido);
        p.CantidadDuplicados = C(EstadoDocumentoOcr.Duplicado);
        p.CantidadRegistrados = C(EstadoDocumentoOcr.Registrado);
        p.CantidadDescartados = C(EstadoDocumentoOcr.Descartado);
        if (p.Estado == EstadoPaqueteOcr.EnProcesamiento && p.CantidadPendientes == 0)
            p.Estado = p.CantidadFallidos > 0 ? EstadoPaqueteOcr.ProcesadoConErrores : EstadoPaqueteOcr.Procesado;
        if (p.Estado is EstadoPaqueteOcr.Procesado or EstadoPaqueteOcr.ProcesadoConErrores
            && cuenta.Where(kv => kv.Value > 0).All(kv => Finales.Contains(kv.Key)))
        {
            p.Estado = EstadoPaqueteOcr.Cerrado;
            p.CerradoEn = reloj.Ahora;
        }
    }

    /// <summary>Corrección o complemento de campos (RN-057): cada cambio queda con valor anterior, nuevo, usuario y motivo.</summary>
    public async Task CorregirAsync(DocumentoOcr doc, IReadOnlyDictionary<string, string?> cambios, string? motivo, Guid usuario, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(cambios);
        if (doc.Estado is not (EstadoDocumentoOcr.Completo or EstadoDocumentoOcr.Incompleto or EstadoDocumentoOcr.Duplicado))
            throw ReglaException.Conflicto("DOCUMENTO_NO_EDITABLE", doc.Estado == EstadoDocumentoOcr.Registrado
                ? "El documento ya se registró: se corrige desde el comparendo, no desde OCR."
                : "El documento no está en un estado que se pueda corregir.");
        if (doc.FuenteDuplicado == "ARCHIVO")
            throw ReglaException.Conflicto("ARCHIVO_DUPLICADO", "Este archivo ya se había cargado: no se puede registrar dos veces.");
        var desconocidos = cambios.Keys.Where(k => !CamposOcr.Todos.Contains(k)).ToList();
        if (desconocidos.Count > 0)
            throw new ReglaException("CAMPO_DESCONOCIDO", $"Campos desconocidos: {string.Join(", ", desconocidos)}.");

        var campos = await db.DocumentoOcrCampos.Where(c => c.DocumentoOcrId == doc.Id).ToListAsync(ct).ConfigureAwait(false);
        var ahora = reloj.Ahora;
        foreach (var (nombre, valor) in cambios)
        {
            var nuevo = string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
            var campo = campos.FirstOrDefault(c => c.NombreCampo == nombre);
            if (campo is null)
            {
                campo = new DocumentoOcrCampo { Id = Guid.CreateVersion7(), DocumentoOcrId = doc.Id, NombreCampo = nombre, EsNull = true };
                db.DocumentoOcrCampos.Add(campo);
                campos.Add(campo);
            }

            if (campo.ValorFinal == nuevo)
                continue;
            db.DocumentoOcrEdiciones.Add(new DocumentoOcrEdicion
            {
                Id = Guid.CreateVersion7(), DocumentoOcrId = doc.Id, Campo = nombre, ValorAnterior = campo.ValorFinal, ValorNuevo = nuevo, UsuarioId = usuario, EditadoEn = ahora,
                Motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim(),
            });
            (campo.ValorCorregido, campo.ValorFinal) = (nuevo, nuevo);
        }

        (doc.RevisadoPor, doc.RevisadoEn) = (usuario, ahora);
        await EvaluarAsync(doc, campos, ct).ConfigureAwait(false);
        var paquete = await db.PaquetesOcr.FirstAsync(p => p.Id == doc.PaqueteId, ct).ConfigureAwait(false);
        await CerrarProcesoAsync(paquete, ct).ConfigureAwait(false);
    }

    /// <summary>§6.18 Registrar: crea el comparendo (tipo 2, o 3 si el operador completó obligatorios) con el archivo como evidencia.</summary>
    public async Task<Registro> RegistrarAsync(DocumentoOcr doc, Actor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(actor);
        if (doc.Estado != EstadoDocumentoOcr.Completo)
            throw ReglaException.Conflicto("DOCUMENTO_NO_COMPLETO", doc.Estado == EstadoDocumentoOcr.Duplicado
                ? "El comparendo ya existe: no se puede registrar de nuevo."
                : "Solo se registran documentos completos.");
        var campos = await db.DocumentoOcrCampos.Where(c => c.DocumentoOcrId == doc.Id).ToListAsync(ct).ConfigureAwait(false);
        await EvaluarAsync(doc, campos, ct).ConfigureAwait(false);
        if (doc.Estado != EstadoDocumentoOcr.Completo)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            throw ReglaException.Conflicto(doc.Estado == EstadoDocumentoOcr.Duplicado ? "COMPARENDO_DUPLICADO" : "DOCUMENTO_NO_COMPLETO",
                doc.Estado == EstadoDocumentoOcr.Duplicado ? "El comparendo ya existe: no se puede registrar de nuevo." : "Faltan campos obligatorios.");
        }

        string? V(string n) => Valor(campos, n);
        var complementado = campos.Any(c => CamposOcr.Obligatorios.Contains(c.NombreCampo) && c.ValorExtraido is null && c.ValorFinal is not null);
        var fotomulta = ValoresOcr.Booleano(V(CamposOcr.EsFotomulta));
        var imposicion = ValoresOcr.Fecha(V(CamposOcr.FechaInfraccion));
        var item = new ItemFuente
        {
            Numero = V(CamposOcr.Numero)!,
            Placa = V(CamposOcr.Placa),
            TipoDocumentoInfractor = ValoresOcr.Documento(V(CamposOcr.TipoDocumento)),
            NumeroDocumentoInfractor = V(CamposOcr.NumeroDocumento),
            NombreInfractor = V(CamposOcr.Nombre),
            CodigoInfraccion = V(CamposOcr.CodigoInfraccion)?.ToUpperInvariant(),
            DescripcionInfraccion = V(CamposOcr.DescripcionInfraccion),
            DireccionInfraccion = V(CamposOcr.Direccion),
            FechaImposicion = imposicion,
            HoraImposicion = ValoresOcr.Hora(V(CamposOcr.HoraInfraccion)),
            // S-40: el físico sin fecha de notificación se notificó en la vía.
            FechaNotificacion = ValoresOcr.Fecha(V(CamposOcr.FechaNotificacion)) ?? (fotomulta == false ? imposicion : null),
            EsFotomulta = fotomulta,
            Secretaria = V(CamposOcr.Secretaria),
            TipoVehiculo = V(CamposOcr.TipoVehiculo),
            ValorTotal = Infraestructura.Excel.LectorExcel.Valor(new(V(CamposOcr.Valor) ?? string.Empty, null)),
        };

        var config = await capacidades.DeAsync(doc.TenantId, ct).ConfigureAwait(false);
        var ahora = reloj.Ahora;
        var c = new Comparendo
        {
            Id = Guid.CreateVersion7(),
            TenantId = doc.TenantId,
            NumeroNormalizado = doc.NumeroNormalizado!,
            TipoIngreso = complementado ? TipoIngreso.OcrComplemento : TipoIngreso.Ocr,
            FuentePrincipal = null,
            ArchivoEvidenciaId = doc.ArchivoId,
            EstadoCambioContraventor = config.AllowsContravenerChange ? EstadoCambioContraventor.SinSolicitar : EstadoCambioContraventor.NoAplica,
            EstadoConsultaContraventor = config.TipoIntegracionConductor == TipoIntegracionConductor.ApiRenting && config.AllowsContravenerConsultation
                ? EstadoConsultaContraventor.Pendiente
                : EstadoConsultaContraventor.NoAplica,
            CreadoEn = ahora,
            UltimoEventoEn = ahora,
        };
        Reconciliador.AplicarCampos(c, item);
        c.OrganismoTransitoId = await mapeador.MapearAsync(item.Secretaria, ct).ConfigureAwait(false);
        Plazos.Recalcular(c, await calendario.MotorAsync(ct).ConfigureAwait(false), ahora);
        db.Comparendos.Add(c);
        var paquete = await db.PaquetesOcr.FirstAsync(p => p.Id == doc.PaqueteId, ct).ConfigureAwait(false);
        timeline.Agregar(c, TipoEventoTimeline.Ingreso,
            $"Ingresó por OCR{(complementado ? " con campos completados por el operador" : string.Empty)} (paquete «{paquete.Nombre}», archivo {doc.NombreArchivo}).",
            actor, new { canal = "OCR", tipoIngreso = (short)c.TipoIngreso, paquete = paquete.Id, documento = doc.Id, archivo = doc.ArchivoId });
        timeline.Agregar(c, TipoEventoTimeline.PlazosCalculados, Reconciliador.DescribirPlazos(c), Actor.Sistema);

        (doc.Estado, doc.ComparendoId, doc.RevisadoPor, doc.RevisadoEn) = (EstadoDocumentoOcr.Registrado, c.Id, actor.UsuarioId, ahora);
        try
        {
            await CerrarProcesoAsync(paquete, ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            throw ReglaException.Conflicto("COMPARENDO_DUPLICADO", "El comparendo se registró por otra vía mientras lo revisabas.");
        }

        var yaMonitoreada = c.Placa is { } placa
            && await db.PlacasMonitoreadas.AnyAsync(p => p.TenantId == c.TenantId && p.Placa == placa && p.Activo, ct).ConfigureAwait(false);
        return new Registro(doc.Id, c.Id, c.Numero, c.Placa, yaMonitoreada, c.TipoIngreso);
    }

    public async Task DescartarAsync(DocumentoOcr doc, string? motivo, Guid usuario, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (doc.Estado is EstadoDocumentoOcr.Registrado or EstadoDocumentoOcr.Descartado or EstadoDocumentoOcr.EnProcesamiento or EstadoDocumentoOcr.Pendiente)
            throw ReglaException.Conflicto("DOCUMENTO_NO_DESCARTABLE", "Este documento no se puede descartar en su estado actual.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new ReglaException("MOTIVO_REQUERIDO", "Indica el motivo del descarte.");
        (doc.Estado, doc.DescartadoPor, doc.MotivoDescarte) = (EstadoDocumentoOcr.Descartado, usuario, motivo.Trim().Length > 500 ? motivo.Trim()[..500] : motivo.Trim());
        var paquete = await db.PaquetesOcr.FirstAsync(p => p.Id == doc.PaqueteId, ct).ConfigureAwait(false);
        await CerrarProcesoAsync(paquete, ct).ConfigureAwait(false);
    }

    /// <summary>Reintento manual de un FALLIDO por fallo técnico (§4.8); un ilegible o un archivo inválido no se reintenta.</summary>
    public async Task ReintentarAsync(DocumentoOcr doc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (doc.Estado != EstadoDocumentoOcr.Fallido || !ResultadoOcr.EsTecnico(doc.TipoFallo))
            throw ReglaException.Conflicto("NO_REINTENTABLE", "Solo se reintentan los documentos que fallaron por un problema técnico del proveedor.");
        (doc.Estado, doc.Intentos, doc.ProximoIntentoEn) = (EstadoDocumentoOcr.Pendiente, 0, null);
        var paquete = await db.PaquetesOcr.FirstAsync(p => p.Id == doc.PaqueteId, ct).ConfigureAwait(false);
        if (paquete.Estado == EstadoPaqueteOcr.ProcesadoConErrores)
            paquete.Estado = EstadoPaqueteOcr.EnProcesamiento;
        await CerrarProcesoAsync(paquete, ct).ConfigureAwait(false);
    }

    /// <summary>RN-055: tras el último intento técnico fallido, correo a los destinatarios de TODAS o SOLO_OCR.</summary>
    private async Task AlertarAsync(PaqueteOcr paquete, DocumentoOcr doc, CancellationToken ct)
    {
        var config = await db.AlertaConfiguraciones.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (config is not { AlertasHabilitadas: true })
            return;
        var para = await db.AlertaDestinatarios.AsNoTracking()
            .Where(d => d.Activo && (d.TipoAlerta == TipoAlertaDestinatario.Todas || d.TipoAlerta == TipoAlertaDestinatario.SoloOcr))
            .Select(d => new Destinatario(d.Correo, d.Nombre)).ToListAsync(ct).ConfigureAwait(false);
        if (para.Count == 0)
            return;
        var empresa = (await capacidades.DeAsync(paquete.TenantId, ct).ConfigureAwait(false)).EmpresaNombre ?? paquete.TenantId.ToString();
        var momento = ColombiaTime.From(reloj.Ahora).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var cuerpo = $"""
            <p>El proveedor de OCR no respondió tras {MaximoIntentos} intentos.</p>
            {HtmlCorreo.Tabla([("Empresa", empresa), ("Paquete", paquete.Nombre), ("Archivo", doc.NombreArchivo), ("Error", $"{doc.TipoFallo}: {doc.MensajeError}"), ("Motor", doc.ProveedorModelo)])}
            <p>El documento quedó FALLIDO y se puede reintentar desde el paquete.</p>
            <p><a href="{correo.AppUrl}/ocr/paquetes/{paquete.Id}">Ver el paquete</a></p>
            """;
        await correo.RegistrarAsync(new CorreoNuevo
        {
            Tipo = TipoComunicacion.AlertaFalloOcr,
            Plantilla = "comparendos.alerta_fallo_ocr",
            Para = para,
            Asunto = config.PlantillaAsunto.Replace("{FUENTE}", "OCR", StringComparison.Ordinal).Replace("{TIMESTAMP}", momento, StringComparison.Ordinal),
            Html = HtmlCorreo.Envolver("Fallo del proveedor de OCR", cuerpo),
            RemitenteNombre = config.RemitenteNombre,
        }, ct).ConfigureAwait(false);
    }
}
