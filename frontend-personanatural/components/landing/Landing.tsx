"use client";

import { useState } from "react";
import { portal, type ClientePortal } from "@/lib/api/cliente";
import { TEXTO_POLITICAS } from "@/lib/landing/contenido";
import { AccionesPrincipales } from "./AccionesPrincipales";
import { AvisoModal, type Aviso } from "./AvisoModal";
import { Cabecera } from "./Cabecera";
import { CarruselBanners } from "./CarruselBanners";
import { OrganismosIntegrados } from "./OrganismosIntegrados";
import { PasosTramite, VideoExplicativo } from "./PasosYVideo";
import { Pie } from "./Pie";
import { PreguntasFrecuentes } from "./PreguntasFrecuentes";
import { OtrosServicios, ServiciosAdicionales } from "./Servicios";
import { SomosFlit } from "./SomosFlit";
import { useOrganismos, useTiposTramite } from "./useCatalogos";

/** Landing del portal (diseño de referencia) conectada a los catálogos reales del BFF. */
export function Landing({ cliente = portal }: { cliente?: ClientePortal }) {
  const organismos = useOrganismos(cliente);
  const tipos = useTiposTramite(cliente);
  const [aviso, setAviso] = useState<Aviso | null>(null);
  const proximamente = (titulo: string) => setAviso({ titulo, tipo: "proximamente" });

  return (
    <div className="dashboard-shell bg-background font-sans text-foreground">
      <Cabecera />
      <div className="dashboard-layout dashboard-two-col">
        <main className="dashboard-main">
          {/* El diseño no muestra un h1; este queda solo para lectores de pantalla. */}
          <h1 className="sr-only">Trámites de tránsito 100% en línea</h1>
          <CarruselBanners />
          <AccionesPrincipales tipos={tipos} />
          <PasosTramite />
          <VideoExplicativo />
          <SomosFlit />
        </main>
        <aside className="dashboard-side" aria-label="Servicios y organismos">
          <ServiciosAdicionales onAbrir={proximamente} />
          <OrganismosIntegrados organismos={organismos} />
        </aside>
        <div className="dashboard-fullwidth">
          <OtrosServicios onAbrir={proximamente} />
          <PreguntasFrecuentes />
        </div>
      </div>
      <Pie onAbrirPoliticas={() => setAviso({ titulo: TEXTO_POLITICAS, tipo: "documento" })} />
      <AvisoModal aviso={aviso} onCerrar={() => setAviso(null)} />
    </div>
  );
}
