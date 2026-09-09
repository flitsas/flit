"use client";

/**
 * Bloque de una parte compareciente con prellenado encadenado — HU #12209 (Feature #12201, CF-25).
 *
 * <p><b>La cadena depende del tipo de parte y se enuncia en pantalla</b>, no solo en el código:</p>
 * <ul>
 *   <li><b>Persona jurídica:</b> directorio de representantes legales del tenant y, solo si no
 *       responde, RUES. <b>El RUES no devuelve al representante legal</b>: certifica la
 *       <i>facultad</i> de representación, no la persona. Cuando la fuente efectiva es RUES llegan
 *       razón social, domicilio y matrícula mercantil, y el representante legal y su documento
 *       quedan de captura manual. La interfaz no promete lo contrario.</li>
 *   <li><b>Persona natural:</b> RUNT persona y, si no responde, `contact-lookup`. Ese endpoint
 *       <b>nunca devuelve nombre ni documento</b> por contrato: si es la fuente efectiva, se
 *       hidrata el domicilio y el nombre queda manual.</li>
 * </ul>
 *
 * <p>El <b>DV del NIT se muestra calculado</b> y no se captura: en cliente es solo visual
 * (`lib/documentos/nit-dv.ts`); el backend es la fuente de verdad y el formulario no lo envía.</p>
 */
import { RefreshCw, Search } from "lucide-react";
import { calcularDigitoVerificacion } from "@/lib/documentos/nit-dv";
import type {
  TransferParteInput,
  TransferValidationIssue,
} from "@/lib/api/types-generacion-documental";
import { CampoPrellenado, FIELD_CLASS } from "./CampoPrellenado";
import {
  CADENA_PERSONA_JURIDICA,
  CADENA_PERSONA_NATURAL,
  etiquetaFuente,
  type PrefillBloque,
} from "./useEncadenamientoPrefill";

export type ParteId = "transferente" | "adquirente";

const TIPOS_DOC = [
  { value: "CC", label: "CC" },
  { value: "CE", label: "CE" },
  { value: "PAS", label: "Pasaporte" },
  { value: "NIT", label: "NIT" },
];

/** Campos de texto de la parte que el prellenado puede hidratar (anexo §5.2 y §5.3). */
const CAMPOS_TEXTO: { key: keyof TransferParteInput; sufijo: string; label: string; soloPJ?: boolean }[] = [
  { key: "nombreRazonSocial", sufijo: "nombre", label: "Nombre o razón social" },
  { key: "domicilio", sufijo: "domicilio", label: "Ciudad de domicilio" },
  { key: "representanteLegal", sufijo: "rl", label: "Representante legal", soloPJ: true },
  { key: "ccRepresentanteLegal", sufijo: "ccrl", label: "C.C. del representante legal", soloPJ: true },
];

export interface ParteFieldsProps {
  parte: ParteId;
  titulo: string;
  valores: TransferParteInput;
  onChange: (key: keyof TransferParteInput, valor: string) => void;
  errorDe: (field: string) => TransferValidationIssue[];
  prefill: PrefillBloque;
}

