using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Backoffice;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>Respuesta del llenado manual: la celda ya COMPLETADO, su versión del tablero y el estado del lote.</summary>
internal sealed record LlenadoManualDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("diagnostico_id")] Guid DiagnosticoId,
    [property: JsonPropertyName("vehiculo_id")] Guid VehiculoId,
    [property: JsonPropertyName("fuente")] string Fuente,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("origen")] string Origen,
    [property: JsonPropertyName("usuario_manual_id")] Guid UsuarioManualId,
    [property: JsonPropertyName("dato_obtenido_en")] DateTimeOffset DatoObtenidoEn,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("estado_lote")] string EstadoLote);

/// <summary>
/// Lo que el backoffice necesita para llenar una celda (HU #13800, contrato de F06-H5): la celda, su versión (la que se devuelve
/// en el PUT), si admite el llenado y, si no, por qué, y en las fuentes de persona SOLO las personas en error, con su
/// <c>persona_id</c> y el documento enmascarado. Nunca el documento en claro ni el payload.
/// </summary>
internal sealed record FormularioManualDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("diagnostico_id")] Guid DiagnosticoId,
    [property: JsonPropertyName("vehiculo_id")] Guid VehiculoId,
    [property: JsonPropertyName("fuente")] string Fuente,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("tipo_error")] string? TipoError,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("admite_manual")] bool AdmiteManual,
    [property: JsonPropertyName("motivo")] string? Motivo,
    [property: JsonPropertyName("personas")] IReadOnlyList<PersonaEnErrorDto>? Personas);

/// <summary>Una persona en error de una celda de persona, para el formulario del backoffice.</summary>
internal sealed record PersonaEnErrorDto(
    [property: JsonPropertyName("persona_id")] Guid PersonaId,
    [property: JsonPropertyName("orden")] int Orden,
    [property: JsonPropertyName("rol")] string Rol,
    [property: JsonPropertyName("tipo_documento")] string TipoDocumento,
    [property: JsonPropertyName("documento_enmascarado")] string DocumentoEnmascarado,
    [property: JsonPropertyName("declarado_por_cliente")] bool DeclaradoPorCliente,
    [property: JsonPropertyName("consulta")] string Consulta,
    [property: JsonPropertyName("tipo_error")] string? TipoError);

