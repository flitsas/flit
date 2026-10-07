'use client';

import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';
import { Download, FileArchive } from 'lucide-react';
import { Modal } from '@/components/atom/Modal';
import { InlineAlert } from '@/components/atom/InlineAlert';
import type { ModeloSeleccionLote } from '@/hooks/useSeleccionLote';
import {
  consolidadoLotesClient,
  interpretarErrorCrearLote,
  MENSAJE_LOTE_ACTIVO,
  MENSAJE_REINTENTAR_DESCARGA,
} from '@/lib/api/consolidado-lotes-client';
import {
  ETIQUETA_ESTADO_LOTE,
  type LoteConsolidados,
  type TipoDocumentoLote,
} from '@/lib/api/types-consolidado-lotes';
import { useWizardFocusTrap } from './use-wizard-focus-trap';
import { WIZARD_CTA_GRADIENT } from './wizard-field-styles';

/**
 * HU #13381 (épica #13216) — confirmación y creación de la descarga masiva de consolidados.
 *
 * <p>El texto del efecto es el aprobado (CF-08, diseño §2.5.4) y NO se parafrasea. Confirmar envía
 * `POST /api/v1/tramites/consolidados/lotes` con `confirmaEfectos: true` y la selección tal cual la
 * arma `useSeleccionLote` (#13380). Al crearse, el dueño de la selección cierra el modal y la
 * limpia (AC2); el lote corre en segundo plano y su seguimiento es de #13382.</p>
 *
 * <p>Estados: vacío (sin trámites, no se puede confirmar), lleno (texto + confirmar), cargando
 * (creando: botón deshabilitado, `aria-busy`, Escape inerte), error (422 tope / 503 o red con
 * reintento) y lote activo (409: aviso + resumen del lote que ya corre, sin confirmar).</p>
 *
 * <p>Accesibilidad (WCAG 2.1 AA): `Modal` da `role="dialog"`, `aria-modal` y el título; aquí se
 * atrapa el foco (Tab/Shift+Tab) con la trampa compartida de los diálogos del módulo y el foco
 * inicial va a «Cancelar», la opción que no tiene efectos.</p>
 */

/** AC1 — texto aprobado, exacto. */
export const TEXTO_CONFIRMACION_DESCARGA_MASIVA =
  'Se descargará el consolidado que cada trámite tiene guardado, tal como está, aunque no refleje cambios posteriores. Solo se generará el consolidado de los trámites que todavía no tienen uno.';

/**
 * HU #13394 — texto aprobado de la variante maestro (bandeja del OT, CF-11), exacto. HU #13387: es
 * también el texto del Super Admin cuando elige «Consolidado maestro» en el selector.
 */
export const TEXTO_CONFIRMACION_DESCARGA_MAESTROS =
  'Se descargará el consolidado maestro que cada trámite tiene guardado, tal como está, aunque no refleje cambios posteriores. Solo se generará el consolidado maestro de los trámites que todavía no tienen uno.';

/** Qué consolidado se descarga: el del Gestor (`consolidado`) o el maestro de la bandeja OT. */
export type VarianteDescargaMasiva = 'consolidado' | 'maestro';

const TEXTO_POR_VARIANTE: Record<VarianteDescargaMasiva, string> = {
  consolidado: TEXTO_CONFIRMACION_DESCARGA_MASIVA,
  maestro: TEXTO_CONFIRMACION_DESCARGA_MAESTROS,
};

/**
 * Crea el lote con la selección; lanza `ConsolidadoLotesApiError` (409/422/403/503/red). HU #13387:
 * con el selector de tipo visible recibe también el tipo elegido.
 */
export type CrearLoteDescargaMasiva<TFiltro> = (
  seleccion: ModeloSeleccionLote<TFiltro>,
  tipoDocumento?: TipoDocumentoLote,
) => Promise<LoteConsolidados>;

/** HU #13387 — opciones del selector del Super Admin (CF-03), en el orden del AC. */
const OPCIONES_TIPO: ReadonlyArray<{ valor: TipoDocumentoLote; etiqueta: string }> = [
  { valor: 'consolidado', etiqueta: 'Consolidado' },
  { valor: 'consolidado_maestro', etiqueta: 'Consolidado maestro' },
];

