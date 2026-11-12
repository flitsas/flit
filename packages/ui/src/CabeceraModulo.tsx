import type { ReactNode } from "react";
import { cn } from "./cn";
import { IconoModulo } from "./Icono";
import { Miga, type MigaItem } from "./Miga";

/**
 * Cabecera de página del Manual UI Kit 2026 (propuesta-2, `.page-header`): tarjeta blanca con borde de 1 px y radio de
 * 9 px, sin degradado ni adornos; el ícono del módulo (disco de 56 px) a la izquierda, la miga de pan, el antetítulo, el
 * título (32 px, azul) y la bajada; las acciones a la derecha. En celular el ícono se oculta y las acciones ocupan el
 * ancho. Es la misma pieza en las 4 apps: la clave del ícono (`icono`, ver MAPA_ICONO) la pasa quien la usa.
 */
export interface CabeceraModuloProps {
  titulo: ReactNode;
  /** Bajada bajo el título. Va en un `div`: algunas pantallas pasan bloques (chip, barra de progreso). */
  descripcion?: ReactNode;
  /** Antetítulo en versalitas sobre el título (p. ej. «Gestión integral»). */
  antetitulo?: ReactNode;
  /** Clave del ícono del módulo en MAPA_ICONO (p. ej. "tramites", "reportes"). Es decorativo. */
  icono?: string;
  /** @deprecated Usa `icono`. Ilustración WebP del diseño anterior; se pinta a 56 px mientras se migra. */
  ilustracion?: string;
  /** Miga de pan con los niveles que la pantalla ya tiene (su «Volver a…» y su título). */
  miga?: MigaItem[];
  /** Lo que va a la derecha, dentro de la cabecera: indicadores y acciones, en ese orden. */
  acciones?: ReactNode;
  /** Contenido sobre el título y el antetítulo, dentro de la columna del texto (p. ej. migas de pan). */
  encima?: ReactNode;
  /** Contenido bajo la descripción, dentro de la columna del texto (p. ej. una nota). */
  debajo?: ReactNode;
  /** Elemento raíz: `header` por defecto; `section` donde la pantalla la etiqueta con `aria-labelledby`. */
  as?: "header" | "section";
  /** `id` del `h1`, para un `aria-labelledby`. */
  tituloId?: string;
  "aria-labelledby"?: string;
  /** Clases extra del título (p. ej. `break-all` para un nombre largo sin espacios). */
  tituloClassName?: string;
  className?: string;
}

export function CabeceraModulo({
  titulo,
  descripcion,
  antetitulo,
  icono,
  ilustracion,
  miga,
  acciones,
  encima,
  debajo,
  as: Raiz = "header",
  tituloId,
  "aria-labelledby": ariaLabelledby,
  tituloClassName,
  className,
}: CabeceraModuloProps) {
  const conIcono = Boolean(icono || ilustracion);
  return (
    <Raiz
      aria-labelledby={ariaLabelledby}
      className={cn(
        "kit-cabecera relative flex shrink-0 flex-wrap items-center gap-4 rounded-[9px] border border-[var(--di-border)] bg-[var(--di-surface-elevated)] py-4 pr-6 shadow-none max-[640px]:p-4",
        conIcono ? "pl-[92px]" : "pl-6",
        className,
      )}
    >
      {icono ? (
        <IconoModulo clave={icono} disco size={56} className="absolute left-5 top-1/2 -translate-y-1/2 max-[640px]:hidden" />
      ) : ilustracion ? (
        <img
          src={ilustracion}
          alt=""
          aria-hidden="true"
          className="absolute left-5 top-1/2 h-14 w-14 -translate-y-1/2 object-contain max-[640px]:hidden"
        />
      ) : null}
      <div className="min-w-0 flex-[1_1_280px]">
        {miga && miga.length > 0 && <Miga items={miga} />}
        {encima && <div className="mb-1">{encima}</div>}
        {antetitulo && (
          <p className="mb-0.5 text-xs font-medium uppercase tracking-[0.04em]" style={{ color: "var(--di-text-tertiary)" }}>
            {antetitulo}
          </p>
        )}
        <h1
          id={tituloId}
          className={cn("text-[2rem] font-bold leading-[1.2] max-[640px]:text-[1.625rem]", tituloClassName)}
          style={{ color: "var(--di-heading)" }}
        >
          {titulo}
        </h1>
        {descripcion && (
          <div className="mt-1 max-w-[70ch] text-sm" style={{ color: "var(--di-text-primary)" }}>
            {descripcion}
          </div>
        )}
        {debajo}
      </div>
      {acciones && <div className="flex flex-wrap items-center gap-3 max-[640px]:w-full max-[640px]:[&>*]:flex-[1_1_140px]">{acciones}</div>}
    </Raiz>
  );
}
