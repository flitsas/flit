'use client';

import { type ReactNode, useEffect, useState } from 'react';
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
 * Tras reenviar NO se cierra: muestra el resultado por persona.
 * Bug #14674: el enlace de captura no se muestra ni se copia desde este modal; la validación llega por correo.
 */

const FIELD_LABEL_CLS = 'block text-xs font-semibold text-foreground';
// `ring-inset`: un ring hacia afuera se recorta contra el `overflow-y-auto` del Modal.
const FIELD_CLS =
  'w-full rounded-xl border px-3 py-2 text-sm text-foreground focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-flit-brand dark:bg-transparent';
const FIELD_BORDER = { borderColor: 'var(--border)' };
export const PARTE_LABEL: Record<string, string> = {
  vendedor: 'Vendedor',
  comprador: 'Comprador',
  locatario: 'Locatario',
};

/** Sin familia conocida (p. ej. desde Validaciones de Identidad, que no navega por trámite). */
export const MSG_TODOS_FIRMARON = 'Todas las personas del trámite ya firmaron. No hay a quién reenviar.';

/**
 * «Ya firmaron todos» dicho con las partes que ese trámite tiene: una matrícula (y los demás trámites
 * de la familia OTROS) solo tiene comprador, el traspaso tiene comprador y vendedor, y el traspaso
 * unilateral solo valida al vendedor (ADR-0051). Nombrar una parte que el trámite no tiene confunde.
 */
export function mensajeTodosFirmaron(modalidad?: string | null, tipoCodigo?: string | null): string {
  if (tipoCodigo === 'TRASPASO_UNILATERAL') return 'El vendedor ya firmó. No hay a quién reenviar.';
  if (modalidad === 'TRASPASO') return 'Comprador y vendedor ya firmaron. No hay a quién reenviar.';
  if (modalidad === 'MATRICULAS' || modalidad === 'OTROS')
    return 'El comprador ya firmó. No hay a quién reenviar.';
  return MSG_TODOS_FIRMARON;
}
const MSG_EN_COLA =
  'El reenvío quedó en cola: el proveedor tuvo una falla transitoria y se reintentará automáticamente.';
const MSG_REENVIADA = 'Validación de identidad reenviada.';
const MSG_MANUAL = 'En validación manual: el enlace se regenera desde la revisión manual.';
const MSG_INICIAL_AUSENTE =
  'La validación que elegiste ya no está pendiente de firma. Marca a quién reenviar de la lista.';
const RED = 'var(--destructive)';

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

/**
 * HU #14627 — clave única de fila. Antes se indexaba todo por `validationId` (checkbox, correo
 * capturado, mapa de búsqueda, `key` del `<li>`): con el contrato ampliado, una persona que NUNCA
 * tuvo validación llega con `validationId: null` (status `no_iniciada`) — no hay id real hasta que
 * el backend cree la fila al enviar. Se sintetiza una clave estable con `partyRole`/`ordinal` para
 * esos casos; las filas con fila ya existente siguen usando su `validationId` tal cual.
 */
function claveFila(p: Pick<AdminValidacionReenviable, 'validationId' | 'partyRole' | 'ordinal'>): string {
  return p.validationId ?? `sin-iniciar:${p.partyRole}:${p.ordinal}`;
}

/**
 * HU #14627 — clave de emparejamiento request↔resultado. El backend siempre ecoa `partyRole`/
 * `ordinal` en cada `AdminReenviarLoteItemResult`, también cuando la fila YA existía — es la única
 * clave estable en ambos lados: `validationId` de la fila nueva solo se conoce DESPUÉS del envío (el
 * backend lo crea), así que no sirve para encontrar la persona original que se marcó en la lista.
 */
