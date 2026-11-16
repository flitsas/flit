import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { PestanaOperacion } from "@/components/configuracion/PestanaOperacion";
import { DISPONIBLES_CON_IMPUESTO, configuracion, cuerpo } from "./datos";

// Uso de ejemplo: <PestanaOperacion configuracion={cfg} puedeEditar={sesion.capacidades.editarConfiguracion} onGuardado={setCfg} />
// edita visibilidad, fuentes por defecto (solo entre las habilitadas) y estimado de consultas con PUT /configuracion.

afterEach(() => vi.unstubAllGlobals());

describe("pestaña Operación", () => {
  it("ofrece por defecto solo entre habilitadas y guarda visibilidad, fuentes y estimado", async () => {
    const guardada = configuracion({
      operacion: { visibilidad_usuario: "PROPIOS", fuentes_por_defecto: ["RUNT", "AVALUO"], estimado_consultas: "ANTES_DE_LANZAR" },
    });
    const fetchMock = vi.fn().mockResolvedValueOnce(Response.json(guardada));
    vi.stubGlobal("fetch", fetchMock);
    const onGuardado = vi.fn();

    render(<PestanaOperacion configuracion={configuracion()} puedeEditar onGuardado={onGuardado} />);

    expect(screen.getAllByRole("checkbox")).toHaveLength(3);
    for (const nombre of ["RUNT vehículo", "SIMIT vehículo", "Avalúo comercial"]) expect(screen.getByRole("checkbox", { name: nombre })).toBeInTheDocument();
    expect(screen.queryByRole("checkbox", { name: "RUNT propietario" })).toBeNull();
    expect(screen.getByRole("checkbox", { name: "RUNT vehículo" })).toBeChecked();

    fireEvent.click(screen.getByRole("radio", { name: /solo los propios/i }));
    fireEvent.click(screen.getByRole("checkbox", { name: "Avalúo comercial" }));
    fireEvent.click(screen.getByRole("radio", { name: /antes de lanzar/i }));
    fireEvent.click(screen.getByRole("button", { name: "Guardar operación" }));

    expect(await screen.findByText("Configuración guardada.")).toBeInTheDocument();
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("/api/v1/diagnostico/configuracion");
    expect(init.method).toBe("PUT");
    expect(cuerpo(fetchMock, 0)).toEqual({ visibilidad_usuario: "PROPIOS", fuentes_por_defecto: ["RUNT", "AVALUO"], estimado_consultas: "ANTES_DE_LANZAR" });
    expect(onGuardado).toHaveBeenCalledWith(guardada);
  });

  it("deshabilitada sin permiso editar: controles inactivos y no puede guardar", () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    render(<PestanaOperacion configuracion={configuracion()} puedeEditar={false} />);

    for (const control of [...screen.getAllByRole("checkbox"), ...screen.getAllByRole("radio")]) expect(control).toBeDisabled();
    const guardar = screen.getByRole("button", { name: "Guardar operación" });
    expect(guardar).toBeDisabled();
    fireEvent.click(guardar);
    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.getByText(/no tienes permiso para editar/i)).toBeInTheDocument();
  });

  it("muestra el 422 del API sin perder lo digitado", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(Response.json({ code: "POR_DEFECTO_FUERA_DE_HABILITADAS", error: "Una fuente por defecto no está habilitada en el contrato." }, { status: 422 }));
    vi.stubGlobal("fetch", fetchMock);

    render(<PestanaOperacion configuracion={configuracion()} puedeEditar />);
    fireEvent.click(screen.getByRole("checkbox", { name: "SIMIT vehículo" }));
    fireEvent.click(screen.getByRole("radio", { name: /al finalizar/i }));
    fireEvent.click(screen.getByRole("button", { name: "Guardar operación" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Una fuente por defecto no está habilitada en el contrato.");
    expect(screen.getByRole("checkbox", { name: "SIMIT vehículo" })).toBeChecked();
    expect(screen.getByRole("radio", { name: /al finalizar/i })).toBeChecked();
  });

  it("un error del servidor (p. ej. conflicto de concurrencia) ofrece Reintentar y vuelve a guardar", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(Response.json({}, { status: 500 })).mockResolvedValueOnce(Response.json(configuracion()));
    vi.stubGlobal("fetch", fetchMock);

    render(<PestanaOperacion configuracion={configuracion()} puedeEditar />);
    fireEvent.click(screen.getByRole("button", { name: "Guardar operación" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/Diagnóstico no respondió/);
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByText("Configuración guardada.")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("avisa cuando el contrato cambió después de la última configuración (recorte de fuentes por defecto)", () => {
    render(
      <PestanaOperacion
        configuracion={configuracion({ contrato: { actualizado_en: "2026-10-20T15:00:00Z" }, operacion: { actualizado_en: "2026-10-01T10:00:00Z" } })}
        puedeEditar
      />,
    );

    expect(screen.getByText(/se quitaron las fuentes por defecto que dejaron de estar habilitadas/i)).toBeInTheDocument();
  });

  it("sin fuentes habilitadas en el contrato muestra el estado vacío de fuentes por defecto", () => {
    render(
      <PestanaOperacion configuracion={configuracion({ contrato: { fuentes_habilitadas: [] }, operacion: { fuentes_por_defecto: [] } })} puedeEditar />,
    );

    expect(screen.queryAllByRole("checkbox")).toHaveLength(0);
    expect(screen.getByText(/no tiene fuentes habilitadas/i)).toBeInTheDocument();
  });
});

// HU #13865: las fuentes por defecto se ofrecen solo entre las habilitadas y disponibles (fuentes_disponibles).
describe("pestaña Operación — fuentes por defecto según fuentes_disponibles (HU #13865)", () => {
  it("AC4: con el impuesto habilitado pero no disponible no se ofrece como fuente por defecto", () => {
    render(<PestanaOperacion configuracion={configuracion({ contrato: { fuentes_habilitadas: ["RUNT", "SIMIT_VEHICULO", "IMPUESTO_DEPARTAMENTAL"] } })} puedeEditar />);

    expect(screen.queryByRole("checkbox", { name: "Impuesto departamental" })).toBeNull();
    expect(screen.getAllByRole("checkbox").map((c) => c.getAttribute("value"))).toEqual(["RUNT", "SIMIT_VEHICULO"]);
  });

  it("con el impuesto habilitado y disponible se ofrece y se guarda como fuente por defecto", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(Response.json(configuracion()));
    vi.stubGlobal("fetch", fetchMock);
    render(
      <PestanaOperacion
        configuracion={configuracion({ contrato: { fuentes_habilitadas: ["RUNT", "IMPUESTO_DEPARTAMENTAL"] }, fuentes_disponibles: DISPONIBLES_CON_IMPUESTO })}
        puedeEditar
      />,
    );

    fireEvent.click(screen.getByRole("checkbox", { name: "Impuesto departamental" }));
    fireEvent.click(screen.getByRole("button", { name: "Guardar operación" }));

    expect(await screen.findByText("Configuración guardada.")).toBeInTheDocument();
    expect((cuerpo(fetchMock, 0) as { fuentes_por_defecto: string[] }).fuentes_por_defecto).toEqual(["RUNT", "IMPUESTO_DEPARTAMENTAL"]);
  });

  it("una fuente por defecto ya guardada que dejó de estar disponible no se ofrece pero no se borra en silencio al guardar", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(Response.json(configuracion()));
    vi.stubGlobal("fetch", fetchMock);
    render(
      <PestanaOperacion
        configuracion={configuracion({
          contrato: { fuentes_habilitadas: ["RUNT", "IMPUESTO_DEPARTAMENTAL"] },
          operacion: { fuentes_por_defecto: ["RUNT", "IMPUESTO_DEPARTAMENTAL"] },
        })}
        puedeEditar
      />,
    );

    expect(screen.queryByRole("checkbox", { name: "Impuesto departamental" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Guardar operación" }));

    expect(await screen.findByText("Configuración guardada.")).toBeInTheDocument();
    expect((cuerpo(fetchMock, 0) as { fuentes_por_defecto: string[] }).fuentes_por_defecto).toEqual(["RUNT", "IMPUESTO_DEPARTAMENTAL"]);
  });
});
