'use client';

import { useEffect, useRef, useState } from 'react';
import { Icono } from '@flit/ui/Icono';
import { Modal } from '@/components/atom/Modal';
import { InlineAlert } from '@/components/atom/InlineAlert';
import { SeccionCargando, SeccionError, SeccionVacia } from '@/components/operacion/detalle/primitivos';
import { WIZARD_CTA_GRADIENT } from './wizard-field-styles';
import { tramitesClient, type AdminTipoDocumento } from '@/lib/api/tramites-client';
import { estadoLabel } from '@/lib/tramites/estados';
import { mismoDocTipo } from '@/lib/documents/doc-tipo';
import { esVigente } from '@/lib/documents/vigencia';
import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

/** Bug #14604 — formatos y tope que acepta el backend (`invalid_mime` / `file_too_large`). */
export const CARGA_DOC_MIMES = ['application/pdf', 'image/jpeg', 'image/png', 'image/webp'] as const;
export const CARGA_DOC_MAX_BYTES = 20 * 1024 * 1024;
export const CARGA_DOC_OBSERVACION_MAX = 1000;
const ACCEPT = '.pdf,.jpg,.jpeg,.png,.webp,application/pdf,image/jpeg,image/png,image/webp';

const LABEL_CLS = 'block text-xs font-semibold text-foreground';
const FIELD_CLS =
  'w-full rounded-xl border px-3 py-2 text-sm text-foreground focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-flit-brand dark:bg-transparent';
const BORDER = { borderColor: 'var(--border)' };

/** Valida el archivo en cliente; devuelve el mensaje de error o `null` si es válido. */
export function validarArchivoCarga(file: File): string | null {
  const mimeOk = (CARGA_DOC_MIMES as readonly string[]).includes(file.type);
  if (!mimeOk) return 'Formato no permitido. Usa PDF, JPG, PNG o WEBP.';
  if (file.size > CARGA_DOC_MAX_BYTES) return 'El archivo supera los 20 MB.';
  if (file.size === 0) return 'El archivo está vacío.';
  return null;
}

/** Mensaje claro por código de negocio del backend; si no se reconoce, el `detail` tal cual. */
export function mensajeErrorCargaDocumento(e: unknown): string {
  const fallback = 'No se pudo cargar el documento. Inténtalo de nuevo.';
  if (!(e instanceof Error)) return fallback;
  // Con ProblemDetails, `.message` ya es el `detail` del backend (ver `problemMessage`); solo se
  // reescribe cuando el código de negocio (`title`) tiene un texto más claro para el admin.
  const problem = (e as { problem?: Record<string, unknown> | null }).problem;
  const code = typeof problem?.title === 'string' ? problem.title : '';
  switch (code) {
    case 'missing_file':
      return 'Elige un archivo para cargar.';
    case 'invalid_mime':
      return 'Formato no permitido. Usa PDF, JPG, PNG o WEBP.';
    case 'file_too_large':
      return 'El archivo supera los 20 MB.';
    case 'observacion_requerida':
      return 'Escribe la observación.';
    case 'tipo_no_habilitado':
      return 'Ese tipo de documento no está habilitado para este trámite.';
    default:
      return e.message || fallback;
  }
}

interface Props {
  open: boolean;
  onClose: () => void;
  item: InstanceSummary;
  tenantId?: string;
  onSuccess: (message: string) => void;
  onError: (message: string) => void;
}

/**
 * Bug #14604 — «Actualizar documento»: el admin sube un documento en CUALQUIER estado del trámite, con
 * observación obligatoria. Si el tipo ya tenía uno vigente, este pasa a «versión anterior».
 */
