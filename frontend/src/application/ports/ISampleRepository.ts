

import type { SampleOutputDTO } from "@/domain/outputDTO/SampleOutputDTO";
import type { SampleFiltersInput } from "@/application/inputDTO/SampleFiltersInput";

export interface SamplePage {
  items: SampleOutputDTO[];
  total: number;
  offset: number;
  limit: number;
}

export interface ISampleRepository {
  listPaginated(filters: SampleFiltersInput, offset: number, limit: number): Promise<SamplePage>;
  getByUuid(uuid: string): Promise<SampleOutputDTO | null>;
  countReceivedToday(): Promise<number>;
  exportCsv(filters: SampleFiltersInput, destPath: string): Promise<void>;
  openInExplorer(uuid: string): Promise<void>;
}
