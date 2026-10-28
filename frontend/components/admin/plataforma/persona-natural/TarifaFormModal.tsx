"use client";

// HU #14074 AC3 — Crear una tarifa o versionar una existente. Modal normal (no lateral): lote por concepto con una
// misma fecha de inicio (hoy o futura). Los valores son pesos enteros; un valor negativo o no numérico se marca en su
// campo. Una excepción por organismo se registra eligiendo el organismo sobre el mismo tipo (prevalece sobre el valor
// general). Al versionar, el ámbito (tipo y organismo) queda fijo y solo se envían los conceptos que cambiaron.
import { useMemo, useState } from "react";
import { ReceiptText, Save } from "lucide-react";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { formatFechaCalendario } from "@/lib/format/date";
import { Modal } from "@/components/atom/Modal";
import { SearchableSelect } from "@/components/atom/SearchableSelect";
import {
  CONCEPTOS_QUE_EXIGEN_TIPO,
  CONCEPTOS_TARIFA,
  CONCEPTO_LABEL,
  formatPesos,
  hoyColombiaIso,
  parsePesos,
  registrarTarifasPn,
  type ConceptoTarifa,
  type GrupoTarifa,
  type TarifaPn,
} from "@/lib/api/admin-pn-tarifas";
import type { OrganismoPn, TipoPn } from "@/lib/api/admin-pn-config";
import { PN_FIELD_ERROR_CLS, PN_HINT_CLS, PN_INPUT_CLS, PN_INPUT_ERROR_CLS, PN_LABEL_CLS } from "./pn-ui";
import { PnButton } from "./pn-componentes";
import { pnError } from "./pnErrors";

export type TarifaFormMode = { kind: "create" } | { kind: "version"; grupo: GrupoTarifa };

export interface TarifaFormModalProps {
  mode: TarifaFormMode;
  tipos: readonly TipoPn[];
  organismos: readonly OrganismoPn[];
  onClose: () => void;
  /** Se invoca con las versiones nuevas que devolvió la API. */
  onSaved: (nuevas: TarifaPn[]) => void;
}

type Errores = Partial<Record<ConceptoTarifa | "tipo" | "fecha" | "valores" | "general", string>>;

