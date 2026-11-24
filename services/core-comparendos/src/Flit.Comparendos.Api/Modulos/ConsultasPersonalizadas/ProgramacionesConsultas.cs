using System.Globalization;
using Flit.Comparendos.Api.Modulos.Catalogos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.ConsultasPersonalizadas;

/// <summary>Cuerpo de <c>POST /programaciones</c> (CONTRATO §4). <see cref="Hora"/> en «HH:mm» de Colombia.</summary>
public sealed record GuardarProgramacionRequest(
    Guid? Id,
    Guid ConsultaId,
    string? Periodicidad,
    int? DiaSemana,
    int? DiaMes,
    string? Hora,
    IReadOnlyList<string>? Destinatarios,
    bool Activa = true);

/// <summary>
/// Una programación tal como la ve la UI. <see cref="DiaSemana"/> es ISO (1 = lunes … 7 = domingo).
/// <see cref="UltimoResultado"/> va en términos de negocio («Enviado a 3 destinatarios»).
/// </summary>
public sealed record ConsultaProgramadaDto(
    Guid Id,
    Guid ConsultaId,
    string ConsultaNombre,
    string Periodicidad,
    int? DiaSemana,
    int? DiaMes,
    string Hora,
    IReadOnlyList<string> Destinatarios,
    bool Activa,
    DateTimeOffset? UltimaEjecucion,
    DateTimeOffset? ProximaEjecucion,
    string? UltimoResultado);

/// <summary>Reglas de la programación por correo de una consulta guardada (Reportes 2.0, ronda 2).</summary>
public static class Programaciones
{
    public const string Semanal = "semanal";
    public const string Mensual = "mensual";
    /// <summary>Igual que la programación de reportes de Trámites.</summary>
    public const int MaxDestinatarios = 10;
    public const int MaxPorEmpresa = 50;

    /// <summary>
    /// El siguiente instante (UTC) estrictamente después de <paramref name="ahora"/> que cae en el día y la hora de
    /// Colombia de la programación. <paramref name="diaSemana"/> es ISO (1 = lunes … 7 = domingo); en .NET el domingo es 0.
    /// </summary>
    public static DateTimeOffset Proxima(string periodicidad, int? diaSemana, int? diaMes, TimeOnly hora, DateTimeOffset ahora)
    {
        var local = ColombiaTime.From(ahora);
        var hoy = DateOnly.FromDateTime(local.DateTime);
        DateTimeOffset En(DateOnly dia) => new DateTimeOffset(dia.ToDateTime(hora), ColombiaTime.Offset).ToUniversalTime();

        if (periodicidad == Semanal)
        {
            var objetivo = (DayOfWeek)((diaSemana ?? 1) % 7);
            var dias = ((int)objetivo - (int)hoy.DayOfWeek + 7) % 7;
            var candidato = En(hoy.AddDays(dias));
            return candidato > ahora ? candidato : En(hoy.AddDays(dias + 7));
        }

        var dia = Math.Clamp(diaMes ?? 1, 1, 28);
        var esteMes = En(new DateOnly(hoy.Year, hoy.Month, dia));
        if (esteMes > ahora)
            return esteMes;
        var siguiente = new DateOnly(hoy.Year, hoy.Month, 1).AddMonths(1);
        return En(new DateOnly(siguiente.Year, siguiente.Month, dia));
    }

    /// <summary>Valida y deja el pedido en forma canónica. 422 <c>PROGRAMACION_INVALIDA</c> con un mensaje que nombra el problema.</summary>
    public static (string Periodicidad, int? DiaSemana, int? DiaMes, TimeOnly Hora, List<string> Destinatarios) Validar(GuardarProgramacionRequest? p)
    {
        if (p is null)
            throw Invalida("Falta la programación.");
        var periodicidad = p.Periodicidad?.Trim().ToLowerInvariant();
        switch (periodicidad)
        {
            case Semanal when p.DiaSemana is not (>= 1 and <= 7) || p.DiaMes is not null:
                throw Invalida("Una programación semanal lleva el día de la semana (1 = lunes … 7 = domingo) y no el día del mes.");
            case Mensual when p.DiaMes is not (>= 1 and <= 28) || p.DiaSemana is not null:
                throw Invalida("Una programación mensual lleva el día del mes (del 1 al 28) y no el día de la semana.");
            case Semanal or Mensual:
                break;
            default:
                throw Invalida("La periodicidad es «semanal» o «mensual».");
        }

        if (!TimeOnly.TryParseExact(p.Hora?.Trim(), ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var hora))
            throw Invalida("La hora va en formato HH:mm (hora de Colombia).");
        var destinatarios = CatalogosEndpoints.NormalizarCorreos(p.Destinatarios ?? []);
        if (destinatarios.Count == 0)
            throw Invalida("Agrega al menos un destinatario.");
        if (destinatarios.Count > MaxDestinatarios)
            throw Invalida($"Se puede enviar a {MaxDestinatarios} destinatarios como máximo.");
        return (periodicidad, p.DiaSemana, p.DiaMes, new TimeOnly(hora.Hour, hora.Minute), destinatarios);
    }

