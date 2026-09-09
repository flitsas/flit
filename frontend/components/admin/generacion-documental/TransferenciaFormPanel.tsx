"use client";

import { useMemo, useState } from "react";
import { AlertTriangle, Info, ShieldAlert } from "lucide-react";
import { generateTransferenciaDocument } from "@/lib/api/admin-generacion-documental";
import { ApiError, ApiValidationError } from "@/lib/api/types";
import type {
  TransferGenerateResult,
  TransferNegocioInput,
  TransferParteInput,
  TransferTituloJuridico,
  TransferValidationIssue,
  TransferVehiculoInput,
} from "@/lib/api/types-generacion-documental";

const FIELD_CLASS =
  "w-full rounded-xl border px-3 py-2 text-xs focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]";

const TITULOS: { value: TransferTituloJuridico; label: string }[] = [
  { value: "COMPRAVENTA", label: "Compraventa" },
  { value: "DACION_EN_PAGO", label: "Dación en pago" },
  { value: "PERMUTA", label: "Permuta" },
  { value: "DONACION", label: "Donación" },
  { value: "OTRO", label: "Otro negocio traslaticio" },
];

/** Estado del formulario. Cadena vacía = «sin capturar», nunca `undefined`. */
interface TransferenciaFormState {
  vehiculo: Required<Pick<TransferVehiculoInput, "placa">> & TransferVehiculoInput;
  transferente: TransferParteInput;
  adquirente: TransferParteInput;
  negocio: TransferNegocioInput;
  gravamenActivo: boolean;
  tieneLevantamientoOAutorizacion: boolean;
}

