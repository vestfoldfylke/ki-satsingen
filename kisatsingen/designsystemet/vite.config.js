import path from 'node:path';
import { defineConfig } from 'vite';

export default defineConfig({
  build: {
    lib: {
      entry: path.resolve(__dirname, 'index.js'),
      formats: ['es'],
      fileName: 'designsystemet'
    },
    outDir: path.resolve(__dirname, '../wwwroot/designsystemet'),
    emptyOutDir: true
  }
});