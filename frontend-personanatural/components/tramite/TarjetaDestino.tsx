"use client";

// Tarjeta «Secretaría de destino» del traslado de cuenta. Al elegir, guarda de una vez el id en el borrador (el servidor deriva
// el resto de claves del organismo): así el diagnóstico puede esperar a que haya destino antes de consultar. La monta el
// diagnóstico del portal web y el paso Requisitos del punto (que no tiene diagnóstico propio).
import { useState } from "react";
import { useWizard } from "@/components/tramite/ContextoWizard";
import { OrganismoDestinoField } from "@/components/tramite/OrganismoDestinoField";
import { Card } from "@/components/tramite/piezas";
import type { OrganismoDestino } from "@/lib/api/cliente";
import { mensajeDe } from "@/lib/api/errores";
import { CLAVE_NOMBRE_DESTINO, destinoDelTipo } from "@/lib/tramite/declaraciones";
import { MENSAJE_OBLIGATORIO } from "@/lib/tramite/requisitos";
import { esEditable } from "@/lib/tramite/pasos";

export function TarjetaDestino({ intentoAvance }: { intentoAvance: number }) {
  const { cliente, definicion, solicitud, guardarConVersion, reportarError } = useWizard();
  const destino = destinoDelTipo(definicion);
  const [guardando, setGuardando] = useState(false);
  const [errorGuardado, setErrorGuardado] = useState<string | null>(null);
  const [nombre, setNombre] = useState<string | undefined>(undefined);
  if (!destino) return null;

  const valor = (solicitud.fieldValues[destino.fieldKey] ?? "").trim();
  const falta = destino.obligatoria && valor === "";
  const error = errorGuardado ?? (intentoAvance > 0 && falta ? MENSAJE_OBLIGATORIO : undefined);

  const elegir = async (organismo: OrganismoDestino) => {
    setGuardando(true);
    setErrorGuardado(null);
    setNombre(organismo.nombre);
    try {
      await guardarConVersion((version) => cliente.solicitudes.guardarCampos(solicitud.solicitudId, { version, valores: { [destino.fieldKey]: organismo.id } }));
    } catch (e) {
      reportarError(e);
      setErrorGuardado(mensajeDe(e));
    } finally {
      setGuardando(false);
    }
  };

  return (
    <Card title="Secretaría de destino" id="requisitos-destino">
      <p className="mb-3 text-sm font-light text-muted-foreground">Elige la secretaría de tránsito a la que se trasladará la cuenta del vehículo.</p>
      <div className="max-w-xl">
        <OrganismoDestinoField
          cliente={cliente}
          valor={valor}
          nombreElegido={nombre ?? solicitud.fieldValues[CLAVE_NOMBRE_DESTINO] ?? (valor ? "Secretaría elegida" : undefined)}
          obligatorio={destino.obligatoria}
          disabled={guardando || !esEditable(solicitud.estado)}
          error={error}
          onElegir={(o) => void elegir(o)}
        />
      </div>
      {guardando && (
        <p role="status" className="mt-2 text-xs text-muted-foreground">
          Guardando secretaría de destino...
        </p>
      )}
    </Card>
  );
}
