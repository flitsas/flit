'use client';

import { type ReactNode, useEffect, useState } from 'react';
import { Send } from 'lucide-react';
import { Modal } from '@/components/atom/Modal';
import { InlineAlert } from '@/components/atom/InlineAlert';
import {
  SeccionCargando,
  SeccionError,
  SeccionVacia,
} from '@/components/operacion/detalle/primitivos';
import { WIZARD_CTA_GRADIENT } from './wizard-field-styles';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { BiometricValidation } from '@/lib/api/types/procedure-runtime';

/**
 * HU #12161 (Feature #12155) — Reenviar validación de identidad de UN trámite fuera del gate del
 * wizard: reenvía el enlace (mismo registro, token nuevo) y admite actualizar el correo antes de
 * enviarlo. Extraído a su propio módulo en HU #12164 para reutilizarse en dos puntos de montaje:
 *
 *   - `AdminTramiteAcciones.tsx` (HU #12163) — menú de acciones avanzadas de una fila del Dashboard
 *     de Trámites, que ya tiene el `InstanceSummary` completo de la fila.
 *   - `Validaciones.tsx` (HU #12164) — módulo de Validaciones de Identidad, que NO navega por
 *     trámite: identifica la fila por `TenantBiometricValidation` (persona/validación agrupada) y
 *     solo conoce `instanceId` + `referenceNumber`, no el `InstanceSummary` entero.
 *
 * Por eso el contrato pide solo lo mínimo (`instanceId`/`referenceNumber`), no `InstanceSummary`.
 */

const FIELD_LABEL_CLS = 'block text-xs font-semibold text-[#162744] dark:text-white';
// `ring-inset`: ver el mismo comentario en `AdminTramiteAcciones.tsx` — un ring hacia afuera se
// recorta contra el `overflow-y-auto` del Modal (que fuerza `overflow-x: auto` también) cuando el
// campo es `w-full` sin margen horizontal.
const FIELD_CLS =
  'w-full rounded-xl border px-3 py-2 text-sm text-[#162744] focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[#557EFF] dark:border-white/15 dark:bg-transparent dark:text-white';
const FIELD_BORDER = { borderColor: '#DFE5ED' };

export const PARTE_LABEL: Record<string, string> = { vendedor: 'Vendedor', comprador: 'Comprador' };

/**
 * Mismo patrón de validación de formato de correo que `PrevalidacionForm.tsx` (`validEmail`): un
 * único regex de formato para todo el módulo de identidad, no uno nuevo por formulario. El campo es
 * OPCIONAL (vacío = reenvía al correo ya registrado, AC1 de HU #12161); solo se exige formato válido
 * cuando el admin escribe algo (AC2 de HU #12164).
 */
function esCorreoConFormatoValido(v: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v.trim());
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
      className={`rounded-xl border py-2.5 text-sm font-medium text-[#162744] transition hover:bg-[#162744]/[0.04] disabled:cursor-not-allowed disabled:opacity-60 dark:border-white/20 dark:text-white ${className}`}
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
   * Preselecciona esta validación en el selector (el admin ya la identificó desde su propia grilla,
   * a diferencia del menú del Dashboard que arranca sin ninguna elegida). Si no está en la lista
   * cargada, se ignora y cae al primer resultado, igual que antes.
   */
  initialValidationId?: string;
  onSuccess: (message: string) => void;
  onError: (message: string) => void;
}

