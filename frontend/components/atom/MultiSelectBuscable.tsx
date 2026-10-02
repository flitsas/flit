"use client";

// HU #13248b (F9 #13245) — selector múltiple con buscador, reutilizable. Reemplaza las listas de
// checks paginadas del formulario del mandatario: chips con lo elegido (se quitan con ✕), contador,
// buscador que filtra mientras se escribe (nombre o NIT, ignorando puntos y guion), lista COMPLETA con
// scroll, «Seleccionar las filtradas» y «Limpiar». Con pocas opciones (`chipsHasta`) se muestran
// directamente como chips conmutables, sin buscador.

import { useId, useMemo, useState } from "react";
import { Check, Search, X } from "lucide-react";

export interface OpcionSeleccionable {
  id: string;
  label: string;
  /** Texto secundario visible junto al nombre (p. ej. «NIT 900123456-1» o el código del organismo). */
  detalle?: string;
  /** NIT u otro código: la búsqueda lo compara solo con dígitos (ignora puntos, espacios y guion). */
  codigo?: string;
  /** Nombre accesible de la casilla. Por defecto «nombre (detalle)». */
  ariaLabel?: string;
}

/** Minúsculas y sin tildes: «Bogotá» se encuentra escribiendo «bogota». */
export function normalizarBusqueda(texto: string): string {
  return texto
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .toLowerCase();
}

const soloDigitos = (texto: string) => texto.replace(/\D/g, "");

/** Coincide por nombre/detalle (sin tildes) o por los dígitos del código (NIT sin puntos ni guion). */
export function coincideBusqueda(opcion: OpcionSeleccionable, consulta: string): boolean {
  const q = consulta.trim();
  if (!q) return true;
  const texto = normalizarBusqueda(`${opcion.label} ${opcion.detalle ?? ""}`);
  if (texto.includes(normalizarBusqueda(q))) return true;
  // Un texto con letras («Compañía 01») es un nombre: el NIT solo se busca con números, puntos y guion.
  if (!/^[\d.\-\s]+$/.test(q)) return false;
  const digitos = soloDigitos(q);
  return digitos.length > 0 && !!opcion.codigo && soloDigitos(opcion.codigo).includes(digitos);
}

const etiquetaAccesible = (o: OpcionSeleccionable) =>
  o.ariaLabel ?? (o.detalle ? `${o.label} (${o.detalle})` : o.label);

