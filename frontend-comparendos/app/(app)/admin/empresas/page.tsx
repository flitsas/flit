"use client";

import { useState } from "react";
import { History, Plus } from "lucide-react";
import { DataTable, InlineAlert, Modal, StatusBadge } from "@flit/ui";
import { ConsultasAdmin } from "@/components/consultas/ConsultasAdmin";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { SearchInput } from "@/components/SearchInput";
import { useToast } from "@/components/Toast";
import { Interruptor } from "@/components/ui/Interruptor";
import { BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { estadoLegal } from "@/lib/etiquetas";
import {
  capacidadesDeConfiguracion,
  cambiosDeAuditoria,
  type CapacidadesForm,
} from "@/lib/parametrizador";
import { formatoFechaHora } from "@/lib/format/date";
import type {
  AuditoriaConfiguracion,
  ConfiguracionEmpresa,
  EmpresaAdmin,
  EmpresaPlataforma,
  Nit,
  Tipologia,
} from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

function NuevaEmpresa({
  onCerrar,
  onCreada,
}: {
  onCerrar: () => void;
  onCreada: (id: string) => void;
}) {
  // Solo las empresas con Comparendos encendido en la plataforma; nombre y NIT salen de allá (hallazgos B2-1 y B2-4).
  const empresas = useCarga("empresas-plataforma", (signal) =>
    apiFetch<EmpresaPlataforma[]>("/admin/empresas/plataforma", { signal }),
  );
  const [id, setId] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const disponibles = (empresas.datos ?? []).filter((e) => !e.registrada);
  const elegida = disponibles.find((e) => e.id === id);

  const crear = async () => {
    setOcupado(true);
    setError(null);
    try {
      await apiFetch("/admin/empresas", { method: "POST", body: { id } });
      onCreada(id);
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo registrar la empresa.");
    } finally {
      setOcupado(false);
    }
  };

  return (
    <Modal
      open
      onClose={onCerrar}
      busy={ocupado}
      title="Registrar empresa en Comparendos"
      description="Aparecen las empresas que tienen Comparendos encendido en Administración de compañías → Productos."
      footer={
        <div className="flex justify-end gap-2">
          <button type="button" className={controlCls(false)} onClick={onCerrar}>
            Cancelar
          </button>
          <button
            type="button"
            className={BOTON_PRIMARIO_CLS}
            disabled={!elegida || ocupado}
            onClick={crear}
          >
            {ocupado ? "Registrando…" : "Registrar"}
          </button>
        </div>
      }
    >
      <div className="flex flex-col gap-3">
        {empresas.estado === "loading" && <p className="text-sm opacity-60">Cargando empresas…</p>}
        {empresas.estado === "error" && <InlineAlert tone="error">{empresas.error}</InlineAlert>}
        {empresas.estado !== "loading" && empresas.estado !== "error" && disponibles.length === 0 && (
          <InlineAlert tone="info">
            No hay empresas por registrar: todas las que tienen Comparendos encendido ya están en la lista.
          </InlineAlert>
        )}
        {disponibles.length > 0 && (
          <label className={ETIQUETA_CLS}>
            Empresa
            <select className={INPUT_CLS} value={id} onChange={(e) => setId(e.target.value)}>
              <option value="">Elige una empresa</option>
              {disponibles.map((e) => (
                <option key={e.id} value={e.id}>
                  {e.nombre}
                  {e.nit ? ` · NIT ${e.nit}` : ""}
                </option>
              ))}
            </select>
          </label>
        )}
        {elegida && <DatosEmpresa nombre={elegida.nombre} nit={elegida.nit} />}
        {error && (
          <InlineAlert tone="error" compact>
            {error}
          </InlineAlert>
        )}
      </div>
    </Modal>
  );
}

/** Nombre y NIT de la empresa: vienen de la plataforma y se corrigen allá, no aquí. */
function DatosEmpresa({ nombre, nit }: { nombre: string | null; nit: string | null }) {
  return (
    <div className="flex flex-col gap-1">
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        <dt className="opacity-70">Nombre</dt>
        <dd className="font-semibold">{nombre ?? "—"}</dd>
        <dt className="opacity-70">NIT</dt>
        <dd className="font-semibold tabular-nums">{nit ?? "Sin NIT en la plataforma"}</dd>
      </dl>
      <p className="text-[11px] opacity-70">
        Se toman de la plataforma; si algo está mal, se corrige en Administración de compañías. El dígito de verificación
        lo calcula el sistema.
      </p>
    </div>
  );
}

function Historial({ empresaId, onCerrar }: { empresaId: string; onCerrar: () => void }) {
  const lista = useCarga(`auditoria:${empresaId}`, (signal) =>
    apiFetch<AuditoriaConfiguracion[]>(`/admin/empresas/${empresaId}/auditoria`, { signal }),
  );
  return (
    <Modal open onClose={onCerrar} title="Historial de cambios" size="lg">
      {lista.estado === "loading" && <p className="text-sm opacity-60">Cargando…</p>}
      {lista.estado === "error" && <InlineAlert tone="error">{lista.error}</InlineAlert>}
      {lista.datos?.length === 0 && <p className="text-sm opacity-70">Sin cambios registrados.</p>}
      <ol className="flex flex-col gap-3">
        {(lista.datos ?? []).map((a) => (
          <li key={a.id} className="border-l-2 border-flit-brand pl-3 text-xs">
            <p className="font-semibold">
              {a.accion} · {a.entidad}
            </p>
            <p className="opacity-70">
              {formatoFechaHora(a.registradoEn)} · {a.usuarioNombre}
            </p>
            <ul className="mt-1">
              {cambiosDeAuditoria(a.cambios).map((c) => (
                <li key={c.campo}>
                  <span className="font-semibold">{c.campo}:</span> {c.anterior} → {c.nuevo}
                </li>
              ))}
            </ul>
          </li>
        ))}
      </ol>
    </Modal>
  );
}

/**
 * NIT que consultan el conductor en la API de contratos (HU #13712). La API de Renting solo conoce su propia flota: se
 * marca NIT por NIT. El interruptor general sigue siendo la integración de la empresa; apagarla no borra estas casillas.
 */
function NitsConsultaContrato({ empresaId, integracionApi }: { empresaId: string; integracionApi: boolean }) {
  const toast = useToast();
  const nits = useCarga(`nits-contrato:${empresaId}`, (signal) =>
    apiFetch<Nit[]>("/monitoreo/nits", { signal, query: { empresa: empresaId } }),
  );

  const cambiar = async (n: Nit, activo: boolean) => {
    try {
      await apiFetch(`/admin/empresas/${empresaId}/nits/${n.id}/consulta-contrato`, { method: "PUT", body: { activo } });
      nits.recargar();
      toast.show(`${n.numero}: ${activo ? "consulta por API" : "asignación manual"}.`, "success");
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo cambiar el NIT.", "error");
    }
  };

  return (
    <section className="flex flex-col gap-2">
      <h2 className="text-sm font-semibold">NIT que consultan el conductor en la API de contratos</h2>
      <p className="text-xs opacity-70">
        La API de Renting solo sabe quién tenía un vehículo de su propia flota. Marca únicamente esos NIT: los demás se
        asignan a mano.
      </p>
      {!integracionApi && (
        <InlineAlert tone="info" compact>
          La integración del contraventor está en Manual o la consulta de contraventor está apagada: no sale ninguna
          consulta. Las casillas se conservan para cuando se vuelva a encender.
        </InlineAlert>
      )}
      <DataTable
        ariaLabel="NIT que consultan la API de contratos"
        rows={nits.datos ?? []}
        getRowKey={(n) => n.id}
        status={nits.estado}
        errorMessage={nits.error ?? undefined}
        onRetry={nits.recargar}
        emptyMessage="La empresa no tiene NIT monitoreados."
        columns={[
          {
            key: "numero",
            header: "NIT",
            render: (n) => (
              <span className="font-mono">
                {n.tipoDocumento} {n.numero}
              </span>
            ),
          },
          { key: "titular", header: "Titular", render: (n) => n.nombreTitular ?? "—" },
          {
            key: "estado",
            header: "Monitoreo",
            render: (n) => <StatusBadge label={n.activo ? "Activo" : "Inactivo"} tone={n.activo ? "info" : "neutral"} />,
          },
          {
            key: "api",
            header: "Consulta por API",
            align: "right",
            render: (n) => (
              <Interruptor
                activo={n.consultaContratoApi}
                onCambio={(v) => cambiar(n, v)}
                etiqueta="Consultar por API"
              />
            ),
          },
        ]}
      />
    </section>
  );
}

function Configuracion({ empresaId, onGuardado }: { empresaId: string; onGuardado: () => void }) {
  const toast = useToast();
  const config = useCarga(`config:${empresaId}`, (signal) =>
    apiFetch<ConfiguracionEmpresa>(`/admin/empresas/${empresaId}/configuracion`, { signal }),
  );
  const tipologias = useCarga(`tipologias:${empresaId}`, (signal) =>
    apiFetch<Tipologia[]>("/admin/tipologias", { signal, query: { empresa: empresaId } }),
  );
  const [form, setForm] = useState<{ base: string; valores: CapacidadesForm } | null>(null);
  const [confirmar, setConfirmar] = useState(false);
  const [historial, setHistorial] = useState(false);
  const [ocupado, setOcupado] = useState(false);
  const [errorGuardar, setErrorGuardar] = useState<string | null>(null);

  if (config.estado === "loading")
    return <p className="text-sm opacity-60">Cargando configuración…</p>;
  if (config.estado === "error" || !config.datos)
    return <InlineAlert tone="error">{config.error ?? "No se pudo cargar."}</InlineAlert>;

  const base = capacidadesDeConfiguracion(config.datos);
  const clave = JSON.stringify(base);
  const valores = form && form.base === clave ? form.valores : base;
  const cambiar = (parcial: Partial<CapacidadesForm>) =>
    setForm({ base: clave, valores: { ...valores, ...parcial } });
  const sucio = JSON.stringify(valores) !== clave;

  const guardar = async () => {
    setOcupado(true);
    setErrorGuardar(null);
    try {
      await apiFetch(`/admin/empresas/${empresaId}/capacidades`, { method: "PUT", body: valores });
      setConfirmar(false);
      setForm(null);
      config.recargar();
      onGuardado();
      toast.show("Configuración guardada.", "success");
    } catch (e) {
      // B2-2: el error se ve dentro del diálogo, que sigue abierto.
      setErrorGuardar(e instanceof Error ? e.message : "No se pudo guardar.");
    } finally {
      setOcupado(false);
    }
  };

  const habilitar = async (t: Tipologia, habilitada: boolean) => {
    try {
      await apiFetch(`/admin/empresas/${empresaId}/tipologias/${t.id}`, {
        method: "PUT",
        body: { habilitada },
      });
      tipologias.recargar();
      toast.show(`${t.codigo} ${habilitada ? "habilitada" : "deshabilitada"}.`, "success");
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo cambiar la tipología.", "error");
    }
  };

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <DatosEmpresa nombre={config.datos.empresaNombre} nit={config.datos.empresaNit} />
        <button type="button" className={controlCls(false)} onClick={() => setHistorial(true)}>
          <History className="h-3.5 w-3.5" aria-hidden />
          Historial de cambios
        </button>
      </div>

      <section className="flex flex-col gap-3">
        <h2 className="text-sm font-semibold">Capacidades contratadas</h2>
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
          <Interruptor
            activo={valores.hasRightPetitionModule}
            onCambio={(v) => cambiar({ hasRightPetitionModule: v })}
            etiqueta="Derechos de petición"
            ayuda="Generar y radicar DPs ante los organismos."
          />
          <Interruptor
            activo={valores.hasOcrAccess}
            onCambio={(v) => cambiar({ hasOcrAccess: v })}
            etiqueta="Carga por OCR"
            ayuda="Paquetes de comparendos físicos escaneados."
          />
          <Interruptor
            activo={valores.allowsContravenerConsultation}
            onCambio={(v) => cambiar({ allowsContravenerConsultation: v })}
            etiqueta="Consulta de contraventor"
            ayuda="Consultar el conductor en la API de contratos."
          />
          <Interruptor
            activo={valores.allowsContravenerChange}
            onCambio={(v) => cambiar({ allowsContravenerChange: v })}
            etiqueta="Cambio de contraventor"
            ayuda="Solicitar el cambio ante el organismo de tránsito."
          />
          <Interruptor
            activo={valores.hasReportesModule}
            onCambio={(v) => cambiar({ hasReportesModule: v })}
            etiqueta="Reportes"
            ayuda="Dashboard, productividad y reportes automáticos."
          />
          <Interruptor
            activo={valores.requiereRepresentanteLegal}
            onCambio={(v) => cambiar({ requiereRepresentanteLegal: v })}
            etiqueta="Exige representante legal"
            ayuda="Sin representante activo no se envían DPs."
          />
        </div>
      </section>

      <section className="grid grid-cols-1 gap-3 md:grid-cols-3">
        <div className={ETIQUETA_CLS}>
          Canal al conductor
          <div className="flex gap-2">
            <span className={controlCls(true)}>Correo</span>
            <span
              className={`${controlCls(false)} cursor-not-allowed opacity-50`}
              title="Llega en una entrega posterior"
            >
              WhatsApp (próximamente)
            </span>
          </div>
        </div>
        <label className={ETIQUETA_CLS}>
          Integración del contraventor
          <select
            className={INPUT_CLS}
            value={valores.tipoIntegracionConductor}
            onChange={(e) => cambiar({ tipoIntegracionConductor: e.target.value })}
          >
            <option value="Manual">Manual (el operador lo asigna)</option>
            <option value="ApiRenting">API de contratos de Renting</option>
          </select>
        </label>
        <label className={ETIQUETA_CLS}>
          Conjunto de plantillas
          <select
            className={INPUT_CLS}
            value={valores.conjuntoPlantillas}
            onChange={(e) => cambiar({ conjuntoPlantillas: e.target.value })}
          >
            <option value="Flit">FLIT</option>
            <option value="Renting">Renting</option>
          </select>
        </label>
      </section>

      <div className="flex justify-end gap-2">
        <button
          type="button"
          className={controlCls(false)}
          disabled={!sucio}
          onClick={() => setForm(null)}
        >
          Descartar
        </button>
        <button
          type="button"
          className={BOTON_PRIMARIO_CLS}
          disabled={!sucio}
          onClick={() => setConfirmar(true)}
        >
          Guardar cambios
        </button>
      </div>

      <NitsConsultaContrato
        empresaId={empresaId}
        integracionApi={base.tipoIntegracionConductor === "ApiRenting" && base.allowsContravenerConsultation}
      />

      <section className="flex flex-col gap-2">
        <h2 className="text-sm font-semibold">Tipologías de derecho de petición habilitadas</h2>
        <DataTable
          ariaLabel="Tipologías por empresa"
          rows={(tipologias.datos ?? []).filter((t) => t.activo || t.habilitada)}
          getRowKey={(t) => t.id}
          status={tipologias.estado}
          errorMessage={tipologias.error ?? undefined}
          onRetry={tipologias.recargar}
          emptyMessage="El catálogo de tipologías está vacío."
          columns={[
            {
              key: "codigo",
              header: "Código",
              render: (t) => <span className="font-mono font-semibold">{t.codigo}</span>,
            },
            { key: "nombre", header: "Tipología", render: (t) => t.nombre },
            {
              key: "estados",
              header: "Procede en",
              render: (t) => t.estadosLegalesPermitidos.map((e) => estadoLegal(e).texto).join(", "),
            },
            {
              key: "habilitada",
              header: "Habilitada",
              align: "right",
              render: (t) => (
                <Interruptor
                  activo={!!t.habilitada}
                  onCambio={(v) => habilitar(t, v)}
                  etiqueta={`Habilitar ${t.codigo}`}
                  deshabilitado={!t.activo && !t.habilitada}
                />
              ),
            },
          ]}
        />
      </section>

      {confirmar && (
        <Modal
          open
          onClose={() => {
            setConfirmar(false);
            setErrorGuardar(null);
          }}
          busy={ocupado}
          title="¿Guardar los cambios?"
          description="Las capacidades se aplican de inmediato a todos los usuarios de la empresa y quedan en el historial."
          footer={
            <div className="flex justify-end gap-2">
              <button
                type="button"
                className={controlCls(false)}
                onClick={() => {
                  setConfirmar(false);
                  setErrorGuardar(null);
                }}
              >
                Cancelar
              </button>
              <button
                type="button"
                className={BOTON_PRIMARIO_CLS}
                disabled={ocupado}
                onClick={guardar}
              >
                {ocupado ? "Guardando…" : "Sí, guardar"}
              </button>
            </div>
          }
        >
          <div className="flex flex-col gap-3">
            <p className="text-sm">
              Al activar el cambio de contraventor, los comparendos activos pasan a «sin solicitar».
            </p>
            {errorGuardar && (
              <InlineAlert tone="error" compact>
                {errorGuardar}
              </InlineAlert>
            )}
          </div>
        </Modal>
      )}
      {historial && <Historial empresaId={empresaId} onCerrar={() => setHistorial(false)} />}
    </div>
  );
}