const formatoMiles = (n: number) => n.toLocaleString('es-CO');
const textoTramites = (n: number) =>
  `${formatoMiles(n)} trámite${n === 1 ? '' : 's'} seleccionado${n === 1 ? '' : 's'}`;

export interface DescargaMasivaConfirmModalProps<TFiltro> {
  open: boolean;
  onClose: () => void;
  /** Modelo de `useSeleccionLote`; se envía tal cual. */
  seleccion: ModeloSeleccionLote<TFiltro>;
  /** Trámites en la selección (exacto, de `useSeleccionLote`). */
  contador: number;
  /** El lote se creó (202). Quien llama cierra el modal y limpia la selección. */
  onCreado: (lote: LoteConsolidados) => void;
  /**
   * 409 `lote_activo`: punto de enganche para el seguimiento global (#13382), que mostrará el
   * progreso de ese lote fuera del modal.
   */
  onLoteActivo?: (loteId: string) => void;
  /** HU #13394 — texto de la confirmación; por defecto el del Gestor. */
  variante?: VarianteDescargaMasiva;
  /** HU #13394 — creación inyectada (la bandeja OT usa su ruta); por defecto la del Gestor. */
  crear?: CrearLoteDescargaMasiva<TFiltro>;
  /**
   * HU #13387 — selector «Consolidado» / «Consolidado maestro». Solo el Super Admin en /tramites:
   * el texto sigue al tipo elegido y `crear` lo recibe. Con el selector, un 403 se trata como
   * cualquier fallo reintentable (AC6).
   */
  selectorTipo?: boolean;
}

type Fase =
  | { tipo: 'lista' }
  | { tipo: 'creando' }
  | { tipo: 'error'; mensaje: string; reintentable: boolean }
  | { tipo: 'lote_activo'; loteId: string | null; lote: LoteConsolidados | null };

const BTN_SECUNDARIO =
  'rounded-full border border-flit-gray px-4 py-1.5 text-xs font-semibold text-flit-primary transition hover:bg-flit-primary/[0.04] focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand dark:border-white/20 dark:text-white';
const BTN_PRIMARIO =
  'inline-flex items-center gap-1.5 rounded-full px-4 py-1.5 text-xs font-semibold text-white transition focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-1 disabled:cursor-not-allowed disabled:opacity-60';

