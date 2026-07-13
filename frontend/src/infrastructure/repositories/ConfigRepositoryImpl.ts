

import { ipcInvoke } from "@/infrastructure/ipc/tauriClient";
import type { AppConfigDTO } from "@/domain/outputDTO/AppConfigDTO";

export interface IConfigRepository {
  load(): Promise<AppConfigDTO>;
  save(config: AppConfigDTO): Promise<void>;
  listLocalIps(): Promise<string[]>;
  pickFolder(label: string): Promise<string | null>;
}

export class ConfigRepositoryImpl implements IConfigRepository {
  async load(): Promise<AppConfigDTO> {
    return await ipcInvoke<AppConfigDTO>("get_config");
  }

  async save(config: AppConfigDTO): Promise<void> {
    await ipcInvoke<void>("save_config", { config });
  }

  async listLocalIps(): Promise<string[]> {
    return await ipcInvoke<string[]>("list_local_ips");
  }

  async pickFolder(label: string): Promise<string | null> {
    // sin Tauri: caemos a un prompt HTML5 (el usuario tipea la ruta).
    // TODO en el .NET shell real: exponer endpoint POST /api/system/pick-folder que
    // use IFileDialog en Windows o equivalente macOS/Linux.
    const isTauri = typeof (globalThis as { __TAURI__?: unknown }).__TAURI__ !== "undefined";
    if (isTauri) {
      const { open } = await import("@tauri-apps/plugin-dialog");
      const picked = await open({ directory: true, multiple: false, title: label });
      return typeof picked === "string" ? picked : null;
    }
    const v = window.prompt(`${label} (pega la ruta absoluta)`, "");
    return v && v.trim().length > 0 ? v.trim() : null;
  }
}
