# Ejecutable — PacCollector.MockDevice (pac-mock)

Path: `src/PacCollector.MockDevice/`

Binario: `pac-mock`. Simulador de equipo PAC desde línea de comandos.
Implementa el **lado-equipo** de los dos protocolos para que se pueda
testear el colector sin hardware físico.

---

## Comandos

| Comando | Para qué |
|---|---|
| `pac-mock ipp send` | Manda un IPP print job al colector (modo Print). |
| `pac-mock lims send` | Hace el handshake LIMS Ethernet completo (UDP beacon + ACK + TCP JSON). |

---

## Tres usos

1. **Testing sin hardware**. Durante desarrollo o smoke tests no siempre hay
   un OptiPMD a mano. Con pac-mock se manda una fixture y se verifica que
   el colector reciba y procese todo.

2. **Spec ejecutable del protocolo**. El código de `pac-mock lims send` ES
   la implementación canónica del protocolo proprietary PAC. Si los docs
   markdown divergen del código, el código es la fuente de verdad. Esto
   blinda la documentación: cualquiera puede leer el código, entender
   exactamente qué hace el equipo, y reproducirlo.

3. **CI/CD y demos**. Smoke test end-to-end sin lab. En CI se levanta el
   Api en proceso, se invoca pac-mock para enviar fixtures, se verifica el
   output del hotfolder. Sin hardware. Sin red real.

---

## Program.cs
Path: `src/PacCollector.MockDevice/Program.cs`

Entry point. Dispatcher de subcomandos. Args a mano.

```
pac-mock <command> <subcommand> [options]
```

Comandos:
- `ipp send` → `IppCommand.RunAsync`.
- `lims send` → `LimsCommand.RunAsync`.
- `--help` / `-h` / `help` → muestra usage.

---

## IppCommand.cs
Path: `src/PacCollector.MockDevice/IppCommand.cs`

### `pac-mock ipp send --target HOST:PORT --payload FILE.bin`

Abre TCP al target, manda un POST IPP con el blob como body, lee la
respuesta del colector.

#### Args
- `--target` — destino, ej. `127.0.0.1:631`.
- `--payload` — path al `.bin` capturado.

#### Flujo
1. `File.ReadAllBytesAsync(payload)` → bytes.
2. `TcpClient.ConnectAsync(host, port)`.
3. Manda headers HTTP:
   ```
   POST /ipp HTTP/1.1
   Host: 127.0.0.1:631
   Content-Type: application/ipp
   Content-Length: <N>
   User-Agent: pac-mock/0.1
   Connection: close
   ```
4. Manda el body (los bytes del archivo).
5. `Shutdown(Send)` — cierra el lado write para que el server sepa que
   terminó el payload.
6. Lee la respuesta con timeout 5s.
7. Imprime preview de los primeros 200 bytes.

#### Exit codes
- `0` — OK (mandó y leyó respuesta, sin importar el contenido).
- `64` — Args inválidos.

---

## LimsCommand.cs
Path: `src/PacCollector.MockDevice/LimsCommand.cs`

### `pac-mock lims send --target HOST --json FILE [--udp-port 3000] [--timeout-ms 5000]`

Implementa el lado-equipo del **protocolo LIMS Ethernet completo**, paso
por paso.

#### Constantes
- `Beacon = [0x01, 0x02, 0x03]` (los 3 bytes mágicos del PAC).

#### Args
- `--target` — IP destino (típicamente la IP del colector).
- `--json` — path al archivo JSON con el payload del sample.
- `--udp-port` — puerto UDP destino del beacon (default 3000).
- `--timeout-ms` — timeout para ACK y respuesta TCP (default 5000).

#### Flujo (los 8 pasos)

1. **Bind UDP local en puerto efímero**:
   ```csharp
   using var udp = new UdpClient(0);  // puerto 0 = efímero asignado por OS
   ```

