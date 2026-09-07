import path from 'node:path';
import { defineConfig } from 'vite';

export default defineConfig({
  build: {
    lib: {
      entry: path.resolve(__dirname, 'index.ts'),
      formats: ['es'],
      fileName: () => 'designsystemet.mjs',
      cssFileName: 'designsystemet'
    },
    outDir: path.resolve(__dirname, '../wwwroot/designsystemet'),
    emptyOutDir: true
  }
});