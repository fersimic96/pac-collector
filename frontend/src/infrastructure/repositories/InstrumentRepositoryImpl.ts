

import { ipcInvoke } from "@/infrastructure/ipc/tauriClient";
import type { InstrumentOutputDTO } from "@/domain/outputDTO/InstrumentOutputDTO";

export interface IInstrumentRepository {
  listAll(): Promise<InstrumentOutputDTO[]>;
  updateAlias(serial: string, alias: string | null): Promise<void>;
}

export class InstrumentRepositoryImpl implements IInstrumentRepository {
  async listAll(): Promise<InstrumentOutputDTO[]> {
    return await ipcInvoke<InstrumentOutputDTO[]>("list_instruments");
  }

  async updateAlias(serial: string, alias: string | null): Promise<void> {
    await ipcInvoke<void>("update_instrument_alias", { serial, alias });
  }
}
