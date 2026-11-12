"use client";

import { useEffect, useState, type ComponentType } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { SearchX } from "lucide-react";
import { Icono } from "@flit/ui/Icono";
import { iconoComo } from "@flit/ui/iconoComo";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { useSesion } from "@/components/DiagnosticoShell";
import { Migas } from "@/components/comun/Migas";
import { BOTON_PRIMARIO_CLS, BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { ApiError } from "@/lib/api";
import { accesoNegado } from "@/lib/carga/api";
import { urlVolverAlListado } from "@/lib/diagnosticos/listado";
import { cerrarPanel, mensajeCierre, type CeldaGrilla, type FilaVehiculo, type Grilla } from "@/lib/tablero/api";
import { hayPendientes, marcarReprocesada } from "@/lib/tablero/fusionarCeldas";
import { useGrillaIncremental } from "@/lib/tablero/useGrillaIncremental";
import { BotonAbrirDictamen, PanelDictamen } from "./PanelDictamen";
import { BotonPdfVehiculo } from "./BotonPdfVehiculo";
import { DescargasLote } from "./DescargasLote";
import { EstadoGlobal, EstimadoVsConsumido } from "./EstadoGlobal";
import { GrillaSemaforos } from "./GrillaSemaforos";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";

/** «Cerrar panel» procede con el lote EN_PROCESO, sin celdas abiertas y con alguna en error (si no, la API responde 409). */
const puedeCerrarPanel = (g: Grilla) =>
  g.estado === "EN_PROCESO" && !hayPendientes(g) && g.vehiculos.some((v) => v.celdas.some((c) => c.estado === "ERROR"));

/** Ícono del estado vacío: del kit (`iconoComo`) o de lucide cuando el kit no trae el glifo (SearchX). */
type IconoVacio = ComponentType<{ className?: string; strokeWidth?: number; "aria-hidden"?: boolean | "true" | "false" }>;
const ICONO_VACIO = iconoComo("portapapeles");

function Vacio({ titulo, children, Icono = ICONO_VACIO }: { titulo: string; children?: React.ReactNode; Icono?: IconoVacio }) {
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
 * error (con Reintentar; 404 como no encontrado), vacío (sin vehículos o sin permiso) y lleno. Arriba, migas «Diagnósticos ›
 * {lote}» y «Volver a diagnósticos» al listado con los filtros que traía (?desde=, solo como search params de /, HU #14087).
 * Cada semáforo abre su detalle en una pestaña nueva.
 */
export function TableroLote({ diagnosticoId, desde = null }: { diagnosticoId: string; desde?: string | null }) {
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
  const [dictamen, setDictamen] = useState<{ vehiculo: FilaVehiculo; disparador: HTMLElement | null } | null>(null);
  const volver = urlVolverAlListado(desde);

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
        <p className="max-w-md text-sm text-muted-foreground">Pídele acceso al administrador de tu compañía.</p>
      </Vacio>
    );
  } else if (cargando || codigo403) {
    contenido = <VehicleLoader label="Cargando el tablero…" />;
  } else if (error instanceof ApiError && error.status === 404) {
    contenido = (
      <Vacio titulo="No encontramos este diagnóstico" Icono={SearchX}>
        <p className="max-w-md text-sm text-muted-foreground">Puede que no exista o que no tengas acceso a él.</p>
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
        <p className="max-w-md text-sm text-muted-foreground">El tablero se llena cuando se confirman los vehículos del lote.</p>
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
            accionEncabezado={(v) => (
              <BotonAbrirDictamen
                identificador={v.placa || v.vin || v.identificador}
                onAbrir={(disparador) => setDictamen({ vehiculo: v, disparador })}
              />
            )}
          />
          <EstimadoVsConsumido estimado={grilla.estimado_consultas} />
        </section>
      </>
    );
  }

  return (
    <>
      <CabeceraModulo
        as="section"
        aria-labelledby="tablero-titulo"
        tituloId="tablero-titulo"
        titulo={grilla?.nombre ?? "Tablero del diagnóstico"}
        descripcion={sesion?.empresa.nombre}
        encima={<Migas migas={[{ texto: "Diagnósticos", href: volver }, { texto: grilla?.nombre ?? "Tablero del diagnóstico" }]} />}
        icono="diagnostico"
        acciones={
          <>
            <Link href={volver} className={BOTON_SECUNDARIO_CLS}>
              <Icono nombre="izquierda" className="h-4 w-4" />
              Volver a diagnósticos
            </Link>
            {grilla && puedeCerrarPanel(grilla) && (
              <button type="button" onClick={() => void cerrar()} disabled={cerrando} aria-busy={cerrando} className={BOTON_SECUNDARIO_CLS}>
                <Icono nombre="portapapeles" className="h-4 w-4" />
                Cerrar panel
              </button>
            )}
          </>
        }
      />
      {contenido}
      {dictamen && (
        <PanelDictamen
          diagnosticoId={diagnosticoId}
          vehiculoId={dictamen.vehiculo.id}
          identificador={dictamen.vehiculo.placa || dictamen.vehiculo.vin || dictamen.vehiculo.identificador}
          modo="compania"
          puedeCorregir={false}
          onClose={() => setDictamen(null)}
          devolverFocoA={dictamen.disparador}
        />
      )}
    </>
  );
}