/** Capacidades (HU #13476) y consultas automáticas (HU #13522) de la empresa elegida. */
function Pestanas({ empresaId, onGuardado }: { empresaId: string; onGuardado: () => void }) {
  const [pestana, setPestana] = useState<"capacidades" | "consultas">("capacidades");
  return (
    <div className="flex flex-col gap-4">
      <div
        role="tablist"
        aria-label="Configuración de la empresa"
        className="flex gap-1 border-b border-[#E2E8F0] dark:border-white/10"
      >
        {(
          [
            ["capacidades", "Capacidades y tipologías"],
            ["consultas", "Consultas automáticas"],
          ] as const
        ).map(([valor, etiqueta]) => (
          <button
            key={valor}
            type="button"
            role="tab"
            aria-selected={pestana === valor}
            onClick={() => setPestana(valor)}
            className={`-mb-px border-b-2 px-3 py-2 text-xs font-semibold ${pestana === valor ? "border-[#3B4FD6] text-[#3B4FD6] dark:text-white" : "border-transparent opacity-70 hover:opacity-100"}`}
          >
            {etiqueta}
          </button>
        ))}
      </div>
      {pestana === "capacidades" ? (
        <Configuracion empresaId={empresaId} onGuardado={onGuardado} />
      ) : (
        <ConsultasAdmin empresaId={empresaId} />
      )}
    </div>
  );
}