export function ParteFields({
  parte,
  titulo,
  valores,
  onChange,
  errorDe,
  prefill,
}: ParteFieldsProps) {
  const esJuridica = valores.tipoPersona === "PJ";
  const cadena = esJuridica ? CADENA_PERSONA_JURIDICA : CADENA_PERSONA_NATURAL;
  const documento = (valores.numeroDoc ?? "").trim();
  const consultando = prefill.estado === "consultando";
  // El DV solo tiene sentido sobre un NIT. Se muestra; nunca se captura ni se envía.
  const dv = esJuridica ? (prefill.dv ?? calcularDigitoVerificacion(documento)) : null;

  const campoId = (sufijo: string) => `tf-${parte}-${sufijo}`;

  return (
    <fieldset className="rounded-2xl border p-4" data-testid={`transferencia-parte-${parte}`}>
      <legend className="px-1 text-[11px] font-semibold uppercase opacity-70">{titulo}</legend>

      <p className="mb-2 text-[11px] opacity-80" data-testid={`tf-${parte}-cadena`}>
        Se consulta en orden:{" "}
        {cadena.map((fuente, indice) => (
          <span key={fuente}>
            {indice > 0 ? " · " : ""}
            {indice + 1}. {etiquetaFuente(fuente)}
          </span>
        ))}
        .{" "}
        {esJuridica
          ? "El RUES certifica la facultad de representación, no la persona: si el directorio no responde, el representante legal y su documento se capturan a mano."
          : "Los datos de contacto no devuelven nombre ni documento: si el RUNT no responde, el nombre se captura a mano."}
      </p>

      <div className="mb-3 flex flex-wrap items-center gap-2">
        <button
          type="button"
          data-testid={`tf-${parte}-consultar`}
          onClick={() => void prefill.ejecutar()}
          disabled={!documento || consultando}
          aria-describedby={`tf-${parte}-prefill-estado`}
          className="flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
          style={{ backgroundColor: "#557EFF" }}
        >
          <Search className="h-3.5 w-3.5" aria-hidden="true" />
          {consultando
            ? "Consultando…"
            : esJuridica
              ? "Consultar el NIT"
              : "Consultar el documento"}
        </button>

        <p
          id={`tf-${parte}-prefill-estado`}
          data-testid={`tf-${parte}-prefill-estado`}
          role="status"
          aria-live="polite"
          className="text-[11px] opacity-80"
        >
          {prefill.estado === "idle" ? "Sin consultar: captura manual." : null}
          {consultando ? "Consulta en curso…" : null}
          {prefill.estado === "hidratado"
            ? `Datos traídos de ${etiquetaFuente(prefill.fuente)}.`
            : null}
          {prefill.estado === "sin-antecedente"
            ? "Sin antecedente en las fuentes consultadas: completa los datos manualmente."
            : null}
          {prefill.estado === "error"
            ? "La consulta falló: los campos siguen editables y el documento se puede generar igual."
            : null}
        </p>
      </div>

      {prefill.estado === "error" ? (
        <div
          role="alert"
          data-testid={`tf-${parte}-prefill-error`}
          className="mb-3 flex flex-wrap items-center gap-2 rounded-xl border border-red-300 bg-red-50 p-3 text-[11px] text-red-800"
        >
          <span>No se pudo consultar esta parte. {prefill.error}</span>
          <button
            type="button"
            onClick={() => void prefill.ejecutar()}
            className="flex items-center gap-1 rounded-lg border border-red-300 px-2 py-1 font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          >
            <RefreshCw className="h-3 w-3" aria-hidden="true" />
            Reintentar la consulta
          </button>
        </div>
      ) : null}

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
        <div>
          <label htmlFor={campoId("tipopersona")} className="mb-1 block text-[11px] font-medium">
            Tipo de persona
          </label>
          <select
            id={campoId("tipopersona")}
            className={FIELD_CLASS}
            value={valores.tipoPersona}
            onChange={(e) => {
              // Cambiar el tipo de parte cambia la cadena: lo hidratado por la anterior deja de
              // ser válido y el bloque vuelve a captura manual.
              prefill.reiniciar();
              onChange("tipoPersona", e.target.value);
            }}
          >
            <option value="PN">Persona natural</option>
            <option value="PJ">Persona jurídica</option>
          </select>
        </div>

        <CampoPrellenado
          id={campoId("nombre")}
          label="Nombre o razón social"
          value={valores.nombreRazonSocial ?? ""}
          onChange={(valor) => {
            prefill.marcarTocado("nombreRazonSocial");
            onChange("nombreRazonSocial", valor);
          }}
          issues={errorDe(`${parte}.nombreRazonSocial`)}
          hidratado={prefill.hidratados.nombreRazonSocial}
          discrepancia={prefill.discrepancias.find((d) => d.campo === "nombreRazonSocial")}
          onLiberar={() => prefill.liberar("nombreRazonSocial")}
          onAdoptarValorFuente={() => prefill.adoptarValorFuente("nombreRazonSocial")}
          onDescartarDiscrepancia={() => prefill.descartarDiscrepancia("nombreRazonSocial")}
        />

        <div>
          <label htmlFor={campoId("tipodoc")} className="mb-1 block text-[11px] font-medium">
            Tipo de documento
          </label>
          <select
            id={campoId("tipodoc")}
            className={FIELD_CLASS}
            value={valores.tipoDoc}
            onChange={(e) => onChange("tipoDoc", e.target.value)}
          >
            {TIPOS_DOC.map((tipo) => (
              <option key={tipo.value} value={tipo.value}>
                {tipo.label}
              </option>
            ))}
          </select>
        </div>

        {/*
          El número de documento es la LLAVE de la consulta, igual que la placa en el vehículo: se
          teclea siempre y no se hidrata desde una fuente que se consultó con él.
        */}
        <div>
          <label htmlFor={campoId("numerodoc")} className="mb-1 block text-[11px] font-medium">
            Número de documento
          </label>
          <input
            id={campoId("numerodoc")}
            className={FIELD_CLASS}
            value={valores.numeroDoc ?? ""}
            onChange={(e) => onChange("numeroDoc", e.target.value)}
            aria-describedby={
              errorDe(`${parte}.numeroDoc`).length ? `${campoId("numerodoc")}-error` : undefined
            }
          />
          {errorDe(`${parte}.numeroDoc`).map((issue) => (
            <p
              key={`${issue.code}-${issue.field}`}
              id={`${campoId("numerodoc")}-error`}
              role="alert"
              className="mt-1 text-[11px] text-red-700"
            >
              <span className="font-semibold">{issue.code}</span> — {issue.message}
            </p>
          ))}
        </div>

        {CAMPOS_TEXTO.filter((campo) => campo.key !== "nombreRazonSocial")
          .filter((campo) => !campo.soloPJ || esJuridica)
          .map((campo) => (
            <CampoPrellenado
              key={campo.key}
              id={campoId(campo.sufijo)}
              label={campo.label}
              value={valores[campo.key] ?? ""}
              onChange={(valor) => {
                prefill.marcarTocado(campo.key);
                onChange(campo.key, valor);
              }}
              issues={errorDe(`${parte}.${campo.key}`)}
              hidratado={prefill.hidratados[campo.key]}
              discrepancia={prefill.discrepancias.find((d) => d.campo === campo.key)}
              onLiberar={() => prefill.liberar(campo.key)}
              onAdoptarValorFuente={() => prefill.adoptarValorFuente(campo.key)}
              onDescartarDiscrepancia={() => prefill.descartarDiscrepancia(campo.key)}
            />
          ))}

        {esJuridica ? (
          <p className="self-end text-[11px] opacity-70" data-testid={`tf-${parte}-dv`}>
            El dígito de verificación del NIT lo calcula el sistema
            {dv ? <>: DV {dv}</> : null}.
          </p>
        ) : null}
      </div>
    </fieldset>
  );
}
