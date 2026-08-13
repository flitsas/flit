'use client';

// Panel de pre-vuelo (semáforo de requisitos). Presentacional: el fetch/estado
// viven en el wizard/hook. La consulta real (RUNT/SIMIT) se cablea en #10201;
// por ahora el hook alimenta un snapshot stub.

import { useWizardReadOnly } from './WizardReadOnlyContext';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import type {
  FineDetail,
  PreflightCheckStatus,
  PreflightSnapshot,
} from '@/lib/api/types/procedure-runtime';

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

/** Palabra visible en la pastilla (UNKNOWN → NO ENCONTRADO). */
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
      return 'NO ENCONTRADO';
  }
}

/** Detalle del unknown: alude a que no existe / no se encontró en la fuente. */
function unknownPillDetail(message: string, sourceLabelText: string): string {
  if (/sin informaci[oó]n/i.test(message)) return 'No existe en la fuente';
  if (/sin documento/i.test(message)) return 'No existe';
  if (message && message.length <= 42) return message;
  if (sourceLabelText) return `Sin dato en ${sourceLabelText}`;
  return 'No encontrado';
}

/**
 * Texto de la pastilla: `OK (RUNT)` / `NO ENCONTRADO (No existe en la fuente)`.
 * Conserva status + fuente o mensaje (misma información, formato compacto).
 */
export function checkPillLabel(check: {
  status: PreflightCheckStatus;
  source?: string | null;
  message?: string | null;
}): string {
  const word = statusPillWord(check.status);
  const src = sourceLabel(check.source);
  const msg = check.message?.trim() ?? '';
  if (check.status === 'unknown') {
    return `${word} (${unknownPillDetail(msg, src)})`;
  }
  if (check.status === 'ok' && src) return `${word} (${src})`;
  // Mensajes cortos van en la pastilla; los largos quedan debajo.
  if (msg && msg.length <= 42 && check.status !== 'ok') return `${word} (${msg})`;
  if (src) return `${word} (${src})`;
  if (msg) {
    const short = msg.length > 42 ? `${msg.slice(0, 39)}…` : msg;
    return `${word} (${short})`;
  }
  return word;
}

function checkPillTone(check: { status: PreflightCheckStatus; message?: string | null }): StatusTone {
  return STATUS_PILL_TONE[check.status];
}


/**
 * Semáforo global del pre-vuelo. Exportado para que quien embeba el panel dentro de un acordeón
 * (`bare`) pueda pintar el mismo estado en la cabecera plegada, que es donde el gestor lo busca
 * cuando el panel está cerrado.
 */
export function preflightOverall(overall: string | null | undefined) {
  return overall ? (OVERALL[overall] ?? null) : null;
}

const OVERALL: Record<string, { label: string; bg: string; color: string }> = {
  green: { label: 'Pre-vuelo en verde', bg: 'rgba(140,198,63,0.15)', color: '#8CC63F' },
  yellow: { label: 'Pre-vuelo con advertencias', bg: 'rgba(249,172,0,0.15)', color: '#F9AC00' },
  red: { label: 'Pre-vuelo con bloqueos', bg: 'rgba(255,78,0,0.15)', color: '#FF4E00' },
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
                <span className="text-xs font-bold" style={{ color: '#B47800' }}>
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

  return (
    <div className={bare ? '' : 'mt-4 rounded-2xl border bg-white p-4 dark:bg-[#162744]'}>
      {/* Embebido en un acordeón la cabecera sobra: el título y el semáforo global ya viven en la
          suya. El botón tampoco se pierde — los callers que embeben pasan `showRunButton={false}`
          porque el disparo de la consulta está arriba, junto al identificador del vehículo. */}
      {!bare && (
        <div className="mb-3 flex items-center justify-between gap-3">
          <div>
            <h4 className="text-sm font-bold">Pre-vuelo de requisitos</h4>
            <p className="text-xs opacity-60">
              RUNT · SIMIT · RNMC — consulta antes de radicar el trámite
            </p>
          </div>
          <div className="flex items-center gap-2">
            {ov && (
              <span
                className="shrink-0 rounded-full px-3 py-1 text-xs font-bold"
                style={{ background: ov.bg, color: ov.color }}
                role="status"
                aria-live="polite"
              >
                {ov.label}
              </span>
            )}
            {canRun && (
              <button
                type="button"
                onClick={() => onRun(hasResult)}
                disabled={loading}
                className="rounded-xl px-5 py-2.5 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
                style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
                aria-label={hasResult ? 'Actualizar consulta' : 'Consultar RUNT y SIMIT'}
              >
                {loading ? 'Consultando…' : hasResult ? 'Actualizar' : 'Consultar RUNT'}
              </button>
            )}
          </div>
        </div>
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
          <span
            className="rounded px-1.5 py-0.5 text-xs font-semibold uppercase"
            style={{ background: 'rgba(85,126,255,0.15)', color: '#557EFF' }}
          >
            Consulta pendiente
          </span>
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
          <span
            className="rounded px-1.5 py-0.5 text-xs font-semibold uppercase"
            style={{ background: 'rgba(85,126,255,0.15)', color: '#557EFF' }}
          >
            Dato reutilizado
          </span>
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
          className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3"
          aria-label="Resultados del pre-vuelo"
        >
          {visibleChecks.map((c) => {
            const pill = checkPillLabel(c);
            const pillTone = checkPillTone(c);
            const msg = c.message?.trim() ?? '';
            // Si el mensaje ya está en la pastilla, no lo repetimos debajo.
            const msgInPill =
              !!msg &&
              (pill.includes(`(${msg}`) || (msg.length > 42 && pill.includes('…')));
            const showMessage = !!msg && !msgInPill;
            return (
              <li
                key={c.key}
                className="flex flex-col gap-1.5 rounded-2xl border bg-white px-3 py-2.5 dark:bg-[#162744]"
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="min-w-0 text-xs font-semibold leading-snug">
                    {c.label}
                    {checkRoleSuffix(c.key)}
                  </span>
                  <StatusBadge
                    label={pill}
                    tone={pillTone}
                    ariaLabel={`${c.label}: ${pill}`}
                    className="shrink-0 uppercase tracking-wide"
                  />
                </div>
                {showMessage && (
                  <p className="text-xs opacity-70">{c.message}</p>
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
              style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
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
          <p className="text-xs font-bold" style={{ color: '#F9AC00' }}>
            Advertencias del pre-vuelo
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
          <p className="mt-2 text-xs opacity-70">
            Puedes continuar con el trámite; el organismo de tránsito verá estas
            observaciones.
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