export function TarifaFormModal({ mode, tipos, organismos, onClose, onSaved }: TarifaFormModalProps) {
  const versionando = mode.kind === "version";
  const grupo = versionando ? mode.grupo : null;
  const hoy = useMemo(() => hoyColombiaIso(), []);

  const [tipoId, setTipoId] = useState(grupo?.tipoTramiteId ?? "");
  const [organismoId, setOrganismoId] = useState(grupo?.organismoId ?? "");
  const [fecha, setFecha] = useState(hoy);
  const [valores, setValores] = useState<Record<ConceptoTarifa, string>>(() => {
    const inicial = {} as Record<ConceptoTarifa, string>;
    for (const c of CONCEPTOS_TARIFA) {
      const actual = grupo?.porConcepto[c];
      inicial[c] = actual ? String(actual.valorCop) : "";
    }
    return inicial;
  });
  const [errores, setErrores] = useState<Errores>({});
  const [busy, setBusy] = useState(false);

  const opcionesOrganismo = useMemo(
    () => organismos.map((o) => ({ value: o.id, label: o.nombre, hint: `${o.municipio} · ${o.departamento}` })),
    [organismos],
  );

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const nuevos: Errores = {};
    const enviar: { concepto: ConceptoTarifa; valorCop: number }[] = [];

    if (!fecha) nuevos.fecha = "Indica desde qué fecha rige la tarifa.";
    else if (fecha < hoy) nuevos.fecha = "La fecha de inicio no puede ser anterior a hoy.";

    for (const concepto of CONCEPTOS_TARIFA) {
      const texto = valores[concepto];
      if (texto.trim() === "") continue;
      const r = parsePesos(texto);
      if (!r.ok) {
        nuevos[concepto] = r.mensaje;
        continue;
      }
      // Al versionar solo viajan los conceptos que cambiaron: repetir el vigente chocaría con su propia vigencia.
      const vigente = grupo?.porConcepto[concepto]?.valorCop;
      if (versionando && vigente === r.valor) continue;
      if (!tipoId && CONCEPTOS_QUE_EXIGEN_TIPO.includes(concepto)) {
        nuevos.tipo = "Los derechos de tránsito y el servicio digital se registran siempre para un trámite concreto: elige el trámite.";
      }
      enviar.push({ concepto, valorCop: r.valor });
    }

    if (enviar.length === 0 && Object.keys(nuevos).length === 0) {
      nuevos.valores = versionando
        ? "Cambia al menos un valor para registrar el cambio."
        : "Escribe el valor de al menos un cargo.";
    }
    setErrores(nuevos);
    if (Object.keys(nuevos).length > 0) return;

    setBusy(true);
    try {
      const nuevas = await registrarTarifasPn({
        tipoTramiteId: tipoId || null,
        organismoId: organismoId || null,
        vigenteDesde: fecha,
        valores: enviar,
      });
      onSaved(nuevas);
    } catch (err) {
      const info = pnError(err);
      // `tarifa_solapada` y la fecha pasada se marcan en el campo de la fecha; el resto, arriba del pie.
      if (info.code === "tarifa_solapada" || info.code === "fecha_inicio_pasada") {
        setErrores({ fecha: info.message });
      } else {
        setErrores({ general: info.message });
      }
    } finally {
      setBusy(false);
    }
  }

  const actualizaValor = (concepto: ConceptoTarifa, texto: string) =>
    setValores((v) => ({ ...v, [concepto]: texto }));

  return (
    <Modal
      open
      onClose={onClose}
      busy={busy}
      title={versionando ? "Cambiar valores de la tarifa" : "Nueva tarifa"}
      description={
        versionando
          ? `${grupo?.tipoTramiteNombre ?? "Todos los trámites"} · ${grupo?.organismoNombre ?? "Todos los organismos"}`
          : "Elige el trámite, escribe los valores en pesos y la fecha desde la que rigen."
      }
      icon={ReceiptText}
      size="lg"
      footer={
        <div className="flex justify-end gap-2">
          <PnButton variant="secondary" onClick={onClose} disabled={busy}>
            Cancelar
          </PnButton>
          <PnButton type="submit" form="pn-tarifa-form" variant="primary" icon={Save} loading={busy}>
            Guardar tarifa
          </PnButton>
        </div>
      }
    >
      <form id="pn-tarifa-form" onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
        <InlineAlert tone="info">
          Las cotizaciones ya emitidas no cambian: la tarifa nueva solo aplica desde su fecha de inicio. La tarifa
          anterior deja de regir el día anterior.
        </InlineAlert>

        <div className="grid gap-4 sm:grid-cols-2">
          <label className={PN_LABEL_CLS}>
            Trámite
            <select
              value={tipoId}
              onChange={(e) => setTipoId(e.target.value)}
              disabled={versionando}
              aria-invalid={Boolean(errores.tipo)}
              className={`${PN_INPUT_CLS} ${errores.tipo ? PN_INPUT_ERROR_CLS : ""}`}
            >
              <option value="">Todos los trámites (solo cargos adicionales)</option>
              {tipos.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.nombre}
                </option>
              ))}
            </select>
            {errores.tipo && (
              <span role="alert" className={PN_FIELD_ERROR_CLS}>
                {errores.tipo}
              </span>
            )}
          </label>

          <SearchableSelect
            label="Organismo"
            options={opcionesOrganismo}
            value={organismoId}
            onChange={setOrganismoId}
            defaultLabel="Todos los organismos (valor general)"
            disabled={versionando}
            placeholder="Buscar organismo…"
          />
        </div>
        {!versionando && (
          <p className={PN_HINT_CLS}>
            Si el valor es distinto en un organismo, elígelo aquí: su valor tiene prioridad sobre el valor general del trámite.
          </p>
        )}

        <label className={PN_LABEL_CLS}>
          Rige desde
          <input
            type="date"
            value={fecha}
            min={hoy}
            onChange={(e) => setFecha(e.target.value)}
            aria-invalid={Boolean(errores.fecha)}
            className={`${PN_INPUT_CLS} sm:max-w-[220px] ${errores.fecha ? PN_INPUT_ERROR_CLS : ""}`}
          />
          {errores.fecha ? (
            <span role="alert" className={PN_FIELD_ERROR_CLS}>
              {errores.fecha}
            </span>
          ) : (
            fecha && (
              <span className={PN_HINT_CLS}>
                Desde el {formatFechaCalendario(fecha)} el portal cotiza con estos valores.
              </span>
            )
          )}
        </label>

        <fieldset className="flex flex-col gap-3">
          <legend className="mb-1 text-sm font-semibold text-flit-primary dark:text-white">Valores en pesos colombianos</legend>
          <div className="grid gap-3 sm:grid-cols-2">
            {CONCEPTOS_TARIFA.map((concepto) => {
              const actual = grupo?.porConcepto[concepto];
              return (
                <label key={concepto} className={PN_LABEL_CLS}>
                  {CONCEPTO_LABEL[concepto]}
                  <input
                    inputMode="numeric"
                    autoComplete="off"
                    value={valores[concepto]}
                    onChange={(e) => actualizaValor(concepto, e.target.value)}
                    onBlur={() => {
                      const r = parsePesos(valores[concepto]);
                      if (r.ok) actualizaValor(concepto, formatPesos(r.valor).slice(2));
                    }}
                    placeholder={versionando ? "Sin cambios" : "Ej.: 250.000"}
                    aria-invalid={Boolean(errores[concepto])}
                    className={`${PN_INPUT_CLS} ${errores[concepto] ? PN_INPUT_ERROR_CLS : ""}`}
                  />
                  {actual && versionando && (
                    <span className={PN_HINT_CLS}>Hoy: {formatPesos(actual.valorCop)}</span>
                  )}
                  {errores[concepto] && (
                    <span role="alert" className={PN_FIELD_ERROR_CLS}>
                      {errores[concepto]}
                    </span>
                  )}
                </label>
              );
            })}
          </div>
          {errores.valores && (
            <p role="alert" className={PN_FIELD_ERROR_CLS}>
              {errores.valores}
            </p>
          )}
        </fieldset>

        {errores.general && <InlineAlert tone="error">{errores.general}</InlineAlert>}
      </form>
    </Modal>
  );
}
