import { FUENTES } from "@/lib/configuracion";
import type { EstimadoFuente } from "@/lib/carga/api";
import { AYUDA_CLS } from "@/components/configuracion/estilos";

/**
 * Estimado de consultas por fuente (HU #13754, F01-C11). La API solo lo manda con estimado_consultas = ANTES_DE_LANZAR;
 * sin la propiedad (NO_MOSTRAR o AL_FINALIZAR) no se pinta nada.
 */
export function EstimadoConsultas({ estimado }: { estimado?: EstimadoFuente[] }) {
  if (!estimado?.length) return null;
  return (
    <section aria-labelledby="estimado-titulo" className="flex flex-col gap-2">
      <h3 id="estimado-titulo" className="text-sm font-semibold text-flit-primary dark:text-white">
        Estimado de consultas
      </h3>
      <p className={AYUDA_CLS}>Consultas previstas por fuente según el último análisis del archivo.</p>
      <dl className="grid grid-cols-2 gap-2 sm:grid-cols-4">
        {estimado.map((e) => (
          <div key={e.fuente} className="flex flex-col rounded-xl border border-[var(--color-flit-gray)] px-3 py-2 dark:border-white/15">
            <dt className={AYUDA_CLS}>{FUENTES.find((f) => f.codigo === e.fuente)?.nombre ?? e.fuente}</dt>
            <dd className="text-lg font-semibold text-flit-primary dark:text-white">{e.consultas}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}
