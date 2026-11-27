"use client";

import { useId, useState, type FormEvent } from "react";
import { Button } from "@flit/ui/Button";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { Interruptor } from "@flit/ui/Interruptor";
import { ListaDefinicion } from "@flit/ui/ListaDefinicion";
import { etiquetaKit } from "@flit/ui/StatusBadge";
import { CAMPO_CLS, CAMPO_INVALIDO_CLS, ERROR_CLS, ETIQUETA_CLS, MARCA_CLS, OPCION_CLS } from "@/components/comun/estilos";
import { mensajeErrorFijo } from "@/lib/errores";
import {
  ERRORES_CONFIGURACION,
  MAXIMO_ABSOLUTO_LOTE,
  guardarConfiguracionContractual,
  type ConfiguracionImpuestos,
} from "@/lib/configuracion/api";

export interface PestanaContractualProps {
  configuracion: ConfiguracionImpuestos;
  /** impuestos.configuracion.contrato.editar (solo Super Admin). */
  puedeEditar: boolean;
  /** Compañía cuyo contrato se edita (la de ?empresa=). Sin ella, solo lectura. */
  tenantId: string | null;
  onGuardado: (configuracion: ConfiguracionImpuestos) => void;
}

const cifra = (n: number) => new Intl.NumberFormat("es-CO").format(n);

/**
 * Pestaña «Contractual» (spec-visual §3.9; D-CONFIG): lo contratado por la compañía. Para el Administrador Impuestos es
 * de solo lectura (ListaDefinicion); el Super Admin con una compañía elegida la edita: máximo de placas por lote y
 * departamentos habilitados («Todos los departamentos» = null, todos los activos del proveedor).
 */
export function PestanaContractual({ configuracion, puedeEditar, tenantId, onGuardado }: PestanaContractualProps) {
  if (!puedeEditar || !tenantId) return <ContratoSoloLectura configuracion={configuracion} />;
  return <EditorContrato configuracion={configuracion} tenantId={tenantId} onGuardado={onGuardado} />;
}

function ContratoSoloLectura({ configuracion }: { configuracion: ConfiguracionImpuestos }) {
  const { maxRegistrosLote, departamentosHabilitados } = configuracion.contractual;
  const nombre = (codigo: string) => configuracion.departamentosDisponibles.find((d) => d.codigo === codigo)?.nombre ?? codigo;
  return (
    <ListaDefinicion
      columnas={2}
      items={[
        { termino: "Máximo de placas por lote contratado", valor: <span className="tabular-nums">{cifra(maxRegistrosLote)}</span> },
        {
          termino: "Departamentos habilitados",
          valor:
            departamentosHabilitados === null ? (
              "Todos los departamentos"
            ) : departamentosHabilitados.length === 0 ? null : (
              <ul className="flex flex-wrap gap-1.5" aria-label="Departamentos habilitados">
                {departamentosHabilitados.map((codigo) => (
                  <li key={codigo} {...etiquetaKit("neutral")}>
                    {nombre(codigo)}
                  </li>
                ))}
              </ul>
            ),
        },
      ]}
    />
  );
}

