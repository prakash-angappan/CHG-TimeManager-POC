import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  base: "/staff/",
  plugins: [react()],
  build: {
    outDir: "../src/GamingStation.Api/wwwroot/staff",
    emptyOutDir: true,
  },
  server: {
    proxy: {
      "/api": "http://127.0.0.1:5080",
      "/hubs": {
        target: "http://127.0.0.1:5080",
        ws: true,
      },
    },
  },
  test: {
    environment: "node",
    globals: true,
  },
});
