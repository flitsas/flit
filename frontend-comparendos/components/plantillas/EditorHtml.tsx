"use client";

import { useCallback, useRef, useState, type ReactNode } from "react";
import { AlignCenter, AlignJustify, AlignLeft, AlignRight, Bold, Braces, Code2, Heading2, Heading3, Italic, List, ListOrdered, Table, Underline } from "lucide-react";
import { controlCls, TEXTAREA_CLS } from "@/components/ui/estilos";
import { limpiarHtml, TABLA_SIMPLE } from "@/lib/editorHtml";
import type { VariablePlantilla } from "@/lib/tipos";

function Boton({ titulo, onClick, children }: { titulo: string; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      title={titulo}
      aria-label={titulo}
      className="inline-flex h-8 w-8 items-center justify-center rounded-lg text-[#162744] hover:bg-[#EFF6FF] dark:text-white dark:hover:bg-white/10"
      // mousedown: que el foco y la selección sigan en el área de edición.
      onMouseDown={(e) => e.preventDefault()}
      onClick={onClick}
    >
      {children}
    </button>
  );
}

/**
 * Editor enriquecido sencillo para las plantillas (HU #13486). Solo ofrece lo que el backend sabe convertir a PDF
 * (títulos, párrafos, negrita, cursiva, subrayado, listas, alineación y tablas simples) y limpia el HTML al guardar.
 * Las variables {{…}} se insertan como texto. «Ver HTML» permite revisar o pegar el código directamente.
 */
export function EditorHtml({ inicial, onCambio, variables }: { inicial: string; onCambio: (html: string) => void; variables: VariablePlantilla[] }) {
  const area = useRef<HTMLDivElement | null>(null);
  const [codigo, setCodigo] = useState<string | null>(null);
  const [verVariables, setVerVariables] = useState(false);

  const montar = useCallback(
    (nodo: HTMLDivElement | null) => {
      area.current = nodo;
      if (nodo && !nodo.dataset.montado) {
        nodo.innerHTML = inicial;
        nodo.dataset.montado = "1";
      }
    },
    [inicial],
  );

  const emitir = () => area.current && onCambio(limpiarHtml(area.current.innerHTML));
  const comando = (c: string, valor?: string) => {
    area.current?.focus();
    document.execCommand(c, false, valor);
    emitir();
  };

  const alternarCodigo = () => {
    if (codigo === null) {
      setCodigo(limpiarHtml(area.current?.innerHTML ?? ""));
      return;
    }
    const limpio = limpiarHtml(codigo);
    setCodigo(null);
    onCambio(limpio);
    requestAnimationFrame(() => {
      if (area.current) area.current.innerHTML = limpio;
    });
  };

  const grupos = variables.reduce<Record<string, VariablePlantilla[]>>((acc, v) => ({ ...acc, [v.grupo]: [...(acc[v.grupo] ?? []), v] }), {});

  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-1 rounded-xl border border-[#DFE5ED] px-2 py-1 dark:border-white/15" role="toolbar" aria-label="Formato">
        <Boton titulo="Negrita" onClick={() => comando("bold")}><Bold className="h-4 w-4" /></Boton>
        <Boton titulo="Cursiva" onClick={() => comando("italic")}><Italic className="h-4 w-4" /></Boton>
        <Boton titulo="Subrayado" onClick={() => comando("underline")}><Underline className="h-4 w-4" /></Boton>
        <Boton titulo="Título" onClick={() => comando("formatBlock", "h2")}><Heading2 className="h-4 w-4" /></Boton>
        <Boton titulo="Subtítulo" onClick={() => comando("formatBlock", "h3")}><Heading3 className="h-4 w-4" /></Boton>
        <Boton titulo="Párrafo" onClick={() => comando("formatBlock", "p")}><span className="text-xs font-bold">P</span></Boton>
        <Boton titulo="Lista con viñetas" onClick={() => comando("insertUnorderedList")}><List className="h-4 w-4" /></Boton>
        <Boton titulo="Lista numerada" onClick={() => comando("insertOrderedList")}><ListOrdered className="h-4 w-4" /></Boton>
        <Boton titulo="Alinear a la izquierda" onClick={() => comando("justifyLeft")}><AlignLeft className="h-4 w-4" /></Boton>
        <Boton titulo="Centrar" onClick={() => comando("justifyCenter")}><AlignCenter className="h-4 w-4" /></Boton>
        <Boton titulo="Alinear a la derecha" onClick={() => comando("justifyRight")}><AlignRight className="h-4 w-4" /></Boton>
        <Boton titulo="Justificar" onClick={() => comando("justifyFull")}><AlignJustify className="h-4 w-4" /></Boton>
        <Boton titulo="Tabla" onClick={() => comando("insertHTML", TABLA_SIMPLE)}><Table className="h-4 w-4" /></Boton>
        <span className="mx-1 h-5 w-px bg-[#DFE5ED]" />
        <button type="button" className={controlCls(verVariables)} onMouseDown={(e) => e.preventDefault()} onClick={() => setVerVariables((v) => !v)}>
          <Braces className="h-3.5 w-3.5" aria-hidden />
          Insertar variable
        </button>
        <button type="button" className={controlCls(codigo !== null)} onClick={alternarCodigo}>
          <Code2 className="h-3.5 w-3.5" aria-hidden />
          {codigo === null ? "Ver HTML" : "Volver al editor"}
        </button>
      </div>
      {verVariables && (
        <div className="max-h-56 overflow-auto rounded-xl border border-[#DFE5ED] p-2 dark:border-white/15">
          {Object.entries(grupos).map(([grupo, lista]) => (
            <div key={grupo} className="mb-2">
              <p className="text-[11px] font-semibold uppercase opacity-60">{grupo}</p>
              <div className="flex flex-wrap gap-1">
                {lista.map((v) => (
                  <button
                    key={v.nombre}
                    type="button"
                    title={`${v.descripcion} · Ej.: ${v.ejemplo}`}
                    className="rounded-lg bg-[#EFF6FF] px-2 py-1 font-mono text-[11px] text-[#3B4FD6] hover:bg-[#DCE8FF] dark:bg-white/10 dark:text-white"
                    onMouseDown={(e) => e.preventDefault()}
                    onClick={() => (codigo === null ? comando("insertText", `{{${v.nombre}}}`) : setCodigo(`${codigo}{{${v.nombre}}}`))}
                  >
                    {`{{${v.nombre}}}`}
                  </button>
                ))}
              </div>
            </div>
          ))}
        </div>
      )}
      {codigo === null ? (
        <div
          ref={montar}
          contentEditable
          suppressContentEditableWarning
          role="textbox"
          aria-multiline
          aria-label="Contenido de la plantilla"
          onInput={emitir}
          className="plantilla-editor min-h-[360px] overflow-auto rounded-xl border border-[#DFE5ED] bg-white p-4 text-sm leading-relaxed text-[#1F2A3D] focus:border-[#557EFF] focus:outline-none dark:border-white/15 dark:bg-[#0B0F14] dark:text-white [&_h2]:text-lg [&_h2]:font-semibold [&_h3]:font-semibold [&_ol]:list-decimal [&_ol]:pl-6 [&_table]:border-collapse [&_td]:border [&_td]:px-2 [&_th]:border [&_th]:bg-[#EEF2F7] [&_th]:px-2 [&_ul]:list-disc [&_ul]:pl-6"
        />
      ) : (
        <textarea className={`${TEXTAREA_CLS} min-h-[360px] font-mono`} value={codigo} onChange={(e) => setCodigo(e.target.value)} aria-label="Código HTML de la plantilla" />
      )}
    </div>
  );
}
