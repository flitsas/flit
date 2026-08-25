'use client';

import { useEffect, useRef, useState } from 'react';
import { Paperclip } from 'lucide-react';
import type { ProcedureAttachment } from '@/lib/api/types/procedure-runtime';
import { cn } from '@/lib/utils';
import { tramitesClient } from '@/lib/api/tramites-client';
import {
  BLINDAJE_DOC_TIPO,
  BLINDAJE_FLAG_FIELD_KEY,
  BLINDAJE_NIVEL_FIELD_KEY,
  BLINDAJE_OPCIONES,
  type BlindajeOpcion,
  blindajeCompleto,
  blindajeObservacionFur,
  dejaElVehiculoBlindado,
  esCertificadoDeBlindaje,
  parseBlindajeOpcion,
} from '@/lib/catalogs/blindaje';
import { WIZARD_INPUT, WIZARD_LABEL } from './wizard-field-styles';

/**
 * Declaración de blindaje del tipo `BLINDAJE` (familia OTROS): qué se hace —instalar un nivel o
 * retirar el blindaje— y el certificado que lo acredita.
 *
 * <p>Antes esto era un párrafo informativo: el trámite afirmaba `blindaje = true` por su cuenta y no
 * preguntaba nada, de modo que un nivel 1, un nivel 3 y un desmonte salían con el FUR idéntico. El
 * nivel no cabe en la casilla del formulario —que es un SÍ/NO— así que viaja en las observaciones,
 * y el desmonte además invierte esa casilla: el vehículo queda SIN blindaje.</p>
 *
 * <p>El certificado se pide aquí, junto a la opción, y no solo en el checklist: es el documento que
 * acredita lo que este control declara, y separarlos obligaba al gestor a recordar la relación.
 * Sigue siendo el MISMO adjunto del checklist (`certificado_blindaje`), no una copia.</p>
 */
