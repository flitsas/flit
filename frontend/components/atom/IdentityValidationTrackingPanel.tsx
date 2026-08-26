'use client';

import { useCallback, useEffect, useState } from 'react';
import { ChevronRight, RefreshCw } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { IdentityAuditEvent } from '@/lib/api/types/procedure-runtime';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { FLIT } from '@/lib/flit-design-tokens';

/**
 * Etiquetas legibles de `stage` (código técnico en BD sin cambiar).
 * Desconocidas se muestran tal cual. "webhook" → "Notificación" en UI.
 */
const STAGE_LABEL: Record<string, string> = {
  send: 'Envío al proveedor',
  send_response: 'Respuesta del proveedor',
  send_error: 'Error de envío',
  webhook_received: 'Notificación recibida',
  webhook_not_verifiable: 'Notificación no verificable',
  webhook_signature_invalid: 'Firma de notificación inválida',
  webhook_applied: 'Resultado de la notificación aplicado',
  reconcile: 'Consulta de estado (reconciliación)',
  expired: 'Enlace vencido',
  contact_edited: 'Datos de contacto editados',
  resend: 'Reenvío',
};

/**
 * Etiquetas legibles de `outcome`. Desconocidas (p. ej. estado biométrico crudo) se muestran tal cual.
 */
const OUTCOME_LABEL: Record<string, string> = {
  ok: 'OK',
  error: 'Error',
  received: 'Recibido',
  not_found: 'No encontrado',
  aprobado: 'Aprobado',
  rechazado: 'Rechazado',
  pendiente: 'Pendiente',
  secret_missing: 'Secreto ausente',
  decrypt_failed: 'Fallo al descifrar',
  firma_invalida: 'Firma inválida',
  proveedor_no_disponible: 'Proveedor no disponible',
  expirado: 'Expirado',
  cuerpo_invalido: 'Cuerpo inválido',
};

const STAGE_LABEL_DETAIL: Record<string, string> = {
  send_response: 'Respuesta proveedor',
  reconcile: 'Consulta de estado',
  expired: 'Cierre por vencimiento',
};

function auditStageLabel(stage: string, detail = false): string {
  if (detail && STAGE_LABEL_DETAIL[stage]) return STAGE_LABEL_DETAIL[stage];
  return STAGE_LABEL[stage] ?? stage;
}

function auditOutcomeText(outcome: string): string {
  return OUTCOME_LABEL[outcome] ?? outcome;
}

/**
 * Texto de la columna «Detalle».
 *
 * Antes era `errorType ?? providerStatus ?? message`, y los tres son nulos cuando el paso salió
 * bien: la columna quedaba vacía justo en las filas que el gestor más mira, las que fueron bien.
 * El error sigue teniendo prioridad —es lo que hay que leer primero—, y cuando no hay ninguno se
 * dice qué pasó en vez de dejar el hueco.
 *
 * La bitácora del backend es diagnóstico de soporte y no trae una descripción redactada por
 * evento; esto compone la mejor frase posible con lo que sí manda, sin inventar nada.
 */
/**
 * ¿El evento fue bien? Gobierna el verde de la columna «Resultado», como en la referencia.
 * Se decide por el `outcome` conocido y no por ausencia de error: un `outcome` nuevo del proveedor
 * no debe pintarse en verde solo porque todavía no tenga etiqueta.
 */
function eventoCorrecto(e: IdentityAuditEvent): boolean {
  return ['ok', 'received', 'aprobado'].includes(e.outcome);
}

/**
 * Columna «Cifrado».
 *
 * La referencia muestra ahí el algoritmo (`AES-256`, `SHA-256`). El contrato del backend NO lo
 * manda: `IdentityAuditEvent` solo trae `decryptOk`, es decir si el cuerpo cifrado del proveedor se
 * pudo descifrar. Se conserva el nombre de columna de la referencia porque habla de lo mismo —la
 * capa de cifrado del envío— pero el valor dice lo que de verdad se sabe: que se verificó, que
 * falló, o que ese evento no cifra nada. Inventar «AES-256» sería afirmar un algoritmo que este
 * frontend no conoce, en la pantalla que certifica una identidad.
 */
function auditCifradoText(e: IdentityAuditEvent, detail = false): string {
  if (e.decryptOk == null) return detail ? '—' : 'No aplica';
  if (e.decryptOk) return detail ? 'OK' : 'Verificado';
  return 'Falló';
}

