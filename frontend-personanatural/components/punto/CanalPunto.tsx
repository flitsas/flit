"use client";

// HU #14218 / #14222 — Al entrar a /o/{codigo} se abre el canal. Consultar = mismo modal/diseño del portal web.
// La pantalla de entrada replica la de la web de persona (cabecera, banner, acciones, pasos, video y preguntas
// frecuentes, Cotizar y el asistente Dr. FLIT) con el organismo fijo y la marca blanca. En lugar de la lista de organismos
// va «Consulta el valor de tu trámite» con los trámites del OT. Los banners son los publicados; mientras el OT no tenga
// los suyos, rige el banner de su marca. En la columna lateral, el carrusel de imágenes del OT (si lo configuró) reemplaza
// a «Servicios Adicionales». «Somos», servicios y pie llevan el nombre del organismo.
// Tras arranque OK el landing se oculta y el cascarón del wizard (DiagnosticoPunto) ocupa la página.
import { useCallback, useEffect, useState } from "react";
import { MobilityLoader } from "@/components/MobilityLoader";
import { AvisoModal, type Aviso, type TonoAviso } from "@/components/landing/AvisoModal";
import { DrFlit } from "@/components/chat/DrFlit";
import { Cabecera } from "@/components/landing/Cabecera";
import { CarruselBanners } from "@/components/landing/CarruselBanners";
import { PasosTramite, VideoExplicativo } from "@/components/landing/PasosYVideo";
import { Pie } from "@/components/landing/Pie";
import { PreguntasFrecuentes } from "@/components/landing/PreguntasFrecuentes";
import { OtrosServicios, ServiciosAdicionales } from "@/components/landing/Servicios";
import { SomosFlit } from "@/components/landing/SomosFlit";
import { boton } from "@/components/landing/estilos";
import { useTiposTramite } from "@/components/landing/useCatalogos";
import { useConfiguracion } from "@/components/landing/useConfiguracion";
import { ConsultarModal } from "@/components/modales/ConsultarModal";
import { CotizarModal } from "@/components/modales/CotizarModal";
import { useMarca } from "@/components/marca/MarcaPunto";
import { portal, type ClientePortal, type EntradaPunto } from "@/lib/api/cliente";
import { mensajeDeFallo } from "@/lib/api/errores";
import { AccionesPunto } from "./AccionesPunto";
import { CarruselServiciosPunto } from "./CarruselServiciosPunto";
import { ValorTramitesPunto } from "./ValorTramitesPunto";

type Fase = "cargando" | "error" | "listo";