export function BlindajeDeclaracionCard({
  instanceId,
  readOnly,
  onCompletenessChange,
  onDocumentsChanged,
}: {
  instanceId: string | null;
  readOnly: boolean;
  /** Notifica si hay opción declarada + certificado adjunto (gate de Continuar). */
  onCompletenessChange?: (complete: boolean) => void;
  onDocumentsChanged?: () => void;
}) {
  const [opcion, setOpcion] = useState<BlindajeOpcion | null>(null);
  const [attachments, setAttachments] = useState<ProcedureAttachment[]>([]);
  const [saving, setSaving] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!instanceId) return;
    let active = true;
    void tramitesClient
      .getInstance(instanceId)
      .then((d) => {
        if (!active) return;
        setOpcion(
          parseBlindajeOpcion(
            d?.fieldValues?.find((f) => f.fieldKey === BLINDAJE_NIVEL_FIELD_KEY)?.valueText,
          ),
        );
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [instanceId]);

  useEffect(() => {
    // Sin instancia no se limpia el estado: ya arranca vacío y sin instancia nunca se llenó. El
    // reset explícito sobraba y además encendía react-hooks/set-state-in-effect.
    if (!instanceId) return;
    let active = true;
    void tramitesClient
      .getAttachments(instanceId)
      .then((list) => {
        if (active) setAttachments(list);
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [instanceId]);

  useEffect(() => {
    onCompletenessChange?.(blindajeCompleto(opcion, attachments));
  }, [opcion, attachments, onCompletenessChange]);

  const certificado = attachments.find(esCertificadoDeBlindaje);
  const disabled = readOnly || saving;
  const busy = uploading || deletingId !== null;

  const elegir = async (valor: string) => {
    const nueva = parseBlindajeOpcion(valor);
    if (!instanceId || !nueva) return;
    setSaving(true);
    setError(null);
    try {
      // La bandera se escribe DERIVADA de la opción, en el mismo PATCH: son un solo hecho, y
      // dejarlas viajar por separado permitía que un desmonte conservara `blindaje = true` de una
      // elección anterior y el FUR marcara la casilla al revés.
      await tramitesClient.patchFieldValues(instanceId, [
        { formFieldId: null, fieldKey: BLINDAJE_NIVEL_FIELD_KEY, valueText: nueva, valueJson: null },
        {
          formFieldId: null,
          fieldKey: BLINDAJE_FLAG_FIELD_KEY,
          valueText: dejaElVehiculoBlindado(nueva) ? 'true' : 'false',
          valueJson: null,
        },
      ]);
      setOpcion(nueva);
    } catch {
      setError('No se pudo guardar la opción de blindaje. Reintenta.');
    } finally {
      setSaving(false);
    }
  };

  const refrescarAdjuntos = async () => {
    if (!instanceId) return;
    const list = await tramitesClient.getAttachments(instanceId).catch(() => null);
    if (list) setAttachments(list);
    onDocumentsChanged?.();
  };

  const subir = async (file: File) => {
    if (!instanceId || readOnly) return;
    setError(null);
    setUploading(true);
    try {
      await tramitesClient.uploadAttachment(instanceId, BLINDAJE_DOC_TIPO, file);
      await refrescarAdjuntos();
    } catch {
      setError('No se pudo adjuntar el certificado. Reintenta.');
    } finally {
      setUploading(false);
    }
  };

  const borrar = async (attachmentId: string) => {
    if (!instanceId || readOnly) return;
    setError(null);
    setDeletingId(attachmentId);
    try {
      await tramitesClient.deleteAttachment(instanceId, attachmentId);
      await refrescarAdjuntos();
    } catch {
      setError('No se pudo eliminar el certificado. Reintenta.');
    } finally {
      setDeletingId(null);
    }
  };

  const listo = opcion !== null && !!certificado;

  return (
    <div className="space-y-4">
      <p className="text-[13px] opacity-70">
        Escoge qué declara el trámite y adjunta el certificado: es lo que el FUR va a imprimir.
      </p>

      {error && (
        <p className="text-xs font-medium" style={{ color: '#FF4E00' }} role="alert">
          {error}
        </p>
      )}

      <div
        className={cn(
          'relative flex flex-col rounded-xl bg-white p-4 shadow-sm transition hover:shadow-md sm:max-w-sm dark:bg-[#162744]',
          listo ? 'border' : 'border-2 border-dashed hover:border-[#557EFF] hover:bg-[#F0F5FF]',
        )}
        style={{ borderColor: '#E2E8F0' }}
      >
        <span
          className={cn(
            'absolute right-3 top-3 whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs',
            listo ? 'font-semibold text-white' : 'bg-red-50 font-medium text-red-600',
          )}
          style={listo ? { background: '#8CC63F' } : undefined}
        >
          {listo ? 'Validado' : '* Obligatorio'}
        </span>

        <p className="pr-28 text-[13px] font-semibold leading-tight" style={{ color: '#162744' }}>
          Certificado de blindaje
        </p>
        <p className="mt-1 text-[11px] opacity-70">PDF, JPG hasta 5MB</p>
        {certificado && (
          <p className="mt-1 truncate text-[11px] opacity-60">{certificado.filename}</p>
        )}

        <div className="mt-3 space-y-1.5">
          <label htmlFor="blindaje-opcion" className={WIZARD_LABEL}>
            Opción del trámite *
          </label>
          <select
            id="blindaje-opcion"
            value={opcion ?? ''}
            disabled={disabled}
            onChange={(e) => void elegir(e.target.value)}
            className={`${WIZARD_INPUT} disabled:opacity-60`}
            aria-invalid={opcion === null}
          >
            <option value="">Selecciona una opción…</option>
            {BLINDAJE_OPCIONES.map((o) => (
              <option key={o.codigo} value={o.codigo}>
                {o.label}
              </option>
            ))}
          </select>
          {opcion === null && (
            <p className="text-[11px] font-medium" style={{ color: '#FF4E00' }}>
              Escoge el nivel de blindaje o el desmonte.
            </p>
          )}
          {!certificado && (
            <p className="text-[11px] font-medium" style={{ color: '#FF4E00' }}>
              Adjunta el certificado obligatorio.
            </p>
          )}
        </div>

        <div className="mt-auto flex flex-wrap items-center gap-2 pt-4">
          {instanceId && !readOnly ? (
            <>
              <input
                ref={inputRef}
                type="file"
                accept="application/pdf,image/jpeg,image/png,image/webp"
                className="hidden"
                aria-label="Adjuntar certificado de blindaje"
                onChange={(e) => {
                  const file = e.target.files?.[0];
                  e.target.value = '';
                  if (file) void subir(file);
                }}
              />
              <button
                type="button"
                onClick={() => inputRef.current?.click()}
                disabled={busy || disabled}
                className="inline-flex h-9 items-center gap-1.5 rounded-lg border bg-white px-4 text-[12px] font-semibold transition hover:bg-[#EFF6FF] disabled:cursor-not-allowed disabled:opacity-50 dark:bg-transparent"
                style={{
                  borderColor: certificado ? '#557EFF' : '#FF4E00',
                  color: certificado ? '#557EFF' : '#FF4E00',
                  borderWidth: certificado ? 1 : 2,
                }}
              >
                <Paperclip className="h-3.5 w-3.5" aria-hidden="true" />
                {uploading ? 'Subiendo…' : certificado ? 'Reemplazar archivo' : 'Adjuntar archivo'}
              </button>
              {certificado && (
                <button
                  type="button"
                  onClick={() => void borrar(certificado.id)}
                  disabled={busy || disabled}
                  className="text-xs font-semibold disabled:opacity-50"
                  style={{ color: '#FF4E00' }}
                  aria-label="Borrar certificado de blindaje"
                >
                  {deletingId === certificado.id ? 'Borrando…' : 'Borrar'}
                </button>
              )}
            </>
          ) : (
            <p className="text-[11px] opacity-60">
              {certificado
                ? 'Documento adjunto'
                : 'Guarda el trámite para poder adjuntar el certificado.'}
            </p>
          )}
        </div>
      </div>

      <p
        aria-live="polite"
        className={cn('rounded-xl px-3 py-2 text-xs', opcion ? 'font-medium' : 'opacity-70')}
        style={
          opcion ? { background: 'rgba(85,126,255,0.08)', color: '#557EFF' } : undefined
        }
      >
        {opcion
          ? `Se registrará en el FUR — ${resumenFur(opcion)}`
          : 'Sin opción declarada: el FUR no puede decir qué blindaje se solicita.'}
      </p>
    </div>
  );
}

/**
 * Lo que el FUR va a imprimir, para que el gestor lo vea ANTES de generar el PDF: el texto de
 * observaciones y, junto a él, la casilla «vehículo blindado» —que es donde el desmonte se separa de
 * los tres niveles y donde más fácil sería equivocarse sin verlo.
 */
function resumenFur(opcion: BlindajeOpcion): string {
  const casilla = dejaElVehiculoBlindado(opcion) ? 'BLINDADO: SÍ' : 'BLINDADO: NO';
  return `${blindajeObservacionFur(opcion)} · ${casilla}`;
}
