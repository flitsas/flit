using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.Identity;

/// <summary>Códigos estables del motivo por el que NO se envió la validación de identidad (HU #11665).</summary>
public static class EnvioValidacionMotivos
{
    /// <summary>El proveedor configurado no envía correos (mock): con él no se envía nada, nunca.</summary>
    public const string ProveedorNoEnvia = "proveedor_no_envia";

    /// <summary>El sujeto de identidad resuelto no está marcado como representante legal.</summary>
    public const string SujetoNoEsRepresentante = "sujeto_no_es_representante";

    /// <summary>El representante legal declarado no trae tipo o número de documento.</summary>
    public const string RlSinDocumento = "rl_sin_documento";

    /// <summary>El representante legal declarado no trae correo.</summary>
    public const string RlSinCorreo = "rl_sin_correo";

    /// <summary>INFORMATIVO: la firma del baúl ya cubre a esta parte; no hay nada que validar.</summary>
    public const string CubiertoPorBaul = "cubierto_por_baul";

    /// <summary>INFORMATIVO: el representante ya tiene identidad aprobada y vigente.</summary>
    public const string RepresentanteUtilizable = "representante_utilizable";
}

/// <summary>
/// Motivo tipificado de no envío. <paramref name="Informativo"/> distingue lo que el gestor debe
/// CORREGIR (dato faltante o proveedor que no envía) de lo que solo explica una ausencia legítima
/// (la parte ya está cubierta). La UI no debe pintar los informativos como bloqueo.
/// </summary>
public sealed record EnvioValidacionMotivo(string Codigo, bool Informativo);

/// <summary>
/// Estado —ya cargado— del que depende la decisión de FILTRADO DE DATOS del disparador. Es un valor
/// puro: no se consulta nada para construirlo más allá del actor y su sujeto de identidad.
/// </summary>
public sealed record EnvioValidacionEstado(
    bool ProveedorEnvia,
    bool ActorEsJuridico,
    bool SujetoEsRepresentanteLegal,
    bool RepresentanteDeclarado,
    bool RepresentanteTieneDocumento,
    bool SujetoTieneCorreo);

/// <summary>
/// HU #11665 — regla ÚNICA que responde «por qué no se envió la validación de identidad a esta parte».
///
/// <para><b>Por qué existe.</b> El disparador de la parte jurídica
/// (<c>PutActorsHandler.EnviarValidacionAlRepresentanteDeLaParteJuridicaAsync</c>) omitía el envío en
/// silencio: ni error, ni log de negocio, ni señal en la UI. El gestor veía un trámite que no avanzaba
/// y no tenía forma de saber qué le faltaba. Peor: las tres omisiones por datos incompletos eran UN
/// SOLO <c>continue</c> con condición compuesta, así que ni el código sabía cuál de las tres era.</para>
///
/// <para><b>Un solo sitio para escritor y lector.</b> La consume el ESCRITOR (el disparador, que
/// sustituye por esto sus condiciones inline) y el LECTOR (el listado de biometría, que la publica en
/// la respuesta). Si cada uno la calculara por su cuenta, el motivo mostrado podría contradecir al
/// motivo real, que es la clase de divergencia que ADR-0039 señala como origen del Bug #11141.</para>
///
/// <para><b>Alcance real de cada motivo.</b> Por el PUT de actores solo son alcanzables
/// <c>proveedor_no_envia</c> y <c>rl_sin_documento</c>: la captura ya rechaza antes al actor jurídico
/// sin correo del representante (<c>rl_email_requerido</c>), que es lo que produciría
/// <c>rl_sin_correo</c> y —al no haber bloque de RL— <c>sujeto_no_es_representante</c>. Esos dos no
/// sobran: el LECTOR sí ve actores guardados por otras vías (datos anteriores a esa validación,
/// integraciones), y sin ellos volvería a explicar dos situaciones distintas con el mismo silencio.</para>
///
/// <para><b>El motivo NO se persiste.</b> Es derivable del estado actual y se calcula al vuelo: en
/// cuanto el gestor corrige el dato, desaparece. Persistirlo lo dejaría obsoleto. Además, escribirlo
/// en <c>tramites.procedure_instance_field_values</c> sería activamente destructivo: esa tabla tiene un
/// trigger de inmutabilidad que rechaza también el INSERT salvo en borrador, y una escritura sobre un
/// trámite radicado aborta la transacción y se lleva por delante el documento que se esté
/// generando.</para>
/// </summary>
public static class EnvioValidacionBloqueoRules
{
    /// <summary>
    /// Estado del filtrado de datos para una parte. Usa los MISMOS predicados que el disparador
    /// (<see cref="ActorPersonTypes.IsJuridical"/> sobre <c>person_type</c> y el sujeto de identidad de
    /// <see cref="IdentitySubjectResolver"/>), para que no puedan divergir.
    /// <para>
    /// Se lee además el representante DECLARADO en <c>actor.metadata</c>, y no solo el sujeto: cuando el
    /// RL no trae documento, <see cref="IdentitySubjectResolver"/> cae al actor (el NIT de la empresa) y
    /// devuelve <c>EsRepresentanteLegal = false</c>. Mirando solo el sujeto, «hay un RL al que le falta
    /// el documento» y «no hay RL» serían el mismo caso, que es exactamente la confusión que esta HU
    /// viene a deshacer.
    /// </para>
    /// </summary>
    public static EnvioValidacionEstado EstadoDe(
        ProcedureInstanceActor actor,
        IdentitySubject subject,
        bool proveedorEnvia)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(subject);

