# HU08 — [FRONTEND] Aviso del detalle del trámite acorde al estado real

| Campo | Valor |
|-------|-------|
| Tipo | `[FRONTEND]` |
| Story Points | 3 |
| Estado | **Implementada y verificada** (Active en ADO, pendiente de PR) |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | **#11053** |
| Commit | `0efe1b33` |
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

## Implementación (commit `0efe1b33`)

Se extrae el aviso a `ReadOnlyStateNotice` con una tabla `READ_ONLY_NOTICE` por estado, en vez del
texto fijo que se imprimía para cualquier estado no editable:

| Estado | Mensaje | ¿Menciona generar? |
|--------|---------|--------------------|
| `entregado` | Enviado a tránsito — solo visualización | Sí (es cierto en este estado) |
| `aprobado` | Trámite aprobado — documentación definitiva | No; dice explícitamente que ya no se regenera |
| `rechazado` | Trámite rechazado — remite al motivo | No |
| `anulado` | Trámite anulado — quedó sin efecto | No |
| `preparado` | Borrador preparado — se puede radicar | No |

Con fallback para cualquier estado no contemplado. Se conservan el banner propio del borrador
finalizado y el `SubsanacionPanel`: un rechazado **con** subsanación activa no es solo-lectura, así que
no llega a este aviso.

## Verificación

`npx tsc --noEmit` y `eslint` limpios · suite del wizard **50/50** (4 tests nuevos, uno por estado, que
además comprueban que no se anuncia lo que el estado no permite) · `hu10874-subsanacion-wizard` sigue
en verde. El test preexistente que asertaba `/solo visualización/i` no requirió cambios: el mensaje de
`entregado` conserva esa expresión.

## Archivos

- `frontend/components/operacion/TramiteWizard.tsx`
- `frontend/__tests__/tramite-wizard.test.tsx`
