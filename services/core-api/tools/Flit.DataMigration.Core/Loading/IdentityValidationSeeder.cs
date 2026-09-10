using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.DataMigration.V1.Mapping;
using Flit.DataMigration.V1.Source;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flit.DataMigration.V1.Loading;

/// <summary>Cuántas validaciones de identidad se sembraron y qué quedó sin sembrar.</summary>
public sealed record IdentitySeedResult(int Creadas, int YaExistian, IReadOnlyList<string> Avisos)
{
    public static readonly IdentitySeedResult Vacio = new(0, 0, []);
}

/// <summary>
/// Traslada a V2 el hecho de que la identidad de una parte YA quedó acreditada en V1.
///
/// <para>
/// Sin esto, un trámite migrado llega a V2 con su carta selfie en el expediente —la prueba de que esa
/// persona validó su identidad— y aun así el wizard le pide al gestor que la valide otra vez, ahora
/// pagando una verificación con Kyverum. El trabajo ya estaba hecho; lo que faltaba era decirlo en el
/// vocabulario de V2.
/// </para>
///
/// <para>
/// Se ejecuta en la instancia 3 y DESPUÉS de materializar los documentos, no antes, por una razón
/// concreta: para la identidad validada biométricamente, la carta selfie es la única evidencia que
/// queda cuando V1 se apague. Si la carta no llegó, no se afirma nada — antes que un expediente que
/// dice "identidad aprobada" y no puede mostrar con qué, es preferible que V2 la vuelva a pedir.
/// </para>
///
/// <para>
/// <b>Alcance deliberado.</b> La fila que se escribe queda anclada a SU trámite y a ningún otro:
/// <c>provider = migracion_v1</c> la excluye de las dos consultas de reuso de identidad
/// (<c>FindVigenteApprovedByDocumentAsync</c> y <c>ListVigenteApprovedIdentityKeysAsync</c>), y
/// <c>person_id</c> va nulo para no crear ni tocar personas del tenant. Una identidad traída de V1
/// acredita el trámite que la trajo; no habilita trámites nuevos que nadie validó.
/// </para>
///
/// <para>
/// <b>Lo que NO hace.</b> No toca la firma de la compraventa
/// (<c>procedure_instance_signatures</c>): son dos conceptos distintos en V2 y lo que V1 acreditaba
/// era la identidad, no el acuerdo entre las partes.
/// </para>
/// </summary>
public sealed class IdentityValidationSeeder(V1ProcedureKind kind, FlitDbContext db)
{
    /// <summary>
    /// Escribe las validaciones aprobadas del trámite. <paramref name="cartaSelfieSha"/> mapea la clave
    /// de cada pieza del snapshot a su sha256, para poder anclar la evidencia; si la carta de una parte
    /// no está ahí, esa parte no se siembra.
    /// </summary>
    public async Task<IdentitySeedResult> SeedAsync(
        V1SourceRecord record,
        TramiteTarget targetRef,
        IReadOnlyDictionary<string, string> cartaSelfieSha,
        DateTimeOffset now,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(targetRef);
        ArgumentNullException.ThrowIfNull(cartaSelfieSha);

        var aprobadas = kind.IdentityPolicy.AprobadasEnV1(record);
        if (aprobadas.Count == 0)
        {
            return IdentitySeedResult.Vacio;
        }

        var avisos = new List<string>();
        var creadas = 0;
        var yaExistian = 0;

        var actores = await db.Set<ProcedureInstanceActor>()
            .Where(a => a.ProcedureInstanceId == targetRef.V2Id)
            .ToListAsync(cancellationToken);

        foreach (var aprobada in aprobadas)
        {
            // Sin carta no hay afirmación. El aviso importa: es la señal de que ese trámite llegará a
            // V2 pidiendo una validación que en V1 ya estaba hecha, y de por qué.
            if (aprobada.ExigeCartaSelfie && !cartaSelfieSha.ContainsKey(aprobada.SelfieLetterPieceKey))
            {
                avisos.Add(
                    $"identidad del {aprobada.Nombre}: aprobada en V1 pero no se marca en V2 — V1 no entregó "
                    + "su carta selfie y esa carta es la única evidencia que respalda la aprobación. "
                    + "En V2 habrá que validar la identidad de nuevo.");
                continue;
            }

            var actor = actores.Find(a =>
                string.Equals(a.ActorType, aprobada.PartyRole, StringComparison.OrdinalIgnoreCase));
            if (actor is null)
            {
                avisos.Add(
                    $"identidad del {aprobada.Nombre}: aprobada en V1 pero no se marca en V2 — el trámite no "
                    + $"tiene actor '{aprobada.PartyRole}' (la instancia 1 no lo migró, normalmente por falta "
                    + "de documento en V1).");
                continue;
            }

            // Quién valida NO siempre es el actor: si la parte es una persona jurídica, quien acredita
            // identidad es su representante legal. Se resuelve con el MISMO servicio que usa V2 al
            // iniciar una validación, porque es el mismo que después compara documentos en el gate
            // (BiometricRules.DocumentoCoincide). Copiar el NIT aquí dejaría la fila sin efecto: el gate
            // la descartaría por no coincidir con el documento del sujeto.
            var subject = IdentitySubjectResolver.For(actor);
            if (string.IsNullOrWhiteSpace(subject.TipoDocumento)
                || string.IsNullOrWhiteSpace(subject.NumeroDocumento))
            {
                avisos.Add(
                    $"identidad del {aprobada.Nombre}: aprobada en V1 pero no se marca en V2 — no hay documento "
                    + "del sujeto que valida"
                    + (subject.EsRepresentanteLegal ? " (representante legal sin documento en V1)." : "."));
                continue;
            }

            var validationId = DeterministicGuid.ForV1Child(
                record.SourceTable, record.Id, $"identidad:{aprobada.PartyRole}");

            var existe = await db.Set<ProcedureInstanceBiometricValidation>()
                .AnyAsync(v => v.Id == validationId, cancellationToken);
            if (existe)
            {
                yaExistian++;
                continue;
            }

            var validadaEn = aprobada.AprobadaEnV1 ?? FechaDeRespaldo(record, now);

            var validation = new ProcedureInstanceBiometricValidation
            {
                Id = validationId,
                TenantId = targetRef.TenantId,
                ProcedureInstanceId = targetRef.V2Id,
                // Nulo A PROPÓSITO: sembrar personas del tenant desde la migración es otra decisión y
                // otro alcance. Sin person_id la fila acredita el trámite sin entrar al catálogo de
                // personas (el CHECK de la tabla se satisface con procedure_instance_id).
                PersonId = null,
                PartyRole = aprobada.PartyRole,
                Name = Truncar(subject.Nombre ?? actor.FullName, 200),
                DocumentType = Truncar(subject.TipoDocumento, 20),
                DocumentNumber = Truncar(subject.NumeroDocumento, 40),
                // V1 no siempre tiene correo de la parte, y aquí no se envía nada: no hay enlace de
                // captura que mandar. La columna es NOT NULL, así que va vacía antes que inventada.
                Email = Truncar(subject.Email ?? string.Empty, 320),
                Status = BiometricEstados.Aprobado,
                Provider = BiometricProviders.MigracionV1,
                // No hubo magic-link. El hash es determinístico sobre el origen para no chocar con el
                // índice único ni cambiar si se re-corre la instancia, y no abre nada: no existe token
                // del que sea el hash.
                TokenHash = HashDeterministico(record.SourceTable, record.Id, aprobada.PartyRole),
                // Un enlace que nunca existió no puede estar vigente.
                ExpiresAt = validadaEn,
                ValidatedAt = validadaEn,
                // OJO — esto DIVERGE de Approve(), y es la única concesión deliberada de este sembrado.
                //
                // Approve() estampa `validado_at + 30 días`. Con la fecha real de V1 eso deja
                // prácticamente TODA identidad migrada nacida vencida: en la copia de producción, la
                // aprobación más reciente de un borrador es de hace casi seis meses. El gate volvería a
                // pedir la validación y el trámite migrado quedaría igual que si no hubiéramos sembrado
                // nada.
                //
                // Así que la vigencia se cuenta desde la MIGRACIÓN, no desde V1. `valid_until` es
                // justamente el campo pensado para eso (ver BiometricRules.EsAprobadaVigente: es la
                // fuente de verdad del vencimiento y es editable para vencer o extender). `validated_at`
                // conserva la fecha REAL de V1, que es la que ve el gestor y la que queda en auditoría:
                // no se falsea cuándo validó la persona, se decide hasta cuándo lo aceptamos.
                //
                // Efecto conocido: la grilla de Validaciones calcula "días restantes" desde
                // `validated_at` y mostrará 0 para estas filas, aunque el filtro de vigencia (que sí lee
                // `valid_until`) las dé por vigentes. Es un número informativo en una grilla de
                // administración; se prefirió eso antes que mentir en la fecha de validación.
                ValidUntil = BiometricRules.FechaFinVigencia(now),
                CertificateHash = cartaSelfieSha.TryGetValue(aprobada.SelfieLetterPieceKey, out var sha)
                    ? sha
                    : null,
                Attempts = 0,
                MaxAttempts = BiometricRules.MaxIntentos,
                Detail = Procedencia(record, aprobada, now),
                CreatedAt = now,
            };

            if (!dryRun)
            {
                db.Set<ProcedureInstanceBiometricValidation>().Add(validation);
                await db.SaveChangesAsync(cancellationToken);
            }

            creadas++;
        }

        return new IdentitySeedResult(creadas, yaExistian, avisos);
    }

