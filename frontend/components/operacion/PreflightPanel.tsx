'use client';

// Panel de pre-vuelo (semáforo de requisitos). Presentacional: el fetch/estado
// viven en el wizard/hook. La consulta real (RUNT/SIMIT) se cablea en #10201;
// por ahora el hook alimenta un snapshot stub.

import { useWizardReadOnly } from './WizardReadOnlyContext';
import { WizardCardHeader } from './wizard-atoms';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import type {
  FineDetail,
  PreflightCheckStatus,
  PreflightSnapshot,
} from '@/lib/api/types/procedure-runtime';
import { WIZARD_BTN, WIZARD_BTN_SOLID, WIZARD_CTA_GRADIENT } from './wizard-field-styles';

interface Props {
  snapshot: PreflightSnapshot | null;
  loading: boolean;
  // HU #10885 (Feature #10862, CF-04, AC2) — `forceRefresh=true` cuando el disparo viene del botón
  // "Actualizar" (ya hay un resultado precargado): el caller lo reenvía a
  // `tramitesClient.runConsultation`/`runConsulta` para saltar el reúso de caché. Los callers que
  // ignoran el argumento (p. ej. `() => void handleRun()`) siguen siendo válidos (sin regresión).
  onRun: (forceRefresh?: boolean) => void;
  riesgoAceptado: boolean;
  onToggleRiesgo: (v: boolean) => void;
  /** Persistiendo la aceptación de riesgo: deshabilita el checkbox para evitar dobles clics. */
  saving?: boolean;
  // El disparo de la consulta puede vivir fuera del panel (p. ej. junto al campo
  // VIN en matrícula). En ese caso el panel es solo presentacional (semáforo).
  showRunButton?: boolean;
  /**
   * El panel se monta dentro de un acordeón que ya pinta la tarjeta y el título. Quita el marco y
   * la cabecera propia para no anidar dos tarjetas ni repetir el encabezado; el semáforo global
   * pasa a ser el badge del acordeón, que lo compone el caller.
   */
  bare?: boolean;
  // R3 (HU #10539) — en matrícula, cuando el preflight detecta que el VIN ya tiene
  // matrícula previa (check `vin_matricula`), el panel ofrece iniciar el traspaso del
  // vehículo. El wizard inyecta la navegación (sembrando placa/VIN); el panel es
  // presentacional. Ausente ⇒ no se ofrece el CTA (p. ej. en traspaso).
  onIniciarTraspaso?: () => void;
  /**
   * El trámite viene de una migración: llega sin consultas hechas y hay que correrlas antes de
   * radicar. Sube el aviso de "aún no hay consulta" de nota gris a llamada a la acción destacada.
   *
   * <p>El texto NO menciona la migración ni por qué no hay resultados —los de RUNT/SIMIT caducan en
   * minutos y por eso no viajan desde el sistema anterior—: eso es interioridad del sistema, no le
   * cambia nada a quien opera y solo siembra la duda de si el expediente llegó incompleto.</p>
   *
   * Ausente/false ⇒ trámite nativo, nota de siempre.
   */
  esMigrado?: boolean;
}

/**
 * Tono semántico del chip según el estado del check.
 *
 * Antes era un mapa de colores de relleno pleno con texto blanco, que el sistema prohíbe: el verde
 * de marca da 2.05:1 sobre blanco y el ámbar 2.0:1 — ninguno legible. `StatusBadge` resuelve el
 * tintado y el contraste desde la paleta única de badges, y de paso el chip queda igual que en las
 * tablas y en el resto del asistente.
 */
const STATUS_PILL_TONE: Record<PreflightCheckStatus, StatusTone> = {
  ok: 'success',
  warn: 'warning',
  fail: 'danger',
  unknown: 'warning',
  error: 'danger',
};

/**
 * Palabra visible en la pastilla.
 *
 * <p><c>unknown</c> se rotula «SIN DATO» y no «NO ENCONTRADO». Son cosas distintas y la etiqueta
 * anterior decía la que no era: <c>fail</c> es «la fuente respondió y esto no existe» (el vehículo
 * no está en el RUNT), mientras <c>unknown</c> es «no se pudo averiguar» — la consulta no se corrió
 * porque el organismo no la exige, o faltaba el documento del actor. Decirle al gestor «no
 * encontrado» de algo que nadie llegó a buscar lo manda a corregir un dato que está bien.</p>
 *
 * <p>De paso es cinco caracteres más corta, que en una pastilla que no encoge dentro de una tarjeta
 * de un cuarto de fila no es un detalle cosmético.</p>
 */
