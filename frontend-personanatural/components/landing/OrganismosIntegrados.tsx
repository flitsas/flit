"use client";

import { Search } from "lucide-react";
import { useState } from "react";
import { MobilityLoader } from "@/components/MobilityLoader";
import { mensajeDe } from "@/lib/api/errores";
import { filtrarOrganismos } from "@/lib/landing/catalogos";
import { boton } from "./estilos";
import type { useOrganismos } from "./useCatalogos";

type Organismos = ReturnType<typeof useOrganismos>;

/**
 * Columna «Organismos de tránsito integrados». Cuatro estados: cargando, error (con reintento manual), vacío y lleno.
 * «Ver tarifas» queda deshabilitado: el BFF aún no expone las tarifas por organismo (llegan con la cotización, HU E4).
 */
export function OrganismosIntegrados({ organismos }: { organismos: Organismos }) {
  const [consulta, setConsulta] = useState("");
  const lista = filtrarOrganismos(organismos.data ?? [], consulta);

  return (
    <section id="organismos" className="dashboard-card ot-module" aria-label="Organismos de tránsito integrados" aria-busy={organismos.isPending}>
      <h2 className="dashboard-title">Organismos de tránsito integrados a nuestro ecosistema</h2>
      <div className="relative mt-4">
        <Search aria-hidden className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-primary" />
        <input aria-label="Buscar organismo de tránsito" className="field !pl-10" placeholder="Buscar municipio" autoComplete="off" value={consulta} onChange={(e) => setConsulta(e.target.value)} disabled={!organismos.isSuccess} />
      </div>
      {organismos.isPending && <MobilityLoader />}
      {organismos.isError && (
        <div role="alert" className="mt-4 flex flex-col items-start gap-2 text-sm">
          <p>{mensajeDe(organismos.error)}</p>
          <button type="button" className={boton("flit", "h-11 px-6", "none")} onClick={() => void organismos.refetch()} disabled={organismos.isFetching}>Reintentar</button>
        </div>
      )}
      {organismos.isSuccess && (
        <ul className="ot-list mt-4" aria-live="polite">
          {lista.map((o) => (
            <li key={o.id} className="flex items-center justify-between gap-3 border-b border-border py-2.5 last:border-0">
              <p className="min-w-0 text-sm font-medium">{o.nombre}</p>
              <button type="button" disabled title="Próximamente" aria-label={`Ver tarifas de ${o.nombre} (próximamente)`} className={boton("link", "h-11 shrink-0 p-0 text-[11px]", "none")}>Ver tarifas</button>
            </li>
          ))}
          {lista.length === 0 && <li className="py-2 text-sm">No hay organismos que coincidan.</li>}
        </ul>
      )}
    </section>
  );
}
