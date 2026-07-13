# Print over Ethernet — equipos PAC como impresoras IPP

## Propósito de este documento

Documentar el flujo de captura de datos cuando el operador activa en el
firmware del equipo PAC la opción de salida **"Print over Ethernet"**, en la
que el equipo se conecta a la IP configurada como impresora de red.

## Naturaleza del protocolo

El transporte es **Internet Printing Protocol (IPP)**, estándar IETF
documentado en RFCs públicos:

- [RFC 8010 — Internet Printing Protocol/1.1: Encoding and Transport](https://datatracker.ietf.org/doc/html/rfc8010)
- [RFC 8011 — Internet Printing Protocol/1.1: Model and Semantics](https://datatracker.ietf.org/doc/html/rfc8011)

El puerto destino estándar es **TCP 631** (asignado por IANA al servicio IPP).

Para equipos PAC más antiguos que no implementan IPP completo, el colector
también acepta **raw PCL/HP-GL** sobre la misma conexión TCP — un patrón
clásico de "raw print queue" sin envoltura HTTP/IPP. El colector clasifica
automáticamente la conexión entrante mirando los primeros bytes (HTTP method
prefix → IPP, cualquier otra cosa → raw).

## Flujo completo

```
┌─────────────┐                              ┌─────────────────┐
│  Equipo PAC │                              │ Colector (IPP)  │
│             │                              │ bind TCP 631    │
│             │                              │                  │
│ start print │                              │                  │
│             │ ─── TCP SYN → :631 ────────► │                  │
│             │                              │                  │
│             │ ─── POST /ipp HTTP/1.1 ────► │                  │
│             │     Content-Type:            │                  │
│             │       application/ipp        │                  │
│             │     [body: IPP request +     │                  │
│             │      PCL/HP-GL print job]    │                  │
│             │                              │                  │
│             │ ◄─── HTTP 200 OK ──────────  │                  │
│             │      Content-Type:           │                  │
│             │        application/ipp       │                  │
│             │      [body: IPP response     │                  │
│             │       successful-ok 0x0000]  │                  │
│             │                              │                  │
│             │ ─── TCP FIN ───────────────► │                  │
└─────────────┘                              └─────────────────┘
```

### Equipos legacy (raw PCL)

Equipos PAC más antiguos (línea IRIS) abren la conexión TCP a 631 pero
**no envuelven** el print job en IPP/HTTP. Envían directamente el flujo PCL
crudo, incluyendo:

- Secuencias de escape PCL (`ESC <param> <final>`)
- Texto plano del reporte intercalado
- Bloque HP-GL final (`%1BIN; ...`) con la curva gráfica
- Marcadores UEL (`ESC %-12345X`) como delimitadores de fin de job

El colector clasifica como `Raw` cualquier conexión cuyos primeros bytes no
matcheen un HTTP method prefix conocido (`POST `, `GET `, `HEAD `, etc.) y
procesa el flujo acumulando bytes hasta encontrar dos UELs o un gap de
inactividad.

## Procesamiento del payload

Tanto en modo IPP como en modo raw, el payload "imprimible" final pasa por
el mismo pipeline:

1. **Strip PCL**: extracción del texto legible removiendo secuencias de
   escape. Implementado en `PclStripper.cs`.
2. **CR-overwrite (opcional)**: para equipos que usan layout de impresora
   de línea con dos columnas vía CR (Windows printer driver behavior).
   Implementado en `CrOverwriteRenderer.cs`.
3. **Field extraction**: el plugin específico del equipo (config JSON en
   `plugins/print/`) aplica patrones `Label: value` o regex custom para
   extraer cada campo del reporte.

El parser engine es agnóstico del equipo. La spec JSON define qué labels y
qué patrones aplicar, por lo que agregar un equipo nuevo no requiere
modificar código C#.

## Identidad del colector como impresora

El colector responde a IPP `Get-Printer-Attributes` declarándose como una
impresora compatible HP LaserJet 4 (`printer-make-and-model: "HP LaserJet 4"`).
Esto maximiza la compatibilidad con drivers de equipos PAC, que asumen
impresoras de la línea HP. La elección del modelo no afecta el procesamiento
del job recibido.

Implementación en `src/PacCollector.Infrastructure/Network/IppResponseBuilder.cs`.

## Por qué este modo es relevante operativamente

El modo "Print over Ethernet" es el **fallback estándar** del colector frente
al modo LIMS Ethernet:

- Si PAC modifica el wire format de LIMS Ethernet en una firmware update, el
  modo print sigue funcionando (IPP es estándar IETF, no cambia).
- Si un equipo nuevo no implementa LIMS Ethernet, casi seguro implementa
  "Print over Ethernet" (es la única vía de output de muchos equipos PAC
  legacy).
- Operativamente, el operador del lab puede preferir este modo porque es
  más simple de configurar en el equipo (es "imprimir a la impresora",
  concepto familiar).

Ambos modos producen el mismo `Sample` canónico downstream — el colector
escribe los mismos archivos en master.csv y hotfolder, independientemente
de por dónde haya llegado el dato.
