"use client";

import { useId, useState } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@flit/ui/Button";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { Icono } from "@flit/ui/Icono";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { PasosHorizontal, type EstadoPaso } from "@flit/ui/PasosHorizontal";
import { ZonaArrastre } from "@flit/ui/ZonaArrastre";
import { AYUDA_CLS, BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS, TARJETA_CLS } from "@/components/comun/estilos";
import { ResumenAnalisis } from "@/components/liquidaciones/ResumenAnalisis";
import { ApiError } from "@/lib/api";
import { hrefConEmpresa, useEmpresaSeleccionada } from "@/lib/empresaSeleccionada";
import { mensajeErrorFijo } from "@/lib/errores";
import { analizarCarga, crearMasiva, descargarPlantilla } from "@/lib/liquidaciones/api";
import { ERRORES_LIQUIDACIONES, PANTALLA } from "@/lib/liquidaciones/estados";
import type { AnalisisCarga } from "@/lib/liquidaciones/tipos";

const MAX_BYTES = 5 * 1024 * 1024;
/** Códigos con literal propio del contrato; el resto cae en el texto genérico con Reintentar. */
const CODIGOS_CON_LITERAL = new Set([
  "FORMATO_NO_SOPORTADO",
  "ARCHIVO_DEMASIADO_GRANDE",
  "COLUMNA_PLACA_AUSENTE",
  "ARCHIVO_SIN_PLACAS",
  "LIMITE_SUPERADO",
  "APROVISIONAMIENTO_NO_LISTO",
  "PRODUCTO_SUSPENDIDO",
]);

type Fallo = { tipo: "regla"; texto: string } | { tipo: "otro"; al: "analizar" | "crear" } | null;

/**
 * Carga masiva (spec-visual §3.4, F3-CF1/CF2). Paso 1: archivo (.xlsx o .csv, máximo 5 MB) y plantilla; el análisis no
 * consulta el RUNT ni el proveedor. Paso 2: resumen (ResumenAnalisis). Paso 3: «Iniciar liquidación de N placas» crea el
 * lote con el mismo archivo y navega a su detalle. Con `superaTope` el CTA queda deshabilitado con el literal
 * LIMITE_SUPERADO asociado y no se llama a la API. Errores con los literales del contrato (413, 415, 422, 409); otro,
 * «No se pudo analizar el archivo» + Reintentar sobre el mismo archivo.
 */
