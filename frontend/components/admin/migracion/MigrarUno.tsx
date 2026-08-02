"use client";

import { useState } from "react";
import { FileSearch, Loader2, Play } from "lucide-react";
import { ErrorMigracion, migrarTramite } from "@/lib/migracion/client";
import {
  ETIQUETA_TRAMITE,
  TIPOS_TRAMITE,
  type Instancia,
  type MigracionRespuesta,
  type TipoTramite,
} from "@/lib/migracion/types";
import { OpcionesMigracion } from "./OpcionesMigracion";
import { ReporteMigracion } from "./ReporteMigracion";
import { AvisoError } from "./AvisoError";

/**
 * Migrar un trámite y ver el reporte. Es la pantalla que reemplaza a lanzar la petición desde
 * Postman, y la que conviene usar para probar antes de cargar un archivo de veinte.
 */
export function MigrarUno() {
  const [tramite, setTramite] = useState<TipoTramite>("transfer");
  const [id, setId] = useState("");
  const [instancias, setInstancias] = useState<Instancia[]>([]);
  const [dryRun, setDryRun] = useState(true);

  const [corriendo, setCorriendo] = useState(false);
  const [respuesta, setRespuesta] = useState<MigracionRespuesta | null>(null);
  const [error, setError] = useState<ErrorMigracion | Error | null>(null);

  const v1Id = Number.parseInt(id.trim(), 10);
  const idValido = /^\d+$/.test(id.trim()) && Number.isSafeInteger(v1Id) && v1Id > 0;

  async function lanzar(evento: React.FormEvent) {
    evento.preventDefault();
    if (!idValido || corriendo) {
      return;
    }

    setCorriendo(true);
    setError(null);
    // El reporte anterior se limpia al empezar: dejarlo en pantalla mientras corre la siguiente
    // hace que se lea como si fuera el resultado nuevo.
    setRespuesta(null);

    try {
      setRespuesta(
        await migrarTramite({
          tramite,
          v1Id,
          instancias: instancias.length > 0 ? instancias : undefined,
          dryRun,
        }),
      );
    } catch (e) {
      setError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setCorriendo(false);
    }
  }

  return (
    <div className="grid grid-cols-1 items-start gap-4 lg:grid-cols-[minmax(0,340px)_minmax(0,1fr)]">
      <form
        onSubmit={lanzar}
        className="flex flex-col gap-4 rounded-2xl border border-[#DFE5ED] p-4 dark:border-white/10"
      >
        <div className="flex flex-col gap-1.5">
          <label htmlFor="tipo-tramite" className="text-xs font-semibold uppercase tracking-wide opacity-60">
            Tipo de trámite
          </label>
          <select
            id="tipo-tramite"
            value={tramite}
            onChange={(e) => setTramite(e.target.value as TipoTramite)}
            disabled={corriendo}
            className="rounded-lg border border-[#DFE5ED] bg-transparent px-3 py-2 text-sm dark:border-white/10"
          >
            {TIPOS_TRAMITE.map((t) => (
              <option key={t} value={t}>
                {ETIQUETA_TRAMITE[t]}
              </option>
            ))}
          </select>
        </div>

        <div className="flex flex-col gap-1.5">
          <label htmlFor="id-v1" className="text-xs font-semibold uppercase tracking-wide opacity-60">
            Id en V1
          </label>
          <input
            id="id-v1"
            // inputMode y no type="number": el control numérico trae flechas que invitan a
            // incrementar un id, y un id contiguo es otro trámite real de otra empresa.
            inputMode="numeric"
            value={id}
            onChange={(e) => setId(e.target.value)}
            disabled={corriendo}
            placeholder="26350"
            className="rounded-lg border border-[#DFE5ED] bg-transparent px-3 py-2 text-sm tabular-nums dark:border-white/10"
          />
          {id.trim() !== "" && !idValido && (
            <p className="text-xs text-red-500">El id debe ser un número entero positivo.</p>
          )}
        </div>

        <OpcionesMigracion
          instancias={instancias}
          onInstancias={setInstancias}
          dryRun={dryRun}
          onDryRun={setDryRun}
          deshabilitado={corriendo}
        />

        <button
          type="submit"
          disabled={!idValido || corriendo}
          // El color sigue al modo, no al revés: ámbar cuando va a escribir en V2. El gris de
          // antes se leía como un botón deshabilitado justo cuando sí se podía pulsar.
          className={`flex items-center justify-center gap-2 rounded-lg px-4 py-2.5 text-sm font-semibold text-white transition-colors disabled:opacity-50 ${
            dryRun ? "bg-[#557EFF]" : "bg-amber-600 hover:bg-amber-700"
          }`}
        >
          {corriendo ? (
            <>
              <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
              Migrando…
            </>
          ) : (
            <>
              <Play className="h-4 w-4" aria-hidden="true" />
              {dryRun ? "Simular" : "Migrar de verdad"}
            </>
          )}
        </button>

        {corriendo && (
          <p className="text-xs opacity-70">
            Un trámite completo puede tardar un minuto largo. No cierres la pestaña; si se corta,
            la migración sigue en el servidor y podrás consultarla.
          </p>
        )}
      </form>

      {/*
        Con contenido, la tarjeta crece con él. Vacía, se queda en una caja discreta y centrada en
        vez de un rectángulo de 400 px de alto esperando: la versión anterior dejaba media pantalla
        en blanco y la página parecía a medio cargar.
      */}
      <div className="rounded-2xl border border-[#DFE5ED] p-4 dark:border-white/10">
        {error && <AvisoError error={error} />}
        {!error && respuesta && <ReporteMigracion respuesta={respuesta} />}
        {!error && !respuesta && (
          <div className="flex min-h-[8rem] flex-col items-center justify-center gap-2 text-center">
            {corriendo ? (
              <>
                <Loader2
                  className="h-5 w-5 animate-spin"
                  aria-hidden="true"
                  style={{ color: "#557EFF" }}
                />
                <p className="text-sm opacity-70">Esperando al migrador…</p>
              </>
            ) : (
              <>
                <FileSearch className="h-6 w-6 opacity-30" aria-hidden="true" />
                <p className="max-w-xs text-sm opacity-60">
                  Elige un trámite y lanza la migración: el reporte aparecerá aquí.
                </p>
              </>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
