"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { useSesion } from "@/components/DiagnosticoShell";
import { Migas } from "@/components/comun/Migas";
import { BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { ApiError } from "@/lib/api";
import { accesoNegado } from "@/lib/carga/api";
import { nombreFuente } from "@/lib/configuracion";
import {
  esFuente,
  obtenerDetalleCelda,
  traducirErrorDetalle,
  type CargadorDetalle,
  type DetalleCelda as Detalle,
  type ErrorDetalle,
} from "@/lib/tablero/detalle";
import { VistaDetalleCelda } from "./DetalleCelda";

type Carga = { tipo: "cargando" } | { tipo: "error"; error: ErrorDetalle; causa: unknown } | { tipo: "listo"; detalle: Detalle };

const NO_DISPONIBLE: ErrorDetalle = traducirErrorDetalle(new ApiError(404, "", "NO_ENCONTRADO"));
const SIN_PERMISO_TEXTO = "Pídele acceso al administrador de tu compañía.";

export interface PaginaDetalleConsultaProps {
  loteId: string;
  vehiculoId: string;
  /** Segmento crudo de la ruta: si no es una fuente conocida, «no disponible» sin pedir nada. */
  fuente: string;
  /** Cargador del detalle; por defecto GET de la compañía. El backoffice pasa el suyo con `?empresa=`. */
  cargar?: CargadorDetalle;
  /** Primera miga («Diagnósticos»); por defecto el listado `/`. */
  rutaListado?: string;
  /** Tablero del lote (segunda miga y «Volver al diagnóstico»); por defecto /diagnosticos/{id}. */
  rutaDiagnostico?: string;
  /**
   * Backoffice: la capacidad la exige AccesoBackoffice (no verDiagnosticos) y un 403 va a /403?code=SIN_PERMISO_BACKOFFICE.
   * En la compañía, un 403 de sesión va a /403?code=… como en el tablero.
   */
  backoffice?: boolean;
  /** Nota bajo el título con el detalle ya cargado (solo el backoffice: origen manual). */
  nota?: (detalle: Detalle) => ReactNode;
}

/**
 * Página del detalle de una consulta (HU #14087), que el semáforo de la grilla abre en una pestaña nueva en vez del modal:
 * tarjeta título con migas «Diagnósticos › {nombre_diagnostico} › {fuente} · {identificador}», h1 «{fuente} ·
 * {identificador}» y «Volver al diagnóstico»; debajo, a ancho completo, la «Auditoría de consulta» y la «Respuesta de la
 * fuente» (VistaDetalleCelda). Cuatro estados: cargando (loader de @flit/ui), error (404 o fuente desconocida → no
 * disponible; 403 → página 403 o sin permiso; otro con Reintentar), vacío (lo dice VistaDetalleCelda: sin ejecutar, sin
 * personas o sin campos) y lleno. Sin la capacidad verDiagnosticos de la sesión no se pide nada, como en el tablero.
 */
export function PaginaDetalleConsulta({
  loteId,
  vehiculoId,
  fuente,
  cargar = obtenerDetalleCelda,
  rutaListado = "/",
  rutaDiagnostico = `/diagnosticos/${encodeURIComponent(loteId)}`,
  backoffice = false,
  nota,
}: PaginaDetalleConsultaProps) {
  const router = useRouter();
  const { sesion } = useSesion();
  const puedeVer = backoffice || (sesion?.capacidades.verDiagnosticos ?? false);
  const valida = esFuente(fuente);
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);
  const cargarRef = useRef(cargar);
  useEffect(() => {
    cargarRef.current = cargar;
  }, [cargar]);

  useEffect(() => {
    if (!puedeVer || !valida) return;
    const control = new AbortController();
    cargarRef.current(loteId, vehiculoId, fuente, control.signal).then(
      (detalle) => {
        if (!control.signal.aborted) setCarga({ tipo: "listo", detalle });
      },
      (e: unknown) => {
        if (!control.signal.aborted) setCarga({ tipo: "error", error: traducirErrorDetalle(e), causa: e });
      },
    );
    return () => control.abort();
  }, [loteId, vehiculoId, fuente, valida, puedeVer, intento]);

  const causa = carga.tipo === "error" ? carga.causa : null;
  const codigo403 = backoffice ? (causa instanceof ApiError && causa.status === 403 ? "SIN_PERMISO_BACKOFFICE" : null) : accesoNegado(causa);
  useEffect(() => {
    if (codigo403) router.replace(`/403?code=${encodeURIComponent(codigo403)}`);
  }, [codigo403, router]);

  function reintentar() {
    setCarga({ tipo: "cargando" });
    setIntento((n) => n + 1);
  }

  const nombre = valida ? nombreFuente(fuente) : "Detalle de la consulta";
  const detalle = carga.tipo === "listo" ? carga.detalle : null;
  const titulo = detalle?.identificador ? `${nombre} · ${detalle.identificador}` : nombre;

  let contenido: ReactNode;
  if (!valida) {
    contenido = (
      <InlineAlert tone="warning" title={NO_DISPONIBLE.titulo}>
        {NO_DISPONIBLE.mensaje}
      </InlineAlert>
    );
  } else if (!puedeVer) {
    contenido = (
      <InlineAlert tone="error" title="No tienes permiso para ver este detalle">
        {SIN_PERMISO_TEXTO}
      </InlineAlert>
    );
  } else if (carga.tipo === "cargando" || codigo403) {
    contenido = <VehicleLoader label="Cargando el detalle…" />;
  } else if (carga.tipo === "error") {
    const { error } = carga;
    contenido = (
      <InlineAlert
        tone={error.tipo === "NO_DISPONIBLE" ? "warning" : "error"}
        title={error.titulo}
        action={
          error.tipo === "FALLO" ? (
            <button type="button" onClick={reintentar} className={BOTON_REINTENTAR_CLS}>
              Reintentar
            </button>
          ) : undefined
        }
      >
        {error.mensaje}
      </InlineAlert>
    );
  } else {
    contenido = <VistaDetalleCelda detalle={carga.detalle} />;
  }

  return (
    <>
      <section aria-labelledby="detalle-consulta-titulo" className={`${TARJETA_CLS} flex flex-wrap items-start justify-between gap-3`}>
        <div className="flex min-w-0 flex-col gap-2">
          <Migas
            migas={[
              { texto: "Diagnósticos", href: rutaListado },
              { texto: detalle?.nombre_diagnostico || "Diagnóstico", href: rutaDiagnostico },
              { texto: titulo },
            ]}
          />
          <h1 id="detalle-consulta-titulo" className="break-all text-2xl font-semibold text-flit-primary dark:text-white">
            {titulo}
          </h1>
          {detalle && nota?.(detalle)}
        </div>
        <Link href={rutaDiagnostico} className={BOTON_SECUNDARIO_CLS}>
          <ArrowLeft className="h-4 w-4" aria-hidden="true" />
          Volver al diagnóstico
        </Link>
      </section>
      <section aria-label="Detalle de la consulta" className={TARJETA_CLS}>
        {contenido}
      </section>
    </>
  );
}