/** HU #12161/#12164 — Reenviar validación de identidad: 4 estados (carga/error/vacío/lleno) + formulario. */
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
  const [validations, setValidations] = useState<BiometricValidation[]>([]);
  const [validationId, setValidationId] = useState('');
  const [email, setEmail] = useState('');
  const [busy, setBusy] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    // Carga las validaciones de identidad cada vez que el modal se abre.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setLoading(true);
    setLoadError(null);
    setSubmitError(null);
    setEmail('');
    tramitesClient
      .listBiometricExpediente(instanceId, tenantId)
      .then((res) => {
        if (cancelled) return;
        const lista = res.validations ?? [];
        setValidations(lista);
        const preseleccion =
          initialValidationId && lista.some((v) => v.id === initialValidationId)
            ? initialValidationId
            : (lista[0]?.id ?? '');
        setValidationId(preseleccion);
      })
      .catch((e: unknown) => {
        if (cancelled) return;
        setLoadError(
          e instanceof Error ? e.message : 'No se pudieron cargar las validaciones de identidad.',
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

  // AC2 (HU #12164) — el correo es opcional, pero si el admin escribe algo tiene que tener formato
  // válido antes de habilitar "Reenviar". Copiar/pegar funciona nativo: es un `<input>` sin `onPaste`
  // bloqueado.
  const emailTrimmed = email.trim();
  const emailInvalido = emailTrimmed !== '' && !esCorreoConFormatoValido(emailTrimmed);
  const selectedValidation = validations.find((v) => v.id === validationId);

  const confirmar = async () => {
    if (!validationId || emailInvalido) return;
    setBusy(true);
    setSubmitError(null);
    try {
      const res = await tramitesClient.adminReenviarValidacionIdentidad(
        instanceId,
        validationId,
        emailTrimmed || null,
        tenantId,
      );
      onSuccess(
        res.queued
          ? 'El reenvío quedó en cola: el proveedor tuvo una falla transitoria y se reintentará automáticamente.'
          : 'Validación de identidad reenviada.',
      );
      onClose();
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'No se pudo reenviar la validación de identidad.';
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
      title="Reenviar validación de identidad"
      icon={Send}
      description={referenceNumber}
      size="sm"
      busy={busy}
    >
      {/* `contents`: envoltorio solo para detener la propagación del clic hacia una fila clickable
          que lo monte (el portal de `Modal` burbujea por el árbol de React, no por el DOM) — no
          aporta layout propio. */}
      <div className="contents" onClick={(e) => e.stopPropagation()}>
        {loading ? <SeccionCargando etiqueta="Cargando validaciones de identidad" filas={2} /> : null}
        {!loading && loadError ? (
          <SeccionError
            mensaje={loadError}
            contexto="las validaciones de identidad"
            onReintentar={() => setReloadKey((k) => k + 1)}
          />
        ) : null}
        {!loading && !loadError && validations.length === 0 ? (
          <SeccionVacia mensaje="Este trámite no tiene validaciones de identidad registradas." />
        ) : null}
        {!loading && !loadError && validations.length > 0 ? (
          <div className="space-y-3">
            {/* Con 1 sola validación (matrícula inicial, otros trámites: solo comprador) no hay nada
                que elegir — el selector solo aporta valor en traspaso (vendedor + comprador). */}
            {validations.length > 1 ? (
              <>
                <label className={FIELD_LABEL_CLS} htmlFor={`admin-reenviar-validacion-${instanceId}`}>
                  Validación a reenviar
                </label>
                <select
                  id={`admin-reenviar-validacion-${instanceId}`}
                  value={validationId}
                  onChange={(e) => setValidationId(e.target.value)}
                  disabled={busy}
                  className={FIELD_CLS}
                  style={FIELD_BORDER}
                >
                  {validations.map((v) => (
                    <option key={v.id} value={v.id}>
                      {(v.partyRole ? `${PARTE_LABEL[v.partyRole] ?? v.partyRole} · ` : '') + v.name}
                    </option>
                  ))}
                </select>
              </>
            ) : null}
            {selectedValidation ? (
              <p className="text-xs text-[#162744]/60 dark:text-white/60">
                Correo registrado: <span className="font-medium">{selectedValidation.email}</span>
              </p>
            ) : null}

            <label className={FIELD_LABEL_CLS} htmlFor={`admin-reenviar-email-${instanceId}`}>
              Nuevo correo (opcional)
            </label>
            <input
              id={`admin-reenviar-email-${instanceId}`}
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              disabled={busy}
              placeholder="Deja vacío para reenviar al correo actual"
              className={FIELD_CLS}
              style={FIELD_BORDER}
              aria-invalid={emailInvalido}
              aria-describedby={emailInvalido ? `admin-reenviar-email-err-${instanceId}` : undefined}
            />
            {emailInvalido ? (
              <p
                id={`admin-reenviar-email-err-${instanceId}`}
                role="alert"
                className="text-[11px] font-medium"
                style={{ color: '#FF4E00' }}
              >
                Correo inválido.
              </p>
            ) : null}

            {submitError ? <InlineAlert tone="error">{submitError}</InlineAlert> : null}

            <div className="flex gap-3 pt-1">
              <SecondaryButton className="flex-1" onClick={onClose} disabled={busy}>
                Cancelar
              </SecondaryButton>
              <PrimaryButton
                className="flex-1"
                onClick={() => void confirmar()}
                disabled={busy || !validationId || emailInvalido}
              >
                {busy ? 'Reenviando…' : 'Reenviar'}
              </PrimaryButton>
            </div>
          </div>
        ) : null}
      </div>
    </Modal>
  );
}
