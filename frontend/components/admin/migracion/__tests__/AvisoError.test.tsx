import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { AvisoError } from "@/components/admin/migracion/AvisoError";
import { ErrorMigracion } from "@/lib/migracion/client";

function pintar(titulo: string, detalle: string, estado = 500) {
  return render(<AvisoError error={new ErrorMigracion({ titulo, detalle, estado })} />);
}

/**
 * Los estados que solo se arreglan tocando el despliegue dicen el hecho y nada más. El detalle que
 * traen —nombres de variables, el contenedor a recrear, el error de red crudo— es diagnóstico:
 * quien lo ve en pantalla no puede hacer nada con él, y quien sí puede lo tiene en la respuesta.
 */
describe("AvisoError — estados del ambiente", () => {
  it.each([
    ["migracion.apagado", "El migrador está apagado en este ambiente"],
    ["migracion.sin_llave", "El migrador no está disponible en este ambiente"],
    ["migracion.host_inalcanzable", "El migrador no está disponible en este ambiente"],
    ["migracion.llave_invalida", "El migrador rechazó la llave"],
  ])("%s muestra el titular y nada más", (codigo, esperado) => {
    pintar(codigo, "MIGRACION_API_KEY no está puesta; recrea el contenedor migracion-api");

    expect(screen.getByText(esperado)).toBeInTheDocument();
    expect(screen.queryByText(/MIGRACION_API_KEY/)).not.toBeInTheDocument();
    expect(screen.queryByText(/migracion-api/)).not.toBeInTheDocument();
  });
});

/** Los que sí dependen de quien opera conservan su detalle y su siguiente paso. */
describe("AvisoError — lo que sí puede resolver quien opera", () => {
  it("un trámite en curso explica qué hacer", () => {
    pintar("migracion.tramite_en_curso", "Ya hay una migración en vuelo para el 26350.");

    expect(screen.getByText("Ese trámite ya se está migrando")).toBeInTheDocument();
    expect(screen.getByText(/Ya hay una migración en vuelo/)).toBeInTheDocument();
    expect(screen.getByText(/Reintentar no duplica nada/)).toBeInTheDocument();
  });

  it("el migrador ocupado dice que se reintente", () => {
    pintar("migracion.ocupado", "Dos migraciones a la vez es el tope.");

    expect(screen.getByText("El migrador está ocupado")).toBeInTheDocument();
    expect(screen.getByText(/Reintenta en un momento/)).toBeInTheDocument();
  });

  /** Un error que no viene del host tampoco puede dejar la caja vacía. */
  it("un error cualquiera cae en el titular genérico", () => {
    render(<AvisoError error={new Error("se cayó la red")} />);

    expect(screen.getByText("La migración no se pudo lanzar")).toBeInTheDocument();
    expect(screen.getByText("se cayó la red")).toBeInTheDocument();
  });
});
