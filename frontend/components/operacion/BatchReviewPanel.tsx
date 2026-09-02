'use client';

import { AlertTriangle, FileWarning, Layers } from 'lucide-react';
import { OcrStatusPanel, tipoLabel } from './DocumentChecklist';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { WizardCardHeader } from './wizard-atoms';
import { WIZARD_CTA_GRADIENT } from './wizard-field-styles';
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

/**
 * Lista de páginas comprimida en rangos: `4, 7, 12–15 y 20`.
 *
 * <p>El backend devuelve los números de página (`BatchUnrecognized.Paginas`) y hasta ahora la
 * pantalla solo mostraba CUÁNTAS eran. Con un PDF de treinta páginas, «12 de 30 sin clasificar» deja
 * al gestor abriendo el archivo entero para dar con las doce; el dato que lo resuelve ya venía en la
 * respuesta, sin usar.</p>
 *
 * <p>Se comprime en rangos y no se vuelca la lista cruda porque en un expediente real las páginas
 * sobrantes van seguidas (portadas, anexos), y cuarenta números separados por comas no se leen.</p>
 */
function comprimirPaginas(paginas: number[]): string {
  if (paginas.length === 0) return '';
  const orden = [...paginas].sort((a, b) => a - b);
  const tramos: string[] = [];
  let inicio = orden[0];
  let previa = orden[0];

  const cerrar = () => {
    if (inicio === previa) tramos.push(String(inicio));
    // Dos páginas seguidas se enumeran («7, 8»): un rango de dos no ahorra nada y se lee peor.
    else if (previa - inicio === 1) tramos.push(`${inicio}, ${previa}`);
    else tramos.push(`${inicio}–${previa}`);
  };

  for (const pagina of orden.slice(1)) {
    if (pagina === previa + 1) {
      previa = pagina;
      continue;
    }
    cerrar();
    inicio = pagina;
    previa = pagina;
  }
  cerrar();

  if (tramos.length === 1) return tramos[0];
  return `${tramos.slice(0, -1).join(', ')} y ${tramos[tramos.length - 1]}`;
}