export function DescargaMasivaConfirmModal<TFiltro>({
  open,
  onClose,
  seleccion,
  contador,
  onCreado,
  onLoteActivo,
  variante = 'consolidado',
  crear,
  selectorTipo = false,
}: DescargaMasivaConfirmModalProps<TFiltro>) {
  const [fase, setFase] = useState<Fase>({ tipo: 'lista' });
  const [tipoDocumento, setTipoDocumento] = useState<TipoDocumentoLote>('consolidado');
  const creando = fase.tipo === 'creando';
  const idSelector = useId();

  // Cada apertura empieza limpia (un error de la vez anterior no se arrastra).
  const [abiertoAntes, setAbiertoAntes] = useState(open);
  if (open !== abiertoAntes) {
    setAbiertoAntes(open);
    if (open) {
      setFase({ tipo: 'lista' });
      setTipoDocumento('consolidado');
    }
  }

  // Trampa de foco sobre el diálogo completo (incluye la X del encabezado de `Modal`). `Modal` no
  // expone ref: se resuelve desde un ancla del cuerpo al `role="dialog"` que la contiene.
  const anclaRef = useRef<HTMLDivElement>(null);
  const cancelarRef = useRef<HTMLButtonElement>(null);
  const dialogoRef = useMemo(
    () => ({
      get current(): HTMLElement | null {
        return anclaRef.current?.closest<HTMLElement>('[role="dialog"]') ?? null;
      },
    }),
    [],
  );
  useWizardFocusTrap(dialogoRef, { active: open, initialFocusRef: cancelarRef });

  // Si el modal se desmonta a media petición, no se escribe estado sobre un componente muerto.
  const vivoRef = useRef(true);
  useEffect(() => {
    vivoRef.current = true;
    return () => {
      vivoRef.current = false;
    };
  }, []);

  const confirmar = useCallback(async () => {
    if (contador < 1 || creando) return;
    setFase({ tipo: 'creando' });
    try {
      const tipo = selectorTipo ? tipoDocumento : undefined;
      const lote = crear
        ? await (tipo ? crear(seleccion, tipo) : crear(seleccion))
        : await consolidadoLotesClient.crearLote({ seleccion, tipoDocumento: tipo });
      if (!vivoRef.current) return;
      setFase({ tipo: 'lista' });
      onCreado(lote);
    } catch (err) {
      if (!vivoRef.current) return;
      const r = interpretarErrorCrearLote(err);
      if (r.tipo === 'permiso' && selectorTipo) {
        // HU #13387 AC6 — el Super Admin no depende del claim: un 403 es un fallo transitorio.
        setFase({ tipo: 'error', mensaje: MENSAJE_REINTENTAR_DESCARGA, reintentable: true });
        return;
      }
      if (r.tipo !== 'lote_activo') {
        // 403: sin permiso no hay reintento que valga (HU #13394 AC5).
        setFase({ tipo: 'error', mensaje: r.mensaje, reintentable: r.tipo !== 'permiso' });
        return;
      }
      setFase({ tipo: 'lote_activo', loteId: r.loteActivoId, lote: null });
      if (!r.loteActivoId) return;
      onLoteActivo?.(r.loteActivoId);
      // Resumen del lote que ya corre; si no se puede leer, el aviso basta.
      try {
        const lote = await consolidadoLotesClient.obtenerLote(r.loteActivoId);
        if (vivoRef.current) setFase({ tipo: 'lote_activo', loteId: r.loteActivoId, lote });
      } catch {
        /* sin resumen: queda el aviso */
      }
    }
  }, [contador, creando, seleccion, onCreado, onLoteActivo, crear, selectorTipo, tipoDocumento]);

  const vacio = contador < 1;
  const sinReintento = fase.tipo === 'error' && !fase.reintentable;
  const loteActivo = fase.tipo === 'lote_activo' ? fase : null;
  const textoConfirmacion = selectorTipo
    ? TEXTO_POR_VARIANTE[tipoDocumento === 'consolidado_maestro' ? 'maestro' : 'consolidado']
    : TEXTO_POR_VARIANTE[variante];

  return (
    <Modal
      open={open}
      onClose={onClose}
      busy={creando}
      title="Descargar consolidados en ZIP"
      icon={FileArchive}
      iconBg="var(--color-flit-brand)"
      size="sm"
      footer={
        <div className="flex justify-end gap-2">
          <button ref={cancelarRef} type="button" onClick={onClose} disabled={creando} className={BTN_SECUNDARIO}>
            {loteActivo ? 'Entendido' : 'Cancelar'}
          </button>
          {loteActivo ? null : (
            <button
              type="button"
              onClick={() => void confirmar()}
              disabled={vacio || creando || sinReintento}
              className={BTN_PRIMARIO}
              style={{ background: WIZARD_CTA_GRADIENT }}
            >
              <Download className="h-3.5 w-3.5" aria-hidden="true" />
              {creando ? 'Creando la descarga…' : 'Confirmar descarga'}
            </button>
          )}
        </div>
      }
    >
      <div
        ref={anclaRef}
        data-testid="descarga-masiva-cuerpo"
        aria-busy={creando}
        className="flex flex-col gap-3 text-sm text-flit-primary/80 dark:text-white/80"
      >
        {vacio ? (
          <p>No hay trámites seleccionados. Marca al menos uno en el listado para descargarlo.</p>
        ) : (
          <>
            {selectorTipo ? (
              <div role="radiogroup" aria-labelledby={`${idSelector}-etiqueta`} className="flex flex-col gap-2">
                <span id={`${idSelector}-etiqueta`} className="text-xs font-semibold text-flit-primary dark:text-white">
                  Tipo de documento
                </span>
                <div className="flex flex-wrap gap-2">
                  {OPCIONES_TIPO.map(({ valor, etiqueta }) => {
                    const marcado = tipoDocumento === valor;
                    return (
                      <label
                        key={valor}
                        className={`flex cursor-pointer items-center gap-2 rounded-lg border px-3 py-2 text-xs font-semibold text-flit-primary dark:text-white ${
                          marcado ? 'border-flit-brand bg-flit-brand/[0.06]' : 'border-flit-gray dark:border-white/20'
                        }`}
                      >
                        <input
                          type="radio"
                          name={`${idSelector}-tipo`}
                          value={valor}
                          checked={marcado}
                          disabled={creando}
                          onChange={() => setTipoDocumento(valor)}
                          className="h-4 w-4 accent-flit-brand focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-1"
                        />
                        {etiqueta}
                      </label>
                    );
                  })}
                </div>
              </div>
            ) : null}
            <p>{textoConfirmacion}</p>
            <p className="text-xs font-semibold text-flit-primary dark:text-white">
              {textoTramites(contador)}
            </p>
          </>
        )}

        {fase.tipo === 'error' ? <InlineAlert tone="error">{fase.mensaje}</InlineAlert> : null}

        {loteActivo ? (
          <InlineAlert tone="warning">
            <p>{MENSAJE_LOTE_ACTIVO}</p>
            {loteActivo.lote ? (
              <p className="mt-1 font-semibold">
                {`Descarga ${ETIQUETA_ESTADO_LOTE[loteActivo.lote.estado] ?? loteActivo.lote.estado}: ${formatoMiles(
                  loteActivo.lote.procesados,
                )} de ${formatoMiles(loteActivo.lote.total)} trámites procesados.`}
              </p>
            ) : null}
          </InlineAlert>
        ) : null}
      </div>
    </Modal>
  );
}

