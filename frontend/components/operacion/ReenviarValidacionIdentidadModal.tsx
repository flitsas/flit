'use client';

import { type ReactNode, useEffect, useState } from 'react';
import { Copy, Send } from 'lucide-react';
import { Modal } from '@/components/atom/Modal';
import { InlineAlert } from '@/components/atom/InlineAlert';
import {
  SeccionCargando,
  SeccionError,
  SeccionVacia,
} from '@/components/operacion/detalle/primitivos';
import { WIZARD_CTA_GRADIENT } from './wizard-field-styles';
import { tramitesClient } from '@/lib/api/tramites-client';
import type {
  AdminReenviarLoteItemResult,
  AdminValidacionReenviable,
} from '@/lib/api/tramites-client';

/**
 * HU #12161 (Feature #12155) — Reenviar validación de identidad de UN trámite fuera del gate del
 * wizard: reenvía el enlace (mismo registro, token nuevo) y admite actualizar el correo antes de
 * enviarlo. Extraído a su propio módulo en HU #12164 para reutilizarse en dos puntos de montaje:
 *
 *   - `AdminTramiteAcciones.tsx` (HU #12163) — menú de acciones avanzadas de una fila del Dashboard
 *     de Trámites, que ya tiene el `InstanceSummary` completo de la fila.
 *   - `Validaciones.tsx` (HU #12164) — módulo de Validaciones de Identidad, que NO navega por
 *     trámite: identifica la fila por `TenantBiometricValidation` y solo conoce `instanceId` +
 *     `referenceNumber`, no el `InstanceSummary` entero.
 *
 * HU #13822 (Feature #13819) — el modal lista PERSONAS sin firmar (no filas del expediente): se
 * marcan una o varias, cada una con su «Nuevo correo (opcional)», y se reenvía en un solo clic.
 * Tras reenviar NO se cierra: muestra el resultado por persona y el enlace nuevo con «Copiar enlace».
 */

const FIELD_LABEL_CLS = 'block text-xs font-semibold text-flit-primary dark:text-white';
// `ring-inset`: un ring hacia afuera se recorta contra el `overflow-y-auto` del Modal.
const FIELD_CLS =
  'w-full rounded-xl border px-3 py-2 text-sm text-flit-primary focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-flit-brand dark:border-white/15 dark:bg-transparent dark:text-white';
const FIELD_BORDER = { borderColor: '#DFE5ED' };
const LINK_BTN_CLS =
  'inline-flex items-center gap-1 rounded-lg text-xs font-semibold text-flit-brand underline focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2';

export const PARTE_LABEL: Record<string, string> = {
  vendedor: 'Vendedor',
  comprador: 'Comprador',
  locatario: 'Locatario',
};

export const MSG_TODOS_FIRMARON = 'Comprador y vendedor ya firmaron. No hay a quién reenviar.';
const MSG_EN_COLA =
  'El reenvío quedó en cola: el proveedor tuvo una falla transitoria y se reintentará automáticamente.';
const MSG_REENVIADA = 'Validación de identidad reenviada.';
const MSG_MANUAL = 'En validación manual: el enlace se regenera desde la revisión manual.';
const MSG_INICIAL_AUSENTE =
  'La validación que elegiste ya no está pendiente de firma. Marca a quién reenviar de la lista.';
const GREEN = '#8CC63F';
const RED = '#FF4E00';

/**
 * Mismo patrón de validación de formato de correo que `PrevalidacionForm.tsx` (`validEmail`): un
 * único regex de formato para todo el módulo de identidad. El campo es OPCIONAL (vacío = reenvía al
 * correo ya registrado); solo se exige formato válido cuando el admin escribe algo.
 */
function esCorreoConFormatoValido(v: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v.trim());
}

function etiquetaPersona(p: { partyRole: string | null; name: string }): string {
  const rol = p.partyRole ? (PARTE_LABEL[p.partyRole] ?? p.partyRole) : null;
  return rol ? `${rol} · ${p.name}` : p.name;
}

/** AC4 — hay enlace que copiar solo si existe y no venció. */
function enlaceVigente(
  p: Pick<AdminValidacionReenviable, 'captureUrl' | 'captureExpiresAt'>,
): boolean {
  if (!p.captureUrl) return false;
  if (!p.captureExpiresAt) return true;
  const t = new Date(p.captureExpiresAt).getTime();
  return Number.isNaN(t) || t > Date.now();
}

function PrimaryButton({
  children,
  disabled,
  onClick,
  className = 'w-full',
}: {
  children: ReactNode;
  disabled?: boolean;
  onClick?: () => void;
  className?: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className={`rounded-xl py-2.5 text-sm font-semibold text-white transition disabled:cursor-not-allowed disabled:opacity-60 ${className}`}
      style={{ background: WIZARD_CTA_GRADIENT }}
    >
      {children}
    </button>
  );
}

