

export interface InstrumentOutputDTO {
  serial: string;
  analyzerType: string;
  alias: string | null;
  lastIp: string | null;
  firmware: string | null;
  firstSeenAt: string; 
  lastSeenAt: string; 
  totalSamples: number;
  enabled: boolean;
}