export interface BotonDescargaMasivaZipProps<TFiltro> {
  seleccion: ModeloSeleccionLote<TFiltro>;
  contador: number;
  /** El lote se creó: el dueño de la selección la limpia (AC2). */
  onCreado: (lote: LoteConsolidados) => void;
  /** 409 `lote_activo`: enganche del seguimiento (#13382). */
  onLoteActivo?: (loteId: string) => void;
  /** HU #13394 — texto de la confirmación (ver el modal). */
  variante?: VarianteDescargaMasiva;
  /** HU #13394 — creación inyectada (ver el modal). */
  crear?: CrearLoteDescargaMasiva<TFiltro>;
  /** HU #13387 — selector de tipo del Super Admin (ver el modal). */
  selectorTipo?: boolean;
}

/**
 * Botón «Descargar ZIP» de la barra de selección (va en su `children`). Sin selección no se pinta;
 * el permiso (AC5) lo resuelve quien monta la barra: sin `consolidado-masivo.download` no hay barra.
 */
export function BotonDescargaMasivaZip<TFiltro>({
  seleccion,
  contador,
  onCreado,
  onLoteActivo,
  variante,
  crear,
  selectorTipo,
}: BotonDescargaMasivaZipProps<TFiltro>) {
  const [abierto, setAbierto] = useState(false);
  const cerrar = useCallback(() => setAbierto(false), []);
  const creado = useCallback(
    (lote: LoteConsolidados) => {
      setAbierto(false);
      onCreado(lote);
    },
    [onCreado],
  );

  if (contador < 1 && !abierto) return null;

  return (
    <>
      <button
        type="button"
        onClick={() => setAbierto(true)}
        className="inline-flex items-center gap-1 rounded-lg border border-flit-brand/40 px-2.5 py-1 font-semibold text-flit-brand transition hover:bg-flit-brand/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand"
      >
        <Download className="h-3.5 w-3.5" aria-hidden="true" /> Descargar ZIP
      </button>
      <DescargaMasivaConfirmModal
        open={abierto}
        onClose={cerrar}
        seleccion={seleccion}
        contador={contador}
        onCreado={creado}
        onLoteActivo={onLoteActivo}
        variante={variante}
        crear={crear}
        selectorTipo={selectorTipo}
      />
    </>
  );
}
