"use client";

// Los filtros de una consulta, como fichas sobre la tabla.
//
// La alternativa era el constructor clásico —una pantalla aparte con filas de campo, operador y
// valor y un botón «Ejecutar»—, y se descartó por una razón concreta: se entra a un lienzo en
// blanco. Nadie sabe qué preguntar hasta que ve una pregunta escrita. Aquí la tabla ya está
// poblada, cada ficha que se añade la acota, y «guardar» solo congela el estado al que se llegó.
//
// El catálogo de campos lo sirve el backend: este componente no sabe qué campos existen ni cómo se
// traducen, solo cómo se pinta cada TIPO. Un campo nuevo aparece aquí sin tocar este archivo.

import { useEffect, useMemo, useRef, useState } from "react";
import {
  OPERATOR_LABEL,
  UNARY_OPERATORS,
  type QueryCondition,
  type QueryField,
  type QueryOperator,
} from "@/lib/api/queries";
import { FIELD_CLS, plural } from "./ui";

/**
 * Trocea lo que el usuario pegó. Acepta saltos de línea, comas, punto y coma y tabuladores porque
 * una columna copiada de Excel llega con saltos, una lista escrita a mano llega con comas, y quien
 * pega no debería tener que saber cuál de las dos espera el campo.
 */
export function parseValueList(raw: string): string[] {
  return [
    ...new Set(
      raw
        .split(/[\n\r,;\t]+/)
        .map((v) => v.trim())
        .filter(Boolean),
    ),
  ];
}

export function describeCondition(condition: QueryCondition, fields: QueryField[]): string {
  const field = fields.find((f) => f.id === condition.fieldId);
  const label = field?.label ?? condition.fieldId;
  const operador = OPERATOR_LABEL[condition.operator] ?? condition.operator;

  if (UNARY_OPERATORS.includes(condition.operator)) {
    return `${label} ${operador}`;
  }

  // Con muchos valores se dice cuántos y no cuáles: veinte placas dentro de una ficha la vuelven
  // ilegible, y el detalle está a un clic.
  const valores =
    condition.values.length > 2
      ? plural(condition.values.length, "valor", "valores")
      : condition.values
          .map((v) => field?.options.find((o) => o.value === v)?.label ?? v)
          .join(" o ");

  return `${label} ${operador} ${valores}`;
}

