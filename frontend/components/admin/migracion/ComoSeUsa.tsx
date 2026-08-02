"use client";

import { useState } from "react";
import { ChevronDown, ChevronRight, HelpCircle } from "lucide-react";

/**
 * La ayuda de la consola, plegada por omisión.
 *
 * Se despliega cerrada porque quien migra una ola entra veinte veces al día y no necesita leerla
 * cada vez; pero existe porque la primera vez sí hacen falta tres cosas que no son adivinables:
 * que reintentar no rompe nada, que la simulación no escribe, y que un trámite migrado es una
 * FOTO de solo lectura y no un trámite vivo.
 */
export function ComoSeUsa() {
  const [abierta, setAbierta] = useState(false);

  return (
    <section className="rounded-2xl border border-[#DFE5ED] bg-white/60 dark:border-white/10 dark:bg-[#0B0F14]/60">
      <button
        type="button"
        onClick={() => setAbierta(!abierta)}
        aria-expanded={abierta}
        className="flex w-full items-center gap-2 px-4 py-3 text-left text-sm font-semibold"
      >
        <HelpCircle className="h-4 w-4 shrink-0" aria-hidden="true" style={{ color: "#557EFF" }} />
        Cómo se usa
        {abierta ? (
          <ChevronDown className="ml-auto h-4 w-4 opacity-60" aria-hidden="true" />
        ) : (
          <ChevronRight className="ml-auto h-4 w-4 opacity-60" aria-hidden="true" />
        )}
      </button>

      {abierta && (
        <div className="border-t border-[#DFE5ED] px-4 py-4 text-sm dark:border-white/10">
          {/*
            `max-w-prose` en cada punto: a ancho completo las líneas pasaban de 200 caracteres y el
            ojo se pierde al saltar de renglón. Dos columnas en pantalla ancha para no dejar la
            mitad derecha vacía a cambio.
          */}
          <div className="grid grid-cols-1 gap-x-10 gap-y-4 lg:grid-cols-2 [&>*]:max-w-prose">
          <Punto titulo="Empieza por uno">
            Antes de cargar un archivo de veinte, migra uno solo con la pestaña «Un trámite» y mira
            el reporte. Es la forma barata de descubrir que el tipo estaba mal o que el ambiente no
            tiene el migrador encendido.
          </Punto>

          <Punto titulo="La simulación no escribe nada">
            En modo «Simulación» el migrador lee todo, dice qué haría y no crea nada. Es el modo por
            defecto, y las filas simuladas siguen pendientes: para migrarlas de verdad, cambia el
            modo a «Migración» y vuelve a lanzarlas.
          </Punto>

          <Punto titulo="Reintentar es seguro">
            Un trámite ya migrado no se duplica: el migrador lo reconoce y responde «ya estaba». Si
            una carga se corta a la mitad, vuelve a lanzarla sin miedo.
          </Punto>

          <Punto titulo="Simular adjuntos exige que ya esté migrado">
            Los adjuntos y los documentos se cuelgan de la data plana, y en simulación esa no se
            escribe. Si simulas las tres sobre un trámite que aún no está en V2, esas dos saldrán en
            rojo como «Sin migrar» y no es un fallo. Para la primera pasada, simula solo «Datos».
          </Punto>

          <Punto titulo="Las tres instancias van en orden">
            Los datos primero, luego los adjuntos y al final los documentos: los dos últimos
            necesitan que el trámite exista. Puedes correr solo una, pero si los datos no entraron,
            lo demás no tiene dónde colgarse.
          </Punto>

          <Punto titulo="Un trámite migrado es una foto">
            Lo que llega de V1 se ve en V2 en modo consulta, con su historial y sus documentos. No
            es un trámite en curso: no continúa por el flujo ni cambia de estado.
          </Punto>

          <Punto titulo="Si cierras la pestaña no pierdes el avance">
            El progreso de una carga masiva se guarda en este navegador, y al volver se comprueba
            contra el servidor cuáles quedaron migrados de verdad.
          </Punto>

          <Punto titulo="Para el archivo, usa la plantilla">
            Dos columnas: el tipo (traspaso o matrícula) y el id de V1. Se aceptan .csv y .xlsx. Al
            cargarlo se revisa fila por fila y se dice cuáles no sirven, sin bloquear las demás.
          </Punto>
          </div>
        </div>
      )}
    </section>
  );
}

function Punto({ titulo, children }: { titulo: string; children: React.ReactNode }) {
  return (
    <div>
      <p className="font-semibold">{titulo}</p>
      <p className="mt-0.5 opacity-80">{children}</p>
    </div>
  );
}
