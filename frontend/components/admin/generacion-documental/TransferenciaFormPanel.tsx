"use client";

import { useCallback, useMemo, useState } from "react";
import { AlertTriangle, Info, ShieldAlert } from "lucide-react";
import {
  generateTransferenciaDocument,
  prefillPersonaJuridica,
  prefillPersonaNatural,
  prefillVehiculo,
} from "@/lib/api/admin-generacion-documental";
import { ApiError, ApiValidationError } from "@/lib/api/types";
import type {
  StandaloneDocumentScenario,
  TransferGenerateResult,
  TransferLeasingInput,
  TransferNegocioInput,
  TransferParteInput,
  TransferTituloJuridico,
  TransferValidationIssue,
  TransferVehiculoInput,
} from "@/lib/api/types-generacion-documental";
import { RegimenAplicableControl } from "./RegimenAplicableControl";
import { ParteFields, type ParteId } from "./ParteFields";
import { VehiculoPrefillFields, type PropietarioConsultaInput } from "./VehiculoPrefillFields";
import {
  CAMPOS_VEHICULO_SIEMPRE_EDITABLES,
  CAMPOS_VEHICULO_SIEMPRE_MANUALES,
} from "./prefill-hidratacion";
import { usePrefillBloque } from "./useEncadenamientoPrefill";
import {
  REGIMEN_NINGUNA_APLICA,
  permiteGenerar,
  type RegimenSeleccion,
} from "./transferencia-regimen";

const FIELD_CLASS =
  "w-full rounded-xl border px-3 py-2 text-xs focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]";

const TITULOS: { value: TransferTituloJuridico; label: string }[] = [
  { value: "COMPRAVENTA", label: "Compraventa" },
  { value: "DACION_EN_PAGO", label: "Dación en pago" },
  { value: "PERMUTA", label: "Permuta" },
  { value: "DONACION", label: "Donación" },
  { value: "OTRO", label: "Otro negocio traslaticio" },
];

/**
 * Los tres escenarios del anexo §3. Clasifican la <b>operación jurídica</b>, no la naturaleza de las
 * personas: en los tres pueden participar personas naturales o jurídicas.
 */
const ESCENARIOS: { value: StandaloneDocumentScenario; label: string; detalle: string }[] = [
  {
    value: "A",
    label: "A — Traspaso ordinario (art. 5.3.2.1)",
    detalle: "Transferente y adquirente comparecen y firman.",
  },
  {
    value: "B",
    label: "B — Transferencia unilateral de leasing (art. 5.3.2.2)",
    detalle:
      "La entidad financiera transfiere al locatario por su sola voluntad: firma únicamente la entidad y no se declara precio.",
  },
  {
    value: "C",
    label: "C — Entidad financiera a un tercero (art. 5.3.2.1, sin exenciones)",
    detalle:
      "El adquirente no es el locatario: no hereda ninguna exención del art. 5.3.2.2 y firman ambas partes.",
  },
];

const OPCIONES_COMPRA: { value: string; label: string }[] = [
  { value: "EJERCIDA", label: "Opción de compra ejercida" },
  { value: "AUTOMATICA", label: "Opción de compra automática (Parágrafo 1.º)" },
  { value: "TERMINACION_CONTRATO", label: "Terminación del contrato de leasing" },
];

/** Estado del formulario. Cadena vacía = «sin capturar», nunca `undefined`. */
interface TransferenciaFormState {
  /** Declaración del gate de régimen aplicable (CF-24). `""` = sin responder. */
  regimen: RegimenSeleccion;
  escenario: StandaloneDocumentScenario;
  vehiculo: Required<Pick<TransferVehiculoInput, "placa">> & TransferVehiculoInput;
  transferente: TransferParteInput;
  adquirente: TransferParteInput;
  negocio: TransferNegocioInput;
  leasing: TransferLeasingInput;
  gravamenActivo: boolean;
  tieneLevantamientoOAutorizacion: boolean;
}