/// <summary>
/// Llenado manual de una celda en ERROR por el backoffice FLIT (HU #13800, F06-H3, F06-C8..C11, plan §3.5 «ERROR → COMPLETADO
/// manual», R-1, R-8, R-20). La celda es de la empresa EXPLÍCITA de la ruta <c>/backoffice/*</c> y se lee y escribe solo por
/// <see cref="EscrituraEntreEmpresas"/> (celdas, intentos, cabecera e historial de ESA empresa). En UNA transacción:
/// <list type="number">
/// <item>Valida: la fuente admite manual (no RUNT ni IMPUESTO_DEPARTAMENTAL, 422 <see cref="CodigoFuenteNoAdmite"/>); el cuerpo
/// trae la <c>version</c> de la celda que vio el backoffice y coincide (si no, 409 <see cref="Reproceso.CodigoCeldaModificada"/>);
/// la celda está en ERROR (409 <see cref="Reproceso.CodigoNoEnError"/>) y no <c>BLOQUEADA_POR_RUNT</c> (409
/// <see cref="Reproceso.CodigoBloqueadaPorRunt"/>); el resultado cumple el tipo normalizado de la fuente
/// (<see cref="ValidadorResultadoManual"/>, 422 <c>RESULTADO_INVALIDO</c> con <c>campo</c>). En las de persona solo se llenan
/// las personas en ERROR, todas (una OK en el cuerpo: 422 <see cref="CodigoPersonaNoEnError"/>); las que respondieron por API
/// no se tocan.</item>
/// <item>La celda queda COMPLETADO con <c>origen=MANUAL</c>, <c>usuario_manual_id</c>, <c>tipo_error=null</c> y
/// <c>dato_obtenido_en</c> del servidor, y sube <c>version_tablero</c> (el sondeo la ve cambiar).</item>
/// <item>Un intento <c>disparador=MANUAL</c> con el usuario, sin servicio ni transacción del proveedor (no consume, #13796): uno
/// por la celda, o uno por cada persona llenada con su <c>persona_id</c>.</item>
/// <item>Si el lote estaba COMPLETADO o INCOMPLETO vuelve a EN_PROCESO con historial MANUAL y el usuario; después
/// <see cref="RecalculadorEstado"/> fija el estado global (sin más errores: COMPLETADO, historial AUTO).</item>
/// </list>
/// La compañía no distingue el dato: el payload tiene la misma forma que el de API y su historial muestra MANUAL como AUTO (Q5).
/// Una celda con origen MANUAL nunca se reutiliza el mismo día (<see cref="ReutilizacionMismoDia"/> solo copia origen API).
/// La auditoría del llenado es el propio intento (quién, cuándo, qué celda y qué persona); el log no lleva documentos.
/// </summary>
/// <example>
/// <code>
/// var dto = await llenado.LlenarAsync(empresa, consultaId, cuerpo, ct); // 200 { id, estado: "COMPLETADO", origen: "MANUAL", … }
/// </code>
/// </example>
internal sealed partial class LlenadoManual(
    DiagnosticoDb db, UsuarioActual usuario, IProtectorDocumentoPersona protector, TimeProvider reloj, ILogger<LlenadoManual> logger)
{
    public const string CodigoFuenteNoAdmite = "FUENTE_NO_ADMITE_MANUAL";
    public const string CodigoPersonaNoEnError = "PERSONA_NO_EN_ERROR";
    public const string MotivoSinPersonasEnError = "SIN_PERSONAS_EN_ERROR";

    /// <summary>Lo único que el llenado puede escribir en la compañía de la celda.</summary>
    private static readonly Type[] TiposDelLlenado =
        [typeof(CeldaConsulta), typeof(IntentoConsulta), typeof(CabeceraDiagnostico), typeof(HistorialEstadoDiagnostico)];

    public async Task<LlenadoManualDto> LlenarAsync(Guid empresa, Guid consultaId, JsonElement json, CancellationToken ct)
    {
        var autor = usuario.Id != Guid.Empty
            ? usuario.Id
            : throw ReglaException.Prohibido("SIN_USUARIO", "El token no identifica al usuario: el llenado manual no se puede auditar.");

        // El reintento de Npgsql exige que la transacción del usuario vaya dentro de la estrategia de ejecución.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var celda = await db.ParaEditarDeLaEmpresa<CeldaConsulta>(empresa).SingleOrDefaultAsync(c => c.Id == consultaId, ct).ConfigureAwait(false)
                ?? throw ReglaException.NoEncontrado("La consulta");
            if (!ValidadorResultadoManual.AdmiteManual(celda.Fuente))
                throw new ReglaException(CodigoFuenteNoAdmite, $"La fuente {CodigosDeEnum.Codigo(celda.Fuente)} no se llena a mano.");
            var cuerpo = ValidadorResultadoManual.LeerCuerpo(celda.Fuente, json);
            if (cuerpo.Version != celda.VersionTablero)
                throw ReglaException.Conflicto(Reproceso.CodigoCeldaModificada, "La consulta cambió desde que la abriste. Vuelve a cargar la grilla.");
            if (MotivoNoLlenable(celda) is { } motivo)
                throw ReglaException.Conflicto(motivo, MensajeNoLlenable(motivo, celda));

            var ahora = reloj.GetUtcNow();
            var (payload, intentos) = Resultado(celda, cuerpo, autor, ahora);

            var version = await SubirVersionAsync(empresa, celda.DiagnosticoId, ct).ConfigureAwait(false);
            var cabecera = await db.ParaEditarDeLaEmpresa<CabeceraDiagnostico>(empresa).SingleAsync(c => c.Id == celda.DiagnosticoId, ct).ConfigureAwait(false);
            if (cabecera.Estado is EstadoDiagnostico.Completado or EstadoDiagnostico.Incompleto)
                Reabrir(cabecera, autor, ahora);

            celda.Estado = EstadoCelda.Completado;
            celda.Payload = payload;
            celda.TipoError = null;
            celda.MotivoNa = null;
            celda.Origen = OrigenCelda.Manual;
            celda.UsuarioManualId = autor;
            celda.DatoObtenidoEn = ahora;
            celda.ReutilizadaDeId = null;
            celda.LeaseHasta = null;
            celda.ReprocesoUsuarioId = null;
            celda.ActualizadoEn = ahora;
            celda.VersionTablero = version;
            db.AddRange(intentos);
            try
            {
                await db.GuardarDeLaEmpresaAsync(empresa, TiposDelLlenado, ct).ConfigureAwait(false);
                // R-8: con la celda ya COMPLETADO, el estado global del lote se recalcula en la misma transacción. El historial
                // ordena por fecha: el cierre va después de la reapertura.
                var cierre = reloj.GetUtcNow();
                await RecalculadorEstado.RecalcularAsync(
                    db, db.ParaEditarDeLaEmpresa<CabeceraDiagnostico>(empresa), db.DeLaEmpresa<CeldaConsulta>(empresa), celda.DiagnosticoId,
                    cierre > ahora ? cierre : ahora.AddTicks(10), ct).ConfigureAwait(false);
                await db.GuardarDeLaEmpresaAsync(empresa, TiposDelLlenado, ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw ReglaException.Conflicto(Reproceso.CodigoCeldaModificada, "La consulta cambió mientras se llenaba. Vuelve a cargar la grilla.");
            }

            await transaccion.CommitAsync(ct).ConfigureAwait(false);
            CeldaLlenada(logger, celda.Id, celda.Fuente, empresa, autor, intentos.Count);
            return new LlenadoManualDto(
                celda.Id, celda.DiagnosticoId, celda.VehiculoId, CodigosDeEnum.Codigo(celda.Fuente), CodigosDeEnum.Codigo(celda.Estado),
                CodigosDeEnum.Codigo(OrigenCelda.Manual), autor, ahora, version, CodigosDeEnum.Codigo(cabecera.Estado));
        }).ConfigureAwait(false);
    }

    /// <summary>El formulario de la celda (GET): si se puede llenar y, en las de persona, las personas en error enmascaradas.</summary>
    public async Task<FormularioManualDto> FormularioAsync(Guid empresa, Guid consultaId, CancellationToken ct)
    {
        var celda = await db.DeLaEmpresa<CeldaConsulta>(empresa).SingleOrDefaultAsync(c => c.Id == consultaId, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("La consulta");
        IReadOnlyList<PersonaEnErrorDto>? personas = null;
        var motivo = ValidadorResultadoManual.AdmiteManual(celda.Fuente) ? MotivoNoLlenable(celda) : CodigoFuenteNoAdmite;
        if (EjecutorPorPersonas.EsPorPersona(celda.Fuente))
        {
            personas = motivo is null ? await PersonasEnErrorAsync(empresa, celda, ct).ConfigureAwait(false) : [];
            if (motivo is null && personas.Count == 0)
                motivo = MotivoSinPersonasEnError;
        }

        return new FormularioManualDto(
            celda.Id, celda.DiagnosticoId, celda.VehiculoId, CodigosDeEnum.Codigo(celda.Fuente), CodigosDeEnum.Codigo(celda.Estado), celda.TipoError,
            celda.VersionTablero, motivo is null, motivo, personas);
    }

    /// <summary>Por qué la celda no se llena (código del 409); null si está en ERROR y no la bloquea el RUNT.</summary>
    private static string? MotivoNoLlenable(CeldaConsulta celda) =>
        celda.Estado != EstadoCelda.Error ? Reproceso.CodigoNoEnError
        : celda.TipoError == OrquestadorVehiculo.BloqueadaPorRunt ? Reproceso.CodigoBloqueadaPorRunt
        : null;

    private static string MensajeNoLlenable(string motivo, CeldaConsulta celda) => motivo == Reproceso.CodigoBloqueadaPorRunt
        ? "Esta consulta depende del RUNT del vehículo, que no se ha completado: no se llena a mano."
        : $"Solo se llena a mano una consulta en error; esta está {CodigosDeEnum.Codigo(celda.Estado)}.";

    /// <summary>El payload nuevo de la celda y los intentos MANUAL (uno, o uno por persona llenada).</summary>
    private static (string Payload, List<IntentoConsulta> Intentos) Resultado(CeldaConsulta celda, CuerpoManual cuerpo, Guid autor, DateTimeOffset ahora)
    {
        if (cuerpo.Personas is not { } llenadas)
        {
            var payload = ValidadorResultadoManual.Vehiculo(celda.Fuente, cuerpo.Resultado!.Value, DateOnly.FromDateTime(ahora.ToOffset(VigenciaDelDia.OffsetColombia).DateTime));
            return (payload, [Intento(celda, autor, ahora, null)]);
        }

        var previas = EjecutorPorPersonas.Leer(celda.Payload)?.Personas ?? [];
        var porId = previas.GroupBy(p => p.PersonaId).ToDictionary(g => g.Key, g => g.First());
        var nuevas = new Dictionary<Guid, ResultadoPorPersona>();
        foreach (var llenada in llenadas)
        {
            var campo = $"personas[{llenada.Posicion}]";
            if (!porId.TryGetValue(llenada.PersonaId, out var previa) || previa.Estado != EjecutorPorPersonas.EstadoError)
            {
                throw new ReglaException(CodigoPersonaNoEnError, "Solo se llenan las personas en error de esta consulta; las que respondieron no se tocan.")
                {
                    Campo = $"{campo}.persona_id",
                };
            }

            var resultado = ValidadorResultadoManual.Persona(previa.Consulta, llenada.Resultado, $"{campo}.resultado");
            nuevas[previa.PersonaId] = previa with { Estado = EjecutorPorPersonas.EstadoOk, Resultado = resultado, TipoError = null, ConsultadoEn = ahora };
        }

        var faltan = previas.Where(p => p.Estado == EjecutorPorPersonas.EstadoError && !nuevas.ContainsKey(p.PersonaId)).Select(p => p.Orden).ToList();
        if (faltan.Count > 0)
            throw ValidadorResultadoManual.Invalido("personas", $"Llena todas las personas en error; faltan las de orden {string.Join(", ", faltan)}.");

        var payloadPersonas = JsonSerializer.Serialize(
            new PayloadPersonas([.. previas.Select(p => nuevas.GetValueOrDefault(p.PersonaId) ?? p)]), EjecutorPorPersonas.Json);
        return (payloadPersonas, [.. nuevas.Keys.Select(id => Intento(celda, autor, ahora, id))]);
    }

    private static IntentoConsulta Intento(CeldaConsulta celda, Guid autor, DateTimeOffset ahora, Guid? personaId) => new()
    {
        TenantId = celda.TenantId,
        DiagnosticoId = celda.DiagnosticoId,
        VehiculoId = celda.VehiculoId,
        ConsultaId = celda.Id,
        Fuente = celda.Fuente,
        Disparador = DisparadorIntento.Manual,
        Inicio = ahora,
        Fin = ahora,
        Estado = ResultadoIntento.Success,
        UsuarioId = autor,
        PersonaId = personaId,
    };

    /// <summary>R-8: el lote cerrado vuelve a EN_PROCESO con historial MANUAL y el usuario del backoffice.</summary>
    private void Reabrir(CabeceraDiagnostico cabecera, Guid autor, DateTimeOffset ahora)
    {
        var transicion = TransicionesLote.Transicionar(cabecera.Estado, EstadoDiagnostico.EnProceso, DisparadorEstado.Manual);
        cabecera.Estado = transicion.Nuevo;
        cabecera.CerradoEn = null;
        db.Add(new HistorialEstadoDiagnostico
        {
            TenantId = cabecera.TenantId,
            DiagnosticoId = cabecera.Id,
            EstadoAnterior = transicion.Anterior,
            EstadoNuevo = transicion.Nuevo,
            Disparador = transicion.Disparador,
            UsuarioId = autor,
            En = ahora,
        });
    }

    /// <summary>Sube la versión del tablero del lote de la empresa; el UPDATE bloquea la cabecera hasta el commit (frente al procesador).</summary>
    private async Task<long> SubirVersionAsync(Guid empresa, Guid diagnosticoId, CancellationToken ct)
    {
        var lote = db.ParaEditarDeLaEmpresa<CabeceraDiagnostico>(empresa).Where(c => c.Id == diagnosticoId);
        await lote.ExecuteUpdateAsync(s => s.SetProperty(c => c.VersionTablero, c => c.VersionTablero + 1), ct).ConfigureAwait(false);
        return await lote.Select(c => c.VersionTablero).SingleAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Las personas en ERROR del payload, con su documento descifrado SOLO para enmascararlo.</summary>
    private async Task<IReadOnlyList<PersonaEnErrorDto>> PersonasEnErrorAsync(Guid empresa, CeldaConsulta celda, CancellationToken ct)
    {
        var enError = (EjecutorPorPersonas.Leer(celda.Payload)?.Personas ?? [])
            .Where(p => p.Estado == EjecutorPorPersonas.EstadoError)
            .GroupBy(p => p.PersonaId).ToDictionary(g => g.Key, g => g.First());
        if (enError.Count == 0)
            return [];
        var ids = enError.Keys.ToList();
        var filas = await db.DeLaEmpresa<PersonaVehiculo>(empresa)
            .Where(p => p.VehiculoId == celda.VehiculoId && ids.Contains(p.Id))
            .OrderBy(p => p.Orden)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. filas.Select(p => new PersonaEnErrorDto(
            p.Id, p.Orden, CodigosDeEnum.Codigo(p.Rol), p.DocTipo, Enmascarado(p.DocCifrado), p.Origen == OrigenDocumento.DeclaradoCliente,
            enError[p.Id].Consulta, enError[p.Id].TipoError))];
    }

    private string Enmascarado(byte[] cifrado)
    {
        try
        {
            return EnmascaradorDocumento.Enmascarar(protector.Desproteger(cifrado));
        }
        catch (CryptographicException)
        {
            return EnmascaradorDocumento.Mascara;
        }
    }

    [LoggerMessage(EventId = 138000, Level = LogLevel.Information, Message = "Llenado manual de la celda {CeldaId} ({Fuente}) de la empresa {Empresa} por el usuario {UsuarioId}: {Intentos} intento(s) MANUAL")]
    private static partial void CeldaLlenada(ILogger logger, Guid celdaId, Fuente fuente, Guid empresa, Guid usuarioId, int intentos);
}
