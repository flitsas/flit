import { useId } from "react";
import { CheckCircle2, Clock, FileCheck2, XCircle } from "lucide-react";
import { fechaDelDato } from "@/components/tablero/CeldaSemaforo";
import {
  fechaCorta,
  textoTipoError,
  type EstadoPersona,
  type PersonaDetalle,
  type ResultadoRues,
  type ResultadoRuntPersona,
  type ResultadoSimitDetalle,
} from "@/lib/tablero/detalle";
import { Dato, Etiqueta, ListaDatos, TARJETA_DETALLE_CLS, TEXTO_SECUNDARIO_CLS, Vacio, type TonoEtiqueta } from "./comun";
import { DetalleSimit } from "./DetalleSimit";

const ROLES = { PROPIETARIO: "Propietario", LOCATARIO: "Locatario" } as const;

const ESTADOS: Record<EstadoPersona, { tono: TonoEtiqueta; texto: string; Icono: typeof CheckCircle2 }> = {
  OK: { tono: "success", texto: "Consultada", Icono: CheckCircle2 },
  ERROR: { tono: "danger", texto: "Error", Icono: XCircle },
  SIN_RESULTADO: { tono: "neutral", texto: "Sin resultado", Icono: Clock },
};

const CONSULTAS = {
  RUNT_PERSONA: "Consultado en el RUNT",
  RUES: "Consultado en el RUES (persona jurídica)",
  SIMIT: "Consultado en el SIMIT",
} as const;

function ResultadoRuntPersonaVista({ resultado }: { resultado: ResultadoRuntPersona }) {
  return (
    <ListaDatos>
      <Dato rotulo="Inscripción">{resultado.inscrito ? "Inscrito en el RUNT" : "No inscrito en el RUNT"}</Dato>
      <Dato rotulo="Estado del conductor">{resultado.estado_conductor ?? "—"}</Dato>
      <Dato rotulo="Licencias">
        {resultado.licencias.length === 0 ? (
          "Sin licencias"
        ) : (
          <ul className="flex flex-col gap-0.5">
            {resultado.licencias.map((l, i) => (
              <li key={i}>{`${l.categoria ?? "—"} · ${l.estado ?? "—"} · vigente hasta ${fechaCorta(l.vigente_hasta)}`}</li>
            ))}
          </ul>
        )}
      </Dato>
    </ListaDatos>
  );
}

function ResultadoRuesVista({ resultado }: { resultado: ResultadoRues }) {
  return (
    <ListaDatos>
      <Dato rotulo="Matrícula mercantil">{resultado.encontrada ? "Encontrada" : "No encontrada en el RUES"}</Dato>
      <Dato rotulo="Estado de la matrícula">{resultado.estado_matricula ?? "—"}</Dato>
      <Dato rotulo="Última renovación">{resultado.ultima_renovacion ?? "—"}</Dato>
    </ListaDatos>
  );
}

function ResultadoPersona({ persona, titulo }: { persona: PersonaDetalle; titulo: string }) {
  const r = persona.resultado;
  if (!r) return null;
  if (persona.consulta === "RUES" || "encontrada" in r) return <ResultadoRuesVista resultado={r as ResultadoRues} />;
  if (persona.consulta === "SIMIT" || "comparendos" in r) return <DetalleSimit resultado={r as ResultadoSimitDetalle} titulo={`Comparendos de ${titulo}`} />;
  return <ResultadoRuntPersonaVista resultado={r as ResultadoRuntPersona} />;
}

/**
 * Sección de una persona (HU #13801, F06-C1/C2): rol y orden, tipo de documento y documento enmascarado TAL CUAL llega
 * (nunca se reconstruye), rótulo «Declarado por el cliente» si aplica, estado (OK, error con su texto o sin resultado) y el
 * resultado de lo que se consultó: RUNT, RUES (NIT, decisión Q2) o SIMIT. Sin nombres ni indicador de origen.
 */
export function SeccionPersona({ persona }: { persona: PersonaDetalle }) {
  const idTitulo = useId();
  const titulo = `${ROLES[persona.rol]} ${persona.orden}`;
  const estado = ESTADOS[persona.estado];
  return (
    <section aria-labelledby={idTitulo} className={`${TARJETA_DETALLE_CLS} flex flex-col gap-3`}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="flex flex-col gap-1">
          <h3 id={idTitulo} className="text-sm font-semibold text-flit-primary dark:text-white">
            {titulo}
          </h3>
          <p className="flex flex-wrap items-center gap-2 text-sm text-flit-primary dark:text-white">
            <span className={TEXTO_SECUNDARIO_CLS}>{`Documento ${persona.tipo_documento}`}</span>
            <span className="font-mono font-semibold tracking-wider">{persona.documento_enmascarado}</span>
            {persona.declarado_por_cliente && (
              <Etiqueta tono="info">
                <FileCheck2 className="h-3.5 w-3.5" aria-hidden="true" />
                Declarado por el cliente
              </Etiqueta>
            )}
          </p>
        </div>
        <Etiqueta tono={estado.tono}>
          <estado.Icono className="h-3.5 w-3.5" aria-hidden="true" />
          {estado.texto}
        </Etiqueta>
      </div>
      {persona.consulta && (
        <p className={TEXTO_SECUNDARIO_CLS}>
          {CONSULTAS[persona.consulta]}
          {persona.consultado_en ? ` · ${fechaDelDato(persona.consultado_en)}` : ""}
        </p>
      )}
      {persona.estado === "ERROR" && <p className="text-sm font-medium text-[var(--badge-pending-fg)]">{textoTipoError(persona.tipo_error)}</p>}
      {persona.estado === "OK" && <ResultadoPersona persona={persona} titulo={titulo} />}
    </section>
  );
}

/** Una sección por persona del rol, en orden; sin personas, un estado vacío claro. */
export function SeccionesPersonas({ personas, rol }: { personas: PersonaDetalle[]; rol: "PROPIETARIO" | "LOCATARIO" }) {
  if (personas.length === 0) return <Vacio>{`El vehículo no tiene ${rol === "PROPIETARIO" ? "propietarios" : "locatarios"} para esta consulta.`}</Vacio>;
  return (
    <div className="flex flex-col gap-3">
      {personas.map((p) => (
        <SeccionPersona key={`${p.rol}-${p.orden}`} persona={p} />
      ))}
    </div>
  );
}
