# Descomposición local — Mandatos OT

Registro diferido: sin IDs ADO. Activación `Active` no aplica hasta `/register-work`.

## HU-L1 [BACKEND] · 3 SP

Como SuperAdmin, quiero que al crear un OT nazca con mandato abierto genérico y que el modo del OT cuente en la generación, para no asumir firmante persona.

**AC**
- Given un OT nuevo When se crea el tenant Then existe `transit_office_mandate_config` con `generico` + `open` y mandatario vacío.
- Given config OT `institutional` y sin regla de compañía When se resuelve el mandato Then el modo es `institutional`.
- Given OT legado sin fila When se resuelve Then el modo sigue `signer`.

## HU-L2 [FRONTEND] · 5 SP

Como SuperAdmin u ot_admin, quiero una pestaña Mandatos en el hub del OT para parametrizar el contrato sin ir solo a Plataforma.

**AC**
- Given el hub OT When abro Mandatos Then veo configurar mandato y tipo por compañía (empresa que radica).
- Given estados vacío/carga/error/lleno Then la pantalla los cubre.

## HU-L3 [BACKEND+FRONTEND] · 5 SP

Como gestora, quiero un solo modelo por empresa para todas las familias y que el mandato cliente use plantilla genérica.

**AC**
- Given regla compañía `signer` y OT Sabaneta When se genera Then `template_code` efectivo es `generico`.
- Given la UI de reglas When elijo tipo Then aplica a todas las familias (sin selector por TRASPASO/MATRÍCULAS/OTROS).

## HU-L4 [FRONTEND] · 2 SP

Como SuperAdmin, quiero que Plataforma → Mandatos conviva con el hub OT sobre la misma API/persistencia.

**AC**
- Given ambas pantallas When guardo en una Then la otra lee los mismos datos.
- Given el menú Plataforma When navego a Mandatos Then la entrada sigue visible.

## HU-L5 [BACKEND] · 3 SP

Como operador, quiero que institucional, abierto con `___` e identidad+baúl sigan funcionando.

**AC**
- Given modo institucional When se simula Then no exige firmante persona.
- Given modo abierto When se simula Then el mandatario va vacío y el bloque no se oculta.
- Given mandatario con baúl e identidad When firma Then ambas vías siguen disponibles.