        var rl = IdentitySubjectResolver.ParseRepresentanteLegal(actor.Metadata);

        return new EnvioValidacionEstado(
            proveedorEnvia,
            ActorPersonTypes.IsJuridical(actor.PersonType),
            subject.EsRepresentanteLegal,
            rl is not null,
            rl is not null
                && !string.IsNullOrWhiteSpace(rl.TipoDocumento)
                && !string.IsNullOrWhiteSpace(rl.NumeroDocumento),
            !string.IsNullOrWhiteSpace(subject.Email));
    }

    /// <summary>
    /// Motivo por el que el disparador NO llega a pedir el envío para esta parte, o <c>null</c> si sí
    /// llega. Las personas naturales devuelven <c>null</c> siempre: no entran al disparador, así que no
    /// tienen nada que reportar.
    /// <para>
    /// El orden es el del disparador y es significativo: <c>proveedor_no_envia</c> primero porque corta
    /// el método entero, y después los datos del representante en el orden en que se leen.
    /// </para>
    /// </summary>
    public static EnvioValidacionMotivo? Evaluar(EnvioValidacionEstado estado)
    {
        ArgumentNullException.ThrowIfNull(estado);

        if (!estado.ActorEsJuridico)
            return null;

        if (!estado.ProveedorEnvia)
            return new EnvioValidacionMotivo(EnvioValidacionMotivos.ProveedorNoEnvia, Informativo: false);

        if (!estado.SujetoEsRepresentanteLegal)
        {
            // Un RL declarado al que le falta el documento tiene arreglo evidente (capturarlo); no
            // haber declarado ninguno es otro problema y se nombra distinto.
            return estado.RepresentanteDeclarado && !estado.RepresentanteTieneDocumento
                ? new EnvioValidacionMotivo(EnvioValidacionMotivos.RlSinDocumento, Informativo: false)
                : new EnvioValidacionMotivo(EnvioValidacionMotivos.SujetoNoEsRepresentante, Informativo: false);
        }

        if (!estado.SujetoTieneCorreo)
            return new EnvioValidacionMotivo(EnvioValidacionMotivos.RlSinCorreo, Informativo: false);

        return null;
    }

    /// <summary>
    /// Motivo INFORMATIVO derivado de la precedencia única de envío (ADR-0039). Desde la HU #11662 el
    /// disparador ya no pre-chequea la cobertura: se la pregunta al evaluador río abajo, así que el
    /// motivo se lee de SU decisión y no de una copia local.
    /// <list type="bullet">
    ///   <item><c>cobertura_baul</c> → <c>cubierto_por_baul</c>.</item>
    ///   <item><c>identidad_vigente</c> → <c>representante_utilizable</c>: la persona ya tiene con qué
    ///     acreditarse, que es lo que preguntaba la compuerta retirada, pero ahora sobre la persona
    ///     ELEGIDA en el trámite y no sobre cualquier representante de la compañía.</item>
    /// </list>
    /// <para>Las validaciones en vuelo y los enlaces vencidos NO producen motivo: son estados visibles
    /// en el propio listado de biometría, con su fecha y sus intentos. Reportarlos aquí duplicaría en
    /// forma de código lo que la fila ya dice mejor.</para>
    /// </summary>
    public static EnvioValidacionMotivo? DesdeDecision(IdentitySendDecision? decision)
    {
        if (decision is null || decision.Kind != IdentitySendDecisionKind.NoEnviar)
            return null;

        return decision.Motivo switch
        {
            IdentitySendMotivo.CoberturaBaul =>
                new EnvioValidacionMotivo(EnvioValidacionMotivos.CubiertoPorBaul, Informativo: true),
            IdentitySendMotivo.IdentidadVigente =>
                new EnvioValidacionMotivo(EnvioValidacionMotivos.RepresentanteUtilizable, Informativo: true),
            _ => null,
        };
    }

    /// <summary>
    /// Misma derivación que <see cref="DesdeDecision"/> pero desde el estado que el LECTOR ya tiene
    /// resuelto para pintar el listado: la cobertura del baúl de la parte (el mismo cálculo que alimenta
    /// <c>firmaBaulPartes</c>) y si la parte tiene identidad aprobada y vigente —propia o referenciada—.
    /// Reproduce los pasos 1 y 2 de la precedencia en ese orden, sin consultar nada nuevo.
    /// </summary>
    public static EnvioValidacionMotivo? DesdeCobertura(bool cubiertoPorBaul, bool identidadVigente)
    {
        if (cubiertoPorBaul)
            return new EnvioValidacionMotivo(EnvioValidacionMotivos.CubiertoPorBaul, Informativo: true);

        if (identidadVigente)
            return new EnvioValidacionMotivo(EnvioValidacionMotivos.RepresentanteUtilizable, Informativo: true);

        return null;
    }
}
