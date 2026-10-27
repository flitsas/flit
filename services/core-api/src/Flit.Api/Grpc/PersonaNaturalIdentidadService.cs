using System.Net.Mail;
using Flit.Infrastructure.Persistence;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Flit.Tramites.Grpc.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Grpc;

/// <summary>
/// HU #14044 (Feature #14023, Épica #14021, ADR-0075) — la validación de identidad de las partes de una solicitud
/// del portal de Persona Natural, detrás de la fachada gRPC. Adaptador delgado: el envío del enlace, el proveedor
/// (Kyverum o mock), el webhook y la outbox son los de la prevalidación de FLIT 2
/// (<see cref="IniciarPrevalidacionHandler.HandlePortalAsync"/>); aquí solo
/// se valida la entrada, se fija la empresa de Persona Natural y se traduce el desenlace.
/// <para>
/// Decisión del PO (2026-10-27): cada solicitud pide una validación NUEVA a cada parte; nada se reutiliza entre trámites ni
/// vence por días (<c>vigente_hasta</c> nunca se llena). Lo que FLIT 2 reutiliza para sus propios usuarios no entra aquí.
/// </para>
/// Se instancia por solicitud con <see cref="ActivatorUtilities"/> desde <see cref="PersonaNaturalGrpcService"/> (que ya pasó
/// el candado de cliente y empresa): así la fachada no cambia su constructor ni el registro de servicios.
/// </summary>
internal sealed class PersonaNaturalIdentidadService(
    FlitDbContext db,
    IniciarPrevalidacionHandler iniciar)
{
    /// <summary>Estados de la validación que ve el portal (el proto: pendiente | aprobada | rechazada | vencida).</summary>
    public const string Pendiente = "pendiente";
    public const string Aprobada = "aprobada";
    public const string Rechazada = "rechazada";
    public const string Vencida = "vencida";

    public async Task<SolicitarValidacionIdentidadResponse> SolicitarAsync(SolicitarValidacionIdentidadRequest request, CancellationToken ct)
    {
        var solicitud = Texto(request.SolicitudId) ?? throw Invalido("solicitud_id es obligatorio.");
        var parte = Texto(request.Parte) ?? throw Invalido("parte es obligatoria.");
        var persona = request.Persona ?? throw Invalido("persona es obligatoria.");
        var tipoDocumento = Texto(persona.TipoDocumento) ?? throw Invalido("tipo_documento es obligatorio.");
        var numeroDocumento = Texto(persona.NumeroDocumento) ?? throw Invalido("numero_documento es obligatorio.");
        var nombre = Texto(persona.Nombre) ?? throw Invalido("nombre es obligatorio.");

        // AC6: un NIT es persona jurídica y el portal no la soporta; se rechaza ANTES de tocar nada ni llamar al proveedor.
        if (ActorPersonTypes.IsJuridical(ActorPersonTypes.ResolveForDocument(tipoDocumento, declared: null)))
            throw PlatformRpcErrors.ToRpcException("persona_juridica_no_soportada", "El portal solo atiende personas naturales.", StatusCode.FailedPrecondition);

        // AC4: contacto inválido, sin llamar al proveedor. El campo que falla va en la razón, nunca su valor.
        var correo = Texto(persona.Correo);
        if (correo is null || !EsCorreoValido(correo))
            throw PlatformRpcErrors.ToRpcException("contacto_invalido_correo", "El correo no tiene un formato válido.", StatusCode.InvalidArgument);

        var (resultado, error) = await EnEmpresaAsync(
            () => iniciar.HandlePortalAsync(PlatformTenants.PersonaNatural, solicitud, parte, tipoDocumento, numeroDocumento, nombre, correo, ct), ct)
            .ConfigureAwait(false);
        if (error is not null || resultado is null)
            throw error switch
            {
                "datos_incompletos" => Invalido("Faltan datos de la persona."),
                _ => PlatformRpcErrors.ToRpcException("proveedor_no_disponible", "No fue posible enviar el enlace de validación por ahora.", StatusCode.Unavailable),
            };

        return new SolicitarValidacionIdentidadResponse
        {
            ValidacionId = resultado.Validation.Id.ToString(),
            Estado = EstadoDelPortal(resultado.Validation.Status, resultado.Validation.ExpiresAt, DateTimeOffset.UtcNow),
        };
    }

    /// <summary>
    /// Estado de FLIT 2 → estado del portal. Todo lo que sigue abierto (enviado, en proceso, encolado, manual) es
    /// <c>pendiente</c>; un enlace abierto cuyo plazo ya pasó es <c>vencida</c> (se resuelve reenviando); un envío agotado
    /// (<c>error_envio</c>) sigue <c>pendiente</c> para quien lo resuelve desde FLIT 2.
    /// </summary>
    internal static string EstadoDelPortal(string status, DateTimeOffset expiraEn, DateTimeOffset ahora) => status switch
    {
        BiometricEstados.Aprobado => Aprobada,
        BiometricEstados.Rechazado => Rechazada,
        BiometricEstados.Expirado => Vencida,
        BiometricEstados.Enviado or BiometricEstados.EnProceso when ahora > expiraEn => Vencida,
        _ => Pendiente,
    };

    /// <summary>
    /// El DbContext usa estrategia de reintento: la transacción manual va dentro de ella. Se fija la empresa de la llamada
    /// (<c>set_config</c> local a la transacción, parametrizado) para que la fila nazca y se lea bajo la RLS de Persona
    /// Natural, igual que el lanzamiento de la validación del mandatario.
    /// </summary>
    private async Task<T> EnEmpresaAsync<T>(Func<Task<T>> operacion, CancellationToken ct)
    {
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction is not null)
            return await operacion().ConfigureAwait(false);

        var estrategia = db.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                await db.Database
                    .ExecuteSqlInterpolatedAsync($"SELECT set_config('app.current_tenant_id', {PlatformTenants.PersonaNatural.ToString()}, true)", ct)
                    .ConfigureAwait(false);
                var resultado = await operacion().ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return resultado;
            }
        }).ConfigureAwait(false);
    }

    private static bool EsCorreoValido(string correo) =>
        correo.Length <= 320 && MailAddress.TryCreate(correo, out var mail) && string.Equals(mail.Address, correo, StringComparison.Ordinal);

    private static Guid IdValido(string? valor) =>
        Guid.TryParse(Texto(valor), out var id) ? id : throw Invalido("validacion_id no es un identificador válido.");

    private static string? Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static RpcException Invalido(string detalle) => new(new Status(StatusCode.InvalidArgument, detalle));

    private static RpcException NoEncontrada() =>
        PlatformRpcErrors.ToRpcException("validacion_no_encontrada", "No existe esa validación.", StatusCode.NotFound);
}