    /// <summary>
    /// De dónde salió esta validación, en el <c>detail</c> jsonb. Es lo que permite auditar meses
    /// después por qué un trámite figura con la identidad aprobada sin que nadie la validara en V2.
    /// </summary>
    private static string Procedencia(V1SourceRecord record, IdentityApproval aprobada, DateTimeOffset now) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["origen"] = "migracion_v1",
            ["v1_tabla"] = record.SourceTable,
            ["v1_id"] = record.Id,
            ["parte_v1"] = aprobada.Nombre,
            ["motivo"] = aprobada.PorFirmaFisica
                ? "firma física ante el gestor registrada en V1"
                : "validación biométrica aprobada en V1",
            ["aprobada_en_v1"] = aprobada.AprobadaEnV1?.ToString("O", CultureInfo.InvariantCulture),
            ["vigencia_contada_desde_la_migracion"] = now.ToString("O", CultureInfo.InvariantCulture),
        });

    /// <summary>
    /// Fecha para las acreditaciones que V1 no fechó (típicamente la firma física). Se usa la última
    /// actualización del trámite antes que <c>now</c>: sitúa el hecho en la vida del trámite y no en el
    /// día en que se corrió la migración.
    /// </summary>
    private static DateTimeOffset FechaDeRespaldo(V1SourceRecord record, DateTimeOffset now) =>
        V1MapperShared.ParseDate(record.Column("updated_at"))
        ?? V1MapperShared.ParseDate(record.Column("created_at"))
        ?? now;

    /// <summary>
    /// Relleno de <c>token_hash</c>: 64 hex, único por (trámite, parte) y estable entre corridas. No es
    /// el hash de ningún token porque no hubo token; ocupa una columna NOT NULL con índice único.
    /// </summary>
    private static string HashDeterministico(string table, long id, string partyRole)
    {
        var semilla = string.Create(
            CultureInfo.InvariantCulture, $"migracion_v1:{table}:{id}:identidad:{partyRole}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(semilla)));
    }

    private static string Truncar(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