/** Tono semántico del `StatusBadge` para las etiquetas de esta pantalla (HU consolidación). */
const CHIP_TONE: Record<'info' | 'warn' | 'muted', StatusTone> = {
  info: 'info',
  warn: 'warning',
  muted: 'neutral',
};

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
            <StatusBadge
              label={`${Math.round(piece.confianza * 100)}% de certeza`}
              tone={CHIP_TONE.muted}
            />
            {recortada && (
              <StatusBadge
                label={`recorte · ${rangoPaginas(piece.paginas, piece.totalPaginasOrigen)}`}
                tone={CHIP_TONE.info}
              />
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
          style={{ color: 'var(--badge-warning-fg)' }}
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
          style={{ color: 'var(--badge-warning-fg)' }}
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
      {/* Dos encabezados, no uno con el subtítulo cambiado. Cuando no se reconoció NADA, «Revisa
          antes de adjuntar» pedía revisar lo que no había, y el botón primario ofrecía «Adjuntar 0
          documentos» en gris: un callejón sin salida con forma de pantalla de revisión, que además
          sugiere que el gestor hizo algo mal. Sin nada que revisar, la pantalla dice qué pasó y por
          dónde seguir. */}
      <WizardCardHeader
        title={nada ? 'No reconocimos ningún documento' : 'Revisa el reparto antes de adjuntar'}
        subtitle={
          nada
            ? 'Ninguna de las páginas que cargaste corresponde a un requisito de este trámite. No se adjuntó nada.'
            : `Reconocimos ${items.length} documento${items.length === 1 ? '' : 's'} y lo${items.length === 1 ? '' : 's'} ubicamos en su casilla. Desmarca lo${items.length === 1 ? '' : 's'} que no quieras adjuntar; nada se guarda hasta que confirmes.`
        }
        action={
          !nada ? (
            <span
              className="shrink-0 rounded-full px-3 py-1 text-xs font-bold"
              style={{ background: 'rgba(85,126,255,0.12)', color: '#557EFF' }}
              role="status"
              aria-live="polite"
            >
              {aceptadas.length} seleccionado{aceptadas.length === 1 ? '' : 's'}
            </span>
          ) : undefined
        }
      />

      {items.length > 0 && (
        <ul className="space-y-2" aria-label="Documentos encontrados">
          {items.map((item) => (
            <PieceRow key={item.id} item={item} disabled={subiendo} onToggle={onToggle} />
          ))}
        </ul>
      )}

      {/* Páginas que no correspondían a ningún tipo. Se listan con la salida concreta, no como un
          error: en un expediente real la mayoría de páginas son mandatos, cédulas y portadas —el
          propio flujo lo da por supuesto—, así que el rótulo es descriptivo y no lleva el ámbar de
          advertencia, que hacía leer como avería lo que es lo esperado.

          Cuando no se reconoció nada, el encabezado del panel YA dijo esto mismo: repetirlo en una
          caja aparte es decir dos veces la única cosa que pasó. Ahí queda solo el detalle por
          archivo. */}
      {noReconocidos.length > 0 && (
        <div className="mt-3 rounded-xl border p-3" style={{ borderColor: 'var(--color-border)' }}>
          {!nada && (
            <p className="flex items-center gap-1.5 text-xs font-semibold">
              <FileWarning className="h-3.5 w-3.5 opacity-70" aria-hidden="true" />
              Páginas que no corresponden a ningún requisito
            </p>
          )}
          <ul className={nada ? 'space-y-1' : 'mt-1.5 space-y-1'}>
            {noReconocidos.map((n) => {
              // Que sobren 3 de 16 y que sobren 16 de 16 no son la misma situación, y salían con el
              // mismo texto. En la segunda no hay nada que revisar: el archivo entero queda fuera.
              const todas = n.paginas.length >= n.totalPaginas;
              return (
                <li key={n.sourceFilename} className="text-xs opacity-70">
                  <span className="font-medium">{n.sourceFilename}</span>{' '}
                  {todas ? (
                    <>
                      — ninguna de sus {n.totalPaginas} página
                      {n.totalPaginas === 1 ? '' : 's'} corresponde a un requisito de este trámite.
                    </>
                  ) : (
                    <>
                      — no se adjuntarán las páginas {comprimirPaginas(n.paginas)} ({n.paginas.length}{' '}
                      de {n.totalPaginas}).
                    </>
                  )}
                </li>
              );
            })}
          </ul>
          {/* La salida sube a texto normal: era lo único accionable del bloque y estaba al 60% de
              opacidad, más apagado que el problema que viene a resolver. */}
          <p className="mt-2 text-xs">
            Carga cada documento en su casilla, más abajo. Ahí el sistema ya sabe qué documento
            espera, y lo reconoce mucho mejor que en el reparto automático.
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

      {/* Sin nada reconocido no se ofrece adjuntar: un botón primario apagado que dice «Adjuntar 0
          documentos» se lee como una acción que el gestor debería poder completar, y no la hay.
          Queda un único botón que cierra el panel y lo devuelve a cargar por casillas, que es la
          salida que el propio bloque acaba de indicarle. */}
      <div className="mt-4 flex flex-wrap items-center justify-end gap-2">
        {nada ? (
          <button
            type="button"
            onClick={onCancel}
            className="rounded-xl px-3 py-1.5 text-xs font-semibold text-white"
            style={{ background: WIZARD_CTA_GRADIENT }}
          >
            Entendido
          </button>
        ) : (
          <>
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
              style={{ background: WIZARD_CTA_GRADIENT }}
            >
              {subiendo
                ? 'Adjuntando…'
                : `Adjuntar ${aceptadas.length} documento${aceptadas.length === 1 ? '' : 's'}`}
            </button>
          </>
        )}
      </div>
    </section>
  );
}