function auditOutcomeTone(e: IdentityAuditEvent): StatusTone {
  if (eventoCorrecto(e)) return 'success';
  if (e.outcome === 'pendiente') return 'info';
  if (e.outcome === 'expirado') return 'warning';
  if (['error', 'rechazado', 'decrypt_failed', 'firma_invalida', 'cuerpo_invalido'].includes(e.outcome)) {
    return 'danger';
  }
  return 'neutral';
}

function auditDetailText(e: IdentityAuditEvent): string {
  if (e.errorType) return e.errorType;
  if (e.message) return e.message;
  if (e.providerStatus) return `Estado del proveedor: ${e.providerStatus}`;
  if (e.httpStatus != null) return `Respuesta HTTP ${e.httpStatus}`;
  return 'Sin novedad';
}

/**
 * Panel de tracking de identidad compartido (CF-07, Feature #11004, HU #11007). Extraído del
 * `IdentityAuditPanel` original de `BiometricStep` y generalizado para consumirse desde Validaciones,
 * Prevalidaciones y el propio trámite con un único punto de entrada: `validationId`.
 *
 * D2 — visible para cualquier usuario autenticado del tenant con acceso al módulo (NO restringido a
 * SuperAdmin): el backend ya sanea la bitácora (sin secretos ni PII cruda), así que este componente no
 * evalúa ningún gate de rol.
 *
 * Disclosure colapsable con carga perezosa (o auto-abierto) que consulta
 * `GET /biometric-validations/{validationId}/audit` — el endpoint SIN instancia (CF-07).
 * `refreshKey` permite al drawer de prevalidación re-sincronizar la bitácora en cada poll.
 */