function SecondaryButton({
  children,
  disabled,
  onClick,
  className = 'w-full',
}: {
  children: ReactNode;
  disabled?: boolean;
  onClick?: () => void;
  className?: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className={`rounded-xl border py-2.5 text-sm font-medium text-flit-primary transition hover:bg-[#162744]/[0.04] disabled:cursor-not-allowed disabled:opacity-60 dark:border-white/20 dark:text-white ${className}`}
      style={FIELD_BORDER}
    >
      {children}
    </button>
  );
}

function CopiarEnlace({
  url,
  copiado,
  onCopiar,
}: {
  url: string;
  copiado: boolean | null;
  onCopiar: (url: string) => void;
}) {
  return (
    <div className="flex items-center gap-3">
      <button type="button" onClick={() => onCopiar(url)} className={LINK_BTN_CLS}>
        <Copy className="h-3.5 w-3.5" aria-hidden="true" />
        Copiar enlace
      </button>
      <span role="status" aria-live="polite" className="text-xs font-medium">
        {copiado === true ? <span style={{ color: GREEN }}>Enlace copiado.</span> : null}
        {copiado === false ? <span style={{ color: RED }}>No se pudo copiar el enlace.</span> : null}
      </span>
    </div>
  );
}

export interface ReenviarValidacionIdentidadModalProps {
  open: boolean;
  onClose: () => void;
  /** Id del trámite (`ProcedureInstance`) dueño de la(s) validación(es) a reenviar. */
  instanceId: string;
  /** Solo para el encabezado del modal (número de radicado / referencia). */
  referenceNumber: string;
  /** SuperAdmin viendo el trámite de OTRA compañía. Si se omite, se resuelve al tenant activo. */
  tenantId?: string;
  /**
   * Deja marcada esta validación (el admin ya la identificó desde su propia grilla). Si no está
   * entre las personas sin firmar, el modal lo indica con un aviso en vez de fallar.
   */
  initialValidationId?: string;
  onSuccess: (message: string) => void;
  onError: (message: string) => void;
}