function claveParOrdinal(p: { partyRole: string | null; ordinal: number | null }): string {
  return `${p.partyRole ?? ''}:${p.ordinal ?? ''}`;
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
      className={`rounded-xl border py-2.5 text-sm font-medium text-foreground transition hover:bg-secondary disabled:cursor-not-allowed disabled:opacity-60 ${className}`}
      style={FIELD_BORDER}
    >
      {children}
    </button>
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
  /** Texto del estado vacío según las partes del trámite (ver `mensajeTodosFirmaron`). */
  mensajeVacio?: string;
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
  mensajeVacio = MSG_TODOS_FIRMARON,
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
  const [reloadKey, setReloadKey] = useState(0);
  // AC4 — el aviso de éxito se entrega al CERRAR, no al reenviar: el padre (AdminTramiteAcciones)
  // responde a onSuccess recargando el listado, y esa recarga desmonta la fila con este modal
  // adentro, que se cerraba antes de que el usuario viera el resultado por persona.
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
    setCorreos({});
    tramitesClient
      .adminListValidacionesReenviables(instanceId, tenantId)
      .then((res) => {
        if (cancelled) return;
        const lista = res.personas ?? [];
        setPersonas(lista);
        // `status === 'no_iniciada'` (sin fila) SIEMPRE es elegible: nunca es `esFlujoManual`.
        const elegibles = lista.filter((p) => !p.esFlujoManual);
        const inicial = initialValidationId
          ? elegibles.find((p) => p.validationId === initialValidationId)
          : undefined;
        setInicialAusente(Boolean(initialValidationId) && !inicial && lista.length > 0);
        if (inicial) setMarcadas({ [claveFila(inicial)]: true });
        else if (elegibles.length === 1) setMarcadas({ [claveFila(elegibles[0]!)]: true });
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

  const seleccionadas = personas.filter((p) => marcadas[claveFila(p)] && !p.esFlujoManual);
  // AC3 — el correo es opcional POR PERSONA marcada, pero si se escribe debe tener formato válido.
  const correoInvalido = (id: string) => {
    const v = (correos[id] ?? '').trim();
    return v !== '' && !esCorreoConFormatoValido(v);
  };
  const hayInvalido = seleccionadas.some((p) => correoInvalido(claveFila(p)));

  // HU #14627 — texto del botón primario / mensaje de éxito: todas sin fila previa → "Enviar" (nunca
  // se había mandado nada); todas con fila previa (o NINGUNA marcada, el botón sigue disabled) →
  // "Reenviar" (comportamiento de siempre — `every` en un arreglo vacío es `true` por vacuidad, así
  // que sin selección se conserva el texto de siempre); selección MIXTA → texto neutro
  // "Enviar/Reenviar" (no hay un verbo único que describa la acción conjunta sin mentirle a una de
  // las dos mitades de la selección).
  const todasNuevas = seleccionadas.length > 0 && seleccionadas.every((p) => p.validationId === null);
  const todasExistentes = seleccionadas.every((p) => p.validationId !== null);
  const accionInfinitivo = todasNuevas ? 'Enviar' : todasExistentes ? 'Reenviar' : 'Enviar/Reenviar';
  const accionGerundio = todasNuevas
    ? 'Enviando…'
    : todasExistentes
      ? 'Reenviando…'
      : 'Enviando/Reenviando…';

  const confirmar = async () => {
    if (seleccionadas.length === 0 || hayInvalido) return;
    setBusy(true);
    setSubmitError(null);
    try {
      const res = await tramitesClient.adminReenviarValidacionesLote(
        instanceId,
        seleccionadas.map((p) => ({
          validationId: p.validationId,
          // NUEVO (HU #14627) — siempre se envían, también cuando `validationId` ya existe: el
          // backend los usa igual como echo del resultado (clave de emparejamiento universal).
          partyRole: p.partyRole,
          ordinal: p.ordinal,
          email: (correos[claveFila(p)] ?? '').trim() || null,
        })),
        tenantId,
      );
      const items = res.items ?? [];
      setResultados(items);
      const oks = items.filter((i) => i.ok);
      if (oks.length > 0) {
        const mensajeExito = todasNuevas
          ? 'Validación de identidad enviada.'
          : todasExistentes
            ? MSG_REENVIADA
            : 'Validación de identidad enviada/reenviada.';
        setAvisoPendiente(oks.every((i) => i.queued) ? MSG_EN_COLA : mensajeExito);
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

  // HU #14627 — emparejamiento por `partyRole`/`ordinal`, no por `validationId`: el resultado de una
  // fila que no tenía fila previa llega con el id RECIÉN CREADO por el backend, que no existe en
  // `personas` (la respuesta de carga, anterior al envío). El par partyRole/ordinal sí es estable.
  const porParOrdinal = new Map(personas.map((p) => [claveParOrdinal(p), p]));
  const exitosos = (resultados ?? []).filter((r) => r.ok);
  const fallidos = (resultados ?? []).filter((r) => !r.ok);
  const nombreDe = (r: Pick<AdminReenviarLoteItemResult, 'partyRole' | 'ordinal'>) => {
    const p = porParOrdinal.get(claveParOrdinal(r));
    return p ? etiquetaPersona(p) : 'Persona';
  };
  // Original (pre-envío) de esta fila, para saber si fue un envío nuevo o un reenvío.
  const seleccionPorParOrdinal = new Map(seleccionadas.map((p) => [claveParOrdinal(p), p]));
  const eraEnvioNuevo = (r: Pick<AdminReenviarLoteItemResult, 'partyRole' | 'ordinal'>): boolean =>
    seleccionPorParOrdinal.get(claveParOrdinal(r))?.validationId === null;

  const mostrarVacio = !loading && !loadError && personas.length === 0;
  const mostrarLista = !loading && !loadError && personas.length > 0 && resultados === null;
  const mostrarResultado = !loading && !loadError && resultados !== null && personas.length > 0;

  return (
    <Modal
      open={open}
      onClose={cerrar}
      title="Reenviar validación de identidad"
      titleClassName="text-base font-bold text-flit-brand"
      icono="radicacion"
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
        {mostrarVacio ? <SeccionVacia mensaje={mensajeVacio} /> : null}

        {mostrarLista ? (
          <div className="space-y-3">
            {inicialAusente ? (
              <p role="status" className="text-xs font-medium text-muted-foreground">
                {MSG_INICIAL_AUSENTE}
              </p>
            ) : null}
            <ul className="space-y-3" aria-label="Personas pendientes de firma">
              {personas.map((p) => {
                const id = claveFila(p);
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
                        className="text-sm font-semibold text-foreground"
                      >
                        {etiqueta}
                      </label>
                    </div>
                    {p.esFlujoManual ? (
                      <p className="text-xs text-muted-foreground">{MSG_MANUAL}</p>
                    ) : p.status === 'no_iniciada' ? (
                      <p className="text-xs text-muted-foreground">
                        Aún no se ha enviado la validación. Correo:{' '}
                        <span className="font-medium">{p.email ?? '—'}</span>
                      </p>
                    ) : (
                      <p className="text-xs text-muted-foreground">
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
                {busy ? accionGerundio : accionInfinitivo}
              </PrimaryButton>
            </div>
          </div>
        ) : null}

        {mostrarResultado ? (
          <div className="space-y-3" aria-live="polite">
            {fallidos.length > 0 ? (
              <p className="text-xs font-medium text-foreground">
                {exitosos.length > 0
                  ? `Salió para: ${exitosos.map((r) => nombreDe(r)).join(', ')}. No salió para: ${fallidos.map((r) => nombreDe(r)).join(', ')}.`
                  : `No salió para: ${fallidos.map((r) => nombreDe(r)).join(', ')}.`}
              </p>
            ) : null}
            <ul className="space-y-3" aria-label="Resultado del reenvío">
              {(resultados ?? []).map((r) => {
                const etiqueta = nombreDe(r);
                // HU #14627 — `r.validationId` llega `null` únicamente cuando falló ANTES de crear
                // la fila (`!r.ok`): como `key` de React usamos `partyRole`/`ordinal` (siempre
                // presentes, eco del item enviado), nunca `validationId` crudo, que colisionaría
                // (`null`) entre dos fallos sin fila.
                const claveResultado = claveParOrdinal(r);
                return (
                  <li
                    key={claveResultado}
                    role="group"
                    aria-label={etiqueta}
                    className="space-y-1 rounded-xl border p-3"
                    style={FIELD_BORDER}
                  >
                    <p className="text-sm font-semibold text-foreground">{etiqueta}</p>
                    {!r.ok ? (
                      <p className="text-xs font-medium" style={{ color: RED }}>
                        {r.mensaje || 'No se pudo reenviar la validación de identidad.'}
                      </p>
                    ) : r.queued ? (
                      <p className="text-xs font-medium text-foreground">{MSG_EN_COLA}</p>
                    ) : (
                      <>
                        <p className="text-xs font-medium text-foreground">
                          {eraEnvioNuevo(r) ? 'Validación de identidad enviada.' : MSG_REENVIADA}
                        </p>
                        {r.email ? (
                          <p className="text-xs text-muted-foreground">
                            Enviado a: <span className="font-medium">{r.email}</span>
                          </p>
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
