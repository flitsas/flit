'use client';

import { type ReactNode, useEffect, useRef, useState } from 'react';
import { ArrowRightLeft, Ban, Paperclip, RefreshCcw, Send, UserCog } from 'lucide-react';
import { Modal } from '@/components/atom/Modal';
import { InlineAlert } from '@/components/atom/InlineAlert';
import type { ActionsMenuItem } from '@/components/atom/ActionsMenu';
import {
  SeccionCargando,
  SeccionError,
  SeccionVacia,
} from '@/components/operacion/detalle/primitivos';
import { WIZARD_CTA_GRADIENT } from './wizard-field-styles';
import { ReenviarValidacionIdentidadModal } from './ReenviarValidacionIdentidadModal';
import { usePermissions } from '@/hooks/usePermissions';
import { useToast } from '@/components/admin/Toast';
import { tramitesClient, type GestorOption } from '@/lib/api/tramites-client';
import { estadoLabel } from '@/lib/tramites/estados';
import {
  ADMIN_ESTADO_DESTINO_OPTIONS,
  ADMIN_TRAMITE_PERMISSIONS,
} from '@/lib/tramites/admin-tramite-permissions';
import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

/**
 * HU #12163 (Feature #12155) — menú de acciones avanzadas del administrador sobre un trámite del
 * Dashboard: Limpiar/Cargar consolidado, Cambiar estado, Anular, Reenviar validación y Reasignar
 * gestor. Extiende el `ActionsMenu` YA EXISTENTE de `TramitesTable.tsx` (no monta un menú
 * paralelo): este hook solo aporta los `ActionsMenuItem[]` adicionales (gateados por permiso, AC1)
 * y los modales de confirmación/formulario que esos ítems abren (AC3).
 *
 * AC2 — "Cambiar estado" y "Anular" llegan DESHABILITADOS (con `disabledReason`, tooltip del
 * propio `ActionsMenu`) cuando el trámite está en `aprobado`, ANTES de intentar llamar al backend:
 * el backend igual lo rechaza (422) si algo se cuela, pero la UX debe prevenirlo.
 */

const FIELD_LABEL_CLS = 'block text-xs font-semibold text-[#162744] dark:text-white';
// `ring-inset`: el modal recorta el contenido con `overflow-y-auto` (el navegador fuerza también
// `overflow-x: auto` — no hay forma de pedir solo un eje), y un campo `w-full` toca el borde de ese
// contenedor sin margen horizontal. Un ring normal (fuera de la caja) queda cortado a los lados al
// enfocar; `ring-inset` lo dibuja hacia adentro y nunca se recorta, sin importar el ancestro.
const FIELD_CLS =
  'w-full rounded-xl border px-3 py-2 text-sm text-[#162744] focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[#557EFF] dark:border-white/15 dark:bg-transparent dark:text-white';
// El asa de resize nativa del <textarea> es cuadrada: sobre `rounded-xl` recorta la esquina
// inferior derecha y se ve como una muesca. `resize-none` la quita (el campo ya crece con `rows`).
const TEXTAREA_CLS = `${FIELD_CLS} resize-none`;
const FIELD_BORDER = { borderColor: '#DFE5ED' };

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
      className={`rounded-xl border py-2.5 text-sm font-medium text-[#162744] transition hover:bg-[#162744]/[0.04] disabled:cursor-not-allowed disabled:opacity-60 dark:border-white/20 dark:text-white ${className}`}
      style={FIELD_BORDER}
    >
      {children}
    </button>
  );
}

/** Rojo del token `anulado` (`lib/tramites/estados.ts` → `ESTADO_CHIP_STYLES.anulado.accent`): no un HEX suelto. */
const DANGER_COLOR = '#C1272D';

function DangerButton({
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
      style={{ background: DANGER_COLOR }}
    >
      {children}
    </button>
  );
}

interface ModalBaseProps {
  open: boolean;
  onClose: () => void;
  item: InstanceSummary;
  tenantId?: string;
  onSuccess: (message: string) => void;
  onError: (message: string) => void;
}

