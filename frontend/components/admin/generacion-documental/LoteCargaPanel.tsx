"use client";

import { useCallback, useMemo, useState } from "react";
import { Download, FileSpreadsheet, Upload } from "lucide-react";
import { useRouter } from "next/navigation";
import {
  createStandaloneBatch,
  downloadStandaloneBatchTemplate,
} from "@/lib/api/admin-generacion-documental";
import { ApiError } from "@/lib/api/types";
import { GENERACION_DOCUMENTAL_BASE_PATH } from "./generacion-documental-nav";

/** Tope de filas de datos del contrato v1 (CF-11). El backend lo impone; aquí solo se anuncia. */
const MAX_FILAS = 100;

/**
 * Tope de tamaño del lado del cliente. **No es una regla del backend**: un lote completo pesa unos
 * pocos kilobytes, así que un archivo de más de 10 MB es casi siempre otro archivo. Se corta aquí
 * para no subir 300 MB y esperar a que el servidor los rechace.
 */
const MAX_BYTES = 10 * 1024 * 1024;

const EXTENSION = ".xlsx";

/**
 * Traducción de los rechazos del ARCHIVO COMPLETO (CF-11). Cada uno dice qué pasó y qué hacer:
 * un código crudo en pantalla —`template_invalid`— deja al usuario exactamente donde estaba.
 */
function mensajeDeRechazo(codigo: unknown): string | null {
  switch (codigo) {
    case "too_many_rows":
      return `El archivo supera las ${MAX_FILAS} filas permitidas. No se procesó ninguna: divide los datos en varios lotes y súbelos por separado.`;
    case "template_invalid":
      return "El encabezado no es el de la plantilla v1. Descarga la plantilla de arriba y copia tus datos dentro de ella, sin tocar la fila 1 ni cambiar el orden de las columnas.";
    case "invalid_file":
      return "El archivo no es un XLSX válido. Ábrelo en Excel y usa «Guardar como» en formato .xlsx: un .xls o un .csv renombrados no sirven.";
    case "invalid_request":
      return "No recibimos el archivo. Vuelve a seleccionarlo e inténtalo de nuevo.";
    default:
      return null;
  }
}

/**
 * Pantalla de carga masiva del módulo (HU #12224, CF-11/CF-12/CF-16).
 *
 * <p><b>Por qué existe.</b> El backend de lotes estaba completo —plantilla, parser, worker,
 * idempotencia, ZIP— y era inalcanzable: no había ni un `input type="file"` en toda la aplicación,
 * y la vista de seguimiento solo se abría tecleando a mano un identificador de lote que nada
 * generaba. La descomposición pidió la API en una HU `[BACKEND]` y el seguimiento en otra, y la
 * pantalla de carga no le tocó a ninguna.</p>
 *
 * <p><b>La plantilla se descarga del servidor, no de `public/`.</b> La genera el mismo contrato de
 * columnas que después valida la carga, así que no puede entregarse una plantilla que luego se
 * rechace a sí misma. Un .xlsx estático se quedaría viejo en silencio.</p>
 *
 * <p><b>La clave de idempotencia se fija al elegir el archivo</b>, no al enviarlo: así un doble
 * clic o un reintento tras un error de red devuelven el lote que ya existe (CF-16) en vez de
 * duplicar cien documentos. Elegir otro archivo genera una clave nueva, porque es otro lote.</p>
 *
 * <p>Cuatro estados (CF-22): sin archivo, archivo elegido, subiendo y error. El éxito no es un
 * quinto estado: se navega al seguimiento del lote, que es donde pasa lo siguiente.</p>
 */
