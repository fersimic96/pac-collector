

export interface CurvePointDTO {
  pctRecovered: number;
  temperatureC: number;
}

export interface SampleOutputDTO {
  uuid: string;
  serial: string;
  analyzerType: string;
  sampleIdentifier: string;
  operator: string | null;
  program: string | null;
  startAt: string | null; 
  endAt: string | null;
  ibp: number | null;
  fbp: number | null;
  residue: number | null;
  recovery: number | null;
  fbpVolume: number | null;
  endOfTest: boolean | null;
  alarmBitmask: number | null;
  curve: CurvePointDTO[];
  extra: Record<string, string>;
  sourceIp: string | null;
  receivedAt: string; 
}
