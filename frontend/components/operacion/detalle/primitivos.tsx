'use client';

import type { ReactNode } from 'react';
import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

/**
 * Contrato ÚNICO de toda sección del modal de detalle. Las secciones se montan solo cuando su
 * pestaña está activa, así que montarse ES activarse: cada una pide sus propios datos al montar y
 * no necesita saber nada de las demás.
 */
export interface SeccionDetalleProps {
  instanceId: string;
  /** Tenant de la fila — solo lo lleva el SuperAdmin viendo trámites de otra compañía. */
  tenantId?: string;
  /** Resumen que ya trae la fila: sirve para pintar sin esperar red. */
  item: InstanceSummary;
}

/**
 * Primitivas compartidas por las secciones del modal de detalle de trámite (Frente C).
 *
 * Existen para que las cinco secciones —que se construyeron en paralelo— hablen el MISMO idioma
 * visual: una tarjeta, un par campo/valor, y los tres estados que no son "lleno". Sin esto, cada
 * sección acababa con su propia tarjeta ligeramente distinta, que es la duplicación que la norma
 * FLIT prohíbe expresamente.
 *
 * La composición viene de `DetalleTramiteModal.tsx` de la propuesta (su `CARD` y su `Field`); los
 * valores, de `globals.css` y del token file: la propuesta usa textos de 10/11px y opacidad 0.6,
 * y aquí el piso es 12px (`text-xs`) y 0.7.
 */

const NAVY = '#162744';
const AZUL = '#557EFF';

/** Tarjeta interior de una sección. Radio 18px y borde de marca, como el resto de tarjetas FLIT. */
export function TarjetaDetalle({
  titulo,
  accion,
  children,
  className = '',
}: {
  titulo: string;
  /** Acción opcional a la derecha del título (p. ej. "Descargar paquete"). */
  accion?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section
      className={`rounded-[18px] border bg-white p-4 dark:bg-[#162744] ${className} border-[#DFE5ED] dark:border-white/10`}
      aria-label={titulo}
    >
      <div className="mb-3 flex items-start justify-between gap-3">
        <h4 className="text-sm font-bold" style={{ color: NAVY }}>
          <span className="dark:text-white">{titulo}</span>
        </h4>
        {accion ?? null}
      </div>
      {children}
    </section>
  );
}

/**
 * Par campo/valor dentro de una `<dl>`. Devuelve `<dt>`/`<dd>` sueltos —no una fila cerrada— para
 * que el contenedor pueda alinear todos los valores en la misma columna con una rejilla.
 */
export function CampoValor({ campo, valor }: { campo: string; valor: ReactNode }) {
  return (
    <>
      <dt className="text-xs text-[#162744]/70 dark:text-white/70">{campo}</dt>
      <dd className="text-xs font-medium break-words text-[#162744] dark:text-white">
        {valor === null || valor === undefined || valor === '' ? '—' : valor}
      </dd>
    </>
  );
}

/** Contenedor de `CampoValor`: rejilla de dos columnas, para que los valores queden alineados. */
export function ListaCampos({ children }: { children: ReactNode }) {
  return <dl className="grid grid-cols-[minmax(0,auto)_minmax(0,1fr)] gap-x-3 gap-y-1.5">{children}</dl>;
}

/** Estado "cargando" de una sección: barras de esqueleto con el gris de marca al 50%. */
export function SeccionCargando({ etiqueta, filas = 3 }: { etiqueta: string; filas?: number }) {
  return (
    // `role="status"`: la carga se anuncia, no solo se dibuja. Con `aria-busy` a secas, un lector
    // de pantalla no tiene nada que leer mientras la sección tarda.
    <div className="flex flex-col gap-2" role="status" aria-busy="true" aria-label={etiqueta}>
      {Array.from({ length: filas }).map((_, i) => (
        <div
          key={i}
          className="h-10 animate-pulse rounded-xl dark:bg-white/5"
          style={{ background: 'rgba(223,229,237,0.5)' }}
        />
      ))}
    </div>
  );
}

/**
 * Estado "error" de una sección, con reintento.
 *
 * `contexto` NO es decorativo: en un mismo panel pueden convivir varios bloques que fallan por
 * separado (la cronología, los archivos, la identidad), y tres botones llamados «Reintentar» a
 * secas son indistinguibles para quien navega por lista de botones. Con él, el nombre accesible
 * pasa a ser «Reintentar la trazabilidad», «Reintentar los archivos finales», etc.
 */
export function SeccionError({
  mensaje,
  onReintentar,
  contexto,
}: {
  mensaje: string;
  onReintentar: () => void;
  contexto?: string;
}) {
  return (
    <div className="flex flex-col items-start gap-2" role="alert">
      <p className="text-xs text-[#162744]/70 dark:text-white/70">{mensaje}</p>
      <button
        type="button"
        onClick={onReintentar}
        aria-label={contexto ? `Reintentar ${contexto}` : undefined}
        className="rounded-xl border px-3 py-1.5 text-xs font-semibold transition hover:bg-[#557EFF]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
        style={{ borderColor: AZUL, color: AZUL }}
      >
        Reintentar
      </button>
    </div>
  );
}

/** Estado "vacío" de una sección: se dice qué falta, nunca se deja el hueco mudo. */
export function SeccionVacia({ mensaje }: { mensaje: string }) {
  return <p className="text-xs text-[#162744]/70 dark:text-white/70">{mensaje}</p>;
}