export function CargaMasiva({ bloqueo }: { bloqueo: string | null }) {
  const router = useRouter();
  const { empresa } = useEmpresaSeleccionada();
  const base = useId();
  const idMotivo = `${base}-motivo`;
  const idTope = `${base}-tope`;
  const [archivo, setArchivo] = useState<File | null>(null);
  const [analisis, setAnalisis] = useState<AnalisisCarga | null>(null);
  const [ocupado, setOcupado] = useState<"analizando" | "creando" | null>(null);
  const [fallo, setFallo] = useState<Fallo>(null);
  const [errorPlantilla, setErrorPlantilla] = useState<string | null>(null);

  const traducir = (e: unknown, al: "analizar" | "crear"): Fallo =>
    e instanceof ApiError && e.code && CODIGOS_CON_LITERAL.has(e.code)
      ? { tipo: "regla", texto: mensajeErrorFijo(e, "", ERRORES_LIQUIDACIONES) }
      : { tipo: "otro", al };

  async function analizar() {
    if (!archivo || bloqueo) return;
    setOcupado("analizando");
    setFallo(null);
    try {
      setAnalisis(await analizarCarga(archivo, { empresa }));
    } catch (e) {
      setFallo(traducir(e, "analizar"));
    } finally {
      setOcupado(null);
    }
  }

  async function crear() {
    if (!archivo || !analisis || analisis.superaTope || bloqueo) return;
    setOcupado("creando");
    setFallo(null);
    try {
      const lote = await crearMasiva(archivo, { empresa });
      router.push(hrefConEmpresa(`/liquidaciones/${encodeURIComponent(lote.id)}`, empresa));
    } catch (e) {
      setOcupado(null);
      setFallo(traducir(e, "crear"));
    }
  }

  function otroArchivo() {
    setArchivo(null);
    setAnalisis(null);
    setFallo(null);
  }

  const paso: 1 | 2 = analisis ? 2 : 1;
  const estadoPaso = (n: number): EstadoPaso => (n < paso ? "hecho" : n === paso ? "actual" : "pendiente");
  const describeCrear = [analisis?.superaTope ? idTope : null, bloqueo ? idMotivo : null].filter(Boolean).join(" ") || undefined;

  return (
    <div className="flex flex-col gap-4">
      <CabeceraModulo
        titulo="Liquidación masiva"
        icono="pesos"
        miga={[{ etiqueta: "Liquidaciones", href: hrefConEmpresa("/liquidaciones", empresa) }, { etiqueta: "Liquidación masiva" }]}
        encima={
          <PasosHorizontal
            ariaLabel="Pasos de la carga"
            pasos={[
              { etiqueta: "Subir archivo", estado: estadoPaso(1) },
              { etiqueta: "Revisar", estado: estadoPaso(2) },
              { etiqueta: "Iniciar", estado: estadoPaso(3) },
            ]}
          />
        }
      />
      {bloqueo ? (
        <p id={idMotivo} className={AYUDA_CLS}>
          {bloqueo}
        </p>
      ) : null}
      <section aria-label="Archivo de placas" className={`${TARJETA_CLS} flex flex-col gap-4`}>
        {ocupado === "analizando" ? (
          <VehicleLoader label="Analizando el archivo…" />
        ) : analisis ? (
          <>
            <ResumenAnalisis analisis={analisis} idAvisoTope={idTope} />
            <div className="flex flex-wrap gap-2">
              <Button
                cargando={ocupado === "creando"}
                disabled={analisis.superaTope || Boolean(bloqueo) || analisis.validas === 0}
                aria-describedby={describeCrear}
                onClick={() => void crear()}
              >
                {`Iniciar liquidación de ${analisis.validas} placas`}
              </Button>
              <Button variant="secondary" onClick={otroArchivo} disabled={ocupado === "creando"}>
                Subir otro archivo
              </Button>
            </div>
            {ocupado === "creando" ? <p className={AYUDA_CLS}>Creando el lote…</p> : null}
          </>
        ) : (
          <>
            <ZonaArrastre
              etiqueta="Archivo de placas (.xlsx o .csv)"
              aceptar={[".xlsx", ".csv"]}
              maxBytes={MAX_BYTES}
              archivo={archivo}
              onArchivo={(f) => {
                setArchivo(f);
                setFallo(null);
              }}
              ayuda="Una columna Placa. Máximo 5 MB."
            />
            <p className={AYUDA_CLS}>El análisis no consulta el RUNT ni el proveedor: solo revisa el archivo.</p>
            <div className="flex flex-wrap gap-2">
              <Button disabled={!archivo || Boolean(bloqueo)} aria-describedby={bloqueo ? idMotivo : undefined} onClick={() => void analizar()}>
                Analizar archivo
              </Button>
              <button
                type="button"
                className={BOTON_SECUNDARIO_CLS}
                onClick={() => {
                  setErrorPlantilla(null);
                  descargarPlantilla(empresa).catch((e: unknown) => setErrorPlantilla(mensajeErrorFijo(e, "No se descargó la plantilla. Intenta de nuevo.", ERRORES_LIQUIDACIONES)));
                }}
              >
                <Icono nombre="nube-bajar" size={16} />
                {PANTALLA.accionDescargarPlantilla}
              </button>
            </div>
            {errorPlantilla ? (
              <p role="alert" className="text-xs font-medium text-[var(--badge-danger-fg)]">
                {errorPlantilla}
              </p>
            ) : null}
          </>
        )}
        {fallo?.tipo === "regla" ? (
          <InlineAlert tone="warning" title="Revisa el archivo">
            {fallo.texto}
          </InlineAlert>
        ) : null}
        {fallo?.tipo === "otro" ? (
          <InlineAlert
            tone="error"
            title={fallo.al === "analizar" ? "No se pudo analizar el archivo" : "No se pudo crear el lote"}
            action={
              <button type="button" onClick={() => void (fallo.al === "analizar" ? analizar() : crear())} className={BOTON_REINTENTAR_CLS}>
                Reintentar
              </button>
            }
          >
            El archivo sigue elegido. Intenta de nuevo.
          </InlineAlert>
        ) : null}
      </section>
    </div>
  );
}
