"use client";

import Link from "next/link";
import { useMemo, type ReactNode } from "react";
import { boton } from "@/components/landing/estilos";
import { useDefinicion, useTiposTramite } from "@/components/landing/useCatalogos";
import { MobilityLoader } from "@/components/MobilityLoader";
import { Cascaron } from "@/components/tramite/Cascaron";
import type { RegistroRenderers } from "@/components/tramite/ContextoWizard";
import { REGISTRO_RENDERERS } from "@/components/tramite/renderers/registro";
import { ReanudarSesion } from "@/components/tramite/ReanudarSesion";
import { useSolicitud } from "@/components/tramite/useSolicitud";
import { portal, type ClientePortal } from "@/lib/api/cliente";
import { ApiError } from "@/lib/api/errores";
import { fijarContexto, useContextoTramite } from "@/lib/tramite/contexto";
import { organismoElegido } from "@/lib/tramite/diagnostico";
import { armarPasos, SECCIONES_PROPIAS } from "@/lib/tramite/pasos";

interface Props {
  /** Código del tipo de trámite de la ruta `/tramite/[tipo]`. */
  tipoCodigo: string;
  cliente?: ClientePortal;
  registro?: RegistroRenderers;
}

function Pantalla({ titulo, children }: { titulo: string; children: ReactNode }) {
  return (
    <main className="mx-auto flex min-h-dvh max-w-xl flex-col items-center justify-center gap-4 p-6 text-center">
      <h1 className="dashboard-title">{titulo}</h1>
      {children}
    </main>
  );
}

const Cargando = () => (
  <div className="fixed inset-0 z-50 grid place-items-center bg-background/80 backdrop-blur-sm">
    <MobilityLoader />
  </div>
);

function PantallaError({ mensaje, onReintentar, cargando }: { mensaje: string; onReintentar: () => void; cargando: boolean }) {
  return (
    <Pantalla titulo="No pudimos cargar el trámite">
      <div role="alert" className="flex flex-col items-center gap-3 rounded-[9px] border border-alert bg-alert/10 p-4 text-sm text-alert">
        <p>{mensaje}</p>
        <button type="button" className={boton("back", "min-h-11 px-6", "none")} onClick={onReintentar} disabled={cargando}>Reintentar</button>
      </div>
      <Link href="/" className="text-sm font-medium text-primary underline-offset-4 hover:underline">Volver al inicio</Link>
    </Pantalla>
  );
}

function NoDisponible() {
  return (
    <Pantalla titulo="Este trámite aún no está disponible en el portal">
      <p role="alert" className="text-sm">Puedes elegir otro trámite desde el inicio.</p>
      <Link href="/" className={boton("flit", "h-11 px-6", "none")}>Volver al Dashboard</Link>
    </Pantalla>
  );
}

const MENSAJE_NO_CARGO = "No pudimos cargar el trámite. Intenta de nuevo.";
const esSesion = (error: unknown) => error instanceof ApiError && (error.code.startsWith("session_") || error.code === "solicitud_no_encontrada" || error.status === 401 || error.status === 403 || error.status === 404);

/**
 * Página del wizard de un tipo de trámite. Resuelve, en orden: que el tipo exista en el catálogo (sin crear sesión ni borrador
 * si no), que haya una sesión (si no, pide reanudar con radicado y cédula), el borrador (rehidratación) y la definición del
 * tipo con la que se arman los pasos. Solo entonces monta el cascarón.
 */
export function PaginaTramite({ tipoCodigo, cliente = portal, registro = REGISTRO_RENDERERS }: Props) {
  const contexto = useContextoTramite();
  const tipos = useTiposTramite(cliente);
  const sesionDelTipo = contexto && contexto.tipoCodigo === tipoCodigo ? contexto : null;
  const enCatalogo = tipos.data?.some((t) => t.code === tipoCodigo) === true;
  const solicitud = useSolicitud(cliente, enCatalogo ? sesionDelTipo?.solicitudId : undefined);
  const organismoId = organismoElegido(solicitud.data?.organismoId) ? solicitud.data?.organismoId : undefined;
  const definicion = useDefinicion(cliente, tipoCodigo, organismoId, solicitud.data !== undefined);

  const soportados = useMemo(() => new Set([...Object.keys(registro)].filter((k) => !SECCIONES_PROPIAS.includes(k))), [registro]);
  const pasos = useMemo(() => (definicion.data ? armarPasos(definicion.data, soportados) : null), [definicion.data, soportados]);

  if (tipos.isPending) return <Cargando />;
  if (tipos.isError) return <PantallaError mensaje={MENSAJE_NO_CARGO} onReintentar={() => void tipos.refetch()} cargando={tipos.isFetching} />;
  if (!enCatalogo) return <NoDisponible />;

  if (!sesionDelTipo?.solicitudId) return <ReanudarSesion cliente={cliente} tipoCodigo={tipoCodigo} contexto={contexto} motivo="sin_sesion" />;

  if (solicitud.isPending) return <Cargando />;
  if (solicitud.isError) {
    if (esSesion(solicitud.error)) {
      return <ReanudarSesion cliente={cliente} tipoCodigo={tipoCodigo} contexto={contexto} motivo="vencida" />;
    }
    return <PantallaError mensaje={MENSAJE_NO_CARGO} onReintentar={() => void solicitud.refetch()} cargando={solicitud.isFetching} />;
  }

  if (definicion.isPending) return <Cargando />;
  if (definicion.isError) {
    if (definicion.error instanceof ApiError && definicion.error.code === "tipo_no_disponible") return <NoDisponible />;
    return <PantallaError mensaje={MENSAJE_NO_CARGO} onReintentar={() => void definicion.refetch()} cargando={definicion.isFetching} />;
  }
  if (!pasos?.ok) return <NoDisponible />;

  return (
    <Cascaron
      cliente={cliente}
      definicion={definicion.data}
      solicitud={solicitud.data}
      pasos={pasos.pasos}
      registro={registro}
      contexto={sesionDelTipo}
      // Sin solicitud en el contexto, la página vuelve a pedir radicado y cédula.
      onSesionVencida={() => fijarContexto({ ...sesionDelTipo, solicitudId: undefined })}
    />
  );
}