export function CanalPunto({ codigo, cliente = portal }: { codigo: string; cliente?: ClientePortal }) {
  const [fase, setFase] = useState<Fase>("cargando");
  const [mensaje, setMensaje] = useState("");
  const [punto, setPunto] = useState<EntradaPunto | null>(null);
  const [consultando, setConsultando] = useState(false);
  // `null` = cerrado; con valor = abierto y, si viene de «Ver valor», con ese trámite elegido ("" = ninguno).
  const [cotizando, setCotizando] = useState<{ tipo: string } | null>(null);
  const marca = useMarca();
  // Con marca publicada firma la marca; si no, el nombre del organismo del punto.
  const nombreOrganismo = marca?.nombre ?? punto?.nombre ?? "FLIT";
  const [aviso, setAviso] = useState<Aviso | null>(null);
  const [enWizard, setEnWizard] = useState(false);
  const tipos = useTiposTramite(cliente);
  // Contenido publicado (preguntas, video, enlaces, soporte): si no hay nada o falla, rige el contenido fijo de siempre.
  const { contenido } = useConfiguracion(cliente);
  // Con banners de la marca publicados son los del organismo (cada uno con su enlace opcional); si solo hay el banner único
  // (marca anterior) es ese, sin enlace; si no, los banners publicados o los del diseño.
  const banners = marca?.banners?.length
    ? marca.banners.map((b, i) => ({ id: `marca-${i}`, src: b.imagenUrl, alt: b.alt, enlace: b.enlace }))
    : marca?.bannerUrl
      ? [{ id: "marca", src: marca.bannerUrl, alt: marca.nombre ? `Banner de ${marca.nombre}` : "Banner del organismo", enlace: null }]
      : contenido.banners;
  const proximamente = useCallback((titulo: string) => setAviso({ titulo, tipo: "proximamente" }), []);
  const avisar = useCallback(
    (titulo: string, texto: string, tono: TonoAviso = "alerta") => setAviso({ titulo, tipo: "mensaje", mensaje: texto, tono }),
    [],
  );
  const alWizard = useCallback((activo: boolean) => setEnWizard(activo), []);

  const entrar = useCallback(async () => {
    setFase("cargando");
    setPunto(null);
    setEnWizard(false);
    try {
      const datos = await cliente.punto.entrada(codigo);
      setPunto(datos);
      setFase("listo");
    } catch (err) {
      setMensaje(mensajeDeFallo(err));
      setFase("error");
    }
  }, [cliente, codigo]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga al entrar: el estado de espera es intencional
    void entrar();
  }, [entrar]);

  return (
    <div className={enWizard ? "bg-background font-sans text-foreground" : "dashboard-shell bg-background font-sans text-foreground"}>
      {!enWizard && <Cabecera />}

      <div className={enWizard ? undefined : "dashboard-layout dashboard-two-col"}>
        <main className={enWizard ? undefined : "dashboard-main"}>
          {!enWizard && <CarruselBanners banners={banners} />}
          {fase === "cargando" && (
            <div className="fixed inset-0 z-50 grid place-items-center bg-background/80 backdrop-blur-sm">
              <MobilityLoader />
            </div>
          )}
          {fase === "error" && (
            <div role="alert" className="rounded-[9px] border border-alert bg-alert/10 p-4 text-sm text-alert">
              <p>{mensaje}</p>
              <button type="button" className={`${boton("flit", "mt-3 h-11 px-6", "none")}`} onClick={() => void entrar()}>
                Reintentar
              </button>
            </div>
          )}
          {fase === "listo" && punto && (
            <>
              <h1 className="sr-only">{punto.nombre}</h1>
              <AccionesPunto
                cliente={cliente}
                organismoId={punto.organismoId}
                organismoNombre={punto.nombre}
                onConsultar={() => setConsultando(true)}
                onCotizar={() => setCotizando({ tipo: "" })}
                onAviso={avisar}
                onWizard={alWizard}
              />
              {!enWizard && (
                <>
                  <PasosTramite />
                  <VideoExplicativo url={contenido.enlaces.VIDEO ?? null} />
                  <SomosFlit nombre={nombreOrganismo} />
                </>
              )}
            </>
          )}
        </main>
        {!enWizard && fase === "listo" && punto && (
          <aside className="dashboard-side" aria-label="Servicios y valor de los trámites">
            {marca?.carrusel?.length ? <CarruselServiciosPunto items={marca.carrusel} /> : <ServiciosAdicionales onAbrir={proximamente} />}
            <ValorTramitesPunto cliente={cliente} organismoId={punto.organismoId} onCotizar={(tipo) => setCotizando({ tipo })} />
          </aside>
        )}
        {!enWizard && fase === "listo" && (
          <div className="dashboard-fullwidth">
            <OtrosServicios onAbrir={proximamente} />
            <PreguntasFrecuentes preguntas={contenido.preguntas ?? undefined} />
          </div>
        )}
      </div>

      {!enWizard && <Pie enlaces={contenido.enlaces} soporte={contenido.soporte} nombre={nombreOrganismo} />}

      {/* Mismo ConsultarModal del landing (título, campos, captcha, tracking). */}
      <ConsultarModal
        abierto={consultando}
        onCerrar={() => setConsultando(false)}
        cliente={cliente}
        tipos={tipos.data}
      />
      {punto && (
        <CotizarModal
          abierto={cotizando !== null}
          onCerrar={() => setCotizando(null)}
          tipoInicial={cotizando?.tipo ?? ""}
          cliente={cliente}
          organismoInicial={{ id: punto.organismoId, nombre: punto.nombre }}
          organismoFijo
        />
      )}
      {!enWizard && fase === "listo" && punto && (
        <DrFlit contexto={{ tipos: tipos.data?.map((t) => t.name), organismos: [punto.nombre], soporte: contenido.soporte }} />
      )}
      <AvisoModal aviso={aviso} onCerrar={() => setAviso(null)} />
    </div>
  );
}
