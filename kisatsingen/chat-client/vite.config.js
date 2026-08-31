import path from 'node:path';
import { defineConfig } from 'vite';

export default defineConfig({
    build: {
        lib: {
            entry: path.resolve(__dirname, 'src/chat-streaming.js'),
            formats: ['es'],
            fileName: () => 'chat-client.mjs',
        },
        outDir: path.resolve(__dirname, '../wwwroot/chat-client'),
        emptyOutDir: true,
    },
});
