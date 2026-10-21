"use client";

import { useEffect, useRef, useState } from "react";
import { StatusBadge } from "@flit/ui/StatusBadge";
import { TABLA_HEADER_BG, TABLA_HEADER_CELL_CLS, TABLA_HEADER_FG } from "@flit/ui/table-styles";
import { accesoNegado, validarFilas, type ErrorFila, type FilaCapturada, type FilaResumen } from "@/lib/carga/api";
import { validarIdentificador, validarLongitudIdentificador } from "@/lib/validation/fieldRules";
import { AYUDA_CLS, BOTON_REINTENTAR_CLS, CAMPO_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { CAMPO_INVALIDO_CLS, ERROR_CLS } from "@/components/parametrizacion/estilos";
import { DocumentoDeclarado } from "@/components/comun/DocumentoDeclarado";

export interface TablaFilasCorregiblesProps {
  diagnosticoId: string;
  /** Filas erróneas del análisis: siguen en la tabla aunque pasen a válidas, para ver el cambio. */
  filas: FilaResumen[];
  /** Cada revalidación del servidor: la fila nueva y lo que se envió (el documento escrito solo vive en memoria). */
  onFilaRevalidada: (fila: FilaResumen, capturada: FilaCapturada) => void;
  onAccesoNegado?: (code: string) => void;
}

export const TABLA_FILAS_ID = "filas-corregibles";

/** Espera tras la última tecla antes de revalidar con la API. */
export const DEBOUNCE_MS = 300;

export const TIPOS_DOCUMENTO = ["CC", "CE", "NIT", "PAS", "TI"] as const;

interface Edicion {
  resumen: FilaResumen;
  identificador: string;
  tipoDocumento: string;
  /** Lo que el usuario escribe; el documento del archivo llega enmascarado y nunca se precarga. */
  documento: string;
  errorLocal: string | null;
  errorServidor: string | null;
  revalidando: boolean;
}

function edicionDe(f: FilaResumen): Edicion {
  return {
    resumen: f,
    identificador: f.identificador,
    tipoDocumento: f.tipo_documento_locatario ?? "",
    documento: "",
    errorLocal: null,
    errorServidor: null,
    revalidando: false,
  };
}

/** La regla de pantalla (fieldRules, la misma de Trámites + el rechazo por longitud); null si pasa. */
export function errorIdentificadorLocal(valor: string): string | null {
  if (!valor.trim()) return "Escribe la placa o el VIN.";
  const largo = validarLongitudIdentificador(valor);
  if (largo) return largo.mensaje;
  if (validarIdentificador(valor).tipo === null) return "No es ni una placa ni un VIN válido.";
  return null;
}

/** Fila a enviar: el documento solo viaja si el usuario lo escribió (el enmascarado del archivo no sirve). */
export function capturadaDe(e: Edicion): FilaCapturada {
  const documento = e.documento.trim();
  return {
    fila: e.resumen.fila,
    identificador: e.identificador,
    tipo_documento_locatario: documento ? e.tipoDocumento || null : null,
    documento_locatario: documento || null,
  };
}

const mensajes = (errores: ErrorFila[], campos: ErrorFila["campo"][]) =>
  errores
    .filter((x) => campos.includes(x.campo))
    .map((x) => x.mensaje)
    .join(" ");

/**
 * Tabla de filas erróneas editable (HU #13754, F02-C9): cada fila se corrige en sitio; al dejar de escribir 300 ms se
 * valida en pantalla con fieldRules y, si pasa, se revalida con POST /diagnosticos/{id}/carga/validar-filas (el servidor
 * manda) y la fila cambia de estado. El documento del locatario se muestra enmascarado y rotulado «Declarado por el
 * cliente»; vacío se conserva el del archivo (la confirmación lo reenvía) y, para corregirlo, se vuelve a escribir: ese
 * valor no se guarda en el navegador.
 */
export function TablaFilasCorregibles({ diagnosticoId, filas, onFilaRevalidada, onAccesoNegado }: TablaFilasCorregiblesProps) {
  const [ediciones, setEdiciones] = useState<Map<number, Edicion>>(() => new Map(filas.map((f) => [f.fila, edicionDe(f)])));
  const temporizadores = useRef(new Map<number, ReturnType<typeof setTimeout>>());
  const controladores = useRef(new Map<number, AbortController>());

  useEffect(() => {
    const t = temporizadores.current;
    const c = controladores.current;
    return () => {
      t.forEach(clearTimeout);
      c.forEach((x) => x.abort());
    };
  }, []);

  const actualizar = (n: number, cambio: (e: Edicion) => Edicion) =>
    setEdiciones((actual) => {
      const previa = actual.get(n);
      if (!previa) return actual;
      return new Map(actual).set(n, cambio(previa));
    });

  async function revalidar(edicion: Edicion) {
    const n = edicion.resumen.fila;
    controladores.current.get(n)?.abort();
    const controller = new AbortController();
    controladores.current.set(n, controller);
    const capturada = capturadaDe(edicion);
    actualizar(n, (e) => ({ ...e, revalidando: true, errorServidor: null }));
    try {
      const resumen = await validarFilas(diagnosticoId, [capturada], controller.signal);
      if (controller.signal.aborted) return;
      const nueva = resumen.filas.find((f) => f.fila === n) ?? resumen.filas[0];
      if (!nueva) return;
      actualizar(n, (e) => ({ ...e, resumen: nueva, revalidando: false }));
      onFilaRevalidada(nueva, capturada);
    } catch (error) {
      if (controller.signal.aborted) return;
      const code = accesoNegado(error);
      if (code && onAccesoNegado) return onAccesoNegado(code);
      const mensaje = error instanceof Error ? error.message : "No se pudo revalidar la fila.";
      actualizar(n, (e) => ({ ...e, revalidando: false, errorServidor: mensaje }));
    }
  }

  function editar(n: number, cambios: Partial<Pick<Edicion, "identificador" | "tipoDocumento" | "documento">>) {
    const previa = ediciones.get(n);
    if (!previa) return;
    const edicion = { ...previa, ...cambios };
    edicion.errorLocal = errorIdentificadorLocal(edicion.identificador);
    setEdiciones((actual) => new Map(actual).set(n, edicion));

    clearTimeout(temporizadores.current.get(n));
    if (edicion.errorLocal) return;
    temporizadores.current.set(
      n,
      setTimeout(() => void revalidar(edicion), DEBOUNCE_MS),
    );
  }

  if (filas.length === 0) {
    return (
      <section id={TABLA_FILAS_ID} tabIndex={-1} aria-label="Filas con errores" className={`${TARJETA_CLS} text-center`}>
        <p className={AYUDA_CLS}>No hay filas con errores. Puedes continuar con los válidos.</p>
      </section>
    );
  }

  return (
    <section id={TABLA_FILAS_ID} tabIndex={-1} aria-labelledby="filas-titulo" className={`${TARJETA_CLS} flex flex-col gap-3`}>
      <div>
        <h2 id="filas-titulo" className="text-lg font-semibold text-flit-primary dark:text-white">
          Filas con errores
        </h2>
        <p className={AYUDA_CLS}>Corrige cada fila en la tabla; se revalida sola al dejar de escribir.</p>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[760px] border-separate border-spacing-y-1 text-sm">
          <thead>
            <tr style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
              {["Fila", "Identificador", "Tipo de documento", "Documento del locatario", "Estado"].map((t) => (
                <th key={t} scope="col" className={TABLA_HEADER_CELL_CLS} style={{ background: TABLA_HEADER_BG }}>
                  {t}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {filas.map((original) => {
              const e = ediciones.get(original.fila) ?? edicionDe(original);
              const n = e.resumen.fila;
              const errorId = e.errorLocal ?? (mensajes(e.resumen.errores, ["Identificador_Vehiculo"]) || null);
              const errorDoc = mensajes(e.resumen.errores, ["Tipo_Documento_Locatario", "Documento_Locatario"]) || null;
              const docConError = original.errores.some((x) => x.campo !== "Identificador_Vehiculo");
              const idError = `fila-${n}-error-id`;
              const idDoc = `fila-${n}-error-doc`;
              return (
                <tr key={n} className="align-top">
                  <td className="px-4 py-2 font-semibold text-flit-primary dark:text-white">{n}</td>
                  <td className="px-4 py-2">
                    <input
                      type="text"
                      aria-label={`Identificador de la fila ${n}`}
                      value={e.identificador}
                      onChange={(x) => editar(n, { identificador: x.target.value })}
                      aria-invalid={errorId ? true : undefined}
                      aria-describedby={errorId ? idError : undefined}
                      autoComplete="off"
                      className={`${CAMPO_CLS} ${CAMPO_INVALIDO_CLS} uppercase tracking-wider`}
                    />
                    {errorId && (
                      <p id={idError} className={`${ERROR_CLS} mt-1`}>
                        {errorId}
                      </p>
                    )}
                  </td>
                  <td className="px-4 py-2">
                    <select
                      aria-label={`Tipo de documento del locatario de la fila ${n}`}
                      value={e.tipoDocumento}
                      onChange={(x) => editar(n, { tipoDocumento: x.target.value })}
                      className={CAMPO_CLS}
                    >
                      <option value="">Sin documento</option>
                      {TIPOS_DOCUMENTO.map((t) => (
                        <option key={t} value={t}>
                          {t}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td className="px-4 py-2">
                    <input
                      type="text"
                      inputMode="text"
                      aria-label={`Documento del locatario de la fila ${n}`}
                      value={e.documento}
                      onChange={(x) => editar(n, { documento: x.target.value })}
                      aria-invalid={errorDoc ? true : undefined}
                      aria-describedby={errorDoc ? idDoc : undefined}
                      autoComplete="off"
                      placeholder={original.documento_locatario ? (docConError ? "Vuelve a escribirlo" : "Vacío conserva el del archivo") : "Opcional"}
                      className={`${CAMPO_CLS} ${CAMPO_INVALIDO_CLS}`}
                    />
                    <div className="mt-1">
                      <DocumentoDeclarado tipo={e.resumen.tipo_documento_locatario} documento={e.resumen.documento_locatario} />
                    </div>
                    {errorDoc && (
                      <p id={idDoc} className={`${ERROR_CLS} mt-1`}>
                        {errorDoc}
                      </p>
                    )}
                  </td>
                  <td className="px-4 py-2">
                    {e.revalidando ? (
                      <StatusBadge tone="info" label="Revalidando…" />
                    ) : e.resumen.valida && !e.errorLocal ? (
                      <StatusBadge tone="success" label="Válida" />
                    ) : (
                      <StatusBadge tone="danger" label="Errónea" />
                    )}
                    {e.errorServidor && (
                      <div role="alert" className={`${ERROR_CLS} mt-1 flex flex-col items-start gap-1`}>
                        {e.errorServidor}
                        <button type="button" onClick={() => void revalidar(e)} className={BOTON_REINTENTAR_CLS}>
                          Reintentar
                        </button>
                      </div>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </section>
  );
}