export function statusPillWord(status: PreflightCheckStatus): string {
  switch (status) {
    case 'ok':
      return 'OK';
    case 'warn':
      return 'ADVERTENCIA';
    case 'fail':
      return 'FALLA';
    case 'error':
      return 'ERROR';
    case 'unknown':
      return 'SIN DATO';
  }
}

/**
 * Texto de la pastilla: `OK - RUNT` · `ADVERTENCIA - RUNT` · `FALLA - RUNT` · `SIN DATO`.
 *
 * <p><b>El mensaje ya no entra en la pastilla.</b> En los estados de fallo se metía el mensaje
 * completo con un tope de 42 caracteres, y eso rompía la tarjeta: «FALLA - VEHÍCULO NO ENCONTRADO EN
 * RUNT» son treinta y ocho caracteres en versalitas que, con `whitespace-nowrap` y `shrink-0`,
 * reclaman más ancho del que tiene una tarjeta de un cuarto de fila. El título quedaba debajo de la
 * pastilla, con las letras montadas.</p>
 *
 * <p>Y además lo decía dos veces: el mismo mensaje se pinta bajo el título, que es su sitio. La
 * pastilla es el semáforo —estado y fuente—, no el detalle; ahora todos los estados tienen la misma
 * forma y ninguno puede volver a crecer sin control.</p>
 */
export function checkPillLabel(check: {
  status: PreflightCheckStatus;
  source?: string | null;
}): string {
  const word = statusPillWord(check.status);
  const src = sourceLabel(check.source);
  // `unknown` va sola: no se llegó a consultar, así que no hay fuente que atribuirle.
  if (check.status === 'unknown') return word;
  return src ? `${word} - ${src}` : word;
}

function checkPillTone(check: { status: PreflightCheckStatus; message?: string | null }): StatusTone {
  return STATUS_PILL_TONE[check.status];
}

/**
 * Pastilla OK del diagnóstico: relleno verde de marca + texto blanco, como el prototipo
 * (`OK - RUNT`). El resto de tonos siguen con `StatusBadge` tintado (contraste AA).
 */
function DiagnosticOkPill({ label, ariaLabel }: { label: string; ariaLabel: string }) {
  return (
    <span
      role="status"
      aria-label={ariaLabel}
      className="inline-flex shrink-0 items-center whitespace-nowrap rounded-full px-2.5 py-0.5 text-[11px] font-semibold text-white"
      style={{ background: '#8CC63F' }}
    >
      {label}
    </span>
  );
}


/**
 * Semáforo global del pre-vuelo. Exportado para que quien embeba el panel dentro de un acordeón
 * (`bare`) pueda pintar el mismo estado en la cabecera plegada, que es donde el gestor lo busca
 * cuando el panel está cerrado.
 */
export function preflightOverall(overall: string | null | undefined) {
  return overall ? (OVERALL[overall] ?? null) : null;
}

/**
 * Chip semántico del semáforo global (HU consolidación de chips — `StatusBadge`).
 *
 * <p><b>El chip ya no nombra la sección.</b> Decía «Pre-vuelo en verde» dentro de un panel titulado
 * «Diagnóstico de Requisitos Previos»: dos nombres para lo mismo en la misma pantalla, y el que se
 * leía era el peor. «Pre-vuelo» es <i>preflight</i> traducido literal —la metáfora del checklist de
 * aviación, jerga de programación— y a un gestor de trámites no le dice nada; no hay ningún avión.</p>
 *
 * <p>Repetir el nombre de la sección en su propio chip tampoco aportaba: lo que el gestor busca ahí
 * es el ESTADO. Así que el chip dice solo el estado, y de paso mide la mitad — que en la cabecera de
 * un acordeón, donde este mismo chip se pinta junto al título, es lo que evita que lo desplace.</p>
 *
 * <p>«Sin novedades» y no «sin bloqueos»: el amarillo tampoco tiene bloqueos, así que ese rótulo no
 * distinguiría el verde del ámbar.</p>
 */