export function MultiSelectBuscable({
  testId,
  grupo,
  opciones,
  seleccion,
  onChange,
  genero = "f",
  buscarLabel,
  buscarPlaceholder = "Buscar por nombre o NIT…",
  chipsHasta = 0,
  loading = false,
  error = false,
  onRetry,
  textoVacio = "No hay opciones disponibles.",
  textoCargando = "Cargando…",
  textoError = "No se pudo cargar la lista.",
  errores = {},
  disabled = false,
}: {
  /** Prefijo de los `data-testid` y de los ids internos. */
  testId: string;
  /** Nombre del grupo para lectores de pantalla (p. ej. «Compañías»). */
  grupo: string;
  opciones: OpcionSeleccionable[];
  /** Lo elegido. Puede traer opciones que ya no están en `opciones` (p. ej. precargadas al editar). */
  seleccion: OpcionSeleccionable[];
  onChange: (next: OpcionSeleccionable[]) => void;
  /** Concordancia del contador: «3 seleccionadas» (f) o «3 seleccionados» (m). */
  genero?: "f" | "m";
  buscarLabel: string;
  buscarPlaceholder?: string;
  /** Con esta cantidad de opciones o menos se muestran como chips conmutables, sin buscador. 0 = siempre selector. */
  chipsHasta?: number;
  loading?: boolean;
  error?: boolean;
  onRetry?: () => void;
  textoVacio?: string;
  textoCargando?: string;
  textoError?: string;
  /** Motivo del rechazo del servidor por opción (id → mensaje). */
  errores?: Record<string, string>;
  disabled?: boolean;
}) {
  const baseId = useId();
  const [consulta, setConsulta] = useState("");

  const ids = useMemo(() => new Set(seleccion.map((s) => s.id)), [seleccion]);
  const filtradas = useMemo(() => opciones.filter((o) => coincideBusqueda(o, consulta)), [opciones, consulta]);
  const faltanPorMarcar = filtradas.some((o) => !ids.has(o.id));

  const alternar = (o: OpcionSeleccionable) =>
    onChange(ids.has(o.id) ? seleccion.filter((s) => s.id !== o.id) : [...seleccion, o]);
  const quitar = (id: string) => onChange(seleccion.filter((s) => s.id !== id));
  const marcarFiltradas = () => onChange([...seleccion, ...filtradas.filter((o) => !ids.has(o.id))]);

  const n = seleccion.length;
  const contador = `${n} ${genero === "m" ? (n === 1 ? "seleccionado" : "seleccionados") : n === 1 ? "seleccionada" : "seleccionadas"}`;
  const idsConError = Object.keys(errores);
  const comoChips = !loading && !error && opciones.length > 0 && opciones.length <= chipsHasta;

  const bloqueErrores =
    idsConError.length > 0 ? (
      <ul className="space-y-0.5" role="alert" data-testid={`${testId}-errores`}>
        {idsConError.map((id) => {
          const nombre = seleccion.find((s) => s.id === id)?.label;
          return (
            <li key={id} className="text-xs leading-tight" style={{ color: "#E5484D" }}>
              {nombre ? `${nombre}: ` : ""}
              {errores[id]}
            </li>
          );
        })}
      </ul>
    ) : null;

  if (error) {
    return (
      <div className="space-y-2" data-testid={testId}>
        <div role="alert" className="flex items-center justify-between gap-2 text-xs">
          <span style={{ color: "#E5484D" }}>{textoError}</span>
          {onRetry ? (
            <button type="button" className="rounded-full border px-3 py-1 font-semibold" onClick={onRetry}>
              Reintentar
            </button>
          ) : null}
        </div>
      </div>
    );
  }

  if (loading) {
    return (
      <p role="status" className="text-xs opacity-70" data-testid={testId}>
        {textoCargando}
      </p>
    );
  }

  if (opciones.length === 0 && n === 0) {
    return (
      <p role="status" className="text-xs opacity-70" data-testid={testId}>
        {textoVacio}
      </p>
    );
  }

  const contadorEl = (
    <p className="text-xs font-semibold" role="status" aria-live="polite" data-testid={`${testId}-contador`}>
      {contador}
    </p>
  );

  if (comoChips) {
    return (
      <div className="space-y-2" data-testid={testId}>
        <div role="group" aria-label={grupo} className="flex flex-wrap gap-2">
          {opciones.map((o) => {
            const activa = ids.has(o.id);
            return (
              <label
                key={o.id}
                className={`inline-flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-xs font-semibold transition-colors has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-[#557EFF] has-[:focus-visible]:ring-offset-2 ${disabled ? "cursor-not-allowed opacity-50" : "cursor-pointer"}`}
                style={
                  activa
                    ? { borderColor: "#8CC63F", background: "rgba(140,198,63,0.12)" }
                    : { borderColor: "#DFE5ED", background: "#FFFFFF" }
                }
              >
                <input
                  type="checkbox"
                  className="sr-only"
                  checked={activa}
                  disabled={disabled}
                  onChange={() => alternar(o)}
                  aria-label={etiquetaAccesible(o)}
                />
                {activa ? <Check className="h-3.5 w-3.5" aria-hidden="true" /> : null}
                <span className="text-[#162744]">{o.label}</span>
                {o.detalle ? <span className="font-normal text-[#59677D]">· {o.detalle}</span> : null}
              </label>
            );
          })}
        </div>
        {contadorEl}
        {bloqueErrores}
      </div>
    );
  }

  const listaId = `${baseId}-lista`;
  return (
    <div className="space-y-2" data-testid={testId}>
      <div className="flex items-center justify-between gap-2">
        {contadorEl}
        <button
          type="button"
          onClick={() => onChange([])}
          disabled={disabled || n === 0}
          className="rounded-full px-3 py-1 text-xs font-semibold text-[#59677D] underline-offset-2 hover:underline disabled:opacity-40"
        >
          Limpiar
        </button>
      </div>

      {n > 0 ? (
        <ul className="flex flex-wrap gap-1.5" aria-label={`${grupo} seleccionadas`} data-testid={`${testId}-seleccion`}>
          {seleccion.map((s) => (
            <li
              key={s.id}
              className="inline-flex items-center gap-1 rounded-full border px-2.5 py-1 text-xs"
              style={{
                borderColor: errores[s.id] ? "#E5484D" : "#8CC63F",
                background: "rgba(140,198,63,0.12)",
              }}
            >
              <span className="text-[#162744]">{s.detalle ? `${s.label} · ${s.detalle}` : s.label}</span>
              <button
                type="button"
                onClick={() => quitar(s.id)}
                disabled={disabled}
                aria-label={`Quitar ${s.label}`}
                className="rounded-full p-0.5 hover:bg-black/5"
              >
                <X className="h-3 w-3" aria-hidden="true" />
              </button>
            </li>
          ))}
        </ul>
      ) : null}

      <div className="relative">
        <Search
          className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 opacity-60"
          aria-hidden="true"
        />
        <label htmlFor={`${baseId}-buscar`} className="sr-only">
          {buscarLabel}
        </label>
        <input
          id={`${baseId}-buscar`}
          type="search"
          value={consulta}
          onChange={(e) => setConsulta(e.target.value)}
          placeholder={buscarPlaceholder}
          disabled={disabled}
          aria-controls={listaId}
          className="w-full rounded-xl border bg-white py-2 pl-8 pr-3 text-xs outline-none focus:border-[#557EFF] dark:bg-[#0B0F14]"
        />
      </div>

      <div className="flex items-center justify-between gap-2">
        <p className="text-xs opacity-70" data-testid={`${testId}-conteo`}>
          {consulta.trim() ? `${filtradas.length} de ${opciones.length}` : `${opciones.length} en total`}
        </p>
        <button
          type="button"
          onClick={marcarFiltradas}
          disabled={disabled || !faltanPorMarcar}
          className="rounded-full border px-3 py-1 text-xs font-semibold text-[#162744] disabled:opacity-40"
        >
          Seleccionar las filtradas
        </button>
      </div>

      <div
        id={listaId}
        className="max-h-56 space-y-1 overflow-y-auto rounded-xl border bg-white p-2 dark:bg-[#0B0F14]"
        data-testid={`${testId}-lista`}
        role="group"
        aria-label={grupo}
      >
        {filtradas.length === 0 ? (
          <p role="status" className="text-xs opacity-70">
            Sin resultados
          </p>
        ) : (
          filtradas.map((o) => (
            <label
              key={o.id}
              className="flex cursor-pointer items-center gap-2 rounded-lg px-1.5 py-1 text-xs hover:bg-[rgba(85,126,255,0.06)] has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-[#557EFF]"
            >
              <input
                type="checkbox"
                checked={ids.has(o.id)}
                disabled={disabled}
                onChange={() => alternar(o)}
                aria-label={etiquetaAccesible(o)}
                className="h-4 w-4 accent-[#557EFF]"
              />
              <span>
                {o.label}
                {o.detalle ? <span className="opacity-70"> · {o.detalle}</span> : null}
              </span>
            </label>
          ))
        )}
      </div>
      {bloqueErrores}
    </div>
  );
}
