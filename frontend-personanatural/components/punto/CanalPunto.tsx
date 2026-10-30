"use client";

// HU #14218 / #14222 — Al entrar a /o/{codigo} se abre el canal. Consultar = mismo modal/diseño del portal web.
import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { MobilityLoader } from "@/components/MobilityLoader";
import { AvisoModal, type Aviso } from "@/components/landing/AvisoModal";
import { Pie } from "@/components/landing/Pie";
import { boton } from "@/components/landing/estilos";
import { useTiposTramite } from "@/components/landing/useCatalogos";
import { ConsultarModal } from "@/components/modales/ConsultarModal";
import { ASSETS } from "@/lib/assets";
import { portal, type ClientePortal, type EntradaPunto } from "@/lib/api/cliente";
import { mensajeDeFallo } from "@/lib/api/errores";
import { AccionesPunto } from "./AccionesPunto";

type Fase = "cargando" | "error" | "listo";

export function CanalPunto({ codigo, cliente = portal }: { codigo: string; cliente?: ClientePortal }) {
  const [fase, setFase] = useState<Fase>("cargando");
  const [mensaje, setMensaje] = useState("");
  const [punto, setPunto] = useState<EntradaPunto | null>(null);
  const [consultando, setConsultando] = useState(false);
  const [aviso, setAviso] = useState<Aviso | null>(null);
  const tipos = useTiposTramite(cliente);
  const avisar = useCallback((titulo: string, texto: string) => setAviso({ titulo, tipo: "mensaje", mensaje: texto }), []);

  const entrar = useCallback(async () => {
    setFase("cargando");
    setPunto(null);
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
    <div className="dashboard-shell bg-background font-sans text-foreground">
      <header className="dashboard-header flex min-h-16 flex-wrap py-2 shrink-0 items-center justify-between gap-4 border-b border-border">
        <Link href="/" aria-label="FLIT — Ir al inicio del portal">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img src={ASSETS.logo} alt="FLIT" className="h-9 w-auto" />
        </Link>
        {punto && (
          <p className="max-w-md truncate text-sm font-medium text-foreground/80" title={punto.nombre}>
            Punto de atención · {punto.nombre}
          </p>
        )}
      </header>

      <div className="dashboard-layout">
        <main className="dashboard-main">
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
              <AccionesPunto cliente={cliente} onConsultar={() => setConsultando(true)} onAviso={avisar} />
            </>
          )}
        </main>
      </div>

      <Pie />

      {/* Mismo ConsultarModal del landing (título, campos, captcha, tracking). */}
      <ConsultarModal
        abierto={consultando}
        onCerrar={() => setConsultando(false)}
        cliente={cliente}
        tipos={tipos.data}
      />
      <AvisoModal aviso={aviso} onCerrar={() => setAviso(null)} />
    </div>
  );
}