const OVERALL: Record<string, { label: string; tone: StatusTone }> = {
  green: { label: 'Sin novedades', tone: 'success' },
  yellow: { label: 'Con advertencias', tone: 'warning' },
  red: { label: 'Con bloqueos', tone: 'danger' },
};

/**
 * Mapea el código de proveedor (interno) a la fuente colombiana que ve el
 * usuario. Nunca debe filtrarse el nombre del proveedor a la UI: el origen del
 * dato es el RUNT/SIMIT/RNMC, no la pasarela de consulta.
 */
const SOURCE_LABEL: Record<string, string> = {
  verifik: 'RUNT',
  verifik_vehicle: 'RUNT',
  verifik_simit: 'SIMIT',
  verifik_rnmc: 'RNMC',
  intempo: 'RUNT',
  kyverum_runt: 'RUNT',
  // El origen último del dato es el SIMIT; Kyverum es solo la pasarela.
  kyverum_fines: 'SIMIT',
  // Cartera propia de FLIT: aquí la fuente sí es interna.
  flit_fines: 'Comparendos FLIT',
  // Checks derivados por la plataforma, no por un proveedor externo
  // (p. ej. `vin_matricula`, o una consulta omitida por configuración del OT).
  system: 'FLIT',
};

export function sourceLabel(source: string | null | undefined): string {
  if (!source) return '';
  return SOURCE_LABEL[source] ?? source.toUpperCase();
}

/**
 * Sello de la consulta: qué fuentes respondieron y cuándo.
 *
 * <p>La fecha solo se mostraba cuando el resultado venía de CACHÉ («Dato reutilizado · Consultado
 * el …»). En una consulta fresca el panel no decía nada, que es justo cuando más vale decirlo: lo
 * que el gestor necesita saber es que esos datos los acaba de responder el RUNT, no que los dedujo
 * la aplicación.</p>
 *
 * <p>Las fuentes salen de los propios checks, sin repetir: un panel con RUNT y SIMIT lo dice, y uno
 * que solo consultó el RUNT no anuncia un SIMIT que nadie llamó. Sin fuentes reconocibles o sin
 * fecha, devuelve `null` y no se pinta nada — nunca una frase a medias.</p>
 */
/**
 * La comprobación no la respondió un tercero, la derivó FLIT. No cuenta como fuente consultada.
 *
 * <p>`flit_fines` NO entra aquí: es la cartera de comparendos de la compañía, un dato real que se
 * consulta igual que el SIMIT, solo que la fuente somos nosotros.</p>
 */
function esFuenteInterna(source: string | null | undefined): boolean {
  return (source ?? '').trim().toLowerCase() === 'system';
}

export function selloDeConsulta(
  checks: readonly { source?: string | null }[],
  fecha: string | null | undefined,
): string | null {
  if (!fecha) return null;
  const cuando = new Date(fecha);
  if (Number.isNaN(cuando.getTime())) return null;

  // Solo fuentes EXTERNAS. El sello afirma «esto lo dijo el RUNT», así que una comprobación que
  // deriva la propia plataforma (`system` → «FLIT»: el VIN ya matriculado, o una consulta que el
  // organismo mandó omitir) no puede aparecer en la lista — «Consultado en RUNT y FLIT» se lee como
  // que nos consultamos a nosotros mismos, y arruina justo la credibilidad que el sello viene a dar.
  const fuentes = [
    ...new Set(
      checks
        .filter((c) => !esFuenteInterna(c.source))
        .map((c) => sourceLabel(c.source))
        .filter(Boolean),
    ),
  ];
  const origen =
    fuentes.length === 0
      ? ''
      : fuentes.length === 1
        ? ` en ${fuentes[0]}`
        : ` en ${fuentes.slice(0, -1).join(', ')} y ${fuentes[fuentes.length - 1]}`;

  return `Consultado${origen} el ${cuando.toLocaleString('es-CO')}`;
}

/**
 * RNMC (HU #10602/#10603) corre por actor persona natural: las claves quedan
 * `rnmc_comprador_*` / `rnmc_vendedor_*`. Devuelve el sufijo de rol para
 * distinguir ambos checks en el panel (el label del proveedor es idéntico).
 */
export function checkRoleSuffix(key: string): string {
  if (key.startsWith('rnmc_comprador')) return ' (comprador)';
  if (key.startsWith('rnmc_vendedor')) return ' (vendedor)';
  return '';
}

