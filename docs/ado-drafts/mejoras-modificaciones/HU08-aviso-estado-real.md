# HU08 — [FRONTEND] Aviso del detalle del trámite acorde al estado real

| Campo | Valor |
|-------|-------|
| Tipo | `[FRONTEND]` |
| Story Points | 3 |
| Estado | Pendiente |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | _pendiente de registro_ |
| Ajuste origen | `modificaciones.txt:42` |
| Depende de | HU06, HU07 (el mensaje debe reflejar lo que el estado permite) |

## Descripción

**Como** gestor que consulta un trámite
**Quiero** que el aviso de la parte superior describa el estado real del trámite
**Para** no leer un mensaje de envío a tránsito cuando el trámite ya está aprobado, rechazado o anulado

## Criterios de aceptación

```gherkin
Escenario: trámite entregado
  Dado un trámite en estado entregado
  Cuando el gestor abre el detalle
  Entonces el aviso indica que fue enviado a tránsito y que ya no puede editarse

Escenario: trámite aprobado
  Dado un trámite en estado aprobado
  Cuando el gestor abre el detalle
  Entonces el aviso indica que el trámite está aprobado y que la documentación es definitiva
  Y no ofrece generar documentación

Escenario: trámite rechazado o anulado
  Dado un trámite en estado rechazado o anulado
  Cuando el gestor abre el detalle
  Entonces el aviso corresponde a ese estado

Escenario: subsanación activa
  Dado un trámite rechazado con subsanación activa
  Cuando el gestor abre el detalle
  Entonces se conserva el panel de subsanación y el aviso no contradice que el trámite es editable
```

## Notas técnicas

`TramiteWizard.tsx:732-748` imprime un texto fijo para **cualquier** estado no editable:

> "Enviado a tránsito — solo visualización. Este trámite ya no puede editarse, pero aún puedes generar
> o descargar el FUR y el expediente consolidado."

El flag que lo dispara es `fullReadOnly`, que agrupa `entregado`, `aprobado`, `rechazado` y `anulado`
(ver la derivación en `:315-324`). Hay que derivar el mensaje del estado concreto (`estadoTramite`).

**Dos incoherencias a corregir de paso:**
1. El mensaje promete "generar" documentación, que tras HU06 dejará de ser cierto en aprobado/anulado.
2. El banner de borrador finalizado (`:750-767`) sí distingue su estado; el nuevo aviso debe convivir
   con él y con `SubsanacionPanel` sin solaparse.

## Archivos previstos

- `frontend/components/operacion/TramiteWizard.tsx`
- Tests: `frontend/__tests__/tramite-wizard.test.tsx` (hoy `:333` asevera el texto "solo
  visualización" — habrá que actualizarlo), `frontend/__tests__/hu10874-subsanacion-wizard.test.tsx`
