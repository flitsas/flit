using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Lectura y reemplazo del dictamen vigente para el backoffice FLIT (corte 7). Cuelgan del grupo <c>/backoffice</c>
/// (<see cref="PoliticaBackoffice"/>: <c>diagnostico.backoffice.ver</c> + <c>tenant_type=FLIT</c>; empresa explícita en
/// <c>?empresa=</c>, y el GET queda auditado antes de tocar la fila). El <c>PUT</c> exige además
/// <c>diagnostico.dictamen.override</c>. La escritura de la otra compañía va por <see cref="EscrituraEntreEmpresas"/>.
/// No vuelve a evaluar, no redacta, no genera PDF y no toca el Excel ni el ZIP.
/// </summary>
internal static class OverrideDictamenEndpoints
{
    public const string Ruta = "/diagnosticos/{id:guid}/vehiculos/{vid:guid}/dictamen";

    /// <summary>Lo único que el override escribe en la compañía del lote.</summary>
    private static readonly Type[] TiposDelOverride = [typeof(DictamenVehiculo)];

    public static void Map(RouteGroupBuilder backoffice)
    {
        ArgumentNullException.ThrowIfNull(backoffice);
        backoffice.MapGet(Ruta, Obtener).WithName("ObtenerDictamenVehiculoBackoffice");
        backoffice.MapPut(Ruta, Reemplazar).RequirePermiso(Permisos.DictamenOverride).WithName("CorregirDictamenVehiculoBackoffice");
    }

    private static async Task<IResult> Obtener(Guid id, Guid vid, HttpContext http, DiagnosticoDb db, CancellationToken ct)
    {
        var empresa = EmpresaExplicita.De(http);
        var fila = await VigenteAsync(db, empresa, id, vid, seguir: false, ct).ConfigureAwait(false);
        return Results.Ok(ConsultaDictamen.Backoffice(fila));
    }

    private static async Task<IResult> Reemplazar(
        Guid id, Guid vid, HttpContext http, JsonElement cuerpo, DiagnosticoDb db, UsuarioActual usuario, TimeProvider reloj,
        IPlatformOutbox outbox, IProveedorRedactor proveedor, CancellationToken ct)
    {
        var veredicto = ExigirCuerpo(cuerpo);
        var empresa = EmpresaExplicita.De(http);
        var autor = usuario.Id != Guid.Empty
            ? usuario.Id
            : throw ReglaException.Prohibido("SIN_USUARIO", "El token no identifica al usuario: el override no se puede auditar.");

        var dto = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var vigente = await VigenteAsync(db, empresa, id, vid, seguir: true, ct).ConfigureAwait(false);
            var cabecera = await db.DeLaEmpresa<CabeceraDiagnostico>(empresa).SingleAsync(c => c.Id == id, ct).ConfigureAwait(false);
            var suspendida = await db.DeLaEmpresa<ConfiguracionEmpresa>(empresa)
                .Select(c => (bool?)c.RedaccionIaSuspendida).SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? false;
            var ahora = reloj.GetUtcNow();
            var fila = EmisorDictamen.FilaDeOverride(
                vigente, veredicto, confirmado: true, autor, cabecera.TipoTramiteCodigo, suspendida, proveedor.Configurado, ahora);
            fila.TenantId = empresa;
            db.Add(fila);
            DictamenEmitido.Encolar(outbox, cabecera, fila, esOverride: true);
            try
            {
                await db.GuardarDeLaEmpresaAsync(empresa, TiposDelOverride, ct).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (EsConflictoDeDictamen(ex))
            {
                throw ReglaException.Conflicto(CodigosDictamen.DictamenEnConflicto, "Otro cambio de dictamen se guardó al mismo tiempo. Vuelve a leerlo.");
            }

            await transaccion.CommitAsync(ct).ConfigureAwait(false);
            return ConsultaDictamen.Backoffice(fila);
        }).ConfigureAwait(false);
        return Results.Ok(dto);
    }

    /// <summary>
    /// <c>confirmado</c> distinto de true no sigue. Un veredicto que no es VIABLE ni BLOQUEADO tampoco. No escribe.
    /// </summary>
    internal static string ExigirCuerpo(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty("confirmado", out var confirmado)
            || confirmado.ValueKind != JsonValueKind.True)
            throw new ReglaException(CodigosDictamen.ConfirmadoRequerido, "Confirma el cambio de veredicto para reemplazarlo.");
        var veredicto = json.TryGetProperty("veredicto", out var valor) && valor.ValueKind == JsonValueKind.String
            ? valor.GetString()
            : null;
        if (veredicto is not ("VIABLE" or "BLOQUEADO"))
            throw new ReglaException(CodigosDictamen.VeredictoInvalido, "El veredicto debe ser VIABLE o BLOQUEADO.");
        return veredicto;
    }

    /// <summary>Choque de dos overrides en <c>ux_dictamen_version</c> o <c>ux_dictamen_vigente</c>.</summary>
    internal static bool EsConflictoDeDictamen(DbUpdateException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        if (ex.InnerException is not PostgresException pg || pg.SqlState != PostgresErrorCodes.UniqueViolation)
            return false;
        if (pg.ConstraintName is "ux_dictamen_version" or "ux_dictamen_vigente")
            return true;
        return pg.MessageText.Contains("ux_dictamen_version", StringComparison.Ordinal)
            || pg.MessageText.Contains("ux_dictamen_vigente", StringComparison.Ordinal);
    }

    private static async Task<DictamenVehiculo> VigenteAsync(DiagnosticoDb db, Guid empresa, Guid id, Guid vid, bool seguir, CancellationToken ct)
    {
        if (!await db.DeLaEmpresa<CabeceraDiagnostico>(empresa).AnyAsync(c => c.Id == id, ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("El diagnóstico");
        if (!await db.DeLaEmpresa<VehiculoDiagnostico>(empresa).AnyAsync(v => v.Id == vid && v.DiagnosticoId == id, ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("El vehículo");

        var consulta = seguir
            ? db.ParaEditarDeLaEmpresa<DictamenVehiculo>(empresa)
            : db.DeLaEmpresa<DictamenVehiculo>(empresa);
        return await consulta.SingleOrDefaultAsync(d => d.VehiculoId == vid && d.Vigente, ct).ConfigureAwait(false) ?? throw ConsultaDictamen.SinFila();
    }
}
