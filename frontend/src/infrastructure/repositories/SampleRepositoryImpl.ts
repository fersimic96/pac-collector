

import { ipcInvoke } from "@/infrastructure/ipc/tauriClient";
import type { SampleOutputDTO } from "@/domain/outputDTO/SampleOutputDTO";

export interface SampleFilters {
  serial?: string | null;
  program?: string | null;
  operator?: string | null;
  from?: string | null;
  to?: string | null;
}

export interface SamplePage {
  items: SampleOutputDTO[];
  total: number;
  offset: number;
  limit: number;
}

export interface ISampleRepository {
  listPaginated(filters: SampleFilters, offset: number, limit: number): Promise<SamplePage>;
  getByUuid(uuid: string): Promise<SampleOutputDTO | null>;
}

export class SampleRepositoryImpl implements ISampleRepository {
  async listPaginated(
    filters: SampleFilters,
    offset: number,
    limit: number,
  ): Promise<SamplePage> {
    return await ipcInvoke<SamplePage>("list_samples", { filters, offset, limit });
  }

  async getByUuid(uuid: string): Promise<SampleOutputDTO | null> {
    return await ipcInvoke<SampleOutputDTO | null>("get_sample", { uuid });
  }
}

export interface ServerStatusDTO {
  serverIp: string;
  tcpPort: number;
  udpPort: number;
  instrumentsCount: number;
  samplesToday: number;
  running: boolean;
  
  printRunning: boolean;
  
  printPort: number;
}

export async function getServerStatus(): Promise<ServerStatusDTO> {
  return await ipcInvoke<ServerStatusDTO>("server_status");
}

export async function startListeners(): Promise<void> {
  await ipcInvoke<void>("start_listeners");
}

export async function stopListeners(): Promise<void> {
  await ipcInvoke<void>("stop_listeners");
}

export async function startPrintListener(): Promise<void> {
  await ipcInvoke<void>("start_print_listener");
}

export async function stopPrintListener(): Promise<void> {
  await ipcInvoke<void>("stop_print_listener");
}
