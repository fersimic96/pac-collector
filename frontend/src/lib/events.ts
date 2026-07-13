

export type AppEvent =
  | { type: "beacon_received"; data: { ip: string; ts: string } }
  | { type: "instrument_discovered"; data: { serial: string; analyzer_type: string; ip: string | null } }
  | { type: "instrument_touched"; data: { serial: string; ip: string | null } }
  | {
      type: "sample_received";
      data: {
        uuid: string;
        serial: string;
        sample_identifier: string;
        ibp: number | null;
        fbp: number | null;
      };
    }
  | { type: "sample_duplicate_skipped"; data: { serial: string; sample_identifier: string } }
  | { type: "plugin_parse_failed"; data: { analyzer_type: string; reason: string } }
  | {
      type: "unknown_payload_received";
      data: {
        analyzer_type: string | null;
        source_ip: string | null;
        bytes: number;
        reason: string;
        saved_path: string;
      };
    }
  | {
      type: "persistence_failed";
      data: {
        stage: string;
        serial: string | null;
        sample_identifier: string | null;
        reason: string;
      };
    }
  | { type: "server_error"; data: { message: string } };