/** AC3 — Cambiar estado (HU #12159): selector de destino (excluye 'aprobado') + motivo opcional. */
function CambiarEstadoModal({ open, onClose, item, tenantId, onSuccess, onError }: ModalBaseProps) {
  const opciones = ADMIN_ESTADO_DESTINO_OPTIONS.filter((e) => e !== item.estado);
  const [toStatus, setToStatus] = useState('');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    // Reinicia el formulario cada vez que el modal se abre (el modal vive siempre montado; solo
    // cambia `open`).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setToStatus('');
    setReason('');
    setError(null);
  }, [open]);

  const confirmar = async () => {
    if (!toStatus) return;
    setBusy(true);
    setError(null);
    try {
      const res = await tramitesClient.adminCambiarEstado(
        item.id,
        toStatus,
        reason.trim() || null,
        tenantId,
      );
      onSuccess(`Estado cambiado a "${estadoLabel(res.newStatus)}".`);
      onClose();
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'No se pudo cambiar el estado del trámite.';
      setError(msg);
      onError(msg);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Cambiar estado"
      icon={ArrowRightLeft}
      description={`${item.referenceNumber} · Estado actual: ${estadoLabel(item.estado)}`}
      size="sm"
      busy={busy}
    >
      {/* La fila entera es clickable (`<tr onClick={handleOpen}>`) y este modal se porta a
          `document.body`: React sigue burbujeando eventos por el árbol de COMPONENTES, no por el
          DOM, así que un clic aquí llegaría a `handleOpen` si no se detiene aquí. Mismo patrón que
          el modal "Procesar" de esta misma pantalla. */}
      <div className="space-y-3" onClick={(e) => e.stopPropagation()}>
        <label className={FIELD_LABEL_CLS} htmlFor={`admin-cambiar-estado-${item.id}`}>
          Nuevo estado
        </label>
        <select
          id={`admin-cambiar-estado-${item.id}`}
          value={toStatus}
          onChange={(e) => setToStatus(e.target.value)}
          disabled={busy}
          className={FIELD_CLS}
          style={FIELD_BORDER}
        >
          <option value="">Selecciona un estado…</option>
          {opciones.map((e) => (
            <option key={e} value={e}>
              {estadoLabel(e)}
            </option>
          ))}
        </select>

        <label className={FIELD_LABEL_CLS} htmlFor={`admin-cambiar-estado-motivo-${item.id}`}>
          Motivo (opcional)
        </label>
        <textarea
          id={`admin-cambiar-estado-motivo-${item.id}`}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          disabled={busy}
          rows={3}
          placeholder="Motivo de la corrección administrativa"
          className={TEXTAREA_CLS}
          style={FIELD_BORDER}
        />

        {error ? <InlineAlert tone="error">{error}</InlineAlert> : null}

        <div className="flex gap-3 pt-1">
          <SecondaryButton className="flex-1" onClick={onClose} disabled={busy}>
            Cancelar
          </SecondaryButton>
          <PrimaryButton className="flex-1" onClick={() => void confirmar()} disabled={busy || !toStatus}>
            {busy ? 'Cambiando…' : 'Confirmar cambio'}
          </PrimaryButton>
        </div>
      </div>
    </Modal>
  );
}

/** AC3 — Anular (HU #12160): motivo opcional + confirmación de tono destructivo. */
function AnularModal({ open, onClose, item, tenantId, onSuccess, onError }: ModalBaseProps) {
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    // Reinicia el formulario cada vez que el modal se abre.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setReason('');
    setError(null);
  }, [open]);

  const confirmar = async () => {
    setBusy(true);
    setError(null);
    try {
      await tramitesClient.adminAnular(item.id, reason.trim() || null, tenantId);
      onSuccess(`Trámite ${item.referenceNumber} anulado.`);
      onClose();
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'No se pudo anular el trámite.';
      setError(msg);
      onError(msg);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Anular trámite"
      icon={Ban}
      iconBg={DANGER_COLOR}
      description={`${item.referenceNumber} · Estado actual: ${estadoLabel(item.estado)}`}
      size="sm"
      busy={busy}
    >
      <div className="space-y-3" onClick={(e) => e.stopPropagation()}>
        <label className={FIELD_LABEL_CLS} htmlFor={`admin-anular-motivo-${item.id}`}>
          Motivo (opcional)
        </label>
        <textarea
          id={`admin-anular-motivo-${item.id}`}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          disabled={busy}
          rows={3}
          placeholder="Motivo de la anulación administrativa"
          className={TEXTAREA_CLS}
          style={FIELD_BORDER}
        />

        {error ? <InlineAlert tone="error">{error}</InlineAlert> : null}

        <div className="flex gap-3 pt-1">
          <SecondaryButton className="flex-1" onClick={onClose} disabled={busy}>
            Cancelar
          </SecondaryButton>
          <DangerButton className="flex-1" onClick={() => void confirmar()} disabled={busy}>
            {busy ? 'Anulando…' : 'Anular trámite'}
          </DangerButton>
        </div>
      </div>
    </Modal>
  );
}

