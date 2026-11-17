import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { HistorialSaneamiento, actorHistorial } from "@/components/backoffice/HistorialSaneamiento";
import type { EventoHistorialBackoffice } from "@/lib/backoffice/saneamientos";

// Uso de ejemplo: <HistorialSaneamiento historial={san.historial} usuarioSesionId={sesion?.usuario.id} /> en la gestión del
// backoffice. E8 (D-42/UA-8): el actor de cada evento es «Tú» (FLIT con el usuario de la sesión), «Otro operador de FLIT»
// (FLIT con otro usuario), «FLIT» (sin usuario: sistema) o «Usuario de la compañía». Nunca un id ni un trozo de id.

const YO = "5f2c9a7e-1b3d-4e6f-8a9b-0c1d2e3f4a5b";
const OTRO = "a1b2c3d4-0000-7000-8000-0000000000f5";
const CLIENTE = "c0ffee00-1234-7000-8000-00000000abcd";

const evento = (e: Partial<EventoHistorialBackoffice>): EventoHistorialBackoffice => ({
  evento: "TOMADO",
  ocurrido_en: "2026-11-04T15:00:00+00:00",
  actor: "FLIT",
  fuente: null,
  motivo: null,
  usuario_id: null,
  ...e,
});

/** Todo trozo de 4+ caracteres hexadecimales de un uuid (los grupos y el prefijo de 8 que se mostraba antes). */
const trozos = (uuid: string) => [uuid, uuid.slice(0, 8), ...uuid.split("-")].filter((t) => t.length >= 4 && !/^0+$/.test(t));

describe("actorHistorial — E8 (D-42/UA-8)", () => {
  it("FLIT con el usuario de la sesión es «Tú»", () => {
    expect(actorHistorial(evento({ usuario_id: YO }), YO)).toBe("Tú");
  });
  it("compara sin distinguir mayúsculas (el uuid puede llegar en otro caso)", () => {
    expect(actorHistorial(evento({ usuario_id: YO.toUpperCase() }), YO)).toBe("Tú");
  });
  it("FLIT con otro usuario es «Otro operador de FLIT»", () => {
    expect(actorHistorial(evento({ usuario_id: OTRO }), YO)).toBe("Otro operador de FLIT");
  });
  it("FLIT con usuario y sin sesión cargada es «Otro operador de FLIT», nunca «Tú»", () => {
    expect(actorHistorial(evento({ usuario_id: OTRO }), null)).toBe("Otro operador de FLIT");
    expect(actorHistorial(evento({ usuario_id: OTRO }), undefined)).toBe("Otro operador de FLIT");
  });
  it("FLIT sin usuario_id (sistema) es «FLIT»", () => {
    expect(actorHistorial(evento({ usuario_id: null }), YO)).toBe("FLIT");
    expect(actorHistorial(evento({ usuario_id: "" }), YO)).toBe("FLIT");
  });
  it("FLIT con el uuid vacío (Guid.Empty del back) es «FLIT», aunque la sesión también lo traiga", () => {
    const vacio = "00000000-0000-0000-0000-000000000000";
    expect(actorHistorial(evento({ usuario_id: vacio }), vacio)).toBe("FLIT");
  });
  it("la compañía es «Usuario de la compañía», aunque el id coincida con el de la sesión", () => {
    expect(actorHistorial(evento({ evento: "SOLICITADO", actor: "COMPANIA", usuario_id: CLIENTE }), YO)).toBe("Usuario de la compañía");
    expect(actorHistorial(evento({ evento: "SOLICITADO", actor: "COMPANIA", usuario_id: YO }), YO)).toBe("Usuario de la compañía");
  });
});

describe("HistorialSaneamiento — los cuatro actores sin ningún fragmento de id", () => {
  const historial = [
    evento({ evento: "SOLICITADO", ocurrido_en: "2026-11-03T15:00:00+00:00", actor: "COMPANIA", usuario_id: CLIENTE }),
    evento({ evento: "TOMADO", ocurrido_en: "2026-11-04T15:00:00+00:00", usuario_id: OTRO }),
    evento({ evento: "OBSERVACION_EDITADA", ocurrido_en: "2026-11-05T15:00:00+00:00", usuario_id: YO }),
    evento({ evento: "CERRADO_SANEADO", ocurrido_en: "2026-11-06T15:00:00+00:00", usuario_id: null }),
  ];

  it("rotula cada evento según quién actuó, el más reciente primero", () => {
    render(<HistorialSaneamiento historial={historial} usuarioSesionId={YO} />);
    const items = within(screen.getByRole("list", { name: "Historial" })).getAllByRole("listitem");
    expect(items.map((li) => li.querySelector("[data-actor]")?.textContent)).toEqual(["FLIT", "Tú", "Otro operador de FLIT", "Usuario de la compañía"]);
  });

  it("no aparece ningún uuid ni trozo de uuid, ni el rótulo viejo «FLIT · usuario»", () => {
    const { container } = render(<HistorialSaneamiento historial={historial} usuarioSesionId={YO} />);
    const texto = container.textContent ?? "";
    for (const id of [YO, OTRO, CLIENTE]) for (const t of trozos(id)) expect(texto.toLowerCase()).not.toContain(t.toLowerCase());
    expect(texto).not.toMatch(/FLIT · usuario/);
    expect(texto).not.toMatch(/[0-9a-f]{8}/i);
  });

  it("sin sesión, el evento propio sale «Otro operador de FLIT» (nunca el id)", () => {
    render(<HistorialSaneamiento historial={[evento({ usuario_id: YO })]} />);
    const [item] = within(screen.getByRole("list", { name: "Historial" })).getAllByRole("listitem");
    expect(item).toHaveTextContent("Otro operador de FLIT");
    expect(item.textContent).not.toContain(YO.slice(0, 8));
  });
});
