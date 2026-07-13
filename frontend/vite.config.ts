import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import path from "path";

const host = process.env.TAURI_DEV_HOST;

export default defineConfig(async () => ({
  plugins: [react()],

  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },

  
  
  
  clearScreen: false,
  
  server: {
    port: 1420,
    strictPort: true,
    host: host || false,
    hmr: host
      ? {
          protocol: "ws",
          host,
          port: 1421,
        }
      : undefined,
    proxy: {
      // websocket dedicado (mas especifico, va primero)
      "/api/events": {
        target: "ws://127.0.0.1:5174",
        ws: true,
        rewriteWsOrigin: true,
      },
      // resto del API REST
      "/api": {
        target: "http://127.0.0.1:5174",
        changeOrigin: true,
      },
    },
    watch: {

      ignored: ["**/src-tauri/**"],
    },
  },
}));
