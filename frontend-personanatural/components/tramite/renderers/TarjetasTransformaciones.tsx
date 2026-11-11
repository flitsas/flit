"use client";

// Transformaciones del vehículo como en FLIT 2 («Trámites Simultáneos»): una tarjeta por transformación, con su nombre e interruptor
// en el encabezado y, al activarla, su opción (catálogo real), el valor actual del RUNT y la carga de su soporte en esa misma
// tarjeta. Solo dibuja: el estado, el guardado y las validaciones viven en FormularioRequisitos y lib/tramite/declaraciones.
import { useId } from "react";
import { ControlDeclaracion, Soporte, type SubidaSoporte } from "@/components/tramite/renderers/DeclaracionesTipo";
import type { Adjunto, ClientePortal, DeclaracionTipo, DefinicionTipo } from "@/lib/api/cliente";
import { declaracionDeTransformacion, soporteDeTransformacion, valorActualEnRunt, type ValoresRunt } from "@/lib/tramite/declaraciones";
import { formatearPesos } from "@/lib/formato";
import { TRANSFORMACIONES } from "@/lib/tramite/requisitos";

interface Props {
  cliente: ClientePortal;
  definicion: DefinicionTipo;
  transformaciones: readonly string[];
  /** Precio que cotizó el back por código de transformación. */
  precioDe: (codigo: string) => number | undefined;
  campos: Readonly<Record<string, string>>;
  valoresRunt?: ValoresRunt | null | undefined;
  solicitudId: string;
  adjuntos: readonly Adjunto[];
  subidas: Readonly<Record<string, SubidaSoporte>>;
  error: (llave: string) => string | undefined;
  errorSoporte: (docCode: string) => string | undefined;
  onAlternar: (codigo: string) => void;
  onCambiarValor: (declaracion: DeclaracionTipo, valor: string) => void;
  onTocar: (llave: string) => void;
  onSubir: (soporte: { docCode: string }, archivo: File) => void;
  onQuitar: (soporte: { docCode: string }) => void;
}

export function TarjetasTransformaciones(props: Props) {
  const { transformaciones, precioDe, onAlternar } = props;
  const idTitulo = useId();
  return (
    <section id="requisitos-transformaciones" aria-labelledby={idTitulo}>
      <h3 id={idTitulo} className="wizard-section-title mb-2 text-2xl">
        Transformaciones del vehículo
      </h3>
      <p>Al activar un switch, el valor de la transformación se sumará automáticamente al valor total liquidado del trámite.</p>
      <div className="mt-3 grid grid-cols-1 items-start gap-4 md:grid-cols-3">
        {TRANSFORMACIONES.map(({ codigo, etiqueta }) => {
          const activa = transformaciones.includes(codigo);
          const precio = precioDe(codigo);
          const idNombre = `transf-${codigo}`;
          return (
            <section key={codigo} aria-labelledby={idNombre} data-transformacion={codigo} className="rounded-[9px] border border-border bg-card p-6">
              <div className="flex items-center justify-between gap-3">
                <h4 id={idNombre} className="font-medium">
                  {etiqueta}
                  {precio != null && (
                    <>
                      {" "}
                      <b className="text-primary">+{formatearPesos(precio)}</b>
                    </>
                  )}
                </h4>
                <button
                  type="button"
                  role="switch"
                  aria-checked={activa}
                  aria-labelledby={idNombre}
                  onClick={() => onAlternar(codigo)}
                  className={`relative h-11 w-16 shrink-0 rounded-full p-0 transition-colors focus-visible:ring-2 focus-visible:ring-primary/40 ${activa ? "bg-success" : "bg-disabled"}`}
                >
                  <span className={`absolute top-2.5 h-6 w-6 rounded-full bg-card transition-all ${activa ? "left-9" : "left-1"}`} />
                </button>
              </div>
              {activa && <DetalleTransformacion {...props} codigo={codigo} />}
            </section>
          );
        })}
      </div>
      {transformaciones.length === 0 && <p className="mt-3 text-xs font-light text-muted-foreground">Sin transformaciones declaradas: se registrará el dato del RUNT.</p>}
    </section>
  );
}

function DetalleTransformacion({ cliente, definicion, codigo, campos, valoresRunt, solicitudId, adjuntos, subidas, error, errorSoporte, onCambiarValor, onTocar, onSubir, onQuitar }: Props & { codigo: string }) {
  const decl = declaracionDeTransformacion(definicion, codigo);
  if (!decl) return null;
  const valor = campos[decl.fieldKey] ?? "";
  const soporte = soporteDeTransformacion(decl, definicion);
  return (
    <div className="mt-4 space-y-4">
      <ControlDeclaracion
        cliente={cliente}
        decl={decl}
        requerida
        solicitudId={solicitudId}
        valor={valor}
        ayuda={valorActualEnRunt(decl, valoresRunt)?.texto}
        error={error(`declaracion.${decl.clave}`)}
        onCambiar={(v) => onCambiarValor(decl, v)}
        onTocar={() => onTocar(`declaracion.${decl.clave}`)}
      />
      {soporte && <Soporte decl={decl} soporte={soporte} valor={valor} adjuntos={adjuntos} subida={subidas[soporte.docCode]} error={errorSoporte(soporte.docCode)} onSubir={onSubir} onQuitar={onQuitar} />}
    </div>
  );
}
