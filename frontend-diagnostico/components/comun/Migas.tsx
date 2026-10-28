import Link from "next/link";
import { FOCO_CLS } from "@/components/configuracion/estilos";

export interface Miga {
  texto: string;
  /** Sin href: es la página actual (la última miga). */
  href?: string;
}

const ENLACE_CLS = `rounded font-medium text-[var(--flit-brand-ink)] underline-offset-4 hover:underline dark:text-white ${FOCO_CLS}`;
const ACTUAL_CLS = "font-semibold text-flit-primary dark:text-white";

/**
 * Migas de pan (HU #14087): «Diagnósticos › {lote} › …». Nav con nombre accesible, lista ordenada y la última miga con
 * aria-current="page". El separador es decorativo (aria-hidden). Mismos tokens de texto de las tarjetas título.
 */
export function Migas({ migas }: { migas: readonly Miga[] }) {
  return (
    <nav aria-label="Migas de pan">
      <ol className="flex flex-wrap items-center gap-1.5 text-sm text-[var(--nav-texto)] dark:text-slate-300">
        {migas.map((m, i) => {
          const ultima = i === migas.length - 1;
          return (
            <li key={`${i}-${m.texto}`} className="flex min-w-0 items-center gap-1.5">
              {ultima || !m.href ? (
                <span aria-current={ultima ? "page" : undefined} className={`break-all ${ACTUAL_CLS}`}>
                  {m.texto}
                </span>
              ) : (
                <Link href={m.href} className={ENLACE_CLS}>
                  {m.texto}
                </Link>
              )}
              {!ultima && <span aria-hidden="true">›</span>}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
