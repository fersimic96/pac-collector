# Frontend bridge

`httpTransport.ts` reemplaza `tauriClient.ts` del frontend React original. Mantiene
exactamente la misma firma (`ipcInvoke`, `ipcListenAppEvent`) para que **los
componentes y views no necesiten cambios**: solo se cambian los imports en los 3
archivos `Repository*Impl.ts` y el resto sigue funcionando.

## Cómo aplicarlo al frontend

Asumiendo que el frontend React vive en `pac-collector-s/src/` (o donde esté):

1. Copiar `httpTransport.ts` a `src/infrastructure/ipc/httpTransport.ts`.
2. En `src/infrastructure/repositories/SampleRepositoryImpl.ts`,
   `InstrumentRepositoryImpl.ts` y `ConfigRepositoryImpl.ts` cambiar:
   ```ts
   import { ipcInvoke } from "@/infrastructure/ipc/tauriClient";
   ```
   por:
   ```ts
   import { ipcInvoke } from "@/infrastructure/ipc/httpTransport";
   ```
3. Si hay un suscriptor de eventos vía `ipcListenAppEvent`, cambiar el import
   análogamente.
4. Eliminar (o renombrar `.bak`) el archivo `tauriClient.ts` para evitar imports
   accidentales.
5. Quitar las deps de Tauri del `package.json` (`@tauri-apps/api`,
   `@tauri-apps/plugin-dialog`). El bridge usa `fetch` y `WebSocket` nativos.
6. `pickFolder` de `ConfigRepositoryImpl.ts` actualmente usa
   `@tauri-apps/plugin-dialog`. En el shell Photino se reemplaza por un endpoint
   del API o por `<input type="file" webkitdirectory>` (HTML5). Ver TODO abajo.

## Configuración runtime

Por default, el transport apunta a `http://127.0.0.1:5174` (puerto del Api). Para
overridear (ej. dev con vite proxy o Photino con base url custom):

```ts
(window as any).__PAC_API_BASE_URL__ = "http://127.0.0.1:5174";
```

## Build pipeline (deploy)

```
cd frontend && pnpm install && pnpm build
```

El `dist/` resultante se copia al `wwwroot/` del Api antes de empaquetar el
installer. En el csproj del Api se puede agregar:

```xml
<ItemGroup>
  <Content Include="wwwroot/**" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

## Pendiente

- **pickFolder**: reemplazar el dialogo Tauri por un endpoint `POST /api/system/pick-folder`
  que use el `IFileDialog` de Windows vía P/Invoke, o equivalente macOS/Linux.
  Mientras tanto el usuario tipea la ruta manualmente.
- **WebSocket reconnect backoff**: incluido (500ms → 30s). Si el frontend quiere
  status visible ("conectado/desconectado") hay que exponer el state del socket.

## Commands soportados

Los 19 commands del IPC original están todos mapeados. Si aparece un command
nuevo en el frontend que no está en `routeFor()`, tira `IpcError` con el nombre.