/** HU #13822 — Reenviar validación por persona: 4 estados (carga/error/vacío/lleno) + resultado. */
export function ReenviarValidacionIdentidadModal({
  open,
  onClose,
  instanceId,
  referenceNumber,
  tenantId,
  initialValidationId,
  onSuccess,
  onError,
}: ReenviarValidacionIdentidadModalProps) {
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [personas, setPersonas] = useState<AdminValidacionReenviable[]>([]);
  const [marcadas, setMarcadas] = useState<Record<string, boolean>>({});
  const [correos, setCorreos] = useState<Record<string, string>>({});
  const [inicialAusente, setInicialAusente] = useState(false);
  const [busy, setBusy] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [resultados, setResultados] = useState<AdminReenviarLoteItemResult[] | null>(null);
  const [copia, setCopia] = useState<{ id: string; ok: boolean } | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  // AC4 — el aviso de éxito se entrega al CERRAR, no al reenviar: el padre (AdminTramiteAcciones)
  // responde a onSuccess recargando el listado, y esa recarga desmonta la fila con este modal
  // adentro, que se cerraba antes de que el usuario viera el resultado y copiara el enlace.
  const [avisoPendiente, setAvisoPendiente] = useState<string | null>(null);

  const cerrar = () => {
    if (avisoPendiente) {
      onSuccess(avisoPendiente);
      setAvisoPendiente(null);
    }
    onClose();
  };

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    // Carga las personas sin firmar cada vez que el modal se abre (o se vuelve a la lista).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setLoading(true);
    setLoadError(null);
    setSubmitError(null);
    setResultados(null);
    setCopia(null);
    setCorreos({});
    tramitesClient
      .adminListValidacionesReenviables(instanceId, tenantId)
      .then((res) => {
        if (cancelled) return;
        const lista = res.personas ?? [];
        setPersonas(lista);
        const elegibles = lista.filter((p) => !p.esFlujoManual);
        const inicial = initialValidationId
          ? elegibles.find((p) => p.validationId === initialValidationId)
          : undefined;
        setInicialAusente(Boolean(initialValidationId) && !inicial && lista.length > 0);
        if (inicial) setMarcadas({ [inicial.validationId]: true });
        else if (elegibles.length === 1) setMarcadas({ [elegibles[0]!.validationId]: true });
        else setMarcadas({});
      })
      .catch((e: unknown) => {
        if (cancelled) return;
        setLoadError(
          e instanceof Error ? e.message : 'No se pudieron cargar las personas pendientes de firma.',
        );
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, instanceId, tenantId, reloadKey]);

  const seleccionadas = personas.filter((p) => marcadas[p.validationId] && !p.esFlujoManual);
  // AC3 — el correo es opcional POR PERSONA marcada, pero si se escribe debe tener formato válido.
  const correoInvalido = (id: string) => {
    const v = (correos[id] ?? '').trim();
    return v !== '' && !esCorreoConFormatoValido(v);
  };
  const hayInvalido = seleccionadas.some((p) => correoInvalido(p.validationId));

  const copiar = async (id: string, url: string) => {
    try {
      await navigator.clipboard.writeText(url);
      setCopia({ id, ok: true });
    } catch {
      setCopia({ id, ok: false });
    }
  };

  const confirmar = async () => {
    if (seleccionadas.length === 0 || hayInvalido) return;
    setBusy(true);
    setSubmitError(null);
    try {
      const res = await tramitesClient.adminReenviarValidacionesLote(
        instanceId,
        seleccionadas.map((p) => ({
          validationId: p.validationId,
          email: (correos[p.validationId] ?? '').trim() || null,
        })),
        tenantId,
      );
      const items = res.items ?? [];
      setResultados(items);
      setCopia(null);
      const oks = items.filter((i) => i.ok);
      if (oks.length > 0) {
        setAvisoPendiente(oks.every((i) => i.queued) ? MSG_EN_COLA : MSG_REENVIADA);
      } else {
        onError('No se pudo reenviar la validación de identidad.');
      }
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'No se pudo reenviar la validación de identidad.';
      setSubmitError(msg);
      onError(msg);
    } finally {
      setBusy(false);
    }
  };

  const porId = new Map(personas.map((p) => [p.validationId, p]));
  const exitosos = (resultados ?? []).filter((r) => r.ok);
  const fallidos = (resultados ?? []).filter((r) => !r.ok);
  const nombreDe = (id: string) => {
    const p = porId.get(id);
    return p ? etiquetaPersona(p) : 'Persona';
  };
  const copiadoDe = (id: string): boolean | null => (copia?.id === id ? copia.ok : null);

  const mostrarVacio = !loading && !loadError && personas.length === 0;
  const mostrarLista = !loading && !loadError && personas.length > 0 && resultados === null;
  const mostrarResultado = !loading && !loadError && resultados !== null && personas.length > 0;

  return (
    <Modal
      open={open}
      onClose={cerrar}
      title="Reenviar validación de identidad"
      titleClassName="text-base font-bold text-flit-brand"
      icon={Send}
      description={`Trámite ${referenceNumber}`}
      size="sm"
      busy={busy}
    >
      {/* `contents`: envoltorio solo para detener la propagación del clic hacia una fila clickable
          que lo monte (el portal de `Modal` burbujea por el árbol de React, no por el DOM). */}
      <div className="contents" onClick={(e) => e.stopPropagation()}>
        {loading ? (
          <SeccionCargando etiqueta="Cargando personas pendientes de firma" filas={2} />
        ) : null}
        {!loading && loadError ? (
          <SeccionError
            mensaje={loadError}
            contexto="las personas pendientes de firma"
            onReintentar={() => setReloadKey((k) => k + 1)}
          />
        ) : null}
        {mostrarVacio ? <SeccionVacia mensaje={MSG_TODOS_FIRMARON} /> : null}

        {mostrarLista ? (
          <div className="space-y-3">
            {inicialAusente ? (
              <p role="status" className="text-xs font-medium text-flit-primary/80 dark:text-white/80">
                {MSG_INICIAL_AUSENTE}
              </p>
            ) : null}
            <ul className="space-y-3" aria-label="Personas pendientes de firma">
              {personas.map((p) => {
                const id = p.validationId;
                const etiqueta = etiquetaPersona(p);
                const marcada = Boolean(marcadas[id]) && !p.esFlujoManual;
                const invalido = marcada && correoInvalido(id);
                return (
                  <li
                    key={id}
                    role="group"
                    aria-label={etiqueta}
                    className="space-y-2 rounded-xl border p-3"
                    style={FIELD_BORDER}
                  >
                    <div className="flex items-start gap-2">
                      <input
                        id={`admin-reenviar-persona-${id}`}
                        type="checkbox"
                        checked={marcada}
                        disabled={busy || p.esFlujoManual}
                        onChange={(e) => setMarcadas((m) => ({ ...m, [id]: e.target.checked }))}
                        className="mt-0.5 h-4 w-4 accent-[var(--color-flit-brand)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2"
                      />
                      <label
                        htmlFor={`admin-reenviar-persona-${id}`}
                        className="text-sm font-semibold text-flit-primary dark:text-white"
                      >
                        {etiqueta}
                      </label>
                    </div>
                    {p.esFlujoManual ? (
                      <p className="text-xs text-flit-primary/70 dark:text-white/70">{MSG_MANUAL}</p>
                    ) : (
                      <p className="text-xs text-flit-primary/60 dark:text-white/60">
                        Correo registrado: <span className="font-medium">{p.email ?? '—'}</span>
                      </p>
                    )}
                    {marcada ? (
                      <div className="space-y-1">
                        <label className={FIELD_LABEL_CLS} htmlFor={`admin-reenviar-email-${id}`}>
                          Nuevo correo (opcional)
                        </label>
                        <input
                          id={`admin-reenviar-email-${id}`}
                          type="email"
                          value={correos[id] ?? ''}
                          onChange={(e) => setCorreos((c) => ({ ...c, [id]: e.target.value }))}
                          disabled={busy}
                          placeholder="Deja vacío para reenviar al correo actual"
                          className={FIELD_CLS}
                          style={FIELD_BORDER}
                          aria-invalid={invalido}
                          aria-describedby={invalido ? `admin-reenviar-email-err-${id}` : undefined}
                        />
                        {invalido ? (
                          <p
                            id={`admin-reenviar-email-err-${id}`}
                            role="alert"
                            className="text-[11px] font-medium"
                            style={{ color: RED }}
                          >
                            Correo inválido.
                          </p>
                        ) : null}
                      </div>
                    ) : null}
                    {!p.esFlujoManual && enlaceVigente(p) ? (
                      <CopiarEnlace
                        url={p.captureUrl!}
                        copiado={copiadoDe(id)}
                        onCopiar={(u) => void copiar(id, u)}
                      />
                    ) : null}
                  </li>
                );
              })}
            </ul>

            {submitError ? <InlineAlert tone="error">{submitError}</InlineAlert> : null}

            <div className="flex gap-3 pt-1">
              <SecondaryButton className="flex-1" onClick={cerrar} disabled={busy}>
                Cancelar
              </SecondaryButton>
              <PrimaryButton
                className="flex-1"
                onClick={() => void confirmar()}
                disabled={busy || seleccionadas.length === 0 || hayInvalido}
              >
                {busy ? 'Reenviando…' : 'Reenviar'}
              </PrimaryButton>
            </div>
          </div>
        ) : null}

        {mostrarResultado ? (
          <div className="space-y-3" aria-live="polite">
            {fallidos.length > 0 ? (
              <p className="text-xs font-medium text-flit-primary dark:text-white">
                {exitosos.length > 0
                  ? `Salió para: ${exitosos.map((r) => nombreDe(r.validationId)).join(', ')}. No salió para: ${fallidos.map((r) => nombreDe(r.validationId)).join(', ')}.`
                  : `No salió para: ${fallidos.map((r) => nombreDe(r.validationId)).join(', ')}.`}
              </p>
            ) : null}
            <ul className="space-y-3" aria-label="Resultado del reenvío">
              {(resultados ?? []).map((r) => {
                const etiqueta = nombreDe(r.validationId);
                return (
                  <li
                    key={r.validationId}
                    role="group"
                    aria-label={etiqueta}
                    className="space-y-1 rounded-xl border p-3"
                    style={FIELD_BORDER}
                  >
                    <p className="text-sm font-semibold text-flit-primary dark:text-white">{etiqueta}</p>
                    {!r.ok ? (
                      <p className="text-xs font-medium" style={{ color: RED }}>
                        {r.mensaje || 'No se pudo reenviar la validación de identidad.'}
                      </p>
                    ) : r.queued ? (
                      <p className="text-xs font-medium text-flit-primary dark:text-white">{MSG_EN_COLA}</p>
                    ) : (
                      <>
                        <p className="text-xs font-medium text-flit-primary dark:text-white">
                          {MSG_REENVIADA}
                        </p>
                        {r.email ? (
                          <p className="text-xs text-flit-primary/60 dark:text-white/60">
                            Enviado a: <span className="font-medium">{r.email}</span>
                          </p>
                        ) : null}
                        {r.captureUrl ? (
                          <CopiarEnlace
                            url={r.captureUrl}
                            copiado={copiadoDe(r.validationId)}
                            onCopiar={(u) => void copiar(r.validationId, u)}
                          />
                        ) : null}
                      </>
                    )}
                  </li>
                );
              })}
            </ul>
            <div className="flex gap-3 pt-1">
              {fallidos.length > 0 ? (
                <SecondaryButton className="flex-1" onClick={() => setReloadKey((k) => k + 1)}>
                  Volver a la lista
                </SecondaryButton>
              ) : null}
              <PrimaryButton className="flex-1" onClick={cerrar}>
                Cerrar
              </PrimaryButton>
            </div>
          </div>
        ) : null}
      </div>
    </Modal>
  );
}