function EditorContrato({ configuracion, tenantId, onGuardado }: { configuracion: ConfiguracionImpuestos; tenantId: string; onGuardado: PestanaContractualProps["onGuardado"] }) {
  const id = useId();
  const [maximo, setMaximo] = useState(String(configuracion.contractual.maxRegistrosLote));
  const [todos, setTodos] = useState(configuracion.contractual.departamentosHabilitados === null);
  const [elegidos, setElegidos] = useState<string[]>(configuracion.contractual.departamentosHabilitados ?? []);
  const [errorMaximo, setErrorMaximo] = useState<string | null>(null);
  const [errorGuardar, setErrorGuardar] = useState<string | null>(null);
  const [guardado, setGuardado] = useState(false);
  const [guardando, setGuardando] = useState(false);
  const errorMaximoId = `${id}-maximo-error`;
  const todosId = `${id}-todos`;

  const alternar = (codigo: string) => setElegidos((actual) => (actual.includes(codigo) ? actual.filter((c) => c !== codigo) : [...actual, codigo]));

  async function guardar(ev: FormEvent) {
    ev.preventDefault();
    if (guardando) return;
    setGuardado(false);
    setErrorGuardar(null);
    const n = Number(maximo.trim());
    if (!maximo.trim() || !Number.isInteger(n) || n < 1 || n > MAXIMO_ABSOLUTO_LOTE) {
      setErrorMaximo(ERRORES_CONFIGURACION.TOPE_INVALIDO);
      return;
    }
    setErrorMaximo(null);
    setGuardando(true);
    try {
      // El orden de las casillas es el del proveedor, no el de los clics.
      const departamentos = todos ? null : configuracion.departamentosDisponibles.map((d) => d.codigo).filter((c) => elegidos.includes(c));
      const nueva = await guardarConfiguracionContractual(tenantId, { maxRegistrosLote: n, departamentosHabilitados: departamentos });
      setGuardado(true);
      onGuardado(nueva);
    } catch (e) {
      setErrorGuardar(mensajeErrorFijo(e, "Revisa tu conexión e intenta de nuevo.", ERRORES_CONFIGURACION));
    } finally {
      setGuardando(false);
    }
  }

  return (
    <form noValidate onSubmit={(ev) => void guardar(ev)} className="flex flex-col gap-5" aria-label="Configuración contractual">
      <div className="flex max-w-md flex-col gap-1">
        <label htmlFor={`${id}-maximo`} className={ETIQUETA_CLS}>
          Máximo de placas por lote contratado
        </label>
        <input
          id={`${id}-maximo`}
          type="number"
          inputMode="numeric"
          min={1}
          max={MAXIMO_ABSOLUTO_LOTE}
          step={1}
          value={maximo}
          onChange={(e) => setMaximo(e.target.value)}
          disabled={guardando}
          aria-invalid={errorMaximo ? true : undefined}
          aria-describedby={errorMaximo ? errorMaximoId : undefined}
          className={`${CAMPO_CLS} ${CAMPO_INVALIDO_CLS} tabular-nums`}
        />
        {errorMaximo && (
          <p id={errorMaximoId} className={ERROR_CLS}>
            {errorMaximo}
          </p>
        )}
      </div>
      <fieldset className="flex flex-col gap-3" disabled={guardando}>
        <legend className={`${ETIQUETA_CLS} mb-1`}>Departamentos habilitados</legend>
        <div className="flex items-center gap-3">
          <Interruptor checked={todos} onChange={setTodos} aria-labelledby={todosId} />
          <span id={todosId} className="text-sm text-flit-primary">
            Todos los departamentos
          </span>
        </div>
        {configuracion.departamentosDisponibles.length > 0 && (
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3">
            {configuracion.departamentosDisponibles.map((d) => (
              <label key={d.codigo} className={OPCION_CLS}>
                <input
                  type="checkbox"
                  className={MARCA_CLS}
                  checked={todos || elegidos.includes(d.codigo)}
                  disabled={todos}
                  onChange={() => alternar(d.codigo)}
                />
                {d.nombre}
              </label>
            ))}
          </div>
        )}
      </fieldset>
      {errorGuardar && (
        <InlineAlert tone="error" title="No se guardó la configuración">
          {errorGuardar}
        </InlineAlert>
      )}
      <div role="status" aria-live="polite" aria-atomic="true">
        {guardado && (
          <InlineAlert tone="success" vivo={false}>
            Configuración guardada.
          </InlineAlert>
        )}
      </div>
      <div className="flex justify-end">
        <Button type="submit" size="sm" cargando={guardando}>
          Guardar contrato
        </Button>
      </div>
    </form>
  );
}
