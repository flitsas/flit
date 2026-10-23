"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ClipboardCheck, ClipboardList, SearchX } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { useSesion } from "@/components/DiagnosticoShell";
import { BOTON_PRIMARIO_CLS, BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { ApiError } from "@/lib/api";
import { accesoNegado } from "@/lib/carga/api";
import { cerrarPanel, mensajeCierre, type CeldaGrilla, type Grilla } from "@/lib/tablero/api";
import { hayPendientes, marcarReprocesada } from "@/lib/tablero/fusionarCeldas";
import { useGrillaIncremental } from "@/lib/tablero/useGrillaIncremental";
import { BotonPdfVehiculo } from "./BotonPdfVehiculo";
import { DescargasLote } from "./DescargasLote";
import { EstadoGlobal, EstimadoVsConsumido } from "./EstadoGlobal";
import { GrillaSemaforos } from "./GrillaSemaforos";

/** «Cerrar panel» procede con el lote EN_PROCESO, sin celdas abiertas y con alguna en error (si no, la API responde 409). */
const puedeCerrarPanel = (g: Grilla) =>
  g.estado === "EN_PROCESO" && !hayPendientes(g) && g.vehiculos.some((v) => v.celdas.some((c) => c.estado === "ERROR"));

function Vacio({ titulo, children, Icono = ClipboardList }: { titulo: string; children?: React.ReactNode; Icono?: typeof ClipboardList }) {
  return (
    <section aria-labelledby="tablero-vacio" className={`${TARJETA_CLS} flex flex-col items-center gap-3 py-12 text-center`}>
      <Icono className="h-10 w-10 text-flit-brand" aria-hidden="true" strokeWidth={1.5} />
      <h2 id="tablero-vacio" className="text-lg font-semibold">
        {titulo}
      </h2>
      {children}
    </section>
  );
}

/**
 * Tablero del lote (HU #13748, F04-H2) en /diagnosticos/{id}, a donde navegan la confirmación de la carga y el Express:
 * tarjeta título con el nombre del lote, estado global y avance, la grilla de semáforos con sondeo incremental cada 3 s,
 * «Reprocesar» para el admin en las rojas y «Cerrar panel» cuando el lote terminó con errores. Cuatro estados: cargando,
 * error (con Reintentar; 404 como no encontrado), vacío (sin vehículos o sin permiso) y lleno.
 */
export function TableroLote({ diagnosticoId }: { diagnosticoId: string }) {
  const router = useRouter();
  const { sesion } = useSesion();
  const puedeVer = sesion?.capacidades.verDiagnosticos ?? false;
  const puedeReprocesar = sesion?.capacidades.reprocesarConsultas ?? false;
  const puedeCargar = sesion?.capacidades.crearDiagnosticos ?? false;
  // HU #13851: PDF por vehículo, Excel y ZIP del lote; sin la capacidad no hay ningún control (la API vuelve a exigirla).
  const puedeDescargar = sesion?.capacidades.descargarReportes ?? false;
  const { grilla, cargando, error, errorSondeo, sondeando, reintentar, reanudar, refrescar, actualizar } = useGrillaIncremental(diagnosticoId, {
    habilitado: puedeVer,
  });
  const [aviso, setAviso] = useState<string | null>(null);
  const [cerrando, setCerrando] = useState(false);

  const codigo403 = accesoNegado(error);
  useEffect(() => {
    if (codigo403) router.replace(`/403?code=${encodeURIComponent(codigo403)}`);
  }, [codigo403, router]);

  function alReprocesar(celda: CeldaGrilla) {
    setAviso(null);
    actualizar((g) => marcarReprocesada(g, celda.id));
    reanudar();
  }

  function alFallarReproceso(mensaje: string, volverALeer: boolean) {
    setAviso(mensaje);
    if (volverALeer) refrescar();
  }

  async function cerrar() {
    setCerrando(true);
    setAviso(null);
    try {
      const cerrado = await cerrarPanel(diagnosticoId);
      actualizar((g) => ({ ...g, estado: cerrado.estado }));
      // El estimado frente a consumido llega con el lote cerrado: se vuelve a leer una vez.
      refrescar();
    } catch (e) {
      const code = accesoNegado(e);
      if (code) return router.replace(`/403?code=${encodeURIComponent(code)}`);
      setAviso(mensajeCierre(e));
      if (e instanceof ApiError && e.status === 409) refrescar();
    } finally {
      setCerrando(false);
    }
  }

  let contenido;
  if (!puedeVer) {
    contenido = (
      <Vacio titulo="No tienes permiso para ver este diagnóstico">
        <p className="max-w-md text-sm text-[var(--nav-texto)] dark:text-slate-300">Pídele acceso al administrador de tu compañía.</p>
      </Vacio>
    );
  } else if (cargando || codigo403) {
    contenido = <VehicleLoader label="Cargando el tablero…" />;
  } else if (error instanceof ApiError && error.status === 404) {
    contenido = (
      <Vacio titulo="No encontramos este diagnóstico" Icono={SearchX}>
        <p className="max-w-md text-sm text-[var(--nav-texto)] dark:text-slate-300">Puede que no exista o que no tengas acceso a él.</p>
        <Link href="/" className={BOTON_SECUNDARIO_CLS}>
          Volver al inicio
        </Link>
      </Vacio>
    );
  } else if (error || !grilla) {
    contenido = (
      <InlineAlert
        tone="error"
        title="No se pudo cargar el tablero"
        action={
          <button type="button" onClick={reintentar} className={BOTON_REINTENTAR_CLS}>
            Reintentar
          </button>
        }
      >
        {error instanceof Error ? error.message : "Intenta de nuevo en un momento."}
      </InlineAlert>
    );
  } else if (grilla.avance.vehiculos === 0) {
    contenido = (
      <Vacio titulo="Este diagnóstico todavía no tiene vehículos">
        <p className="max-w-md text-sm text-[var(--nav-texto)] dark:text-slate-300">El tablero se llena cuando se confirman los vehículos del lote.</p>
        {grilla.estado === "CREADO" && puedeCargar && (
          <Link href={`/diagnosticos/${encodeURIComponent(diagnosticoId)}/carga`} className={BOTON_PRIMARIO_CLS}>
            Cargar vehículos
          </Link>
        )}
      </Vacio>
    );
  } else {
    contenido = (
      <>
        <section aria-label="Avance del lote" className={TARJETA_CLS}>
          <EstadoGlobal grilla={grilla} sondeando={sondeando} />
        </section>
        <DescargasLote diagnosticoId={diagnosticoId} estadoLote={grilla.estado} puedeDescargar={puedeDescargar} />
        {aviso && (
          <InlineAlert tone="error" title="No se completó la acción">
            {aviso}
          </InlineAlert>
        )}
        {errorSondeo != null && (
          <InlineAlert
            tone="warning"
            title="Se detuvo la actualización automática"
            action={
              <button type="button" onClick={refrescar} className={BOTON_REINTENTAR_CLS}>
                Reintentar
              </button>
            }
          >
            {errorSondeo instanceof Error ? errorSondeo.message : "No se pudo leer el avance del lote."}
          </InlineAlert>
        )}
        <section aria-label="Grilla del lote" className={`${TARJETA_CLS} flex flex-col gap-4`}>
          <GrillaSemaforos
            grilla={grilla}
            puedeReprocesar={puedeReprocesar}
            onReprocesada={alReprocesar}
            onErrorReproceso={alFallarReproceso}
            accionVehiculo={puedeDescargar ? (v) => <BotonPdfVehiculo diagnosticoId={grilla.id} estadoLote={grilla.estado} vehiculo={v} /> : undefined}
          />
          <EstimadoVsConsumido estimado={grilla.estimado_consultas} />
        </section>
      </>
    );
  }

  return (
    <>
      <section aria-labelledby="tablero-titulo" className={`${TARJETA_CLS} flex flex-wrap items-start justify-between gap-3`}>
        <div>
          <h1 id="tablero-titulo" className="text-2xl font-semibold text-flit-primary dark:text-white">
            {grilla?.nombre ?? "Tablero del diagnóstico"}
          </h1>
          {sesion?.empresa.nombre && <p className="mt-1 text-sm font-medium text-[var(--nav-texto)] dark:text-slate-300">{sesion.empresa.nombre}</p>}
        </div>
        {grilla && puedeCerrarPanel(grilla) && (
          <button type="button" onClick={() => void cerrar()} disabled={cerrando} aria-busy={cerrando} className={BOTON_SECUNDARIO_CLS}>
            <ClipboardCheck className="h-4 w-4" aria-hidden="true" />
            Cerrar panel
          </button>
        )}
      </section>
      {contenido}
    </>
  );
}
