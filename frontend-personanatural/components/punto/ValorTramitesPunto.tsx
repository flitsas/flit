"use client";

// Columna «Consulta el valor de tu trámite» del punto: los trámites que ofrece el organismo (los que tienen tarifa vigente)
// con un buscador. El BFF no lista tarifas, solo cotiza un tipo en un organismo: por eso cada fila abre «Cotizar» con ese
// trámite ya elegido y el valor sale de la tarifa real, no de un estimado. Cuatro estados: cargando, error, vacío y lleno.
import { Search } from "lucide-react";
import { useState } from "react";
import { MobilityLoader } from "@/components/MobilityLoader";
import { boton } from "@/components/landing/estilos";
import { useTiposTramitePunto } from "@/components/landing/useCatalogos";
import { mensajeDe } from "@/lib/api/errores";
import type { ClientePortal } from "@/lib/api/cliente";

const normalizar = (t: string) => t.normalize("NFD").replace(/[̀-ͯ]/g, "").toLocaleLowerCase("es");

/** Se monta con la sesión del punto ya abierta: la consulta de tipos del punto la exige. Misma consulta que el formulario (se comparte). */
export function ValorTramitesPunto({ cliente, organismoId, onCotizar }: { cliente: ClientePortal; organismoId: string; onCotizar: (tipoCodigo: string) => void }) {
  const tipos = useTiposTramitePunto(cliente, organismoId);
  const [consulta, setConsulta] = useState("");
  const lista = (tipos.data ?? []).filter((t) => normalizar(t.name).includes(normalizar(consulta.trim())));

  return (
    <section id="organismos" className="dashboard-card ot-module" aria-label="Consulta el valor de tu trámite" aria-busy={tipos.isPending}>
      <h2 className="dashboard-title">Consulta el valor de tu trámite</h2>
      <div className="relative mt-4">
        <Search aria-hidden className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-primary" />
        <input
          aria-label="Buscar trámite"
          className="field !pl-10"
          placeholder="Buscar trámite..."
          autoComplete="off"
          value={consulta}
          onChange={(e) => setConsulta(e.target.value)}
          disabled={!tipos.isSuccess}
        />
      </div>
      {tipos.isPending && <MobilityLoader />}
      {tipos.isError && (
        <div role="status" className="mt-4 flex flex-col items-start gap-2 text-sm">
          <p>{mensajeDe(tipos.error)}</p>
          <button type="button" className={boton("flit", "h-11 px-6", "none")} aria-label="Reintentar cargar los trámites" onClick={() => void tipos.refetch()} disabled={tipos.isFetching}>Reintentar</button>
        </div>
      )}
      {tipos.isSuccess && (
        <ul className="ot-list mt-4" aria-live="polite">
          {lista.map((t) => (
            <li key={t.code} className="flex items-center justify-between gap-3 border-b border-border py-2.5 last:border-0">
              <p className="min-w-0 text-sm font-medium">{t.name}</p>
              <button type="button" aria-haspopup="dialog" aria-label={`Ver el valor de ${t.name}`} onClick={() => onCotizar(t.code)} className={boton("link", "h-11 shrink-0 p-0 text-[11px]", "none")}>
                Ver valor
              </button>
            </li>
          ))}
          {lista.length === 0 && (
            <li className="py-2 text-sm">{(tipos.data ?? []).length === 0 ? "Aún no hay valores publicados para este organismo." : "No hay trámites que coincidan."}</li>
          )}
        </ul>
      )}
      <p className="mt-2 text-[11px] text-foreground/70">El valor sale de la tarifa vigente del organismo.</p>
    </section>
  );
}
