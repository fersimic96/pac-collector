
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import path from "path";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: { "@": path.resolve(__dirname, "./src") },
  },
  test: {
    environment: "jsdom",
    globals: true,
    include: ["src/**/*.test.ts", "src/**/*.test.tsx"],
    exclude: [
      "**/node_modules/**",
      "**/src-tauri/**",
      "**/e2e/**",
      "**/._*", 
    ],
    coverage: {
      provider: "v8",
      include: ["src/domain/**", "src/application/**"],
    },
  },
} as any);
