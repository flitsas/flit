import type { SerieGrafica } from "./graficos-kit";

/** Leyenda del kit (`.leyenda`): puntos de 10 px y texto de 13 px. Es la lectura accesible de las series. */
export function LeyendaGrafica({ series, className = "" }: { series: readonly Pick<SerieGrafica, "etiqueta" | "color">[]; className?: string }) {
  return (
    <ul className={`m-0 flex list-none flex-wrap items-center gap-x-[18px] gap-y-1.5 p-0 text-[0.8125rem] ${className}`}>
      {series.map(({ etiqueta, color }) => (
        <li key={etiqueta} className="flex items-center gap-2">
          <span aria-hidden="true" className="h-2.5 w-2.5 rounded-full" style={{ background: color }} />
          {etiqueta}
        </li>
      ))}
    </ul>
  );
}