    private static ReglaException Invalida(string mensaje) => new("PROGRAMACION_INVALIDA", mensaje);

    public static ConsultaProgramadaDto Dto(ConsultaProgramada p, string consultaNombre) => new(
        p.Id, p.ConsultaId, consultaNombre, p.Periodicidad, p.DiaSemana, p.DiaMes, p.Hora.ToString("HH:mm", CultureInfo.InvariantCulture),
        p.Destinatarios, p.Activa, p.UltimaEjecucion, p.ProximaEjecucion, p.UltimoResultado);
}

/// <summary>Las programaciones de una empresa: solo sobre consultas guardadas de la empresa, nunca sobre una de fábrica.</summary>
public sealed class RepositorioProgramaciones(ComparendosDb db, Reloj reloj)
{
    public async Task<IReadOnlyList<ConsultaProgramadaDto>> ListarAsync(Guid tenant, CancellationToken ct)
    {
        var filas = await (from p in db.ConsultasProgramadas.AsNoTracking()
                           join q in db.ConsultasGuardadas.AsNoTracking() on p.ConsultaId equals q.Id
                           where p.TenantId == tenant
                           orderby q.Nombre, p.CreadoEn
                           select new { p, q.Nombre }).ToListAsync(ct).ConfigureAwait(false);
        return filas.Select(x => Programaciones.Dto(x.p, x.Nombre)).ToList();
    }

    public async Task<ConsultaProgramadaDto> GuardarAsync(Guid tenant, Guid usuario, GuardarProgramacionRequest pedido, CancellationToken ct)
    {
        var (periodicidad, diaSemana, diaMes, hora, destinatarios) = Programaciones.Validar(pedido);
        var consulta = await db.ConsultasGuardadas.AsNoTracking().FirstOrDefaultAsync(q => q.Id == pedido.ConsultaId, ct).ConfigureAwait(false);
        // Una guardada es de su usuario (/saved solo le muestra las suyas): programar la de otro haría que sus cambios o su
        // borrado le cambiaran o le quitaran el envío sin saberlo.
        if (consulta is null || consulta.DeFabrica || consulta.TenantId != tenant || consulta.UsuarioId != usuario)
            throw new ReglaException("PROGRAMACION_INVALIDA", consulta?.DeFabrica == true
                ? "Las consultas de fábrica no se programan: guarda una copia y prográmala."
                : "Solo se programan tus propias consultas guardadas.");

        ConsultaProgramada? p = null;
        if (pedido.Id is { } id)
            p = await db.ConsultasProgramadas.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant, ct).ConfigureAwait(false)
                ?? throw ReglaException.NoEncontrado("La programación");
        if (p is null)
        {
            if (await db.ConsultasProgramadas.CountAsync(x => x.TenantId == tenant, ct).ConfigureAwait(false) >= Programaciones.MaxPorEmpresa)
                throw new ReglaException("LIMITE_PROGRAMACIONES", $"La empresa ya tiene {Programaciones.MaxPorEmpresa} envíos programados. Borra alguno para programar otro.", StatusCodes.Status409Conflict);
            p = new ConsultaProgramada { Id = Guid.CreateVersion7(), TenantId = tenant, CreadoPor = usuario, CreadoEn = reloj.Ahora };
            db.ConsultasProgramadas.Add(p);
        }

        // Quien guarda es el dueño de la consulta (se exige arriba) y pasa a ser el dueño del envío: es a quien se le
        // verifica el permiso antes de cada envío.
        (p.ConsultaId, p.Periodicidad, p.DiaSemana, p.DiaMes, p.Hora, p.Destinatarios, p.Activa, p.CreadoPor) =
            (consulta.Id, periodicidad, diaSemana, diaMes, hora, destinatarios, pedido.Activa, usuario);
        p.ProximaEjecucion = p.Activa ? Programaciones.Proxima(periodicidad, diaSemana, diaMes, hora, reloj.Ahora) : null;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Programaciones.Dto(p, consulta.Nombre);
    }

    public async Task BorrarAsync(Guid tenant, Guid id, CancellationToken ct)
    {
        var borradas = await db.ConsultasProgramadas.Where(x => x.Id == id && x.TenantId == tenant).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        if (borradas == 0)
            throw ReglaException.NoEncontrado("La programación");
    }
}