2. **Send beacon** UDP unicast al target:
   ```csharp
   await udp.SendAsync(Beacon, new IPEndPoint(targetAddr, opts.UdpPort));
   ```

3. **Espera ACK** en el UDP socket con timeout:
   ```csharp
   ackResult = await udp.ReceiveAsync(cts.Token);
   ```

4. **Parsea ACK** con regex `^ACK\s+(\S+)\s+(\d+)\s*$`:
   - Group 1 = IP del server.
   - Group 2 = puerto TCP.

5. **Abre TCP** al server:
   ```csharp
   await tcp.ConnectAsync(tcpHost, tcpPort);
   ```

6. **Manda payload**: bytes del JSON + byte `0x00` (NUL terminator).

7. **Lee respuesta** JSON con timeout. Espera algo como
   `{"Error":"","SaveCheckSum":"00A3"}`.

8. **Reporta** y devuelve exit code apropiado.

#### Exit codes
- `0` — Sample procesado, respuesta sin NACK.
- `3` — Timeout esperando ACK.
- `4` — ACK mal formado (no matchea el regex esperado).
- `5` — Respuesta del server contiene `NACK` (procesamiento falló downstream).
- `64` — Args inválidos.

#### Por qué importa que sea spec ejecutable

El protocolo PAC LIMS Ethernet no está documentado por el fabricante. Lo
inferimos por observación de tráfico de red. El doc markdown describe el
formato, pero **el código de LimsCommand.cs es la implementación canónica
del lado-equipo**. Si en algún momento hay duda sobre el formato exacto, el
orden, los timeouts o el manejo de errores, ese archivo es la fuente de
verdad.

---

## Ejemplos de uso

### Smoke test IPP con fixture OptiPMD

```bash
# Terminal A: levanto el colector (con print server enabled)
$ DataDir=/tmp/pac-test dotnet run --project src/PacCollector.Api -c Release

# Terminal B: mando el fixture
$ pac-mock ipp send \
    --target 127.0.0.1:6310 \
    --payload tests/PacCollector.ParityTests/Fixtures/optipmd_print_1216.bin

pac-mock ipp: connecting to 127.0.0.1:6310
pac-mock ipp: payload optipmd_print_1216.bin (40364 bytes)
pac-mock ipp: server response 655 bytes
pac-mock ipp: preview: HTTP/1.1 200 OK
Server: PAC-IRIS-CAPTURE/1.0
Content-Type: application/ipp
...

# Verifico el output
$ cat /tmp/pac-test/db/_global/master.csv
timestamp,serial,analyzerType,sampleId,operator,program,...
2026-06-01T...,1216,OptiPMD,IRAM 2 2024,Fer,ASTM D7345,...,151.5,260.7,1.3,98.1,...
```

### Smoke test LIMS con JSON real

```bash
$ pac-mock lims send \
    --target 127.0.0.1 \
    --json ../lims_json/raw_20260505_172011_140_169_254_69_30.json

pac-mock lims: payload raw_20260505_..._30.json (3873 bytes)
pac-mock lims: target 127.0.0.1:3000 (UDP beacon)
pac-mock lims: bound UDP local :56549
pac-mock lims: sent beacon [0x01 0x02 0x03] -> 127.0.0.1:3000
pac-mock lims: received 18 bytes from 127.0.0.1:3000: "ACK 127.0.0.1 9980"
pac-mock lims: parsed ACK -> TCP 127.0.0.1:9980
pac-mock lims: TCP connected
pac-mock lims: sent 3873B JSON + NUL
pac-mock lims: server response (34B): {"Error":"","SaveCheckSum":"005F"}
```

---

## Por qué no necesita System.CommandLine

Para 2 comandos chicos con 4-5 args cada uno, los args parseados a mano son
~30 líneas por comando. Agregar `System.CommandLine` (paquete beta) suma
una dependencia, complejidad de versionado, y no aporta nada que justifique
esos costos. `pac-mock` y `pac-tool` siguen el mismo patrón.