/** Formatea un valor en pesos colombianos (sin decimales). Null/NaN → cadena vacía. */
function formatCOP(valor: number | null | undefined): string {
  if (valor == null || Number.isNaN(valor)) return '';
  return `$${Math.round(valor).toLocaleString('es-CO')} COP`;
}

/**
 * Detalle de los comparendos/multas de un check, listado bajo su advertencia. Cada fila muestra lo
 * que la fuente expone (número, fecha, organismo, infracción, estado y valor); los campos ausentes
 * se omiten. Nunca incluye datos del infractor (Habeas Data).
 */
export function FineDetailList({ details }: { details: FineDetail[] }) {
  if (details.length === 0) return null;
  return (
    <ul className="mt-1.5 space-y-1.5" aria-label="Detalle de comparendos">
      {details.map((d, i) => {
        const valor = formatCOP(d.valor);
        return (
          <li
            key={d.numero ?? `comparendo-${i}`}
            className="rounded-lg px-2.5 py-1.5"
            style={{ background: 'rgba(249,172,0,0.10)', border: '1px solid rgba(249,172,0,0.25)' }}
          >
            <div className="flex flex-wrap items-center justify-between gap-x-2 gap-y-0.5">
              <span className="text-xs font-semibold">
                {d.numero ? `Comparendo ${d.numero}` : 'Comparendo'}
              </span>
              {valor && (
                <span className="text-xs font-bold" style={{ color: 'var(--badge-warning-fg)' }}>
                  {valor}
                </span>
              )}
            </div>
            {d.infraccion && (
              <p className="mt-0.5 text-xs opacity-80">{d.infraccion}</p>
            )}
            <div className="mt-0.5 flex flex-wrap gap-x-2 gap-y-0.5 text-xs opacity-60">
              {d.fecha && <span>Fecha: {d.fecha}</span>}
              {d.organismo && <span>· {d.organismo}</span>}
              {d.estado && <span>· {d.estado}</span>}
            </div>
          </li>
        );
      })}
    </ul>
  );
}