const ESTADO_INICIAL: TransferenciaFormState = {
  // Sin responder: el botón de generar nace deshabilitado (CF-24).
  regimen: "",
  escenario: "A",
  vehiculo: {
    placa: "",
    marca: "",
    linea: "",
    modeloAnio: "",
    claseVehiculo: "",
    tipoCarroceria: "",
    color: "",
    noMotor: "",
    noChasis: "",
    noSerie: "",
    servicio: "",
    noLicenciaTransito: "",
    organismoTransito: "",
  },
  transferente: {
    tipoPersona: "PN",
    nombreRazonSocial: "",
    tipoDoc: "CC",
    numeroDoc: "",
    domicilio: "",
    representanteLegal: "",
    ccRepresentanteLegal: "",
  },
  adquirente: {
    tipoPersona: "PN",
    nombreRazonSocial: "",
    tipoDoc: "CC",
    numeroDoc: "",
    domicilio: "",
    representanteLegal: "",
    ccRepresentanteLegal: "",
  },
  negocio: {
    tituloJuridico: "",
    descripcionTitulo: "",
    precioLetras: "",
    precioNumeros: "",
    contraprestacionDescripcion: "",
    formaPago: "",
    asumeRetencionFuente: "SEGUN_LEY",
    asumeDerechosTramite: "COMPARTIDOS",
    asumeImpuestoVehiculo: "SEGUN_LEY",
    ciudadFirma: "",
    fechaFirma: "",
  },
  leasing: {
    transferenteEsEntidadFinanciera: false,
    noContratoLeasing: "",
    tipoOpcionCompra: "",
    fechaTerminacion: "",
    locatarioNombre: "",
    locatarioTipoDoc: "CC",
    locatarioNoDoc: "",
  },
  gravamenActivo: false,
  tieneLevantamientoOAutorizacion: false,
};

/** Remolques y semirremolques están exentos del impuesto sobre vehículos (Ley 488/1998). */
function esRemolque(clase?: string): boolean {
  return (clase ?? "").toUpperCase().includes("REMOLQUE");
}

/**
 * Campo con etiqueta y sus errores. <b>Vive fuera del componente a propósito</b>: declarado dentro,
 * React vería un tipo de componente nuevo en cada render, desmontaría el input y el usuario
 * perdería el foco —y los caracteres— al teclear. Es un defecto que solo se ve escribiendo más de
 * una letra.
 */
function CampoBase({
  id,
  label,
  issues,
  children,
}: {
  id: string;
  label: string;
  issues: TransferValidationIssue[];
  children: React.ReactNode;
}) {
  return (
    <div>
      <label htmlFor={id} className="mb-1 block text-[11px] font-medium">
        {label}
      </label>
      {children}
      {issues.map((issue) => (
        <p
          key={`${issue.code}-${issue.field}`}
          id={`${id}-error`}
          role="alert"
          className="mt-1 text-[11px] text-red-700"
        >
          <span className="font-semibold">{issue.code}</span> — {issue.message}
        </p>
      ))}
    </div>
  );
}

/**
 * Formulario de emisión del Documento de Transferencia de Dominio — <b>escenario A</b> (traspaso
 * ordinario, art. 5.3.2.1) — HU #12207, Feature #12201.
 *
 * <p><b>El aviso normativo va arriba y el botón de generar abajo</b> (CF-10). No es decoración: el
 * usuario tiene que haber pasado por la advertencia de que FLIT no garantiza la suficiencia
 * jurídica del documento ni su aprobación por el Organismo de Tránsito antes de llegar al botón.
 * El test de esta HU comprueba el orden en el DOM, no el estilo.</p>
 *
 * <p><b>Errores y avisos son cosas distintas</b> (anexo §6). Las VB bloqueantes vuelven en un 422 y
 * se pintan como error junto al campo; las prevalidaciones VA llegan en la respuesta 200 y se
 * pintan como aviso <b>después</b> de generar: dependen de RUNT, RUES, SOAT o SIMIT, que verifica
 * el OT, y no impiden emitir. Ningún mensaje repite el valor capturado.</p>
 *
 * <p><b>El control de régimen aplicable va antes del selector de escenario</b> (CF-24 / VB-07) y no
 * es un campo más: es un gate de exclusión del anexo §4.0. El botón de generar permanece
 * deshabilitado hasta que se responda, y declarar cualquiera de las once condiciones especiales
 * bloquea con un mensaje que cita el artículo. El backend rechaza el mismo payload con 422 y VB-07
 * aunque este control se omita: la interfaz explica, no autoriza.</p>
 *
 * <p><b>El escenario decide qué campos existen.</b> En B no hay bloque de adquirente ni campos de
 * precio —no se ocultan: no se declaran— porque el acto del art. 5.3.2.2 es unilateral y no tiene
 * contraprestación entre las partes del instrumento (VB-B-05, §10 regla #3). En A y C sí.</p>
 *
 * <p><b>Alcance.</b> Escenarios A, B y C. El prellenado «placa primero» con bloqueo anti-pisado
 * llega en HU-10; aquí todos los campos son de captura manual.</p>
 */