export function LoteCargaPanel() {
  const router = useRouter();

  const [archivo, setArchivo] = useState<File | null>(null);
  const [claveIdempotencia, setClaveIdempotencia] = useState<string | null>(null);
  const [subiendo, setSubiendo] = useState(false);
  const [descargando, setDescargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [arrastrando, setArrastrando] = useState(false);

  const tamano = useMemo(
    () => (archivo ? `${(archivo.size / 1024).toFixed(0)} KB` : null),
    [archivo],
  );

  const elegir = useCallback((elegido: File | null | undefined) => {
    setError(null);

    if (!elegido) {
      setArchivo(null);
      setClaveIdempotencia(null);
      return;
    }

    if (!elegido.name.toLowerCase().endsWith(EXTENSION)) {
      setArchivo(null);
      setClaveIdempotencia(null);
      setError("Solo se admiten archivos .xlsx. Guarda la plantilla desde Excel en ese formato.");
      return;
    }

    if (elegido.size > MAX_BYTES) {
      setArchivo(null);
      setClaveIdempotencia(null);
      setError(
        "El archivo pesa más de 10 MB. Un lote de 100 filas ocupa unos pocos kilobytes: revisa que sea la plantilla y no otro archivo.",
      );
      return;
    }

    setArchivo(elegido);
    // Una clave por archivo elegido. `randomUUID` no existe en contextos no seguros ni en algunos
    // entornos de prueba, de ahí el respaldo.
    setClaveIdempotencia(
      typeof crypto !== "undefined" && typeof crypto.randomUUID === "function"
        ? crypto.randomUUID()
        : `lote-${Date.now()}-${Math.random().toString(36).slice(2)}`,
    );
  }, []);

  const descargarPlantilla = useCallback(async () => {
    setError(null);
    setDescargando(true);
    try {
      await downloadStandaloneBatchTemplate();
    } catch {
      setError("No se pudo descargar la plantilla. Intenta nuevamente en unos minutos.");
    } finally {
      setDescargando(false);
    }
  }, []);

  const cargar = useCallback(async () => {
    if (!archivo || !claveIdempotencia) return;

    setError(null);
    setSubiendo(true);
    try {
      const lote = await createStandaloneBatch(archivo, claveIdempotencia);
      // Se navega igual si el lote ya existía: es el mismo lote y su avance es lo que hay que ver.
      router.push(`${GENERACION_DOCUMENTAL_BASE_PATH}/lotes/${lote.batchId}`);
    } catch (e) {
      if (e instanceof ApiError) {
        // `ApiError` guarda el cuerpo JSON en `body`, y ahí viaja `{ error, field }`.
        const detalle = e.body as { error?: unknown } | null;
        const explicado = mensajeDeRechazo(detalle?.error);
        if (explicado) {
          setError(explicado);
        } else if (e.status === 403) {
          setError("No tienes permiso para cargar lotes en esta compañía.");
        } else {
          setError("No se pudo cargar el lote. Intenta nuevamente en unos minutos.");
        }
      } else {
        setError("No se pudo cargar el lote. Revisa tu conexión e intenta nuevamente.");
      }
      setSubiendo(false);
      return;
    }

    setSubiendo(false);
  }, [archivo, claveIdempotencia, router]);

  return (
    <section aria-labelledby="gd-lotes-title" className="flex flex-1 flex-col gap-4">
      <header className="flex items-start gap-2">
        <FileSpreadsheet
          className="mt-0.5 h-4 w-4 shrink-0"
          style={{ color: "#557EFF" }}
          aria-hidden="true"
        />
        <div>
          <h2 id="gd-lotes-title" className="text-sm font-semibold" style={{ color: "#162744" }}>
            Carga masiva
          </h2>
          <p className="mt-1 text-xs opacity-70">
            Emite hasta {MAX_FILAS} documentos de una vez desde un archivo de Excel. Un mismo lote
            puede mezclar Certificados RUES y documentos de transferencia. El procesamiento ocurre en
            segundo plano y puedes seguirlo fila por fila.
          </p>
        </div>
      </header>

      {/* Paso 1 — la plantilla. Va primero porque es de donde sale todo lo demás. */}
      <div className="rounded-2xl border p-4">
        <h3 className="text-xs font-semibold uppercase opacity-70">Paso 1 · Descarga la plantilla</h3>
        <p className="mt-2 text-xs opacity-80">
          La plantilla trae las columnas en el orden exacto que exige la carga, listas desplegables
          en las columnas de catálogo y una hoja de <strong>Instrucciones</strong> con el
          significado de cada columna y dos ejemplos resueltos. Al pararte en cualquier celda
          aparece la explicación de esa columna.
        </p>
        <button
          type="button"
          onClick={descargarPlantilla}
          disabled={descargando}
          data-testid="lote-descargar-plantilla"
          className="mt-3 inline-flex items-center gap-1.5 rounded-lg px-3 py-2 text-xs font-semibold text-white transition disabled:opacity-60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          style={{ background: "#557EFF" }}
        >
          <Download className="h-3.5 w-3.5" aria-hidden="true" />
          {descargando ? "Preparando la plantilla…" : "Descargar plantilla (.xlsx)"}
        </button>
      </div>

      {/* Paso 2 — el archivo. */}
      <div className="rounded-2xl border p-4">
        <h3 className="text-xs font-semibold uppercase opacity-70">Paso 2 · Sube tu archivo</h3>

        <div
          onDragOver={(e) => {
            e.preventDefault();
            setArrastrando(true);
          }}
          onDragLeave={() => setArrastrando(false)}
          onDrop={(e) => {
            e.preventDefault();
            setArrastrando(false);
            elegir(e.dataTransfer?.files?.[0]);
          }}
          className="mt-3 rounded-xl border border-dashed p-4 transition"
          style={{ borderColor: arrastrando ? "#557EFF" : undefined }}
        >
          <label
            htmlFor="lote-archivo"
            className="block text-xs font-semibold"
            style={{ color: "#162744" }}
          >
            Archivo de la plantilla diligenciada
          </label>
          <p id="lote-archivo-ayuda" className="mt-1 text-[11px] opacity-70">
            Formato .xlsx, máximo {MAX_FILAS} filas de datos. También puedes arrastrarlo hasta aquí.
          </p>
          <input
            id="lote-archivo"
            type="file"
            accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            aria-describedby="lote-archivo-ayuda"
            disabled={subiendo}
            data-testid="lote-archivo"
            onChange={(e) => elegir(e.target.files?.[0])}
            className="mt-2 block w-full text-xs file:mr-3 file:rounded-lg file:border-0 file:px-3 file:py-2 file:text-xs file:font-semibold"
          />

          {archivo && (
            <p className="mt-3 text-xs" data-testid="lote-archivo-elegido">
              Listo para cargar: <strong>{archivo.name}</strong>{" "}
              <span className="opacity-70">({tamano})</span>
            </p>
          )}
        </div>

        {/* El error no se comunica solo por color (CF-22): es texto, y `alert` lo anuncia. */}
        {error && (
          <p role="alert" data-testid="lote-error" className="mt-3 text-xs" style={{ color: "#B42318" }}>
            {error}
          </p>
        )}

        <button
          type="button"
          onClick={cargar}
          disabled={!archivo || subiendo}
          data-testid="lote-cargar"
          className="mt-3 inline-flex items-center gap-1.5 rounded-lg px-3 py-2 text-xs font-semibold text-white transition disabled:opacity-60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          style={{ background: "#557EFF" }}
        >
          <Upload className="h-3.5 w-3.5" aria-hidden="true" />
          {subiendo ? "Cargando el lote…" : "Cargar y procesar"}
        </button>

        {!archivo && !error && (
          <p className="mt-2 text-[11px] opacity-70">
            Elige un archivo para habilitar la carga.
          </p>
        )}
      </div>

      <p className="text-[11px] opacity-70">
        Una fila con datos incompletos no cancela el lote: queda marcada con su motivo y las demás se
        generan igual. Al terminar puedes descargar en un ZIP los documentos que sí salieron.
      </p>
    </section>
  );
}
