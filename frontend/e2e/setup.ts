

import { Page } from "@playwright/test";

export interface MockState {
  instruments: any[];
  samples: any[];
  serverStatus: any;
  plugins: any[];
  config: any;
}

export const DEFAULT_MOCK_STATE: MockState = {
  instruments: [],
  samples: [],
  serverStatus: {
    serverIp: "127.0.0.1",
    tcpPort: 9980,
    udpPort: 3000,
    instrumentsCount: 0,
    samplesToday: 0,
    running: true,
    printRunning: false,
    printPort: 631,
  },
  plugins: [
    {
      id: "optipmd-builtin",
      displayName: "PAC OptiPMD",
      version: "0.1.0",
      vendor: "PAC Collector",
      supportedTypes: ["OptiPMD"],
      source: { kind: "builtin" },
      enabled: true,
    },
  ],
  config: {
    version: 1,
    general: {
      delimiter: ";",
      eol: "<none>",
      showKey: true,
      showUnit: false,
      showAnalyzerSn: true,
      showSampleId: true,
      showStartTime: true,
      dbDir: null,
      recentDir: null,
      recentKeep: 50,
      selectedIp: null,
      autoStartServer: true,
      printServerEnabled: false,
      printPort: 631,
    },
    outputFormats: {
      writeJson: true,
      writeLimsTxt: true,
      writeLegibleTxt: true,
      writeCurveCsv: true,
      writeMasterCsv: true,
      writeGlobalMasterCsv: true,
      mirrorToRecent: true,
    },
    instruments: {},
    instrumentRoutes: {},
  },
};

export async function installTauriMock(page: Page, state: MockState = DEFAULT_MOCK_STATE) {
  await page.addInitScript((mockState) => {
    
    (window as any).__TAURI_INTERNALS__ = {
      invoke: async (cmd: string, args: any) => {
        switch (cmd) {
          case "ping": return "pong";
          case "list_instruments": return mockState.instruments;
          case "list_samples":
            return {
              items: mockState.samples,
              total: mockState.samples.length,
              offset: args?.offset ?? 0,
              limit: args?.limit ?? 50,
            };
          case "get_sample":
            return mockState.samples.find((s: any) => s.uuid === args?.uuid) ?? null;
          case "server_status": return mockState.serverStatus;
          case "start_listeners":
            mockState.serverStatus.running = true;
            return null;
          case "stop_listeners":
            mockState.serverStatus.running = false;
            return null;
          case "start_print_listener":
            mockState.serverStatus.printRunning = true;
            return null;
          case "stop_print_listener":
            mockState.serverStatus.printRunning = false;
            return null;
          case "list_plugins": return mockState.plugins;
          case "set_plugin_enabled":
            const target = mockState.plugins.find((p: any) => p.id === args?.id);
            if (target) target.enabled = args.enabled;
            return null;
          case "set_instrument_route":
            if (!mockState.config.instrumentRoutes) mockState.config.instrumentRoutes = {};
            mockState.config.instrumentRoutes[args.serial] = {
              hotFolderFormat: args.format ?? null,
              hotFolderDir: args.dir ?? null,
              alias: args.alias ?? null,
            };
            return null;
          case "get_config": return mockState.config;
          case "save_config": return null;
          case "list_local_ips": return ["192.168.1.10", "10.0.0.5"];
          case "list_network_interfaces":
            return [
              { name: "en0", ip: "192.168.1.10", netmask: "255.255.255.0", prefixLen: 24, isLinkLocal: false },
              { name: "en11", ip: "169.254.69.5", netmask: "255.255.0.0", prefixLen: 16, isLinkLocal: true },
            ];
          case "os_platform": return "macos";
          case "open_network_settings": return null;
          case "update_instrument_alias": return null;
          default:
            console.warn("unmocked invoke:", cmd, args);
            return null;
        }
      },
    };
    
    (window as any).__TAURI_EVENT_PLUGIN_INTERNALS__ = {
      unregisterListener: () => {},
    };
  }, state);
}
