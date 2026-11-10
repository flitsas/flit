import { rutaIlustracion, type Ilustracion } from "@flit/ui/ilustraciones";

/**
 * Cabecera de módulo de la propuesta «Diseño Inteligente» (`.page-header` de propuesta.css), con el mismo aspecto que
 * `ModuleTitle` de Trámites (HU #14486 Comparendos, HU #14487 Diagnóstico): tarjeta con degradado blanco → cielo, la ilustración del módulo a la
 * izquierda, el título con su línea de acento y los dos círculos decorativos a la derecha.
 *
 * Se aplica sin rehacer las cabeceras que ya existen: la `section` toma {@link CABECERA_CLS}, el `h1`
 * {@link TITULO_CABECERA_CLS} y {@link DecoracionCabecera} va como primer hijo. Todo hijo que no sea decoración queda por
 * encima de los círculos.
 */
export const CABECERA_CLS =
  "relative overflow-clip rounded-[var(--di-radius-header)] border border-[color:var(--di-border)] " +
  "bg-[linear-gradient(120deg,var(--di-surface-elevated)_35%,var(--di-sky)_100%)] py-8 pl-[132px] pr-8 " +
  "shadow-[var(--shadow-card)] max-md:rounded-[20px] max-md:py-5 max-md:pl-[84px] max-md:pr-4 " +
  "[&>*:not([data-decor])]:relative [&>*:not([data-decor])]:z-[1]";

/** Título de la cabecera, con la línea de acento debajo (`.page-header h1::after`). */
export const TITULO_CABECERA_CLS =
  "text-[2rem] font-bold leading-tight text-[color:var(--di-heading)] max-md:text-2xl " +
  "after:mt-2.5 after:block after:h-[5px] after:w-16 after:rounded-full " +
  "after:bg-[linear-gradient(90deg,var(--di-sky-line),var(--di-sky))] after:content-['']";

export function DecoracionCabecera({ ilustracion }: { ilustracion: Ilustracion }) {
  return (
    <>
      <span
        data-decor=""
        aria-hidden="true"
        className="pointer-events-none absolute -right-[60px] -top-[110px] h-[230px] w-[230px] rounded-full bg-[color:var(--di-highlight-decor)] opacity-90 max-md:-right-12 max-md:-top-[60px] max-md:h-[120px] max-md:w-[120px]"
      />
      <span
        data-decor=""
        aria-hidden="true"
        className="pointer-events-none absolute -right-[150px] -top-[60px] h-[420px] w-[420px] rounded-full border-[1.5px] border-[color:var(--di-sky-line)] opacity-50 max-md:hidden"
      />
      {/* eslint-disable-next-line @next/next/no-img-element -- ilustración decorativa estática, igual que ModuleTitle */}
      <img
        data-decor=""
        src={rutaIlustracion(ilustracion)}
        alt=""
        aria-hidden="true"
        className="absolute left-6 top-1/2 h-[92px] w-[92px] -translate-y-1/2 object-contain max-md:left-3.5 max-md:top-5 max-md:h-[58px] max-md:w-[58px] max-md:translate-y-0"
      />
    </>
  );
}
