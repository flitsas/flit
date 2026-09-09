"use client";

/**
 * Bloque de vehículo con captura «placa primero» — HU #12209 (Feature #12201, CF-25).
 *
 * <p>La placa es la llave: se teclea, se consulta y el RUNT devuelve <b>12 de las 13 variables de
 * vehículo</b> del anexo normativo (`docs/plantilla-transferencia-dominio.md` §5.1). La número 13,
 * el <b>número de licencia de tránsito</b>, no la devuelve ninguna consulta —está en el cartón
 * físico, no en el RUNT— y por eso no aparece bloqueada ni marcada como hidratada.</p>
 *
 * <p>De los 12 hidratados, `color` y `tipoCarroceria` quedan <b>editables sin acción previa</b>
 * (decisión del PO, adenda §15.1) y los otros 10 quedan bloqueados con una acción explícita para
 * liberar uno.</p>
 *
 * <p>Si la fuente se cae, este bloque muestra su error con opción de reintentar y <b>todos los
 * campos siguen editables</b>: la generación del documento no depende de que el RUNT responda.</p>
 */
import { RefreshCw, Search } from "lucide-react";
import type {
  TransferValidationIssue,
  TransferVehiculoInput,
} from "@/lib/api/types-generacion-documental";
import { CampoPrellenado } from "./CampoPrellenado";
import { CAMPOS_VEHICULO_SIEMPRE_MANUALES } from "./prefill-hidratacion";
import type { PrefillBloque } from "./useEncadenamientoPrefill";

/** Las 13 variables del anexo §5.1, en el orden en que las lista la cláusula PRIMERA del documento. */
export const CAMPOS_VEHICULO: { key: keyof TransferVehiculoInput; id: string; label: string }[] = [
  { key: "placa", id: "tf-placa", label: "Placa" },
  { key: "marca", id: "tf-marca", label: "Marca" },
  { key: "linea", id: "tf-linea", label: "Línea" },
  { key: "modeloAnio", id: "tf-modelo", label: "Año modelo" },
  { key: "claseVehiculo", id: "tf-clase", label: "Clase" },
  { key: "tipoCarroceria", id: "tf-carroceria", label: "Carrocería" },
  { key: "color", id: "tf-color", label: "Color(es)" },
  { key: "noMotor", id: "tf-motor", label: "Motor No." },
  { key: "noChasis", id: "tf-chasis", label: "Chasis / VIN No." },
  { key: "noSerie", id: "tf-serie", label: "Serie No." },
  { key: "servicio", id: "tf-servicio", label: "Servicio" },
  { key: "noLicenciaTransito", id: "tf-licencia", label: "Licencia de tránsito No." },
  { key: "organismoTransito", id: "tf-organismo", label: "Organismo de tránsito" },
];

export interface VehiculoPrefillFieldsProps {
  valores: TransferVehiculoInput;
  onChange: (key: keyof TransferVehiculoInput, valor: string) => void;
  errorDe: (field: string) => TransferValidationIssue[];
  prefill: PrefillBloque;
}

export function VehiculoPrefillFields({
  valores,
  onChange,
  errorDe,
  prefill,
}: VehiculoPrefillFieldsProps) {
  const placaVacia = !(valores.placa ?? "").trim();
  const consultando = prefill.estado === "consultando";

  return (
    <fieldset className="rounded-2xl border p-4" data-testid="transferencia-vehiculo">
      <legend className="px-1 text-[11px] font-semibold uppercase opacity-70">Vehículo</legend>

      <p className="mb-3 text-[11px] opacity-80">
        Escribe la placa y consulta el RUNT: el bloque se diligencia solo y los datos consultados
        quedan bloqueados para que no se pisen por error. El número de licencia de tránsito no lo
        devuelve ninguna consulta —está en la licencia física— y siempre se captura a mano.
      </p>

      <div className="mb-3 flex flex-wrap items-center gap-2">
        <button
          type="button"
          data-testid="tf-consultar-placa"
          onClick={() => void prefill.ejecutar()}
          disabled={placaVacia || consultando}
          aria-describedby="tf-prefill-vehiculo-estado"
          className="flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
          style={{ backgroundColor: "#557EFF" }}
        >
          <Search className="h-3.5 w-3.5" aria-hidden="true" />
          {consultando ? "Consultando el RUNT…" : "Consultar la placa en el RUNT"}
        </button>

        {/*
          Región viva del bloque: los cuatro estados (vacío, cargando, error y lleno) se anuncian
          por texto, nunca solo por color (CF-22).
        */}
        <p
          id="tf-prefill-vehiculo-estado"
          data-testid="tf-prefill-vehiculo-estado"
          role="status"
          aria-live="polite"
          className="text-[11px] opacity-80"
        >
          {prefill.estado === "idle"
            ? "Sin consultar: todos los campos son de captura manual."
            : null}
          {consultando ? "Consultando el RUNT por placa…" : null}
          {prefill.estado === "hidratado"
            ? "Datos traídos del RUNT. Color y carrocería quedan editables porque el organismo de tránsito los confronta contra la licencia de tránsito, no contra el RUNT."
            : null}
          {prefill.estado === "sin-antecedente"
            ? "La placa no tiene antecedente en la fuente consultada: completa el bloque manualmente."
            : null}
          {prefill.estado === "error"
            ? "La consulta falló: todos los campos siguen editables y el documento se puede generar igual."
            : null}
        </p>
      </div>

      {prefill.estado === "error" ? (
        <div
          role="alert"
          data-testid="tf-prefill-vehiculo-error"
          className="mb-3 flex flex-wrap items-center gap-2 rounded-xl border border-red-300 bg-red-50 p-3 text-[11px] text-red-800"
        >
          <span>No se pudo consultar el vehículo. {prefill.error}</span>
          <button
            type="button"
            onClick={() => void prefill.ejecutar()}
            className="flex items-center gap-1 rounded-lg border border-red-300 px-2 py-1 font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          >
            <RefreshCw className="h-3 w-3" aria-hidden="true" />
            Reintentar la consulta del vehículo
          </button>
        </div>
      ) : null}

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {CAMPOS_VEHICULO.map((campo) => (
          <CampoPrellenado
            key={campo.key}
            id={campo.id}
            label={campo.label}
            value={valores[campo.key] ?? ""}
            onChange={(valor) => {
              prefill.marcarTocado(campo.key);
              onChange(campo.key, valor);
            }}
            issues={errorDe(`vehiculo.${campo.key}`)}
            // Un campo siempre manual jamás recibe estado de hidratación, aunque la respuesta lo
            // trajera: marcarlo mentiría sobre su procedencia.
            hidratado={
              CAMPOS_VEHICULO_SIEMPRE_MANUALES.includes(campo.key)
                ? undefined
                : prefill.hidratados[campo.key]
            }
            discrepancia={prefill.discrepancias.find((d) => d.campo === campo.key)}
            onLiberar={() => prefill.liberar(campo.key)}
            onAdoptarValorFuente={() => prefill.adoptarValorFuente(campo.key)}
            onDescartarDiscrepancia={() => prefill.descartarDiscrepancia(campo.key)}
            transformar={campo.key === "placa" ? (v) => v.toUpperCase() : undefined}
          />
        ))}
      </div>
    </fieldset>
  );
}
