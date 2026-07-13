

export type PluginSource =
  | { kind: "builtin" }
  | { kind: "external"; path: string };

export interface PluginOutputDTO {
  id: string;
  displayName: string;
  version: string;
  vendor: string;
  supportedTypes: string[];
  source: PluginSource;
  enabled: boolean;
}
