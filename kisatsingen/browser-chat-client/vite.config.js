import path from 'node:path';
import { defineConfig } from 'vite';

export default defineConfig({
    build: {
        lib: {
            entry: path.resolve(__dirname, 'src/chat-streaming.ts'),
            formats: ['es'],
            fileName: () => 'chat-client.mjs',
            cssFileName: 'chat-client',
        },
        outDir: path.resolve(__dirname, '../wwwroot/chat-client'),
        emptyOutDir: true,
    },
});