export function PreflightPanel({
  snapshot,
  loading,
  onRun,
  riesgoAceptado,
  onToggleRiesgo,
  saving = false,
  showRunButton = true,
  bare = false,
  onIniciarTraspaso,
  esMigrado = false,
}: Props) {
  // En solo lectura nunca se ofrece el disparo de la consulta (Track C).
  const readOnly = useWizardReadOnly();
  const canRun = showRunButton && !readOnly;
  const hasResult = !!snapshot?.overall;
  const overall = snapshot?.overall;
  const checks = snapshot?.checks ?? [];
  const ov = overall ? OVERALL[overall] : null;
  // Un check "error" = consulta no verificable (proveedor caído/timeout): bloqueo DURO. NO se
  // ofrece "aceptar riesgo" (no es subsanable); el gestor debe reintentar la consulta.
  const hasProviderError = checks.some((c) => c.status === 'error');
  // El RUNT respondió y el vehículo NO existe: bloqueo DURO igual que el error de proveedor, pero
  // por otro motivo (la fuente sí contestó). Sin vehículo verificado no hay trámite que radicar, así
  // que tampoco se ofrece "aceptar riesgo": lo accionable es corregir el identificador.
  const vehiculoNoEncontrado = checks.some(
    (c) => c.key === 'vehiculo' && c.status === 'fail',
  );
  // R3 (HU #10539) — señal server-driven de "VIN ya matriculado": el backend agrega el check
  // `vin_matricula` (warn, con secretaría + fecha del registro previo). Cuando está presente y el
  // wizard proveyó la navegación, se ofrece iniciar el traspaso del vehículo en vez de una matrícula.
  const vinConflicto = checks.find((c) => c.key === 'vin_matricula');
  // Se saca de la lista genérica de checks: su mensaje se muestra —de forma accionable— en la
  // tarjeta CTA de abajo, para no duplicarlo. El resto del semáforo se pinta normal.
  const visibleChecks = checks.filter((c) => c.key !== 'vin_matricula');
  // Hallazgos no bloqueantes (multas, SOAT/RTM, consultas omitidas por el OT): se resumen para que
  // el gestor los vea de un vistazo. Se toman de `visibleChecks` para no repetir `vin_matricula`,
  // que ya tiene su propia tarjeta accionable arriba.
  const warnChecks = visibleChecks.filter((c) => c.status === 'warn');
  // `queriedAt` solo lo completa la consulta con caché (ADR-0030); `createdAt` está siempre, así que
  // es el que sostiene el sello en una consulta fresca — que es donde antes no se decía nada.
  const sello = selloDeConsulta(checks, snapshot?.queriedAt ?? snapshot?.createdAt);

  return (
    <div className={bare ? '' : 'mt-4 rounded-2xl border bg-white p-4 dark:bg-[#162744]'}>
      {/* Embebido en un acordeón la cabecera sobra: el título y el semáforo global ya viven en la
          suya. El botón tampoco se pierde — los callers que embeben pasan `showRunButton={false}`
          porque el disparo de la consulta está arriba, junto al identificador del vehículo. */}
      {!bare && (
        <WizardCardHeader
          title="Diagnóstico de Requisitos Previos"
          subtitle="Validación automática en fuentes RUNT, SIMIT y RNMC antes de radicar."
          action={
            <div className="flex items-center gap-2">
              {ov && <StatusBadge label={ov.label} tone={ov.tone} />}
              {canRun && (
                <button
                  type="button"
                  onClick={() => onRun(hasResult)}
                  disabled={loading}
                  className={`${WIZARD_BTN} flex items-center justify-center gap-2 bg-[#557EFF] text-white focus-visible:ring-[#557EFF] disabled:cursor-not-allowed disabled:opacity-50`}
                  style={{ backgroundColor: WIZARD_BTN_SOLID, backgroundImage: 'none' }}
                  aria-label={hasResult ? 'Actualizar consulta' : 'Consultar RUNT y SIMIT'}
                >
                  {loading ? 'Consultando…' : hasResult ? 'Actualizar' : 'Consultar RUNT'}
                </button>
              )}
            </div>
          }
        />
      )}

      {/* Embebido en acordeón: el título lo pone el acordeón; aquí solo el subtítulo del prototipo. */}
      {bare && (
        <p className="mb-1.5 text-[13px] leading-snug opacity-70">
          Validación automática en fuentes RUNT, SIMIT y RNMC antes de radicar.
        </p>
      )}

      {/* Sello de la consulta: la respalda diciendo qué fuentes contestaron y cuándo. Va debajo del
          subtítulo en las dos variantes del panel (con cabecera propia o embebido en un acordeón),
          porque la afirmación «esto lo dijo el RUNT» pertenece al resultado, no al encabezado. */}
      {hasResult && sello && (
        <p className="mb-4 text-xs opacity-70" role="status" aria-live="polite">
          {sello}
        </p>
      )}

      {!hasResult && !loading && !esMigrado && (
        <p className="text-xs opacity-60">
          Ejecuta la consulta para ver el semáforo de requisitos del vehículo.
        </p>
      )}

      {/* Un trámite migrado llega sin consultas y el operador tiene que correrlas antes de radicar.
          El aviso lo PIDE, no lo justifica: destacarlo basta para que actúe, y el porqué —que los
          resultados de RUNT/SIMIT caducan en minutos y por eso no viajan desde el sistema anterior—
          es interioridad del sistema que a quien opera no le aporta nada y solo invita a preguntar
          si algo salió mal. Mismo motivo para no nombrar la migración: el trámite es uno solo. */}
      {!hasResult && !loading && esMigrado && (
        <div
          className="flex flex-wrap items-start gap-2 rounded-xl border p-2.5 text-xs"
          style={{ borderColor: 'rgba(85,126,255,0.30)', background: 'rgba(85,126,255,0.06)' }}
          role="status"
          aria-live="polite"
        >
          <StatusBadge label="Consulta pendiente" tone="info" />
          <span className="flex-1 opacity-80">
            Ejecuta la consulta para ver el estado actual del vehículo antes de continuar.
          </span>
        </div>
      )}

      {/* AC1 (HU #10885) — origen + fecha del dato precargado, solo cuando el snapshot viene de una
          reutilización de caché vigente (ADR-0030). Ausente en el semáforo multi-proveedor clásico
          (`runPreflight`/`getPreflight`, que no completa `fromCache`). */}
      {hasResult && snapshot?.fromCache && (
        <div
          className="mb-3 flex flex-wrap items-center gap-2 rounded-xl border p-2.5 text-xs"
          style={{ borderColor: 'rgba(85,126,255,0.30)', background: 'rgba(85,126,255,0.06)' }}
          role="status"
          aria-live="polite"
        >
          <StatusBadge label="Dato reutilizado" tone="info" />
          <span className="opacity-80">
            Origen: <span className="font-semibold">{sourceLabel(checks[0]?.source) || 'RUNT'}</span>
            {snapshot.queriedAt && (
              <>
                {' '}
                · Consultado el{' '}
                <span className="font-semibold">
                  {new Date(snapshot.queriedAt).toLocaleString('es-CO')}
                </span>
              </>
            )}
          </span>
        </div>
      )}

      {hasResult && (
        <ul
          // Cuatro columnas dejaban ~240px por tarjeta en un portátil, y ahí no caben un título de
          // dos palabras y su pastilla en la misma línea. La cuarta se reserva para `xl`, que es
          // donde hay ancho de verdad.
          className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4"
          aria-label="Diagnóstico de requisitos previos"
        >
          {visibleChecks.map((c) => {
            const pill = checkPillLabel(c);
            const pillTone = checkPillTone(c);
            const datos = c.datos ?? [];
            const msg = c.message?.trim() ?? '';
            // También en OK. Antes se ocultaba, y la tarjeta quedaba con la pastilla verde y el
            // cuerpo vacío: el gestor veía que «está bien» sin ver de dónde salía eso. Ahora los
            // mapeadores mandan el respaldo del proveedor —vencimiento del SOAT, póliza, aseguradora,
            // CDA de la revisión— y es justo el dato que puede contrastar si el organismo pregunta.
            const showMessage = !!msg;
            const aria = `${c.label}: ${pill}`;
            return (
              <li
                key={c.key}
                className="flex flex-col gap-2 rounded-xl border bg-white p-4 shadow-sm dark:bg-[#162744]"
                style={{ borderColor: '#E2E8F0' }}
              >
                {/* `flex-wrap`: la pastilla no encoge ni parte (`whitespace-nowrap` + `shrink-0`),
                    así que cuando no cabe junto al título tiene que poder bajar a su propia línea.
                    Sin esto empujaba al título fuera de la tarjeta. */}
                <div className="flex flex-wrap items-start justify-between gap-x-2 gap-y-1">
                  <span
                    // `break-words`: un título largo («Medidas correctivas (Policía) (comprador)»)
                    // debe partirse, no desbordar. `min-w-0` solo le permite encoger; sin una regla
                    // de quiebre encogía la caja y dejaba el texto por fuera, encima de la pastilla.
                    className="min-w-0 break-words text-[13px] font-semibold leading-tight"
                    style={{ color: '#162744' }}
                  >
                    {c.label}
                    {checkRoleSuffix(c.key)}
                  </span>
                  {c.status === 'ok' ? (
                    <DiagnosticOkPill label={pill} ariaLabel={aria} />
                  ) : (
                    <StatusBadge
                      label={pill}
                      tone={pillTone}
                      ariaLabel={aria}
                      className="shrink-0 uppercase tracking-wide"
                    />
                  )}
                </div>
                {/* Los datos del proveedor, una fila por dato: la etiqueta arriba en versalitas y el
                    valor debajo, como el resto del asistente presenta los datos del vehículo y de
                    los actores. Encadenados en una frase se leían mal —el salto de línea partía el
                    nombre de la aseguradora por la mitad— y el último campo quedaba sin etiqueta,
                    como si formara parte del anterior. */}
                {datos.length > 0 && (
                  <dl className="space-y-1.5">
                    {datos.map((d) => (
                      <div key={`${d.etiqueta}-${d.valor}`}>
                        <dt className="text-[10px] font-semibold uppercase tracking-wide opacity-55">
                          {d.etiqueta}
                        </dt>
                        <dd className="text-[11.5px] leading-snug break-words">{d.valor}</dd>
                      </div>
                    ))}
                  </dl>
                )}
                {/* El mensaje se conserva para lo que NO es un par etiqueta/valor: «Sin gravámenes ni
                    prendas registradas» es una afirmación, no un campo que etiquetar. */}
                {showMessage && (
                  <p className="text-[11.5px] leading-snug opacity-70">{c.message}</p>
                )}
                {c.action && (
                  <span
                    className="text-xs font-semibold"
                    style={{ color: '#557EFF' }}
                  >
                    {c.action.label} →
                  </span>
                )}
              </li>
            );
          })}
        </ul>
      )}

      {vinConflicto && (
        <div
          className="mt-3 rounded-xl p-3"
          style={{ background: 'rgba(85,126,255,0.06)', border: '1px solid rgba(85,126,255,0.30)' }}
          role="status"
          aria-live="polite"
        >
          <p className="text-xs font-bold" style={{ color: '#557EFF' }}>
            Este VIN ya está matriculado
          </p>
          <p className="mt-0.5 text-xs opacity-70">{vinConflicto.message}</p>
          {onIniciarTraspaso && !readOnly && (
            <button
              type="button"
              onClick={onIniciarTraspaso}
              className="mt-2 rounded-xl px-4 py-2 text-xs font-semibold text-white"
              style={{ background: WIZARD_CTA_GRADIENT }}
            >
              Iniciar traspaso de este vehículo →
            </button>
          )}
        </div>
      )}

      {hasProviderError && (
        <div
          className="mt-3 flex items-start gap-2.5 rounded-xl p-3"
          style={{ background: 'rgba(255,78,0,0.08)', border: '1px solid rgba(255,78,0,0.30)' }}
          role="alert"
        >
          <span className="text-xs font-medium" style={{ color: '#FF4E00' }}>
            No fue posible verificar la información en este momento. Vuelve a
            ejecutar la consulta antes de continuar; no es posible avanzar sin
            estos datos.
          </span>
        </div>
      )}

      {/* Amarillo = hay observaciones, no bloqueos: se informan y se sigue. Por eso `status` y no
          `alert`, y por eso NO se ofrece aceptar riesgo (no hay nada que levantar). */}
      {overall === 'yellow' && warnChecks.length > 0 && (
        <div
          className="mt-3 rounded-xl p-3"
          style={{ background: 'rgba(249,172,0,0.08)', border: '1px solid rgba(249,172,0,0.30)' }}
          role="status"
          aria-live="polite"
        >
          <p className="text-xs font-bold" style={{ color: 'var(--badge-warning-fg)' }}>
            Advertencias de la verificación
          </p>
          <ul className="mt-1.5 space-y-2">
            {warnChecks.map((c) => (
              <li key={c.key} className="text-xs">
                <span className="font-semibold">
                  {c.label}
                  {checkRoleSuffix(c.key)}
                </span>
                {c.message && <span className="opacity-70"> — {c.message}</span>}
                {c.details && c.details.length > 0 && (
                  <FineDetailList details={c.details} />
                )}
              </li>
            ))}
          </ul>
          <p className="mt-2 text-xs font-semibold" style={{ color: '#162744' }}>
            Puedes continuar con la gestión del trámite. El organismo de tránsito correspondiente
            revisará estas observaciones durante el proceso.
          </p>
        </div>
      )}

      {vehiculoNoEncontrado && !hasProviderError && (
        <div
          className="mt-3 flex items-start gap-2.5 rounded-xl p-3"
          style={{ background: 'rgba(255,78,0,0.08)', border: '1px solid rgba(255,78,0,0.30)' }}
          role="alert"
        >
          <span className="text-xs font-medium" style={{ color: '#FF4E00' }}>
            El vehículo no se encontró en el RUNT. Verifica el identificador y el
            documento del propietario, y vuelve a consultar; no es posible
            avanzar sin un vehículo verificado.
          </span>
        </div>
      )}

      {overall === 'red' && !hasProviderError && !vehiculoNoEncontrado && (
        <label
          className="mt-3 flex items-start gap-2.5 rounded-xl p-3"
          style={{ background: 'rgba(255,78,0,0.08)', border: '1px solid rgba(255,78,0,0.30)' }}
        >
          <input
            type="checkbox"
            checked={riesgoAceptado}
            onChange={(e) => onToggleRiesgo(e.target.checked)}
            disabled={readOnly || saving}
            className="mt-0.5 h-4 w-4 shrink-0 accent-[#FF4E00] disabled:opacity-60"
          />
          <span className="text-xs font-medium" style={{ color: '#FF4E00' }}>
            Asumo el riesgo de rechazo en el organismo de tránsito y deseo
            continuar con el trámite.
          </span>
        </label>
      )}
    </div>
  );
}
