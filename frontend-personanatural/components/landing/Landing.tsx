"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ConsultarModal } from "@/components/modales/ConsultarModal";
import { CotizarModal } from "@/components/modales/CotizarModal";
import { IniciarModal, type DatosInicio } from "@/components/modales/IniciarModal";
import { portal, type ClientePortal, type Organismo } from "@/lib/api/cliente";
import { URL_POLITICA_FLIT } from "@/lib/legal";
import { fijarContexto } from "@/lib/tramite/contexto";
import { rutaDelTramite } from "@/lib/tramite/ruta";
import { AccionesPrincipales } from "./AccionesPrincipales";
import { AvisoModal, type Aviso } from "./AvisoModal";
import { DrFlit } from "@/components/chat/DrFlit";
import { Cabecera } from "./Cabecera";
import { CarruselBanners } from "./CarruselBanners";
import { OrganismosIntegrados } from "./OrganismosIntegrados";
import { PasosTramite, VideoExplicativo } from "./PasosYVideo";
import { Pie } from "./Pie";
import { PreguntasFrecuentes } from "./PreguntasFrecuentes";
import { OtrosServicios, ServiciosAdicionales } from "./Servicios";
import { SomosFlit } from "./SomosFlit";
import { useOrganismos, useTiposTramite } from "./useCatalogos";
import { useConfiguracion } from "./useConfiguracion";

export { rutaDelTramite };

/** Landing del portal (diseño de referencia) conectada a los catálogos reales del BFF. */
export function Landing({ cliente = portal }: { cliente?: ClientePortal }) {
  const router = useRouter();
  const organismos = useOrganismos(cliente);
  const tipos = useTiposTramite(cliente);
  // Contenido publicado (preguntas, banners, enlaces, soporte): si no hay nada o falla, rige el contenido fijo de siempre.
  const { contenido } = useConfiguracion(cliente);
  const [aviso, setAviso] = useState<Aviso | null>(null);
  const [consultando, setConsultando] = useState(false);
  // `null` = cerrado; con valor = abierto (y, si viene de «Ver tarifas», con ese organismo elegido).
  const [cotizando, setCotizando] = useState<{ organismo: Organismo | null } | null>(null);
  // Datos de la tarjeta «Iniciar mi trámite» mientras el modal de requisitos está abierto. Solo en memoria.
  const [iniciando, setIniciando] = useState<DatosInicio | null>(null);
  const proximamente = (titulo: string) => setAviso({ titulo, tipo: "proximamente" });
  const urlPolitica = contenido.enlaces.POLITICA_DATOS ?? URL_POLITICA_FLIT;

  return (
    <div className="dashboard-shell bg-background font-sans text-foreground">
      <Cabecera />
      <div className="dashboard-layout dashboard-two-col">
        <main className="dashboard-main">
          {/* El diseño no muestra un h1; este queda solo para lectores de pantalla. */}
          <h1 className="sr-only">Trámites de tránsito 100% en línea</h1>
          <CarruselBanners banners={contenido.banners} />
          <AccionesPrincipales
            tipos={tipos}
            cliente={cliente}
            onIniciar={setIniciando}
            onConsultar={() => setConsultando(true)}
            onCotizar={() => setCotizando({ organismo: null })}
          />
          <PasosTramite />
          <VideoExplicativo url={contenido.enlaces.VIDEO ?? null} />
          <SomosFlit />
        </main>
        <aside className="dashboard-side" aria-label="Servicios y organismos">
          <ServiciosAdicionales onAbrir={proximamente} />
          <OrganismosIntegrados organismos={organismos} onVerTarifas={(organismo) => setCotizando({ organismo })} />
        </aside>
        <div className="dashboard-fullwidth">
          <OtrosServicios onAbrir={proximamente} />
          <PreguntasFrecuentes preguntas={contenido.preguntas ?? undefined} />
        </div>
      </div>
      {/* Asistente simulado (S4): sin servicio externo; solo lee los nombres de los catálogos reales para sus respuestas. */}
      <DrFlit contexto={{ tipos: tipos.data?.map((t) => t.name), organismos: organismos.data?.map((o) => o.nombre), soporte: contenido.soporte }} />
      <Pie enlaces={contenido.enlaces} soporte={contenido.soporte} />
      <ConsultarModal
        abierto={consultando}
        onCerrar={() => setConsultando(false)}
        cliente={cliente}
        tipos={tipos.data}
      />
      <CotizarModal abierto={cotizando !== null} onCerrar={() => setCotizando(null)} cliente={cliente} organismoInicial={cotizando?.organismo ?? null} />
      <IniciarModal
        abierto={iniciando !== null}
        onCerrar={() => setIniciando(null)}
        cliente={cliente}
        datos={iniciando}
        urlPolitica={urlPolitica}
        onSesion={(sesion, datos) => {
          fijarContexto({ intencion: "iniciar", tipoCodigo: sesion.tipoTramiteCodigo, solicitudId: sesion.solicitudId, radicado: sesion.codigoPublico, cedula: datos.cedula, placa: datos.placa, vin: datos.vin });
          router.push(rutaDelTramite(sesion.tipoTramiteCodigo));
        }}
      />
      <AvisoModal aviso={aviso} onCerrar={() => setAviso(null)} />
    </div>
  );
}