export function QueryFilterBar({
  fields,
  conditions,
  onChange,
  disabled,
  testIdPrefix,
}: {
  fields: QueryField[];
  conditions: QueryCondition[];
  onChange: (conditions: QueryCondition[]) => void;
  disabled?: boolean;
  /** Las dos consolas usan esta barra; el prefijo las hace distinguibles en las pruebas. */
  testIdPrefix: string;
}) {
  // `null` = cerrado; una cadena = editando ese campo; "" = eligiendo cuál añadir.
  const [editing, setEditing] = useState<string | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (editing === null) return undefined;

    function onPointerDown(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setEditing(null);
      }
    }

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setEditing(null);
    }

    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [editing]);

  function upsert(condition: QueryCondition) {
    const rest = conditions.filter((c) => c.fieldId !== condition.fieldId);
    onChange([...rest, condition]);
    setEditing(null);
  }

  function remove(fieldId: string) {
    onChange(conditions.filter((c) => c.fieldId !== fieldId));
    setEditing(null);
  }

  const disponibles = fields.filter((f) => !conditions.some((c) => c.fieldId === f.id));
  const grupos = [...new Set(disponibles.map((f) => f.group))];

  return (
    <div className="flex flex-wrap items-center gap-2" ref={containerRef} data-testid={`${testIdPrefix}-filtros`}>
      {conditions.map((condition) => (
        <span
          key={condition.fieldId}
          className="relative inline-flex items-center gap-1 rounded-full border border-[#557EFF]/40 bg-[#557EFF]/10 py-1 pl-3 pr-1 text-xs font-semibold text-[#3355CC] dark:text-[#9DB5FF]"
        >
          <button
            type="button"
            onClick={() => setEditing(editing === condition.fieldId ? null : condition.fieldId)}
            className="max-w-[22rem] truncate"
            data-testid={`${testIdPrefix}-chip-${condition.fieldId}`}
          >
            {describeCondition(condition, fields)}
          </button>
          <button
            type="button"
            onClick={() => remove(condition.fieldId)}
            aria-label={`Quitar filtro ${condition.fieldId}`}
            className="rounded-full px-1.5 text-sm leading-none opacity-60 hover:opacity-100"
          >
            ×
          </button>

          {editing === condition.fieldId && (
            <ConditionEditor
              field={fields.find((f) => f.id === condition.fieldId)!}
              value={condition}
              onApply={upsert}
              onRemove={() => remove(condition.fieldId)}
              testIdPrefix={testIdPrefix}
            />
          )}
        </span>
      ))}

      <div className="relative">
        <button
          type="button"
          disabled={disabled || disponibles.length === 0}
          onClick={() => setEditing(editing === "" ? null : "")}
          className="rounded-full border border-dashed border-[#DFE5ED] px-3 py-1 text-xs font-semibold text-[#6B7280] hover:border-[#557EFF] hover:text-[#557EFF] disabled:opacity-40 dark:border-white/20 dark:text-white/60"
          data-testid={`${testIdPrefix}-agregar-filtro`}
        >
          + Filtro
        </button>

        {editing === "" && (
          <div
            className="absolute left-0 z-50 mt-2 max-h-[24rem] w-64 overflow-y-auto rounded-2xl border border-[#DFE5ED] bg-white p-2 shadow-2xl dark:border-white/10 dark:bg-[#0B0F14]"
            data-testid={`${testIdPrefix}-campos`}
          >
            {grupos.map((grupo) => (
              <div key={grupo} className="mb-1">
                <p className="px-2 py-1 text-[10px] font-semibold uppercase tracking-wide text-[#6B7280] dark:text-white/40">
                  {grupo}
                </p>
                {disponibles
                  .filter((f) => f.group === grupo)
                  .map((field) => (
                    <button
                      key={field.id}
                      type="button"
                      onClick={() => setEditing(field.id)}
                      className="block w-full rounded-lg px-2 py-1.5 text-left text-xs hover:bg-[#F5F7FA] dark:hover:bg-white/5"
                    >
                      {field.label}
                    </button>
                  ))}
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Campo aún sin ficha: el editor se ancla aquí hasta que se aplique. */}
      {editing && !conditions.some((c) => c.fieldId === editing) && (
        <div className="relative">
          <ConditionEditor
            field={fields.find((f) => f.id === editing)!}
            value={null}
            onApply={upsert}
            onRemove={() => setEditing(null)}
            testIdPrefix={testIdPrefix}
          />
        </div>
      )}
    </div>
  );
}

function ConditionEditor({
  field,
  value,
  onApply,
  onRemove,
  testIdPrefix,
}: {
  field: QueryField;
  value: QueryCondition | null;
  onApply: (condition: QueryCondition) => void;
  onRemove: () => void;
  testIdPrefix: string;
}) {
  const [operator, setOperator] = useState<QueryOperator>(
    value?.operator ?? field.operators[0] ?? "es_alguno",
  );
  const [texto, setTexto] = useState(value?.values.join("\n") ?? "");
  const [seleccion, setSeleccion] = useState<string[]>(value?.values ?? []);
  const [busqueda, setBusqueda] = useState("");

  const esUnario = UNARY_OPERATORS.includes(operator);
  const valores = useMemo(
    () => (field.kind === "texto" ? parseValueList(texto) : seleccion),
    [field.kind, texto, seleccion],
  );

  const opciones = busqueda.trim()
    ? field.options.filter((o) => o.label.toLowerCase().includes(busqueda.trim().toLowerCase()))
    : field.options;

  function aplicar() {
    onApply({ fieldId: field.id, operator, values: esUnario ? [] : valores });
  }

  return (
    <div
      className="absolute left-0 top-full z-50 mt-2 w-80 rounded-2xl border border-[#DFE5ED] bg-white p-3 text-left shadow-2xl dark:border-white/10 dark:bg-[#0B0F14]"
      data-testid={`${testIdPrefix}-editor-${field.id}`}
    >
      <p className="mb-2 text-xs font-semibold text-[#0B1F33] dark:text-white">{field.label}</p>

      {field.operators.length > 1 && (
        <select
          value={operator}
          onChange={(e) => setOperator(e.target.value as QueryOperator)}
          aria-label="Operador"
          className={`${FIELD_CLS} mb-2 w-full`}
        >
          {field.operators.map((op) => (
            <option key={op} value={op}>
              {OPERATOR_LABEL[op] ?? op}
            </option>
          ))}
        </select>
      )}

      {!esUnario && field.kind === "texto" && (
        <>
          <textarea
            value={texto}
            onChange={(e) => setTexto(e.target.value)}
            rows={operator === "contiene" ? 1 : 4}
            placeholder={
              operator === "contiene" ? "Parte del texto…" : "Un valor por línea, o pegue una columna de Excel"
            }
            aria-label={`Valores de ${field.label}`}
            className={`${FIELD_CLS} w-full resize-y`}
            data-testid={`${testIdPrefix}-valores-${field.id}`}
          />
          {operator !== "contiene" && valores.length > 0 && (
            <p className="mt-1 text-[11px] text-[#6B7280] dark:text-white/50">
              {plural(valores.length, "valor", "valores")}
            </p>
          )}
        </>
      )}

      {!esUnario && field.kind !== "texto" && (
        <>
          {field.options.length > 8 && (
            <input
              type="search"
              value={busqueda}
              onChange={(e) => setBusqueda(e.target.value)}
              placeholder="Buscar…"
              aria-label={`Buscar en ${field.label}`}
              className={`${FIELD_CLS} mb-2 w-full`}
            />
          )}
          <div className="max-h-48 overflow-y-auto">
            {opciones.map((option) => (
              <label
                key={option.value}
                className="flex cursor-pointer items-center gap-2 rounded-lg px-1.5 py-1 text-xs hover:bg-[#F5F7FA] dark:hover:bg-white/5"
              >
                <input
                  type="checkbox"
                  checked={seleccion.includes(option.value)}
                  onChange={() =>
                    setSeleccion((prev) =>
                      prev.includes(option.value)
                        ? prev.filter((v) => v !== option.value)
                        : [...prev, option.value],
                    )
                  }
                  className="accent-[#557EFF]"
                />
                <span className="min-w-0 flex-1 truncate">{option.label}</span>
              </label>
            ))}
          </div>
        </>
      )}

      {field.hint && (
        <p className="mt-2 text-[11px] leading-snug text-[#6B7280] dark:text-white/50">{field.hint}</p>
      )}

      <div className="mt-3 flex items-center justify-between gap-2">
        <button
          type="button"
          onClick={onRemove}
          className="text-[11px] font-semibold text-[#6B7280] hover:text-[#C0392B] dark:text-white/50"
        >
          {value ? "Quitar" : "Cancelar"}
        </button>
        <button
          type="button"
          onClick={aplicar}
          disabled={!esUnario && valores.length === 0}
          className="rounded-lg bg-[#557EFF] px-3 py-1.5 text-xs font-semibold text-white disabled:opacity-40"
          data-testid={`${testIdPrefix}-aplicar-${field.id}`}
        >
          Aplicar
        </button>
      </div>
    </div>
  );
}