export function AdminCargarDocumentoModal({ open, onClose, item, tenantId, onSuccess, onError }: Props) {
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [tipos, setTipos] = useState<AdminTipoDocumento[]>([]);
  const [vigentes, setVigentes] = useState<string[]>([]);
  const [reloadKey, setReloadKey] = useState(0);
  const [tipo, setTipo] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const [observacion, setObservacion] = useState('');
  const [busy, setBusy] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    // Reinicia el formulario y carga los tipos cada vez que el modal se abre (vive siempre montado).
    /* eslint-disable react-hooks/set-state-in-effect */
    setLoading(true);
    setLoadError(null);
    setSubmitError(null);
    setTipo('');
    setFile(null);
    setFileError(null);
    setObservacion('');
    /* eslint-enable react-hooks/set-state-in-effect */
    tramitesClient
      .adminListarTiposDocumento(item.id, tenantId)
      .then((res) => {
        if (!cancelled) setTipos(res ?? []);
      })
      .catch((e: unknown) => {
        if (!cancelled)
          setLoadError(e instanceof Error ? e.message : 'No se pudieron cargar los tipos de documento.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    // Solo informativo (aviso de «versión anterior»): si falla, simplemente no se muestra el aviso.
    tramitesClient
      .getAttachments(item.id, tenantId)
      .then((list) => {
        if (!cancelled) setVigentes((list ?? []).filter(esVigente).map((a) => a.tipo));
      })
      .catch(() => {
        if (!cancelled) setVigentes([]);
      });
    return () => {
      cancelled = true;
    };
  }, [open, item.id, tenantId, reloadKey]);

  const observacionOk = observacion.trim().length > 0;
  const puedeEnviar = !!tipo && !!file && !fileError && observacionOk && !busy;
  const tipoYaCargado = !!tipo && vigentes.some((v) => mismoDocTipo(v, tipo));

  const onPickFile = (f: File | null) => {
    setFile(f);
    setFileError(f ? validarArchivoCarga(f) : null);
  };

  const enviar = async () => {
    if (!puedeEnviar || !file) return;
    setBusy(true);
    setSubmitError(null);
    try {
      await tramitesClient.adminCargarDocumento(
        item.id,
        { file, tipo, observacion: observacion.trim() },
        tenantId,
      );
      onSuccess('Documento actualizado.');
      onClose();
    } catch (e) {
      const msg = mensajeErrorCargaDocumento(e);
      setSubmitError(msg);
      onError(msg);
    } finally {
      setBusy(false);
    }
  };

  const id = item.id;
  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Actualizar documento"
      titleClassName="text-base font-bold text-flit-brand"
      icono="archivo"
      description={`Trámite ${item.referenceNumber} · Estado actual: ${estadoLabel(item.estado)}`}
      size="sm"
      busy={busy}
    >
      {/* La fila es clickable y el modal se porta a body: se corta el burbujeo de React. */}
      <div className="space-y-3" onClick={(e) => e.stopPropagation()}>
        {loading ? <SeccionCargando etiqueta="Cargando tipos de documento" filas={2} /> : null}
        {!loading && loadError ? (
          <SeccionError
            mensaje={loadError}
            contexto="los tipos de documento"
            onReintentar={() => setReloadKey((k) => k + 1)}
          />
        ) : null}
        {!loading && !loadError && tipos.length === 0 ? (
          <SeccionVacia mensaje="No hay tipos de documento habilitados para este trámite." />
        ) : null}

        {!loading && !loadError && tipos.length > 0 ? (
          <>
            <div className="space-y-1">
              <label className={LABEL_CLS} htmlFor={`admin-cargar-doc-tipo-${id}`}>
                Tipo de documento
              </label>
              <select
                id={`admin-cargar-doc-tipo-${id}`}
                value={tipo}
                onChange={(e) => setTipo(e.target.value)}
                disabled={busy}
                className={FIELD_CLS}
                style={BORDER}
              >
                <option value="">Selecciona un tipo…</option>
                {tipos.map((t) => (
                  <option key={t.tipo} value={t.tipo}>
                    {t.nombre}
                  </option>
                ))}
              </select>
              {tipoYaCargado ? (
                <p className="text-xs text-muted-foreground" role="status">
                  El documento actual quedará como versión anterior.
                </p>
              ) : null}
            </div>

            <div className="space-y-1">
              <span className={LABEL_CLS} id={`admin-cargar-doc-archivo-lbl-${id}`}>
                Archivo
              </span>
              <input
                ref={fileInputRef}
                id={`admin-cargar-doc-archivo-${id}`}
                data-testid="admin-cargar-doc-archivo"
                type="file"
                accept={ACCEPT}
                aria-labelledby={`admin-cargar-doc-archivo-lbl-${id}`}
                disabled={busy}
                onChange={(e) => {
                  onPickFile(e.target.files?.[0] ?? null);
                  e.target.value = '';
                }}
                className="hidden"
              />
              <button
                type="button"
                onClick={() => fileInputRef.current?.click()}
                disabled={busy}
                className="inline-flex items-center gap-1.5 rounded-lg border px-3 py-2 text-xs font-semibold transition hover:bg-flit-brand/[0.06] disabled:cursor-not-allowed disabled:opacity-60"
                style={{ borderColor: 'var(--color-flit-brand)', color: 'var(--color-flit-brand)' }}
              >
                <Icono nombre="archivo" className="h-3.5 w-3.5" />
                {file ? 'Cambiar archivo' : 'Elegir archivo'}
              </button>
              {file ? (
                <p className="truncate text-xs text-muted-foreground">
                  Archivo seleccionado: <span className="font-medium text-foreground">{file.name}</span>
                </p>
              ) : (
                <p className="text-xs text-muted-foreground">PDF, JPG, PNG o WEBP. Máximo 20 MB.</p>
              )}
              {fileError ? (
                <p className="text-xs" style={{ color: 'var(--destructive)' }} role="alert">
                  {fileError}
                </p>
              ) : null}
            </div>

            <div className="space-y-1">
              <label className={LABEL_CLS} htmlFor={`admin-cargar-doc-obs-${id}`}>
                Observación
              </label>
              <textarea
                id={`admin-cargar-doc-obs-${id}`}
                value={observacion}
                onChange={(e) => setObservacion(e.target.value)}
                disabled={busy}
                rows={3}
                maxLength={CARGA_DOC_OBSERVACION_MAX}
                required
                aria-required="true"
                placeholder="¿Por qué actualizas este documento?"
                className={`${FIELD_CLS} resize-none`}
                style={BORDER}
              />
              <p className="text-right text-[11px] text-muted-foreground">
                {observacion.length}/{CARGA_DOC_OBSERVACION_MAX}
              </p>
            </div>

            {submitError ? <InlineAlert tone="error">{submitError}</InlineAlert> : null}

            <div className="flex gap-3 pt-1">
              <button
                type="button"
                onClick={onClose}
                disabled={busy}
                className="flex-1 rounded-xl border py-2.5 text-sm font-medium text-foreground transition hover:bg-secondary disabled:cursor-not-allowed disabled:opacity-60"
                style={BORDER}
              >
                Cancelar
              </button>
              <button
                type="button"
                onClick={() => void enviar()}
                disabled={!puedeEnviar}
                className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white transition disabled:cursor-not-allowed disabled:opacity-60"
                style={{ background: WIZARD_CTA_GRADIENT }}
              >
                {busy ? 'Actualizando…' : 'Actualizar documento'}
              </button>
            </div>
          </>
        ) : null}
      </div>
    </Modal>
  );
}