/** HU #12158 — Limpiar/Cargar consolidado: dos secciones independientes, cada una gateada por su permiso. */
function ConsolidadoModal({
  open,
  onClose,
  item,
  tenantId,
  canLimpiar,
  canCargar,
  onSuccess,
  onError,
}: ModalBaseProps & { canLimpiar: boolean; canCargar: boolean }) {
  const [busyLimpiar, setBusyLimpiar] = useState(false);
  const [busyCargar, setBusyCargar] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [error, setError] = useState<string | null>(null);
  const busy = busyLimpiar || busyCargar;
  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!open) return;
    // Reinicia el formulario cada vez que el modal se abre.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setFile(null);
    setError(null);
  }, [open]);

  const limpiar = async () => {
    setBusyLimpiar(true);
    setError(null);
    try {
      await tramitesClient.adminLimpiarConsolidado(item.id, tenantId);
      onSuccess('Consolidado regenerado.');
      onClose();
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'No se pudo regenerar el consolidado.';
      setError(msg);
      onError(msg);
    } finally {
      setBusyLimpiar(false);
    }
  };

  const cargar = async () => {
    if (!file) return;
    setBusyCargar(true);
    setError(null);
    try {
      await tramitesClient.adminCargarConsolidado(item.id, file, tenantId);
      onSuccess('Consolidado cargado.');
      onClose();
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'No se pudo cargar el consolidado.';
      setError(msg);
      onError(msg);
    } finally {
      setBusyCargar(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Gestionar consolidado"
      icon={RefreshCcw}
      description={item.referenceNumber}
      size="sm"
      busy={busy}
    >
      <div className="space-y-4" onClick={(e) => e.stopPropagation()}>
        {canLimpiar ? (
          <div className="space-y-2 rounded-xl border p-3" style={FIELD_BORDER}>
            <p className="text-xs font-semibold text-[#162744] dark:text-white">Limpiar consolidado</p>
            <p className="text-xs text-[#162744]/70 dark:text-white/70">
              Descarta el consolidado vigente (incluso si lo cargó un admin) y lo regenera desde
              cero.
            </p>
            <SecondaryButton onClick={() => void limpiar()} disabled={busy}>
              {busyLimpiar ? 'Regenerando…' : 'Regenerar consolidado'}
            </SecondaryButton>
          </div>
        ) : null}

        {canCargar ? (
          <div className="space-y-2 rounded-xl border p-3" style={FIELD_BORDER}>
            <p className="text-xs font-semibold text-[#162744] dark:text-white">Cargar consolidado</p>
            <p className="text-xs text-[#162744]/70 dark:text-white/70">
              Registra un PDF externo como el expediente consolidado. Reemplaza cualquier
              consolidado vigente.
            </p>
            <input
              ref={fileInputRef}
              id={`admin-cargar-consolidado-${item.id}`}
              type="file"
              accept="application/pdf"
              aria-label="Archivo PDF del consolidado"
              disabled={busy}
              onChange={(e) => {
                setFile(e.target.files?.[0] ?? null);
                // Permite re-elegir el MISMO archivo dos veces seguidas (si el admin cancela la
                // carga y quiere reintentar sin cambiar de archivo, el navegador no dispara
                // `onChange` si el value no cambia).
                e.target.value = '';
              }}
              className="hidden"
            />
            {/* El input nativo ("Seleccionar archivo / Ningún archivo seleccionado") pasaba
                desapercibido como punto de entrada — el admin probaba "Cargar PDF" primero y lo veía
                deshabilitado sin entender por qué (hallazgo QA). Un botón explícito con el mismo
                patrón "Adjuntar archivo" que ya usa el resto del wizard es la única acción que hace
                falta entender para elegir el archivo. */}
            <button
              type="button"
              onClick={() => fileInputRef.current?.click()}
              disabled={busy}
              className="inline-flex items-center gap-1.5 rounded-lg border px-3 py-2 text-xs font-semibold transition hover:bg-[#557EFF]/[0.06] disabled:cursor-not-allowed disabled:opacity-60"
              style={{ borderColor: '#557EFF', color: '#557EFF' }}
            >
              <Paperclip className="h-3.5 w-3.5" aria-hidden="true" />
              {file ? 'Cambiar archivo' : 'Elegir archivo PDF'}
            </button>
            {file ? (
              <p className="truncate text-xs text-[#162744]/70 dark:text-white/70">
                Archivo seleccionado:{' '}
                <span className="font-medium text-[#162744] dark:text-white">{file.name}</span>
              </p>
            ) : (
              <p className="text-xs text-[#162744]/50 dark:text-white/50">Ningún archivo elegido todavía.</p>
            )}
            <SecondaryButton onClick={() => void cargar()} disabled={busy || !file}>
              {busyCargar ? 'Cargando…' : 'Cargar PDF'}
            </SecondaryButton>
          </div>
        ) : null}

        {error ? <InlineAlert tone="error">{error}</InlineAlert> : null}
      </div>
    </Modal>
  );
}

/** HU #12162 — Reasignar gestor: 4 estados + selector de gestores DISPONIBLES del tenant. */
function ReasignarGestorModal({ open, onClose, item, tenantId, onSuccess, onError }: ModalBaseProps) {
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [gestores, setGestores] = useState<GestorOption[]>([]);
  const [selected, setSelected] = useState('');
  const [busy, setBusy] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    // Carga los gestores disponibles cada vez que el modal se abre.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setLoading(true);
    setLoadError(null);
    setSubmitError(null);
    tramitesClient
      .adminListGestoresDisponibles(tenantId)
      .then((res) => {
        if (cancelled) return;
        setGestores(res ?? []);
        setSelected(res?.[0]?.id ?? '');
      })
      .catch((e: unknown) => {
        if (cancelled) return;
        setLoadError(e instanceof Error ? e.message : 'No se pudieron cargar los gestores disponibles.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [open, tenantId, reloadKey]);

  const confirmar = async () => {
    if (!selected) return;
    setBusy(true);
    setSubmitError(null);
    try {
      await tramitesClient.adminReasignarGestor(item.id, selected, tenantId);
      onSuccess('Gestor reasignado.');
      onClose();
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'No se pudo reasignar el gestor del trámite.';
      setSubmitError(msg);
      onError(msg);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Reasignar gestor"
      icon={UserCog}
      description={item.referenceNumber}
      size="sm"
      busy={busy}
    >
      {/* `contents`: mismo envoltorio anti-bubbling que `ReenviarValidacionModal`. */}
      <div className="contents" onClick={(e) => e.stopPropagation()}>
      {loading ? <SeccionCargando etiqueta="Cargando gestores disponibles" filas={2} /> : null}
      {!loading && loadError ? (
        <SeccionError
          mensaje={loadError}
          contexto="los gestores disponibles"
          onReintentar={() => setReloadKey((k) => k + 1)}
        />
      ) : null}
      {!loading && !loadError && gestores.length === 0 ? (
        <SeccionVacia mensaje="No hay gestores disponibles en este tenant." />
      ) : null}
      {!loading && !loadError && gestores.length > 0 ? (
        <div className="space-y-3">
          <label className={FIELD_LABEL_CLS} htmlFor={`admin-reasignar-gestor-${item.id}`}>
            Nuevo gestor
          </label>
          <select
            id={`admin-reasignar-gestor-${item.id}`}
            value={selected}
            onChange={(e) => setSelected(e.target.value)}
            disabled={busy}
            className={FIELD_CLS}
            style={FIELD_BORDER}
          >
            {gestores.map((g) => (
              <option key={g.id} value={g.id}>
                {g.displayName} · {g.email}
              </option>
            ))}
          </select>

          {submitError ? <InlineAlert tone="error">{submitError}</InlineAlert> : null}

          <div className="flex gap-3 pt-1">
            <SecondaryButton className="flex-1" onClick={onClose} disabled={busy}>
              Cancelar
            </SecondaryButton>
            <PrimaryButton className="flex-1" onClick={() => void confirmar()} disabled={busy || !selected}>
              {busy ? 'Reasignando…' : 'Reasignar'}
            </PrimaryButton>
          </div>
        </div>
      ) : null}
      </div>
    </Modal>
  );
}

export interface UseAdminTramiteAccionesArgs {
  item: InstanceSummary;
  /** SuperAdmin viendo el trámite de OTRA compañía: las llamadas llevan X-Tenant-Id de la FILA. */
  isAdmin: boolean;
  /** Se invoca tras cualquier mutación exitosa, para refrescar la fila/tabla (estado, consolidado,
   *  gestor…) — mismo criterio que el resto de acciones administrativas de esta tabla. */
  onChanged: () => void;
}

export interface UseAdminTramiteAccionesResult {
  /** Ítems a anexar al `ActionsMenu` ya existente de la fila (AC1 — solo los permisos habilitados). */
  items: ActionsMenuItem[];
  /** Modales de las 5 acciones; se renderizan siempre (montados=false cuando `open` es false). */
  modals: ReactNode;
}

/**
 * HU #12163 — hook que arma los ítems de menú y los modales de las 5 acciones avanzadas del
 * administrador para UNA fila del listado de trámites. Vive aparte de `TramitesTable.tsx` para no
 * seguir engordando ese archivo; se consume desde `TramiteRow` exactamente igual que cualquier otro
 * dato derivado de la fila.
 */
export function useAdminTramiteAcciones({
  item,
  isAdmin,
  onChanged,
}: UseAdminTramiteAccionesArgs): UseAdminTramiteAccionesResult {
  const { permissions, isSuperAdmin } = usePermissions();
  const { show } = useToast();
  // Mismo patrón que el resto de llamadas per-instance de esta pantalla (setPriority, pauseInstance…):
  // el tenant de la FILA solo viaja si quien opera es SuperAdmin viendo otra compañía.
  const tenantId = isAdmin ? item.tenantId : undefined;
  const puede = (slug: string) => isSuperAdmin || permissions.includes(slug);
  const esAprobado = item.estado === 'aprobado';

  const [estadoOpen, setEstadoOpen] = useState(false);
  const [anularOpen, setAnularOpen] = useState(false);
  const [consolidadoOpen, setConsolidadoOpen] = useState(false);
  const [reenviarOpen, setReenviarOpen] = useState(false);
  const [reasignarOpen, setReasignarOpen] = useState(false);

  const onSuccess = (message: string) => {
    show(message, 'success');
    onChanged();
  };
  const onError = (message: string) => show(message, 'error');

  const items: ActionsMenuItem[] = [];

  // AC2 — "Cambiar estado" y "Anular" llegan deshabilitados (con tooltip) sobre un trámite
  // Aprobado, ANTES de llamar al backend.
  if (puede(ADMIN_TRAMITE_PERMISSIONS.cambiarEstado)) {
    items.push({
      key: 'admin-cambiar-estado',
      label: 'Cambiar estado',
      icon: ArrowRightLeft,
      disabled: esAprobado,
      disabledReason: 'Un trámite Aprobado no admite cambio de estado administrativo.',
      onSelect: () => setEstadoOpen(true),
    });
  }
  if (puede(ADMIN_TRAMITE_PERMISSIONS.anular)) {
    items.push({
      key: 'admin-anular',
      label: 'Anular',
      icon: Ban,
      disabled: esAprobado,
      disabledReason: 'Un trámite Aprobado no se puede anular.',
      onSelect: () => setAnularOpen(true),
    });
  }
  const canLimpiar = puede(ADMIN_TRAMITE_PERMISSIONS.limpiarConsolidado);
  const canCargar = puede(ADMIN_TRAMITE_PERMISSIONS.cargarConsolidado);
  if (canLimpiar || canCargar) {
    items.push({
      key: 'admin-consolidado',
      label: 'Gestionar consolidado',
      icon: RefreshCcw,
      onSelect: () => setConsolidadoOpen(true),
    });
  }
  if (puede(ADMIN_TRAMITE_PERMISSIONS.reenviarValidacion)) {
    items.push({
      key: 'admin-reenviar-validacion',
      label: 'Reenviar validación',
      icon: Send,
      onSelect: () => setReenviarOpen(true),
    });
  }
  if (puede(ADMIN_TRAMITE_PERMISSIONS.reasignarGestor)) {
    items.push({
      key: 'admin-reasignar-gestor',
      label: 'Reasignar gestor',
      icon: UserCog,
      onSelect: () => setReasignarOpen(true),
    });
  }

  const modals = (
    <>
      <CambiarEstadoModal
        open={estadoOpen}
        onClose={() => setEstadoOpen(false)}
        item={item}
        tenantId={tenantId}
        onSuccess={onSuccess}
        onError={onError}
      />
      <AnularModal
        open={anularOpen}
        onClose={() => setAnularOpen(false)}
        item={item}
        tenantId={tenantId}
        onSuccess={onSuccess}
        onError={onError}
      />
      <ConsolidadoModal
        open={consolidadoOpen}
        onClose={() => setConsolidadoOpen(false)}
        item={item}
        tenantId={tenantId}
        canLimpiar={canLimpiar}
        canCargar={canCargar}
        onSuccess={onSuccess}
        onError={onError}
      />
      <ReenviarValidacionIdentidadModal
        open={reenviarOpen}
        onClose={() => setReenviarOpen(false)}
        instanceId={item.id}
        referenceNumber={item.referenceNumber}
        tenantId={tenantId}
        onSuccess={onSuccess}
        onError={onError}
      />
      <ReasignarGestorModal
        open={reasignarOpen}
        onClose={() => setReasignarOpen(false)}
        item={item}
        tenantId={tenantId}
        onSuccess={onSuccess}
        onError={onError}
      />
    </>
  );

  return { items, modals };
}