/** Parametrizador por empresa (HU #13476, §7.10): capacidades, integración, conjunto de plantillas, tipologías y consultas. */
export default function Empresas() {
  const [buscar, setBuscar] = useState("");
  const [elegida, setElegida] = useState<string | null>(null);
  const [nueva, setNueva] = useState(false);
  const lista = useCarga(`empresas-admin:${buscar}`, (signal) =>
    apiFetch<EmpresaAdmin[]>("/admin/empresas", { signal, query: { buscar } }),
  );

  return (
    <Requiere superAdmin>
      <section className="flex flex-col gap-4">
        <Encabezado
          titulo="Parametrizador por empresa"
          descripcion="Qué capacidades de Comparendos tiene cada empresa y qué tipologías de DP puede usar."
          acciones={
            <button type="button" className={BOTON_PRIMARIO_CLS} onClick={() => setNueva(true)}>
              <Plus className="h-3.5 w-3.5" aria-hidden />
              Registrar empresa
            </button>
          }
        />
        <SearchInput
          value={buscar}
          onChange={setBuscar}
          label="Buscar empresa"
          placeholder="Nombre de la empresa"
        />
        <DataTable
          ariaLabel="Empresas"
          rows={lista.datos ?? []}
          getRowKey={(e) => e.id}
          status={lista.estado}
          errorMessage={lista.error ?? undefined}
          onRetry={lista.recargar}
          onRowClick={(e) => setElegida(e.id)}
          emptyMessage="Todavía no hay empresas en Comparendos. Registra una de las que tienen el producto encendido."
          columns={[
            {
              key: "nombre",
              header: "Empresa",
              render: (e) => <span className="font-semibold">{e.nombre}</span>,
            },
            {
              key: "caps",
              header: "Capacidades",
              // Azul habilitada, gris deshabilitada (Epic GDC-3).
              render: (e) => (
                <div className="flex flex-wrap gap-1">
                  {(
                    [
                      ["DP", e.hasRightPetitionModule],
                      ["OCR", e.hasOcrAccess],
                      ["Consulta de contraventor", e.allowsContravenerConsultation],
                      ["Cambio de contraventor", e.allowsContravenerChange],
                      ["Reportes", e.hasReportesModule],
                    ] as const
                  ).map(([texto, activa]) => (
                    <StatusBadge key={texto} label={texto} tone={activa ? "info" : "neutral"} />
                  ))}
                </div>
              ),
            },
            {
              key: "estado",
              header: "Configuración",
              render: (e) => (
                <span title={e.faltantes.join(" · ") || undefined} className="inline-flex flex-col gap-0.5">
                  <StatusBadge
                    label={e.estadoConfiguracion === "Activa" ? "Activa" : e.estadoConfiguracion === "Inactiva" ? "Inactiva" : "Pendiente de configuración"}
                    tone={e.estadoConfiguracion === "Activa" ? "info" : e.estadoConfiguracion === "Inactiva" ? "neutral" : "warning"}
                  />
                  {e.estadoConfiguracion === "Pendiente" && <span className="text-[11px] opacity-70">{e.faltantes.join(" · ")}</span>}
                </span>
              ),
            },
            {
              key: "activos",
              header: "Comparendos activos",
              align: "right",
              render: (e) => e.comparendosActivos.toLocaleString("es-CO"),
            },
          ]}
        />
        {elegida && (
          <Modal
            open
            onClose={() => setElegida(null)}
            title={lista.datos?.find((e) => e.id === elegida)?.nombre ?? "Empresa"}
            size="2xl"
          >
            <Pestanas key={elegida} empresaId={elegida} onGuardado={lista.recargar} />
          </Modal>
        )}
        {nueva && (
          <NuevaEmpresa
            onCerrar={() => setNueva(false)}
            onCreada={(id) => {
              setNueva(false);
              lista.recargar();
              setElegida(id);
            }}
          />
        )}
      </section>
    </Requiere>
  );
}
