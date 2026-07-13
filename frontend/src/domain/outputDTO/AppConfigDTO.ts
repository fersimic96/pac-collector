

export type HotFolderFormat = "lims_ethernet" | "csv_all" | "csv";

export interface InstrumentSettingsDTO {
  enabled: boolean;
  alias: string | null;
  
  outputDir: string | null;
  
  recentDir: string | null;
  showKey: boolean | null;
  showUnit: boolean | null;
  selectedParameters: string[] | null;
  

  hotFolderDir: string | null;
  
  hotFolderFormat: HotFolderFormat | null;
}

export interface GeneralSettingsDTO {
  delimiter: string;
  eol: string; 
  showKey: boolean;
  showUnit: boolean;
  showAnalyzerSn: boolean;
  showSampleId: boolean;
  showStartTime: boolean;
  dbDir: string | null;
  recentDir: string | null;
  recentKeep: number;
  selectedIp: string | null;
  autoStartServer: boolean;
  
  printServerEnabled: boolean;
  
  printPort: number;
}

export interface OutputFormatsDTO {
  writeJson: boolean;
  writeLimsTxt: boolean;
  writeLegibleTxt: boolean;
  writeCurveCsv: boolean;
  writeMasterCsv: boolean;
  writeGlobalMasterCsv: boolean;
  mirrorToRecent: boolean;
}

export interface InstrumentRouteDTO {
  
  hotFolderFormat: HotFolderFormat | null;
  
  hotFolderDir: string | null;
  
  alias: string | null;
}

export interface AppConfigDTO {
  version: number;
  general: GeneralSettingsDTO;
  outputFormats: OutputFormatsDTO;
  
  instruments: Record<string, InstrumentSettingsDTO>;
  
  instrumentRoutes: Record<string, InstrumentRouteDTO>;
}