export function TransferenciaFormPanel() {
  const [form, setForm] = useState<TransferenciaFormState>(ESTADO_INICIAL);
  /**
   * Documento del propietario inscrito. Vive FUERA de `form` a propósito: es insumo de la consulta
   * al RUNT, no una variable del anexo, y no debe viajar en el cuerpo de la generación ni acabar
   * transcrito en el documento. El tipo por defecto es `CC`, igual que el paso «consulta» del
   * wizard.
   */
  const [propietario, setPropietario] = useState<PropietarioConsultaInput>({
    tipoDoc: "CC",
    numeroDoc: "",
  });
  const [enviando, setEnviando] = useState(false);
  const [errores, setErrores] = useState<TransferValidationIssue[]>([]);
  const [errorGeneral, setErrorGeneral] = useState<string | null>(null);
  const [resultado, setResultado] = useState<TransferGenerateResult | null>(null);

  const erroresPorCampo = useMemo(() => {
    const mapa = new Map<string, TransferValidationIssue[]>();
    for (const issue of errores) {
      mapa.set(issue.field, [...(mapa.get(issue.field) ?? []), issue]);
    }
    return mapa;
  }, [errores]);

  const setVehiculo = (key: keyof TransferVehiculoInput, value: string) =>
    setForm((prev) => ({ ...prev, vehiculo: { ...prev.vehiculo, [key]: value } }));

  const setPropietarioCampo = (campo: keyof PropietarioConsultaInput, valor: string) =>
    setPropietario((prev) => ({ ...prev, [campo]: valor }));

  const setParte = (parte: "transferente" | "adquirente", key: keyof TransferParteInput, value: string) =>
    setForm((prev) => ({ ...prev, [parte]: { ...prev[parte], [key]: value } }));

  const setNegocio = (key: keyof TransferNegocioInput, value: string) =>
    setForm((prev) => ({ ...prev, negocio: { ...prev.negocio, [key]: value } }));

  const setLeasing = (key: keyof TransferLeasingInput, value: string | boolean) =>
    setForm((prev) => ({ ...prev, leasing: { ...prev.leasing, [key]: value } }));

  // ── Prellenado encadenado (CF-25) ─────────────────────────────────────────────────────────────
  //
  // Un bloque, una consulta, un estado de error. El vehículo se hidrata por PLACA; cada parte, por
  // su documento y con la cadena que corresponde a su tipo de persona. Que una fuente falle no
  // apaga las demás y, sobre todo, no impide generar el documento.

  const aplicarVehiculo = useCallback(
    (parche: Record<string, string>) =>
      setForm((prev) => ({ ...prev, vehiculo: { ...prev.vehiculo, ...parche } })),
    [],
  );
  const aplicarParte = useCallback(
    (parte: ParteId) => (parche: Record<string, string>) =>
      setForm((prev) => ({ ...prev, [parte]: { ...prev[parte], ...parche } })),
    [],
  );

  const prefillVehiculoBloque = usePrefillBloque({
    // El documento del propietario NO es opcional en la práctica: el proveedor RUNT devuelve
    // «Se requiere documento del propietario para consulta por placa» y cero campos si falta, y el
    // prellenado no puede distinguir eso de «esta placa no tiene antecedente». El wizard lo impone
    // en su paso «consulta»; aquí se impone igual.
    consultar: () =>
      prefillVehiculo({
        placa: form.vehiculo.placa.trim().toUpperCase(),
        ownerDocumentType: propietario.tipoDoc,
        ownerDocumentNumber: propietario.numeroDoc.trim(),
      }),
    valores: form.vehiculo,
    onAplicar: aplicarVehiculo,
    siempreEditables: CAMPOS_VEHICULO_SIEMPRE_EDITABLES,
    siempreManuales: CAMPOS_VEHICULO_SIEMPRE_MANUALES,
  });

  // La cadena la elige el TIPO de parte, no el endpoint: jurídica → directorio de representantes
  // legales y luego RUES; natural → RUNT persona y luego contact-lookup.
  const consultaDeParte = (parte: ParteId) => () => {
    const datos = form[parte];
    const documento = (datos.numeroDoc ?? "").trim();
    return datos.tipoPersona === "PJ"
      ? prefillPersonaJuridica({ nit: documento })
      : prefillPersonaNatural({
          documentType: datos.tipoDoc ?? "CC",
          documentNumber: documento,
        });
  };

  const prefillTransferente = usePrefillBloque({
    consultar: consultaDeParte("transferente"),
    valores: form.transferente,
    onAplicar: aplicarParte("transferente"),
  });

  const prefillAdquirente = usePrefillBloque({
    consultar: consultaDeParte("adquirente"),
    valores: form.adquirente,
    onAplicar: aplicarParte("adquirente"),
  });

  const prefillDeParte = { transferente: prefillTransferente, adquirente: prefillAdquirente };

  const esUnilateral = form.escenario === "B";

  // CF-24 — sin declaración de régimen no se genera, y declarar una condición especial tampoco.
  const regimenPermiteGenerar = permiteGenerar(form.regimen);

  async function onSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setEnviando(true);
    setErrores([]);
    setErrorGeneral(null);
    setResultado(null);

    try {
      const respuesta = await generateTransferenciaDocument({
        // VB-05: exactamente un escenario, el elegido en el selector.
        escenarios: [form.escenario],
        vehiculo: form.vehiculo,
        transferente: form.transferente,
        // En el escenario B no se envía adquirente: el locatario no es parte del instrumento y sus
        // datos viajan en `leasing`, que alimenta las cláusulas declarativas (§9.2).
        adquirente: esUnilateral ? undefined : form.adquirente,
        negocio: esUnilateral
          ? {
              // Solo ciudad y fecha: el acto unilateral no declara título, precio ni cargas
              // fiscales entre las partes (VB-B-05).
              ciudadFirma: form.negocio.ciudadFirma,
              fechaFirma: form.negocio.fechaFirma,
            }
          : {
              ...form.negocio,
              // El impuesto no aplica a remolques ni semirremolques: no se manda quién lo asume.
              asumeImpuestoVehiculo: esRemolque(form.vehiculo.claseVehiculo)
                ? null
                : form.negocio.asumeImpuestoVehiculo,
            },
        gravamen: {
          gravamenActivo: form.gravamenActivo,
          tieneLevantamientoOAutorizacion: form.tieneLevantamientoOAutorizacion,
        },
        // CF-24 — la declaración y su fecha viajan al servidor, que las conserva en input_summary.
        regimenAplicable: {
          ningunaAplica: form.regimen === REGIMEN_NINGUNA_APLICA,
          condicionesDeclaradas:
            form.regimen === REGIMEN_NINGUNA_APLICA || form.regimen === "" ? [] : [form.regimen],
          declaredAt: new Date().toISOString(),
        },
        ...(esUnilateral ? { leasing: form.leasing } : {}),
      });

      setResultado(respuesta);
    } catch (error) {
      if (error instanceof ApiValidationError) {
        setErrores(error.errors as unknown as TransferValidationIssue[]);
      } else if (error instanceof ApiError) {
        setErrorGeneral(error.message);
      } else {
        setErrorGeneral("No se pudo generar el documento. Intenta nuevamente.");
      }
    } finally {
      setEnviando(false);
    }
  }

  const errorDe = (field: string) => erroresPorCampo.get(field) ?? [];


  return (
    <form onSubmit={onSubmit} className="flex flex-col gap-4" aria-labelledby="transferencia-form-titulo">
      <h2 id="transferencia-form-titulo" className="text-sm font-semibold" style={{ color: "#162744" }}>
        Transferencia de dominio — Documento soporte del traspaso
      </h2>

      {/*
        CF-10 — Aviso normativo. Va PRIMERO, antes de cualquier campo y muy por encima del botón
        de generar: el usuario no puede llegar a emitir sin haberlo tenido delante.
      */}
      <aside
        role="note"
        aria-labelledby="transferencia-aviso-titulo"
        data-testid="transferencia-aviso-normativo"
        className="flex gap-2 rounded-2xl border border-amber-300 bg-amber-50 p-3 text-[11px] text-amber-900"
      >
        <ShieldAlert className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
        <div>
          <p id="transferencia-aviso-titulo" className="font-semibold">
            FLIT no garantiza la suficiencia jurídica del documento ni su aprobación por el organismo
            de tránsito
          </p>
          <p className="mt-1">
            Este es un instrumento privado parametrizado. Su validez jurídica, su suficiencia como
            prueba de dominio y su admisión por el Organismo de Tránsito son responsabilidad de las
            partes y sus asesores jurídicos. El Organismo de Tránsito realiza validaciones propias
            (RUNT, SOAT, SIMIT, RTM y medidas judiciales) que FLIT no puede anticipar: generar el
            documento no equivale a aprobar el trámite.
          </p>
        </div>
      </aside>

      {errorGeneral ? (
        <p role="alert" className="rounded-xl border border-red-300 bg-red-50 p-3 text-[11px] text-red-800">
          {errorGeneral}
        </p>
      ) : null}

      {errores.length > 0 ? (
        <div
          role="alert"
          data-testid="transferencia-errores"
          className="rounded-2xl border border-red-300 bg-red-50 p-3 text-[11px] text-red-800"
        >
          <p className="flex items-center gap-1.5 font-semibold">
            <AlertTriangle className="h-3.5 w-3.5" aria-hidden="true" />
            No se generó el documento: hay validaciones normativas sin cumplir
          </p>
          <ul className="mt-1 list-disc pl-5">
            {errores.map((issue) => (
              <li key={`${issue.code}-${issue.field}`}>
                <span className="font-semibold">{issue.code}</span> · {issue.field} — {issue.message}
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      {resultado ? (
        <div
          data-testid="transferencia-resultado"
          className="rounded-2xl border border-emerald-300 bg-emerald-50 p-3 text-[11px] text-emerald-900"
        >
          <p className="font-semibold">Documento generado. Descárgalo desde el historial.</p>
          {resultado.advisories.length > 0 ? (
            <div className="mt-2" data-testid="transferencia-advisories">
              <p className="flex items-center gap-1.5 font-semibold">
                <Info className="h-3.5 w-3.5" aria-hidden="true" />
                Prevalidaciones pendientes de verificación por el organismo de tránsito (avisos, no
                errores)
              </p>
              <ul className="mt-1 list-disc pl-5">
                {resultado.advisories.map((advisory) => (
                  <li key={`${advisory.code}-${advisory.field}`}>
                    <span className="font-semibold">{advisory.code}</span> — {advisory.message}
                  </li>
                ))}
              </ul>
            </div>
          ) : null}
        </div>
      ) : null}

      {/*
        CF-24 / VB-07 — el gate de régimen aplicable va ANTES del selector de escenario, como en el
        anexo §4.0: primero se descarta que la operación sea un traspaso especial, y solo después
        tiene sentido preguntar cuál de los tres escenarios del art. 5.3.2.1/5.3.2.2 aplica.
      */}
      <RegimenAplicableControl
        seleccion={form.regimen}
        onChange={(regimen) => setForm((prev) => ({ ...prev, regimen }))}
      />

      <fieldset className="rounded-2xl border p-4" data-testid="transferencia-escenario">
        <legend className="px-1 text-[11px] font-semibold uppercase opacity-70">
          Escenario de la operación
        </legend>
        <div role="radiogroup" aria-label="Escenario de la operación" className="flex flex-col gap-1">
          {ESCENARIOS.map((escenario) => (
            <label
              key={escenario.value}
              htmlFor={`tf-escenario-${escenario.value}`}
              className="flex items-start gap-2 rounded-lg px-2 py-1.5 text-[11px] focus-within:ring-2 focus-within:ring-[#557EFF]"
            >
              <input
                id={`tf-escenario-${escenario.value}`}
                type="radio"
                name="tf-escenario"
                className="mt-0.5"
                value={escenario.value}
                checked={form.escenario === escenario.value}
                onChange={() =>
                  setForm((prev) => ({ ...prev, escenario: escenario.value }))
                }
              />
              <span>
                <span className="font-semibold">{escenario.label}</span>
                <span className="block opacity-70">{escenario.detalle}</span>
              </span>
            </label>
          ))}
        </div>
      </fieldset>

      {/*
        CF-25 — captura «placa primero». El bloque conoce sus 13 variables, cuál no la devuelve
        ninguna consulta y cuáles quedan editables por decisión del PO (adenda §15.1).
      */}
      <VehiculoPrefillFields
        valores={form.vehiculo}
        onChange={setVehiculo}
        errorDe={errorDe}
        prefill={prefillVehiculoBloque}
        propietario={propietario}
        onPropietarioChange={setPropietarioCampo}
      />

      {/*
        §9.2 — en el escenario B el bloque del adquirente NO se declara. No es un bloque oculto ni
        deshabilitado: la lista de partes tiene un solo elemento, igual que el documento tendrá un
        solo bloque de firma. Los datos del destinatario se capturan como antecedente de leasing.
      */}
      {(esUnilateral ? (["transferente"] as const) : (["transferente", "adquirente"] as const)).map((parte) => (
        <ParteFields
          key={parte}
          parte={parte}
          titulo={parte === "transferente" ? "Transferente" : "Adquirente"}
          valores={form[parte]}
          onChange={(key, valor) => setParte(parte, key, valor)}
          errorDe={errorDe}
          prefill={prefillDeParte[parte]}
        />
      ))}

      {esUnilateral ? (
        <fieldset className="rounded-2xl border p-4" data-testid="transferencia-leasing">
          <legend className="px-1 text-[11px] font-semibold uppercase opacity-70">
            Antecedente de leasing y destinatario
          </legend>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            <div className="sm:col-span-2 lg:col-span-3">
              <label htmlFor="tf-leasing-financiera" className="flex items-center gap-2 text-[11px]">
                <input
                  id="tf-leasing-financiera"
                  type="checkbox"
                  checked={form.leasing.transferenteEsEntidadFinanciera}
                  onChange={(e) => setLeasing("transferenteEsEntidadFinanciera", e.target.checked)}
                  aria-describedby={
                    errorDe("leasing.transferenteEsEntidadFinanciera").length
                      ? "tf-leasing-financiera-error"
                      : undefined
                  }
                />
                El transferente es un establecimiento bancario, compañía de financiamiento o de
                leasing (art. 5.3.2.2)
              </label>
              {errorDe("leasing.transferenteEsEntidadFinanciera").map((issue) => (
                <p
                  key={issue.code}
                  id="tf-leasing-financiera-error"
                  role="alert"
                  className="mt-1 text-[11px] text-red-700"
                >
                  <span className="font-semibold">{issue.code}</span> — {issue.message}
                </p>
              ))}
            </div>

            <CampoBase
              id="tf-leasing-contrato"
              label="No. del contrato de leasing"
              issues={errorDe("leasing.noContratoLeasing")}
            >
              <input
                id="tf-leasing-contrato"
                className={FIELD_CLASS}
                value={form.leasing.noContratoLeasing}
                onChange={(e) => setLeasing("noContratoLeasing", e.target.value)}
                aria-describedby={
                  errorDe("leasing.noContratoLeasing").length ? "tf-leasing-contrato-error" : undefined
                }
              />
            </CampoBase>

            <CampoBase
              id="tf-leasing-opcion"
              label="Causal de la transferencia"
              issues={errorDe("leasing.tipoOpcionCompra")}
            >
              <select
                id="tf-leasing-opcion"
                className={FIELD_CLASS}
                value={form.leasing.tipoOpcionCompra}
                onChange={(e) => setLeasing("tipoOpcionCompra", e.target.value)}
                aria-describedby={
                  errorDe("leasing.tipoOpcionCompra").length ? "tf-leasing-opcion-error" : undefined
                }
              >
                <option value="">Selecciona la causal</option>
                {OPCIONES_COMPRA.map((opcion) => (
                  <option key={opcion.value} value={opcion.value}>
                    {opcion.label}
                  </option>
                ))}
              </select>
            </CampoBase>

            <CampoBase id="tf-leasing-fecha" label="Fecha de terminación o de ejercicio" issues={[]}>
              <input
                id="tf-leasing-fecha"
                type="date"
                className={FIELD_CLASS}
                value={form.leasing.fechaTerminacion ?? ""}
                onChange={(e) => setLeasing("fechaTerminacion", e.target.value)}
              />
            </CampoBase>

            <CampoBase
              id="tf-leasing-locatario-nombre"
              label="Nombre o razón social del destinatario"
              issues={errorDe("leasing.locatarioNombre")}
            >
              <input
                id="tf-leasing-locatario-nombre"
                className={FIELD_CLASS}
                value={form.leasing.locatarioNombre}
                onChange={(e) => setLeasing("locatarioNombre", e.target.value)}
                aria-describedby={
                  errorDe("leasing.locatarioNombre").length
                    ? "tf-leasing-locatario-nombre-error"
                    : undefined
                }
              />
            </CampoBase>

            <CampoBase id="tf-leasing-locatario-tipodoc" label="Tipo de documento del destinatario" issues={[]}>
              <select
                id="tf-leasing-locatario-tipodoc"
                className={FIELD_CLASS}
                value={form.leasing.locatarioTipoDoc}
                onChange={(e) => setLeasing("locatarioTipoDoc", e.target.value)}
              >
                <option value="CC">CC</option>
                <option value="CE">CE</option>
                <option value="PAS">Pasaporte</option>
                <option value="NIT">NIT</option>
              </select>
            </CampoBase>

            <CampoBase
              id="tf-leasing-locatario-doc"
              label="Número de documento del destinatario"
              issues={errorDe("leasing.locatarioNoDoc")}
            >
              <input
                id="tf-leasing-locatario-doc"
                className={FIELD_CLASS}
                value={form.leasing.locatarioNoDoc}
                onChange={(e) => setLeasing("locatarioNoDoc", e.target.value)}
                aria-describedby={
                  errorDe("leasing.locatarioNoDoc").length ? "tf-leasing-locatario-doc-error" : undefined
                }
              />
            </CampoBase>
          </div>

          <p className="mt-2 text-[11px] opacity-80" data-testid="transferencia-sin-precio">
            La transferencia del art. 5.3.2.2 es un acto unilateral: no se declara precio ni
            contraprestación, y el documento lleva un solo bloque de firma, el de la entidad
            financiera. El destinatario no firma este documento ni el Formato Único.
          </p>
        </fieldset>
      ) : null}

      {esUnilateral ? (
        <fieldset className="rounded-2xl border p-4">
          <legend className="px-1 text-[11px] font-semibold uppercase opacity-70">
            Lugar y fecha de firma
          </legend>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            <CampoBase id="tf-ciudad" label="Ciudad de firma" issues={errorDe("negocio.ciudadFirma")}>
              <input id="tf-ciudad" className={FIELD_CLASS} value={form.negocio.ciudadFirma}
                onChange={(e) => setNegocio("ciudadFirma", e.target.value)} />
            </CampoBase>
            <CampoBase id="tf-fecha" label="Fecha de firma" issues={[]}>
              <input id="tf-fecha" type="date" className={FIELD_CLASS} value={form.negocio.fechaFirma}
                onChange={(e) => setNegocio("fechaFirma", e.target.value)} />
            </CampoBase>
          </div>
        </fieldset>
      ) : null}

      {esUnilateral ? null : (
      <fieldset className="rounded-2xl border p-4" data-testid="transferencia-negocio">
        <legend className="px-1 text-[11px] font-semibold uppercase opacity-70">Negocio</legend>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          <CampoBase id="tf-titulo" label="Título jurídico" issues={errorDe("negocio.tituloJuridico")}>
            <select
              id="tf-titulo"
              className={FIELD_CLASS}
              value={form.negocio.tituloJuridico}
              onChange={(e) => setNegocio("tituloJuridico", e.target.value)}
              aria-describedby={errorDe("negocio.tituloJuridico").length ? "tf-titulo-error" : undefined}
            >
              <option value="">Selecciona el título</option>
              {TITULOS.map((titulo) => (
                <option key={titulo.value} value={titulo.value}>
                  {titulo.label}
                </option>
              ))}
            </select>
          </CampoBase>

          {form.negocio.tituloJuridico === "OTRO" ? (
            <CampoBase id="tf-descripcion-titulo" label="Descripción del negocio" issues={errorDe("negocio.descripcionTitulo")}>
              <input id="tf-descripcion-titulo" className={FIELD_CLASS} value={form.negocio.descripcionTitulo}
                onChange={(e) => setNegocio("descripcionTitulo", e.target.value)} />
            </CampoBase>
          ) : null}

          <CampoBase id="tf-precio-letras" label="Precio en letras" issues={errorDe("negocio.precioLetras")}>
            <input id="tf-precio-letras" className={FIELD_CLASS} value={form.negocio.precioLetras}
              onChange={(e) => setNegocio("precioLetras", e.target.value)}
              aria-describedby={errorDe("negocio.precioLetras").length ? "tf-precio-letras-error" : undefined} />
          </CampoBase>
          <CampoBase id="tf-precio-numeros" label="Precio en números (COP)" issues={errorDe("negocio.precioNumeros")}>
            <input id="tf-precio-numeros" className={FIELD_CLASS} value={form.negocio.precioNumeros}
              onChange={(e) => setNegocio("precioNumeros", e.target.value)}
              aria-describedby={errorDe("negocio.precioNumeros").length ? "tf-precio-numeros-error" : undefined} />
          </CampoBase>
          <CampoBase id="tf-contraprestacion" label="Contraprestación (negocios no monetarios)" issues={[]}>
            <input id="tf-contraprestacion" className={FIELD_CLASS}
              value={form.negocio.contraprestacionDescripcion}
              onChange={(e) => setNegocio("contraprestacionDescripcion", e.target.value)} />
          </CampoBase>
          <CampoBase id="tf-forma-pago" label="Forma de pago o entrega" issues={[]}>
            <input id="tf-forma-pago" className={FIELD_CLASS} value={form.negocio.formaPago}
              onChange={(e) => setNegocio("formaPago", e.target.value)} />
          </CampoBase>

          <CampoBase id="tf-retencion" label="Asume la retención en la fuente" issues={[]}>
            <select id="tf-retencion" className={FIELD_CLASS} value={form.negocio.asumeRetencionFuente}
              onChange={(e) => setNegocio("asumeRetencionFuente", e.target.value)}>
              <option value="TRANSFERENTE">El transferente</option>
              <option value="ADQUIRENTE">El adquirente</option>
              <option value="SEGUN_LEY">Según la ley</option>
            </select>
          </CampoBase>
          <CampoBase id="tf-derechos" label="Asume los derechos del trámite" issues={[]}>
            <select id="tf-derechos" className={FIELD_CLASS} value={form.negocio.asumeDerechosTramite}
              onChange={(e) => setNegocio("asumeDerechosTramite", e.target.value)}>
              <option value="TRANSFERENTE">El transferente</option>
              <option value="ADQUIRENTE">El adquirente</option>
              <option value="COMPARTIDOS">Ambas partes</option>
            </select>
          </CampoBase>

          {esRemolque(form.vehiculo.claseVehiculo) ? (
            <p className="self-end text-[11px] opacity-70" data-testid="transferencia-exencion-impuesto">
              Remolques y semirremolques están exentos del impuesto sobre vehículos (Ley 488 de
              1998): no se declara quién lo asume.
            </p>
          ) : (
            <CampoBase id="tf-impuesto" label="Asume el impuesto sobre vehículos" issues={[]}>
              <select id="tf-impuesto" className={FIELD_CLASS} value={form.negocio.asumeImpuestoVehiculo ?? "SEGUN_LEY"}
                onChange={(e) => setNegocio("asumeImpuestoVehiculo", e.target.value)}>
                <option value="TRANSFERENTE">El transferente</option>
                <option value="ADQUIRENTE">El adquirente</option>
                <option value="SEGUN_LEY">Según la ley</option>
              </select>
            </CampoBase>
          )}

          <CampoBase id="tf-ciudad" label="Ciudad de firma" issues={errorDe("negocio.ciudadFirma")}>
            <input id="tf-ciudad" className={FIELD_CLASS} value={form.negocio.ciudadFirma}
              onChange={(e) => setNegocio("ciudadFirma", e.target.value)} />
          </CampoBase>
          <CampoBase id="tf-fecha" label="Fecha de firma" issues={[]}>
            <input id="tf-fecha" type="date" className={FIELD_CLASS} value={form.negocio.fechaFirma}
              onChange={(e) => setNegocio("fechaFirma", e.target.value)} />
          </CampoBase>
        </div>
      </fieldset>

      )}

      <fieldset className="rounded-2xl border p-4">
        <legend className="px-1 text-[11px] font-semibold uppercase opacity-70">
          Gravámenes y limitaciones
        </legend>
        <div className="flex flex-col gap-2 text-[11px]">
          <label htmlFor="tf-gravamen" className="flex items-center gap-2">
            <input
              id="tf-gravamen"
              type="checkbox"
              checked={form.gravamenActivo}
              onChange={(e) => setForm((prev) => ({ ...prev, gravamenActivo: e.target.checked }))}
            />
            El vehículo tiene un gravamen o limitación a la propiedad activo
          </label>

          {form.gravamenActivo ? (
            <label htmlFor="tf-levantamiento" className="flex items-center gap-2">
              <input
                id="tf-levantamiento"
                type="checkbox"
                checked={form.tieneLevantamientoOAutorizacion}
                onChange={(e) =>
                  setForm((prev) => ({ ...prev, tieneLevantamientoOAutorizacion: e.target.checked }))
                }
                aria-describedby={
                  errorDe("gravamen.tieneLevantamientoOAutorizacion").length
                    ? "tf-levantamiento-error"
                    : undefined
                }
              />
              Se adjunta el levantamiento o la autorización del beneficiario (art. 5.3.2.1 numeral 3.º)
            </label>
          ) : null}

          {errorDe("gravamen.tieneLevantamientoOAutorizacion").map((issue) => (
            <p key={issue.code} id="tf-levantamiento-error" role="alert" className="text-[11px] text-red-700">
              <span className="font-semibold">{issue.code}</span> — {issue.message}
            </p>
          ))}
        </div>
      </fieldset>

      <div className="flex items-center gap-3">
        {/*
          CF-24 — el botón permanece deshabilitado hasta que se responda el control de régimen, y
          sigue deshabilitado si se declaró una condición especial. El motivo va en texto enlazado
          por aria-describedby: el estado no se comunica solo por color ni por opacidad (CF-22).
        */}
        <button
          type="submit"
          disabled={enviando || !regimenPermiteGenerar}
          aria-describedby="tf-generar-ayuda"
          className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
          style={{ backgroundColor: "#557EFF" }}
        >
          {enviando ? "Generando…" : "Generar documento"}
        </button>
        <p id="tf-generar-ayuda" className="text-[11px] opacity-70">
          {form.regimen === ""
            ? "Para habilitar la generación, responde primero el régimen aplicable a la operación."
            : regimenPermiteGenerar
              ? "El documento se genera en el servidor y se descarga desde el historial."
              : "La condición especial declarada impide generar este documento: adelanta el trámite por la vía especial de su artículo."}
        </p>
      </div>
    </form>
  );
}
