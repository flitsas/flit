# HU20 — Correo electrónico repetible entre comprador y vendedor · **DESCARTADA**

| Campo | Valor |
|-------|-------|
| Tipo | — |
| Story Points | 0 |
| Estado | **Descartada — el comportamiento ya existe en `develop`** |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | _no se registra en ADO_ |
| Ajuste origen | `modificaciones.txt:40` |

## Ajuste solicitado

> Ajustar la validación de correo electrónico al momento de registrar un trámite para que permita el
> mismo valor en los pasos de comprador y vendedor. (Se puede repetir el correo electrónico)

## Por qué se descarta

La HU #11019 ya retiró el bloqueo por correo compartido en **las dos capas**, así que el
comportamiento pedido ya está en `develop`:

- **Frontend** — `frontend/components/operacion/ActorsForm.tsx:196-203`: la regla vendedor≠comprador
  solo compara el **documento**. El comentario de la HU #11019 lo dice explícitamente: *"el CORREO
  COMPARTIDO ya no bloquea: es legítimo que ambas partes usen el mismo buzón"*.
- **Backend** — `services/core-api/src/Flit.Tramites.Domain/Tramites/Services/TraspasoPartes.cs:50`:
  `MensajeDuplicadas` devuelve mensaje **solo** si `MismoDocumento`. `DetectarDuplicadas` sigue
  calculando `MismoEmail`, pero se conserva únicamente como dato informativo para quien lo consulte.

## Único residuo corregido

El docstring de `validateActors` (`ActorsForm.tsx:155`) seguía anunciando *"vendedor≠comprador por
doc/email"*, contradiciendo al código. Se corrigió junto con HU19 para que la documentación no
desoriente a quien lea la función:

```
Valida requeridos + formato de email + (traspaso) vendedor≠comprador por DOCUMENTO.
El correo compartido entre las partes no bloquea desde la HU #11019.
```

## Verificación sugerida antes de dar el ajuste por cerrado ante el negocio

Prueba manual en un traspaso: usar el mismo correo en comprador y vendedor y guardar ambos pasos. Debe
guardar sin error. Si el negocio reporta lo contrario en un ambiente concreto, revisar que ese
ambiente tenga la HU #11019 desplegada antes de reabrir la HU.
