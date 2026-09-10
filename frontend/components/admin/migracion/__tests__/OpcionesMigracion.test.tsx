import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OpcionesMigracion } from "@/components/admin/migracion/OpcionesMigracion";
import type { Instancia } from "@/lib/migracion/types";

/**
 * El aviso nació de una corrida real contra la copia de producción: simular el traspaso 26350 con
 * «datos + adjuntos» devuelve `adjuntos → NotMigrated` con problemas, porque el motor busca el
 * trámite en `migration_map` y en simulación la data plana no se escribe. Sin avisar, quien carga
 * veinte y le da a simular con todo marcado ve veinte filas rojas y concluye que la migración está
 * rota.
 */
function pintar(
  props: { instancias?: Instancia[]; dryRun?: boolean; onInstancias?: (v: Instancia[]) => void } = {},
) {
  return render(
    <OpcionesMigracion
      instancias={props.instancias ?? []}
      onInstancias={props.onInstancias ?? vi.fn()}
      dryRun={props.dryRun ?? true}
      onDryRun={vi.fn()}
    />,
  );
}

/**
 * El bug que se vio usando la consola: la lista vacía significa «las tres» y las tres casillas se
 * pintan marcadas, pero al alternar no se expandía. Un clic sobre Documentos —marcado a la vista—
 * no lo encontraba en la lista vacía, así que lo AÑADÍA: desmarcar uno desmarcaba los otros dos.
 */
describe("OpcionesMigracion — marcar y desmarcar instancias", () => {
  it("desmarcar una con la lista vacía deja las OTRAS DOS, no solo esa", async () => {
    const usuario = userEvent.setup();
    const onInstancias = vi.fn();
    pintar({ instancias: [], onInstancias });

    await usuario.click(screen.getByRole("checkbox", { name: /Documentos/i }));

    expect(onInstancias).toHaveBeenCalledWith(["datos", "adjuntos"]);
  });

  it("con la lista vacía las tres se ven marcadas", () => {
    pintar({ instancias: [] });

    for (const nombre of [/Datos/i, /Adjuntos/i, /Documentos/i]) {
      expect(screen.getByRole("checkbox", { name: nombre })).toBeChecked();
    }
  });

  it("vuelve a marcar en el orden canónico, no en el de los clics", async () => {
    const usuario = userEvent.setup();
    const onInstancias = vi.fn();
    pintar({ instancias: ["documentos"], onInstancias });

    await usuario.click(screen.getByRole("checkbox", { name: /Datos/i }));

    expect(onInstancias).toHaveBeenCalledWith(["datos", "documentos"]);
  });

  /**
   * Correr cero instancias no existe, y como la lista vacía significa «las tres», quitar la última
   * las volvería a marcar todas. Se bloquea la casilla antes que hacer eso.
   */
  it("no deja desmarcar la última que queda", () => {
    pintar({ instancias: ["datos"] });

    expect(screen.getByRole("checkbox", { name: /Datos/i })).toBeDisabled();
    expect(screen.getByRole("checkbox", { name: /Adjuntos/i })).toBeEnabled();
  });
});

// Un trozo que vive en UN solo nodo de texto: el aviso lleva un <strong> en medio y una expresión
// que lo cruce no encuentra nada aunque el texto esté en pantalla.
const AVISO = /se cuelgan de la data plana/i;

describe("OpcionesMigracion — aviso de dependencia", () => {
  it("avisa al simular con las tres instancias", () => {
    pintar({ instancias: [], dryRun: true });

    expect(screen.getByText(AVISO)).toBeInTheDocument();
  });

  it("avisa al simular con adjuntos marcado", () => {
    pintar({ instancias: ["datos", "adjuntos"], dryRun: true });

    expect(screen.getByText(AVISO)).toBeInTheDocument();
  });

  /** Solo datos no depende de nada: avisar aquí sería ruido. */
  it("no avisa al simular solo datos", () => {
    pintar({ instancias: ["datos"], dryRun: true });

    expect(screen.queryByText(AVISO)).not.toBeInTheDocument();
  });

  /**
   * Migrando de verdad la data plana SÍ se escribe, así que los adjuntos encuentran el trámite y el
   * aviso dejaría de ser cierto.
   */
  it("no avisa al migrar de verdad", () => {
    pintar({ instancias: [], dryRun: false });

    expect(screen.queryByText(AVISO)).not.toBeInTheDocument();
  });

  it("el aviso aparece y desaparece al cambiar de modo", async () => {
    const usuario = userEvent.setup();

    function Contenedor() {
      const [modo, setModo] = useState(true);
      return (
        <OpcionesMigracion
          instancias={[]}
          onInstancias={vi.fn()}
          dryRun={modo}
          onDryRun={setModo}
        />
      );
    }

    render(<Contenedor />);
    expect(screen.getByText(AVISO)).toBeInTheDocument();

    await usuario.click(screen.getByRole("radio", { name: /Migración/i }));

    expect(screen.queryByText(AVISO)).not.toBeInTheDocument();
  });
});
