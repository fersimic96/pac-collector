// Bridge: cliente HTTP/WebSocket que reemplaza la implementacion Tauri.
// Mantiene las mismas exports (ipcInvoke, ipcListenAppEvent) para no tocar el resto del frontend.
// El backend .NET sirve el API en /api/* (proxy de vite a http://127.0.0.1:5174).

import { IpcError } from "@/domain/errors/domainErrors";

function baseUrl(): string {
  const w = globalThis as unknown as { __PAC_API_BASE_URL__?: string };
  // si esta override, usar eso; si no, usar relative (vite proxy lo manda a :5174)
  return w.__PAC_API_BASE_URL__ ?? "";
}

export async function ipcInvoke<T>(command: string, args?: Record<string, unknown>): Promise<T> {
  const route = routeFor(command, args);
  try {
    const res = await fetch(`${baseUrl()}${route.path}`, {
      method: route.method,
      headers: route.body !== undefined ? { "Content-Type": "application/json" } : undefined,
      body: route.body !== undefined ? JSON.stringify(route.body) : undefined,
    });
    if (!res.ok) {
      const text = await res.text().catch(() => "");
      throw new IpcError(command, `HTTP ${res.status}: ${text || res.statusText}`);
    }
    if (res.status === 204) return undefined as unknown as T;
    const ct = res.headers.get("content-type") ?? "";
    if (ct.includes("application/json")) return (await res.json()) as T;
    return (await res.text()) as unknown as T;
  } catch (e) {
    if (e instanceof IpcError) throw e;
    throw new IpcError(command, String(e));
  }
}

export type UnlistenFn = () => void;

export async function ipcListenAppEvent<T>(callback: (payload: T) => void): Promise<UnlistenFn> {
  let socket: WebSocket | null = null;
  let closed = false;
  let backoffMs = 500;

  const connect = () => {
    if (closed) return;
    // si baseUrl es relativo, construir wsUrl en base a window.location
    const base = baseUrl();
    const wsBase = base
      ? base.replace(/^http/, "ws")
      : `${window.location.protocol === "https:" ? "wss" : "ws"}://${window.location.host}`;
    socket = new WebSocket(`${wsBase}/api/events`);
    socket.onopen = () => { backoffMs = 500; };
    socket.onmessage = (msg) => {
      try {
        const parsed = JSON.parse(msg.data) as T;
        callback(parsed);
      } catch {
        /* no JSON: ignorar */
      }
    };
    socket.onclose = () => {
      if (closed) return;
      setTimeout(connect, backoffMs);
      backoffMs = Math.min(backoffMs * 2, 30_000);
    };
    socket.onerror = () => { /* onclose se encarga del retry */ };
  };

  connect();
  return () => {
    closed = true;
    try { socket?.close(); } catch { /* best-effort */ }
  };
}

// ── mapping command → ruta HTTP ──

interface Route {
  method: "GET" | "POST" | "PATCH" | "DELETE";
  path: string;
  body?: unknown;
}

function routeFor(command: string, args?: Record<string, unknown>): Route {
  switch (command) {
    case "ping":
      return { method: "GET", path: "/api/health" };
    case "list_instruments":
      return { method: "GET", path: "/api/instruments" };
    case "get_sample":
      return { method: "GET", path: `/api/samples/${encodeURIComponent(String(args?.uuid))}` };
    case "list_samples":
      return {
        method: "POST",
        path: "/api/samples/search",
        body: { ...args?.filters as object, offset: args?.offset, limit: args?.limit },
      };
    case "update_instrument_alias":
      return {
        method: "PATCH",
        path: `/api/instruments/${encodeURIComponent(String(args?.serial))}/alias`,
        body: { alias: args?.alias ?? null },
      };
    case "set_instrument_route":
      return {
        method: "PATCH",
        path: `/api/instruments/${encodeURIComponent(String(args?.serial))}/route`,
        body: {
          hotFolderFormat: args?.hotFolderFormat,
          hotFolderDir: args?.hotFolderDir,
          alias: args?.alias,
        },
      };
    case "server_status":
      return { method: "GET", path: "/api/server/status" };
    case "start_listeners":
      return { method: "POST", path: "/api/listeners/start" };
    case "stop_listeners":
      return { method: "POST", path: "/api/listeners/stop" };
    case "start_print_listener":
      return { method: "POST", path: "/api/print-listener/start" };
    case "stop_print_listener":
      return { method: "POST", path: "/api/print-listener/stop" };
    case "list_plugins":
      return { method: "GET", path: "/api/plugins" };
    case "set_plugin_enabled":
      return {
        method: "PATCH",
        path: `/api/plugins/${encodeURIComponent(String(args?.id))}/enabled`,
        body: { enabled: args?.enabled },
      };
    case "get_config":
      return { method: "GET", path: "/api/config" };
    case "save_config":
      return { method: "POST", path: "/api/config", body: args?.config };
    case "list_local_ips":
      return { method: "GET", path: "/api/network/local-ips" };
    case "list_network_interfaces":
      return { method: "GET", path: "/api/network/interfaces" };
    case "open_network_settings":
      return { method: "POST", path: "/api/system/open-network-settings" };
    case "os_platform":
      return { method: "GET", path: "/api/system/platform" };
    default:
      throw new IpcError(command, `no HTTP route mapped for command "${command}"`);
  }
}