export function IdentityValidationTrackingPanel({
  validationId,
  refreshKey = 0,
  defaultOpen = false,
  embebido = false,
  detailLayout = false,
}: {
  validationId: string;
  /** Cuando cambia (p. ej. cada poll del detalle), recarga la bitácora si el panel está abierto. */
  refreshKey?: number;
  /** Abrir el disclosure al montar (detalle en vivo de prevalidación). */
  defaultOpen?: boolean;
  /**
   * Oculta el botón «Ver tracking» propio del panel. Se usa cuando quien lo embebe YA ofrece el
   * desplegable —el asistente lo mete dentro del acordeón «Ver trazabilidad de validación»—, donde
   * el botón propio obligaba a un segundo clic para ver la misma tabla: dos desplegables anidados
   * para un solo contenido. Los demás consumidores (Validaciones, Prevalidaciones) lo conservan.
   */
  embebido?: boolean;
  /** Tabla del modal de Identidad: badges de resultado, «Detalle técnico», cifrado OK. */
  detailLayout?: boolean;
}) {
  const [open, setOpen] = useState(defaultOpen || embebido);
  const [events, setEvents] = useState<IdentityAuditEvent[] | null>(null);
  const [referenced, setReferenced] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadAudit = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await tramitesClient.getBiometricAuditByValidation(validationId);
      setEvents(res.events);
      setReferenced(res.referencedFromOtherProcedure ?? false);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo cargar la bitácora.');
    } finally {
      setLoading(false);
    }
  }, [validationId]);

  // Carga inicial si defaultOpen; re-sync cuando el padre hace poll (refreshKey).
  useEffect(() => {
    if (!open) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: setState ocurre tras el await
    void loadAudit();
  }, [open, refreshKey, loadAudit]);

  const toggle = () => {
    setOpen((prev) => !prev);
  };

  return (
    <div className={embebido ? "" : "mt-3 border-t pt-3"}>
      {!embebido && (
      <button
        type="button"
        onClick={toggle}
        className="flex items-center gap-1.5 text-xs font-semibold opacity-70 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        style={{ outlineColor: FLIT.brand.blue }}
        aria-expanded={open}
        aria-label="Ver tracking de la validación"
      >
        <ChevronRight
          className={`h-3 w-3 transition-transform ${open ? 'rotate-90' : ''}`}
          aria-hidden
        />
        Ver tracking
      </button>
      )}

      {open && (
        <div className="mt-2 space-y-2">
          {loading && (
            <p className="text-xs opacity-60" role="status" aria-live="polite">
              Cargando bitácora…
            </p>
          )}
          {error && (
            <p className="text-xs" style={{ color: FLIT.state.danger }} role="alert" aria-live="polite">
              {error}
            </p>
          )}
          {/*
           * HU #10350 — identidad reutilizada ("apalancada"): la validación se realizó en otro trámite
           * del mismo cliente. Aviso informativo (no error); debajo se muestra su bitácora real.
           */}
          {referenced && (
            <p
              className="rounded-lg px-2.5 py-1.5 text-xs"
              style={{ background: FLIT.blueAlpha(0.1), color: FLIT.brand.blue }}
              role="status"
            >
              Validación reutilizada de otro trámite del mismo cliente (identidad ya verificada). El
              historial técnico corresponde a ese trámite.
            </p>
          )}
          {!loading && !error && events && events.length === 0 && (
            <p className="text-xs opacity-60">Sin eventos registrados todavía.</p>
          )}
          {events && events.length > 0 && (
            // La referencia del diseño reserva un ancho mínimo y deja que la tabla ruede en
            // horizontal: son cinco columnas y la última es texto largo. Sin el mínimo, «Detalle»
            // se estrangula hasta una palabra por línea en cuanto la tarjeta va a media pantalla.
            <div className="overflow-x-auto">
              <table className="w-full min-w-[520px] text-left text-xs">
                {/* Cabecera rellena en versalitas, como en la referencia: la fila de títulos se
                    separa de los datos por fondo, no por opacidad — que además incumplía el piso. */}
                <thead>
                  <tr style={{ background: '#EEF5FF', color: '#59677D' }}>
                    <th scope="col" className="rounded-l-lg px-3 py-2 font-semibold uppercase tracking-wide">
                      Fecha
                    </th>
                    <th scope="col" className="px-3 py-2 font-semibold uppercase tracking-wide">Etapa</th>
                    <th scope="col" className="px-3 py-2 font-semibold uppercase tracking-wide">Resultado</th>
                    <th scope="col" className="px-3 py-2 font-semibold uppercase tracking-wide">Cifrado</th>
                    <th scope="col" className="rounded-r-lg px-3 py-2 font-semibold uppercase tracking-wide">
                      {detailLayout ? 'Detalle técnico' : 'Detalle'}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {events.map((e, i) => (
                    <tr key={i} className="border-t align-top" style={{ borderColor: FLIT.border.soft }}>
                      <td className="whitespace-nowrap px-3 py-2.5 opacity-70">{formatFecha(e.occurredAt)}</td>
                      <td className="px-3 py-2.5 font-medium">{auditStageLabel(e.stage, detailLayout)}</td>
                      <td className="px-3 py-2.5 font-semibold">
                        {detailLayout ? (
                          <StatusBadge
                            label={auditOutcomeLabel(e)}
                            tone={auditOutcomeTone(e)}
                            ariaLabel={`Resultado: ${auditOutcomeLabel(e)}`}
                          />
                        ) : (
                          <span style={eventoCorrecto(e) ? { color: 'var(--flit-success-ink)' } : undefined}>
                            {auditOutcomeLabel(e)}
                          </span>
                        )}
                      </td>
                      <td className="whitespace-nowrap px-3 py-2.5">{auditCifradoText(e, detailLayout)}</td>
                      <td className="px-3 py-2.5 opacity-70">{auditDetailText(e)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {events && events.length > 0 && !embebido && (
            <button
              type="button"
              onClick={() => void loadAudit()}
              disabled={loading}
              className="flex items-center gap-1 text-xs font-semibold disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
              style={{ color: FLIT.brand.blue, outlineColor: FLIT.brand.blue }}
              aria-label="Refrescar bitácora"
            >
              <RefreshCw className={`h-2.5 w-2.5 ${loading ? 'animate-spin' : ''}`} aria-hidden />
              Refrescar
            </button>
          )}
        </div>
      )}
    </div>
  );
}

/** Formatea una fecha ISO a texto legible (es-CO). Devuelve el ISO crudo si no parsea. */
function formatFecha(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium', timeStyle: 'short' }).format(d);
}

/** Texto del resultado de un evento de bitácora, anexando el código HTTP si lo hay. */
function auditOutcomeLabel(e: IdentityAuditEvent): string {
  const label = auditOutcomeText(e.outcome);
  return e.httpStatus != null ? `${label} (HTTP ${e.httpStatus})` : label;
}
