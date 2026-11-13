"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { Icono } from "@flit/ui/Icono";
import { TablaEditable, type ColumnaEditable, type EstadoFilaControlado } from "@flit/ui/TablaEditable";
import { accesoNegado, validarFilas, type ErrorFila, type FilaCapturada, type FilaResumen } from "@/lib/carga/api";
import { validarIdentificador, validarLongitudIdentificador } from "@/lib/validation/fieldRules";
import { AYUDA_CLS, BOTON_SECUNDARIO_CLS, CAMPO_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
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

/** Filas con error por página (AC21: se ven todas, paginadas). */
export const FILAS_POR_PAGINA = 10;

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

/** Fila de la tabla del kit: los valores se leen de la edición de este componente, que manda. */
interface FilaTabla {
  fila: number;
  identificador: string;
  tipoDocumento: string;
  documento: string;
  edicion: Edicion;
  original: FilaResumen;
}

/** Estado de la fila con los rótulos de AC22, para el estado controlado de `TablaEditable`. */
export function estadoDeEdicion(e: Edicion): EstadoFilaControlado {
  if (e.revalidando) return { estado: "guardando", texto: "Revalidando", tono: "info" };
  if (e.errorServidor) return { estado: "error", texto: "Inválido", tono: "danger" };
  if (e.resumen.valida && !e.errorLocal) return { estado: "sin_cambios", texto: "Corregida", tono: "success" };
  return { estado: "sin_cambios", texto: "Inválido", tono: "danger" };
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
 *
 * HU #14562 · M12: la tabla es la `TablaEditable` del kit en modo controlado. Los valores y el estado salen de las
 * ediciones de este componente (`estadoFila`), sin «Guardar» por fila, porque se revalida al dejar de escribir.
 * «Reintentar» aparece solo si falló la revalidación y lleva el motivo como descripción. La región viva de la tabla
 * anuncia «3: Revalidando», «3: Corregida» o «3: Inválido».
 */
export function TablaFilasCorregibles({ diagnosticoId, filas, onFilaRevalidada, onAccesoNegado }: TablaFilasCorregiblesProps) {
  const [ediciones, setEdiciones] = useState<Map<number, Edicion>>(() => new Map(filas.map((f) => [f.fila, edicionDe(f)])));
  const temporizadores = useRef(new Map<number, ReturnType<typeof setTimeout>>());
  const controladores = useRef(new Map<number, AbortController>());
  const [pagina, setPagina] = useState(0);
  const paginas = Math.max(1, Math.ceil(filas.length / FILAS_POR_PAGINA));

  useEffect(() => {
    const t = temporizadores.current;
    const c = controladores.current;
    return () => {
      t.forEach(clearTimeout);
      c.forEach((x) => x.abort());
    };
  }, []);

  const filasTabla = useMemo<FilaTabla[]>(
    () =>
      filas.slice(pagina * FILAS_POR_PAGINA, (pagina + 1) * FILAS_POR_PAGINA).map((original) => {
        const edicion = ediciones.get(original.fila) ?? edicionDe(original);
        return {
          fila: edicion.resumen.fila,
          identificador: edicion.identificador,
          tipoDocumento: edicion.tipoDocumento,
          documento: edicion.documento,
          edicion,
          original,
        };
      }),
    [filas, pagina, ediciones],
  );

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

  // Los editores leen la edición de la fila (cuarto argumento) y escriben con `editar`: la tabla no lleva borrador propio.
  const columnas: ColumnaEditable<FilaTabla>[] = [
    { campo: "fila", titulo: "Fila", mostrar: (f) => <span className="font-semibold text-flit-primary">{f.fila}</span> },
    {
      campo: "identificador",
      titulo: "Identificador",
      editable: true,
      editor: (_valor, _onCambio, _etiqueta, { fila: n, edicion: e }) => {
        const errorId = e.errorLocal ?? (mensajes(e.resumen.errores, ["Identificador_Vehiculo"]) || null);
        const idError = `fila-${n}-error-id`;
        return (
          <>
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
          </>
        );
      },
    },
    {
      campo: "tipoDocumento",
      titulo: "Tipo de documento",
      editable: true,
      editor: (_valor, _onCambio, _etiqueta, { fila: n, edicion: e }) => (
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
      ),
    },
    {
      campo: "documento",
      titulo: "Documento del locatario",
      editable: true,
      editor: (_valor, _onCambio, _etiqueta, { fila: n, edicion: e, original }) => {
        const errorDoc = mensajes(e.resumen.errores, ["Tipo_Documento_Locatario", "Documento_Locatario"]) || null;
        const docConError = original.errores.some((x) => x.campo !== "Identificador_Vehiculo");
        const idDoc = `fila-${n}-error-doc`;
        return (
          <>
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
          </>
        );
      },
    },
  ];

  return (
    <section id={TABLA_FILAS_ID} tabIndex={-1} aria-labelledby="filas-titulo" className={`${TARJETA_CLS} flex flex-col gap-3`}>
      <div>
        <h2 id="filas-titulo" className="text-lg font-semibold text-flit-primary">
          Filas con errores
        </h2>
        <p className={AYUDA_CLS}>Corrige cada fila en la tabla; se revalida sola al dejar de escribir y queda «Corregida» o «Inválido» con el motivo.</p>
      </div>
      <TablaEditable<FilaTabla>
        etiqueta="Filas con errores"
        filas={filasTabla}
        columnas={columnas}
        clave={(f) => String(f.fila)}
        estadoFila={(f) => estadoDeEdicion(f.edicion)}
        errorFila={(f) => f.edicion.errorServidor}
        // «Reintentar» (solo tras un fallo de la revalidación) vuelve a pedirla con lo digitado.
        onGuardar={(f) => revalidar(f.edicion)}
      />
      {paginas > 1 && (
        <nav aria-label="Páginas de filas con errores" className="flex items-center justify-end gap-2">
          <button type="button" aria-label="Página anterior" disabled={pagina === 0} onClick={() => setPagina((n) => n - 1)} className={BOTON_SECUNDARIO_CLS}>
            <Icono nombre="izquierda" className="h-4 w-4" />
          </button>
          <span className={AYUDA_CLS}>
            Página {pagina + 1} de {paginas}
          </span>
          <button type="button" aria-label="Página siguiente" disabled={pagina >= paginas - 1} onClick={() => setPagina((n) => n + 1)} className={BOTON_SECUNDARIO_CLS}>
            <Icono nombre="derecha" className="h-4 w-4" />
          </button>
        </nav>
      )}
    </section>
  );
}