const ESTADO_INICIAL: TransferenciaFormState = {
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
 * <p><b>Alcance.</b> Escenario A. El selector A/B/C y el control de régimen aplicable (CF-24,
 * VB-07) llegan en HU-06; el prellenado «placa primero» con bloqueo anti-pisado, en HU-10. Aquí
 * todos los campos son de captura manual.</p>
 */
export function TransferenciaFormPanel() {
  const [form, setForm] = useState<TransferenciaFormState>(ESTADO_INICIAL);
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

  const setParte = (parte: "transferente" | "adquirente", key: keyof TransferParteInput, value: string) =>
    setForm((prev) => ({ ...prev, [parte]: { ...prev[parte], [key]: value } }));

  const setNegocio = (key: keyof TransferNegocioInput, value: string) =>
    setForm((prev) => ({ ...prev, negocio: { ...prev.negocio, [key]: value } }));

  async function onSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setEnviando(true);
    setErrores([]);
    setErrorGeneral(null);
    setResultado(null);

    try {
      const respuesta = await generateTransferenciaDocument({
        // VB-05: exactamente un escenario. En esta HU es siempre A.
        escenarios: ["A"],
        vehiculo: form.vehiculo,
        transferente: form.transferente,
        adquirente: form.adquirente,
        negocio: {
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
        Transferencia de dominio — Traspaso ordinario (escenario A)
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

      <fieldset className="rounded-2xl border p-4">
        <legend className="px-1 text-[11px] font-semibold uppercase opacity-70">Vehículo</legend>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          <CampoBase id="tf-placa" label="Placa" issues={errorDe("vehiculo.placa")}>
            <input
              id="tf-placa"
              className={FIELD_CLASS}
              value={form.vehiculo.placa}
              onChange={(e) => setVehiculo("placa", e.target.value.toUpperCase())}
              aria-describedby={errorDe("vehiculo.placa").length ? "tf-placa-error" : undefined}
            />
          </CampoBase>
          <CampoBase id="tf-marca" label="Marca" issues={[]}>
            <input id="tf-marca" className={FIELD_CLASS} value={form.vehiculo.marca}
              onChange={(e) => setVehiculo("marca", e.target.value)} />
          </CampoBase>
          <CampoBase id="tf-linea" label="Línea" issues={[]}>
            <input id="tf-linea" className={FIELD_CLASS} value={form.vehiculo.linea}
              onChange={(e) => setVehiculo("linea", e.target.value)} />
          </CampoBase>
          <CampoBase id="tf-modelo" label="Año modelo" issues={[]}>
            <input id="tf-modelo" className={FIELD_CLASS} value={form.vehiculo.modeloAnio}
              onChange={(e) => setVehiculo("modeloAnio", e.target.value)} />
          </CampoBase>
          <CampoBase id="tf-clase" label="Clase" issues={[]}>
            <input id="tf-clase" className={FIELD_CLASS} value={form.vehiculo.claseVehiculo}
              onChange={(e) => setVehiculo("claseVehiculo", e.target.value)} />
          </CampoBase>
          <CampoBase id="tf-carroceria" label="Carrocería" issues={[]}>
            <input id="tf-carroceria" className={FIELD_CLASS} value={form.vehiculo.tipoCarroceria}
              onChange={(e) => setVehiculo("tipoCarroceria", e.target.value)} />
          </CampoBase>
          <CampoBase id="tf-color" label="Color(es)" issues={[]}>
            <input id="tf-color" className={FIELD_CLASS} value={form.vehiculo.color}
              onChange={(e) => setVehiculo("color", e.target.value)} />
          </CampoBase>
          <CampoBase id="tf-motor" label="Motor No." issues={[]}>
            <input id="tf-motor" className={FIELD_CLASS} value={form.vehiculo.noMotor}
              onChange={(e) => setVehiculo("noMotor", e.target.value)} />
          </CampoBase>
          <CampoBase id="tf-chasis" label="Chasis / VIN No." issues={[]}>
            <input id="tf-chasis" className={FIELD_CLASS} value={form.vehiculo.noChasis}
              onChange={(e) => setVehiculo("noChasis", e.target.value)} />
          </CampoBase>
          <CampoBase id="tf-serie" label="Serie No." issues={[]}>
            <input id="tf-serie" className={FIELD_CLASS} value={form.vehiculo.noSerie}
              onChange={(e) => setVehiculo("noSerie", e.target.value)} />
          </CampoBase>
          <CampoBase id="tf-servicio" label="Servicio" issues={[]}>
            <input id="tf-servicio" className={FIELD_CLASS} value={form.vehiculo.servicio}
              onChange={(e) => setVehiculo("servicio", e.target.value)} />
          </CampoBase>
          <CampoBase id="tf-licencia" label="Licencia de tránsito No." issues={[]}>
            <input id="tf-licencia" className={FIELD_CLASS} value={form.vehiculo.noLicenciaTransito}
              onChange={(e) => setVehiculo("noLicenciaTransito", e.target.value)} />
          </CampoBase>
          <CampoBase id="tf-organismo" label="Organismo de tránsito" issues={[]}>
            <input id="tf-organismo" className={FIELD_CLASS} value={form.vehiculo.organismoTransito}
              onChange={(e) => setVehiculo("organismoTransito", e.target.value)} />
          </CampoBase>
        </div>
      </fieldset>

      {(["transferente", "adquirente"] as const).map((parte) => (
        <fieldset key={parte} className="rounded-2xl border p-4">
          <legend className="px-1 text-[11px] font-semibold uppercase opacity-70">
            {parte === "transferente" ? "Transferente" : "Adquirente"}
          </legend>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            <CampoBase id={`tf-${parte}-tipopersona`} label="Tipo de persona" issues={[]}>
              <select
                id={`tf-${parte}-tipopersona`}
                className={FIELD_CLASS}
                value={form[parte].tipoPersona}
                onChange={(e) => setParte(parte, "tipoPersona", e.target.value)}
              >
                <option value="PN">Persona natural</option>
                <option value="PJ">Persona jurídica</option>
              </select>
            </CampoBase>
            <CampoBase id={`tf-${parte}-nombre`} label="Nombre o razón social" issues={errorDe(`${parte}.nombreRazonSocial`)}>
              <input id={`tf-${parte}-nombre`} className={FIELD_CLASS} value={form[parte].nombreRazonSocial}
                onChange={(e) => setParte(parte, "nombreRazonSocial", e.target.value)} />
            </CampoBase>
            <CampoBase id={`tf-${parte}-tipodoc`} label="Tipo de documento" issues={[]}>
              <select
                id={`tf-${parte}-tipodoc`}
                className={FIELD_CLASS}
                value={form[parte].tipoDoc}
                onChange={(e) => setParte(parte, "tipoDoc", e.target.value)}
              >
                <option value="CC">CC</option>
                <option value="CE">CE</option>
                <option value="PAS">Pasaporte</option>
                <option value="NIT">NIT</option>
              </select>
            </CampoBase>
            <CampoBase id={`tf-${parte}-numerodoc`} label="Número de documento" issues={errorDe(`${parte}.numeroDoc`)}>
              <input
                id={`tf-${parte}-numerodoc`}
                className={FIELD_CLASS}
                value={form[parte].numeroDoc}
                onChange={(e) => setParte(parte, "numeroDoc", e.target.value)}
                aria-describedby={
                  errorDe(`${parte}.numeroDoc`).length ? `tf-${parte}-numerodoc-error` : undefined
                }
              />
            </CampoBase>
            <CampoBase id={`tf-${parte}-domicilio`} label="Ciudad de domicilio" issues={[]}>
              <input id={`tf-${parte}-domicilio`} className={FIELD_CLASS} value={form[parte].domicilio}
                onChange={(e) => setParte(parte, "domicilio", e.target.value)} />
            </CampoBase>
            {form[parte].tipoPersona === "PJ" ? (
              <>
                <CampoBase id={`tf-${parte}-rl`} label="Representante legal" issues={[]}>
                  <input id={`tf-${parte}-rl`} className={FIELD_CLASS} value={form[parte].representanteLegal}
                    onChange={(e) => setParte(parte, "representanteLegal", e.target.value)} />
                </CampoBase>
                <CampoBase id={`tf-${parte}-ccrl`} label="C.C. del representante legal" issues={[]}>
                  <input id={`tf-${parte}-ccrl`} className={FIELD_CLASS} value={form[parte].ccRepresentanteLegal}
                    onChange={(e) => setParte(parte, "ccRepresentanteLegal", e.target.value)} />
                </CampoBase>
                <p className="self-end text-[11px] opacity-70">
                  El dígito de verificación del NIT lo calcula el sistema.
                </p>
              </>
            ) : null}
          </div>
        </fieldset>
      ))}

      <fieldset className="rounded-2xl border p-4">
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
        <button
          type="submit"
          disabled={enviando}
          className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
          style={{ backgroundColor: "#557EFF" }}
        >
          {enviando ? "Generando…" : "Generar documento"}
        </button>
        <p className="text-[11px] opacity-70">
          El documento se genera en el servidor y se descarga desde el historial.
        </p>
      </div>
    </form>
  );
}
