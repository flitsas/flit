'use client';

import { AlertTriangle, FileWarning, Layers } from 'lucide-react';
import { OcrStatusPanel, tipoLabel } from './DocumentChecklist';
import type { BatchReviewItem, BatchReviewState } from '@/hooks/useProcedureBatchUpload';
import type { OcrUiResult } from '@/hooks/useProcedureDocuments';

interface Props {
  state: BatchReviewState;
  /** Piezas marcadas para adjuntar. */
  aceptadas: BatchReviewItem[];
  onToggle: (id: string, decision: 'accept' | 'skip') => void;
  onConfirm: () => void;
  onCancel: () => void;
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** «pág. 3» o «págs. 5–7 de 16», según ocupe una página o varias. */
function rangoPaginas(paginas: number[], total: number): string {
  if (paginas.length === 0) return '';
  if (paginas.length === 1) return `pág. ${paginas[0]} de ${total}`;
  const primera = paginas[0];
  const ultima = paginas[paginas.length - 1];
  const consecutivas = ultima - primera + 1 === paginas.length;
  return consecutivas
    ? `págs. ${primera}–${ultima} de ${total}`
    : `págs. ${paginas.join(', ')} de ${total}`;
}

function Chip({ text, tone = 'info' }: { text: string; tone?: 'info' | 'warn' | 'muted' }) {
  const palette =
    tone === 'warn'
      ? { background: 'rgba(249,172,0,0.18)', color: '#B77900' }
      : tone === 'muted'
        ? { background: 'rgba(120,130,145,0.15)', color: '#5B6472' }
        : { background: 'rgba(85,126,255,0.10)', color: '#557EFF' };
  return (
    <span className="rounded px-1.5 py-0.5 text-xs font-semibold" style={palette}>
      {text}
    </span>
  );
}

/** Una pieza propuesta, con su casilla, sus avisos y el mismo resumen OCR del cargue campo a campo. */
function PieceRow({
  item,
  disabled,
  onToggle,
}: {
  item: BatchReviewItem;
  disabled: boolean;
  onToggle: (id: string, decision: 'accept' | 'skip') => void;
}) {
  const { piece, evaluation, conflicto, duplicado, decision } = item;
  const marcada = decision === 'accept';
  const recortada = piece.paginas.length > 0 && piece.paginas.length < piece.totalPaginasOrigen;

  // Mismo indicador OCR del cargue individual (icono + tooltip/modal).
  const ocr: OcrUiResult = {
    status: piece.data ? (evaluation.rechazado ? 'rejected' : 'verified') : 'skipped',
    motivo: evaluation.motivo,
    data: piece.data,
  };

  return (
    <li
      className="rounded-xl border p-3"
      style={{ borderColor: marcada ? '#8CC63F' : 'var(--color-border)' }}
    >
      <label className="flex cursor-pointer items-start gap-2.5">
        <input
          type="checkbox"
          checked={marcada}
          disabled={disabled}
          onChange={(e) => onToggle(item.id, e.target.checked ? 'accept' : 'skip')}
          className="mt-0.5 shrink-0"
          aria-label={`Adjuntar ${tipoLabel(piece.tipo)} de ${piece.sourceFilename}`}
        />

        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-1.5">
            <span className="text-xs font-semibold">{tipoLabel(piece.tipo)}</span>
            <Chip text={`${Math.round(piece.confianza * 100)}% de certeza`} tone="muted" />
            {recortada && (
              <Chip text={`recorte · ${rangoPaginas(piece.paginas, piece.totalPaginasOrigen)}`} />
            )}
          </div>

          <p className="mt-1 truncate text-xs opacity-70">
            De {piece.sourceFilename} · {formatSize(piece.sizeBytes)}
          </p>

          {piece.motivo && (
            <p className="mt-0.5 text-xs opacity-50">{piece.motivo}</p>
          )}
        </div>

        <OcrStatusPanel tipo={piece.tipo} ocr={ocr} />
      </label>

      {/* Avisos que explican por qué la pieza llegó desmarcada. */}
      {conflicto && (
        <p
          className="mt-2 flex items-start gap-1.5 text-xs font-medium"
          style={{ color: '#B77900' }}
        >
          <AlertTriangle className="mt-px h-3 w-3 shrink-0" aria-hidden="true" />
          <span>
            Ya hay un documento en esta casilla ({conflicto.filename}). Si la marcas, lo reemplazas.
          </span>
        </p>
      )}

      {duplicado && (
        <p
          className="mt-2 flex items-start gap-1.5 text-xs font-medium"
          style={{ color: '#B77900' }}
        >
          <Layers className="mt-px h-3 w-3 shrink-0" aria-hidden="true" />
          <span>
            La carga trae otro documento para esta misma casilla. Marca el que quieras conservar.
          </span>
        </p>
      )}
    </li>
  );
}

/**
 * Pantalla de revisión del cargue masivo: qué se propone adjuntar, qué páginas sobraron y qué archivos
 * no se pudieron abrir. Nada llega al expediente hasta que el operador confirma, de modo que un error
 * de clasificación no deja rastro.
 */
export function BatchReviewPanel({ state, aceptadas, onToggle, onConfirm, onCancel }: Props) {
  const subiendo = state.phase === 'uploading';
  const { items, noReconocidos, errores } = state;
  const nada = items.length === 0;

  return (
    <section
      className="mt-3 rounded-2xl border p-4"
      style={{ borderColor: '#557EFF', background: 'rgba(85,126,255,0.04)' }}
      aria-label="Revisión de la carga masiva"
    >
      <div className="mb-3 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h4 className="text-sm font-bold">Revisa antes de adjuntar</h4>
          <p className="text-xs opacity-60">
            {nada
              ? 'No pudimos identificar documentos en lo que cargaste.'
              : `Encontramos ${items.length} documento${items.length === 1 ? '' : 's'}. Marca los que quieras adjuntar; nada se guarda hasta que confirmes.`}
          </p>
        </div>
        {!nada && (
          <span
            className="shrink-0 rounded-full px-3 py-1 text-xs font-bold"
            style={{ background: 'rgba(85,126,255,0.12)', color: '#557EFF' }}
            role="status"
            aria-live="polite"
          >
            {aceptadas.length} seleccionado{aceptadas.length === 1 ? '' : 's'}
          </span>
        )}
      </div>

      {items.length > 0 && (
        <ul className="space-y-2" aria-label="Documentos encontrados">
          {items.map((item) => (
            <PieceRow key={item.id} item={item} disabled={subiendo} onToggle={onToggle} />
          ))}
        </ul>
      )}

      {/* Páginas que no correspondían a ningún tipo. Se listan con la salida concreta, no como un
          error: en un expediente real la mayoría de páginas son mandatos, cédulas y portadas. */}
      {noReconocidos.length > 0 && (
        <div className="mt-3 rounded-xl border p-3" style={{ borderColor: 'var(--color-border)' }}>
          <p className="flex items-center gap-1.5 text-xs font-semibold">
            <FileWarning className="h-3.5 w-3.5" style={{ color: '#B77900' }} aria-hidden="true" />
            Páginas que no pudimos ubicar
          </p>
          <ul className="mt-1.5 space-y-1">
            {noReconocidos.map((n) => (
              <li key={n.sourceFilename} className="text-xs opacity-70">
                <span className="font-medium">{n.sourceFilename}</span>{' '}
                — {n.paginas.length} de {n.totalPaginas} página
                {n.totalPaginas === 1 ? '' : 's'} sin clasificar.
              </li>
            ))}
          </ul>
          <p className="mt-1.5 text-xs opacity-60">
            Si falta algún documento, cárgalo directamente en su casilla más abajo: ahí el análisis se
            hace sabiendo qué tipo esperas y suele acertar donde el reparto automático no pudo.
          </p>
        </div>
      )}

      {errores.length > 0 && (
        <div
          className="mt-3 rounded-xl border p-3"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)' }}
          role="alert"
        >
          <p className="text-xs font-semibold" style={{ color: '#FF4E00' }}>
            Archivos que no pudimos procesar
          </p>
          <ul className="mt-1.5 space-y-1">
            {errores.map((e) => (
              <li key={`${e.filename}-${e.motivo}`} className="text-xs" style={{ color: '#FF4E00' }}>
                <span className="font-medium">{e.filename}</span> — {e.motivo}
              </li>
            ))}
          </ul>
        </div>
      )}

      <div className="mt-4 flex flex-wrap items-center justify-end gap-2">
        <button
          type="button"
          onClick={onCancel}
          disabled={subiendo}
          className="rounded-xl border px-3 py-1.5 text-xs font-semibold disabled:cursor-not-allowed disabled:opacity-60"
          style={{ borderColor: 'var(--color-border)' }}
        >
          Descartar
        </button>
        <button
          type="button"
          onClick={onConfirm}
          disabled={subiendo || aceptadas.length === 0}
          className="rounded-xl px-3 py-1.5 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-60"
          style={{ background: '#557EFF' }}
        >
          {subiendo
            ? 'Adjuntando…'
            : `Adjuntar ${aceptadas.length} documento${aceptadas.length === 1 ? '' : 's'}`}
        </button>
      </div>
    </section>
  );
}
