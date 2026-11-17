import { useId } from "react";
import { FUENTES } from "@/lib/configuracion";
import type { EstimadoFuente } from "@/lib/carga/api";
import { AYUDA_CLS } from "@/components/configuracion/estilos";

/**
 * Valores del estimado por fuente (HU #13754; rótulo de mínimo, HU #13797). Una fuente con minimo=true (propietario o
 * locatario: una consulta por persona y aún no se sabe cuántas hay) se lee «al menos N» y queda descrita por un texto
 * de ayuda; sin la marca, el número tal cual. El rótulo es texto, no depende del color. Lo usan la carga y el Express.
 */
export function ListaEstimado({ estimado }: { estimado: EstimadoFuente[] }) {
  const ayudaId = useId();
  const hayMinimo = estimado.some((e) => e.minimo);
  return (
    <>
      <dl className="grid grid-cols-2 gap-2 sm:grid-cols-4">
        {estimado.map((e) => (
          <div key={e.fuente} className="flex flex-col rounded-xl border border-[var(--color-flit-gray)] px-3 py-2 dark:border-white/15">
            <dt className={AYUDA_CLS}>{FUENTES.find((f) => f.codigo === e.fuente)?.nombre ?? e.fuente}</dt>
            <dd
              aria-describedby={e.minimo ? ayudaId : undefined}
              className="text-lg font-semibold text-flit-primary dark:text-white"
            >
              {e.minimo && <span className="text-xs font-semibold">al menos </span>}
              {e.consultas}
            </dd>
          </div>
        ))}
      </dl>
      {hayMinimo && (
        <p id={ayudaId} className={AYUDA_CLS}>
          «Al menos» indica que el número es un mínimo: las fuentes de propietario y locatario consultan a cada persona del
          vehículo, y un vehículo con varios propietarios o locatarios suma más consultas.
        </p>
      )}
    </>
  );
}

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
      <ListaEstimado estimado={estimado} />
    </section>
  );
}
