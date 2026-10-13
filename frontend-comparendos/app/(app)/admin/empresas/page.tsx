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
  Tipologia,
} from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function NuevaEmpresa({
  onCerrar,
  onCreada,
}: {
  onCerrar: () => void;
  onCreada: (id: string) => void;
}) {
  const [id, setId] = useState("");
  const [nombre, setNombre] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const valido = UUID.test(id.trim()) && nombre.trim().length > 0;

  const crear = async () => {
    setOcupado(true);
    setError(null);
    try {
      await apiFetch("/admin/empresas", {
        method: "POST",
        body: { id: id.trim(), nombre: nombre.trim() },
      });
      onCreada(id.trim());
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
      description="La habilitación del producto vive en la plataforma; aquí se crea su configuración de Comparendos."
      footer={
        <div className="flex justify-end gap-2">
          <button type="button" className={controlCls(false)} onClick={onCerrar}>
            Cancelar
          </button>
          <button
            type="button"
            className={BOTON_PRIMARIO_CLS}
            disabled={!valido || ocupado}
            onClick={crear}
          >
            {ocupado ? "Registrando…" : "Registrar"}
          </button>
        </div>
      }
    >
      <div className="flex flex-col gap-3">
        <label className={ETIQUETA_CLS}>
          Identificador de la empresa en FLIT
          <input
            className={INPUT_CLS}
            value={id}
            onChange={(e) => setId(e.target.value)}
            placeholder="00000000-0000-0000-0000-000000000000"
          />
        </label>
        <label className={ETIQUETA_CLS}>
          Nombre
          <input className={INPUT_CLS} value={nombre} onChange={(e) => setNombre(e.target.value)} />
        </label>
        {error && (
          <InlineAlert tone="error" compact>
            {error}
          </InlineAlert>
        )}
      </div>
    </Modal>
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
          <li key={a.id} className="border-l-2 border-[#557EFF] pl-3 text-xs">
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
    try {
      await apiFetch(`/admin/empresas/${empresaId}/capacidades`, { method: "PUT", body: valores });
      setConfirmar(false);
      setForm(null);
      config.recargar();
      onGuardado();
      toast.show("Configuración guardada.", "success");
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo guardar.", "error");
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
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo cambiar la tipología.", "error");
    }
  };

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-end justify-between gap-2">
        <div className="flex flex-wrap items-end gap-3">
          <label className={`${ETIQUETA_CLS} w-80`}>
            Nombre de la empresa
            <input
              className={INPUT_CLS}
              value={valores.empresaNombre ?? ""}
              onChange={(e) => cambiar({ empresaNombre: e.target.value })}
            />
          </label>
          <label className={`${ETIQUETA_CLS} w-48`}>
            NIT de la empresa
            <input
              className={INPUT_CLS}
              inputMode="numeric"
              placeholder="900123456-8"
              value={valores.empresaNit ?? ""}
              onChange={(e) => cambiar({ empresaNit: e.target.value })}
            />
          </label>
        </div>
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
              render: (t) => t.estadosLegalesPermitidos.join(", "),
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
          onClose={() => setConfirmar(false)}
          busy={ocupado}
          title="¿Guardar los cambios?"
          description="Las capacidades se aplican de inmediato a todos los usuarios de la empresa y quedan en el historial."
          footer={
            <div className="flex justify-end gap-2">
              <button
                type="button"
                className={controlCls(false)}
                onClick={() => setConfirmar(false)}
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
          <p className="text-sm">
            Al activar el cambio de contraventor, los comparendos activos pasan a «sin solicitar».
          </p>
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
          emptyMessage="Todavía no hay empresas en Comparendos. Registra una o espera a que entre su primer usuario."
          columns={[
            {
              key: "nombre",
              header: "Empresa",
              render: (e) => <span className="font-semibold">{e.nombre}</span>,
            },
            {
              key: "caps",
              header: "Capacidades",
              render: (e) => (
                <div className="flex flex-wrap gap-1">
                  {e.hasRightPetitionModule && <StatusBadge label="DP" tone="info" />}
                  {e.hasOcrAccess && <StatusBadge label="OCR" tone="info" />}
                  {e.allowsContravenerChange && (
                    <StatusBadge label="Cambio de contraventor" tone="info" />
                  )}
                  {e.hasReportesModule && <StatusBadge label="Reportes" tone="info" />}
                </div>
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
