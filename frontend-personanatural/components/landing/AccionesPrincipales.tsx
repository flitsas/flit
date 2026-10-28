"use client";

import { useId, useState } from "react";
import { mensajeDe } from "@/lib/api/errores";
import { ASSETS } from "@/lib/assets";
import { boton } from "./estilos";
import { SelectorTipoTramite } from "./SelectorTipoTramite";
import type { useTiposTramite } from "./useCatalogos";

type Tipos = ReturnType<typeof useTiposTramite>;

function IconoAccion({ src }: { src: string }) {
  return (
    <span aria-hidden className="action-top-icon">
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img src={src} alt="" />
    </span>
  );
}

/**
 * «Iniciar mi trámite», «Consultar mi trámite» y «Cotizar mi trámite». Lo que el ciudadano escribe vive solo en el estado
 * de la pantalla (nunca en almacenamiento del navegador). Los modales de consultar, cotizar e iniciar llegan con las HU
 * E3 a E5: hasta entonces los tres controles están deshabilitados y lo dicen.
 */
export function AccionesPrincipales({ tipos, onConsultar, onCotizar }: { tipos: Tipos; onConsultar?: () => void; onCotizar?: () => void }) {
  const idAviso = useId();
  const [cedula, setCedula] = useState("");
  const [placa, setPlaca] = useState("");
  const [tipo, setTipo] = useState("");
  const opciones = tipos.data ?? [];
  const sinTipos = tipos.isSuccess && opciones.length === 0;

  return (
    <section className="dashboard-actions-stack" aria-label="Acciones principales">
      <div className="dashboard-institutional-card action-start" id="nuevo-tramite">
        <IconoAccion src={ASSETS.acciones.iniciar} />
        <h2 className="dashboard-title">Iniciar mi trámite</h2>
        <p>Da clic aquí para iniciar el proceso de tu trámite digital.</p>
        <form className="action-start-fields" onSubmit={(e) => e.preventDefault()}>
          <label>
            Número de Cédula
            <input required className="field" inputMode="numeric" autoComplete="off" placeholder="Ej. 1020456789" value={cedula} onChange={(e) => setCedula(e.target.value.replace(/\D/g, ""))} />
          </label>
          <label>
            Placa del vehículo
            <input required className="field uppercase" autoComplete="off" placeholder="Ej. ABC123" maxLength={6} value={placa} onChange={(e) => setPlaca(e.target.value.toUpperCase().replace(/[^A-Z0-9]/g, "").slice(0, 6))} />
          </label>
          <div className="dashboard-label" aria-busy={tipos.isPending}>
            Tipo de Trámite
            <SelectorTipoTramite
              opciones={opciones}
              valor={tipo}
              onChange={setTipo}
              disabled={!tipos.isSuccess || sinTipos}
              marcador={tipos.isPending ? "Cargando..." : "Selecciona"}
            />
            <div aria-live="polite" className="text-xs font-normal">
              {tipos.isError && (
                <p role="alert">
                  {mensajeDe(tipos.error)}{" "}
                  <button type="button" className={boton("link", "h-auto p-0 text-xs", "none")} onClick={() => void tipos.refetch()}>Reintentar</button>
                </p>
              )}
              {sinTipos && <p>Por ahora no hay trámites disponibles en el portal.</p>}
            </div>
          </div>
          <button type="submit" className={boton("institutional", "dashboard-submit", "none")} disabled aria-describedby={idAviso}>Iniciar trámite</button>
          <p id={idAviso} className="text-center text-xs">El inicio del trámite estará disponible próximamente.</p>
        </form>
      </div>
      <div className="action-rows">
        <AccionFila etiqueta="Consultar mi trámite" src={ASSETS.acciones.consultar} onClick={onConsultar} />
        <AccionFila etiqueta="Cotizar mi trámite" src={ASSETS.acciones.cotizar} onClick={onCotizar} />
      </div>
    </section>
  );
}

/** Fila clicable del diseño. Sin acción conectada queda deshabilitada y lo dice. */
function AccionFila({ etiqueta, src, onClick }: { etiqueta: string; src: string; onClick?: () => void }) {
  if (onClick) {
    return (
      <button type="button" aria-label={etiqueta} aria-haspopup="dialog" onClick={onClick} className="dashboard-institutional-card action-row action-clickable">
        <IconoAccion src={src} />
        <span className="dashboard-title">{etiqueta}</span>
      </button>
    );
  }
  return (
    <button type="button" disabled aria-label={`${etiqueta} (próximamente)`} className="dashboard-institutional-card action-row action-clickable disabled:cursor-not-allowed disabled:opacity-70">
      <IconoAccion src={src} />
      <span className="dashboard-title">{etiqueta}</span>
      <span className="text-xs font-normal">Próximamente</span>
    </button>
  );
}
