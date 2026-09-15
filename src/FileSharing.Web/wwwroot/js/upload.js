// Browser-side upload pipeline for the Web Upload page (Components/Pages/Upload.razor).
//
// Deliberately does everything here in plain JS, never routing file bytes through the Blazor
// Server circuit: a folder is read and zipped (store method — no compression, so this stays a
// simple, dependency-free implementation) entirely in the browser, and the resulting Blob (or a
// single selected File) is PUT straight to a presigned S3 URL via XMLHttpRequest (chosen over
// fetch() specifically because fetch has no upload-progress event). .NET only ever sees file
// metadata (name/size/contentType) and progress fractions — never the bytes themselves.
window.fileSharingUpload = (() => {
    const pending = new Map(); // id -> { blob: Blob|File, xhr?: XMLHttpRequest }

    function newId() {
        return crypto.randomUUID();
    }

    // --- ZIP (store method, ZIP 2.0, UTF-8 filenames) ---
    // Deterministic 32-bit CRC — the standard reflected polynomial table, computed once.
    const crcTable = (() => {
        const table = new Uint32Array(256);
        for (let n = 0; n < 256; n++) {
            let c = n;
            for (let k = 0; k < 8; k++) {
                c = (c & 1) ? (0xedb88320 ^ (c >>> 1)) : (c >>> 1);
            }
            table[n] = c >>> 0;
        }
        return table;
    })();

    function crc32(bytes) {
        let crc = 0xffffffff;
        for (let i = 0; i < bytes.length; i++) {
            crc = crcTable[(crc ^ bytes[i]) & 0xff] ^ (crc >>> 8);
        }
        return (crc ^ 0xffffffff) >>> 0;
    }

    function dosDateTime(jsDate) {
        const time = ((jsDate.getHours() & 0x1f) << 11) | ((jsDate.getMinutes() & 0x3f) << 5) | ((jsDate.getSeconds() >> 1) & 0x1f);
        const date = (((jsDate.getFullYear() - 1980) & 0x7f) << 9) | (((jsDate.getMonth() + 1) & 0xf) << 5) | (jsDate.getDate() & 0x1f);
        return { time, date };
    }

    function utf8Bytes(text) {
        return new TextEncoder().encode(text);
    }

    function writeUint32LE(view, offset, value) {
        view.setUint32(offset, value >>> 0, true);
    }

    function writeUint16LE(view, offset, value) {
        view.setUint16(offset, value & 0xffff, true);
    }

    // Builds a ZIP Blob from [{ relativePath, bytes: Uint8Array, lastModified: Date }, ...],
    // preserving each entry's relative path exactly as given (so the extracted folder keeps its
    // original directory structure).
    function buildZip(entries, onProgress) {
        const localParts = [];
        const centralParts = [];
        let offset = 0;
        const UTF8_FLAG = 0x0800;

        entries.forEach((entry, index) => {
            const nameBytes = utf8Bytes(entry.relativePath);
            const crc = crc32(entry.bytes);
            const { time, date } = dosDateTime(entry.lastModified);
            const size = entry.bytes.length;

            const localHeader = new ArrayBuffer(30);
            const lv = new DataView(localHeader);
            writeUint32LE(lv, 0, 0x04034b50);
            writeUint16LE(lv, 4, 20);
            writeUint16LE(lv, 6, UTF8_FLAG);
            writeUint16LE(lv, 8, 0); // store, no compression
            writeUint16LE(lv, 10, time);
            writeUint16LE(lv, 12, date);
            writeUint32LE(lv, 14, crc);
            writeUint32LE(lv, 18, size);
            writeUint32LE(lv, 22, size);
            writeUint16LE(lv, 26, nameBytes.length);
            writeUint16LE(lv, 28, 0);

            localParts.push(new Uint8Array(localHeader), nameBytes, entry.bytes);

            const centralHeader = new ArrayBuffer(46);
            const cv = new DataView(centralHeader);
            writeUint32LE(cv, 0, 0x02014b50);
            writeUint16LE(cv, 4, 20);
            writeUint16LE(cv, 6, 20);
            writeUint16LE(cv, 8, UTF8_FLAG);
            writeUint16LE(cv, 10, 0);
            writeUint16LE(cv, 12, time);
            writeUint16LE(cv, 14, date);
            writeUint32LE(cv, 16, crc);
            writeUint32LE(cv, 20, size);
            writeUint32LE(cv, 24, size);
            writeUint16LE(cv, 28, nameBytes.length);
            writeUint16LE(cv, 30, 0);
            writeUint16LE(cv, 32, 0);
            writeUint16LE(cv, 34, 0);
            writeUint16LE(cv, 36, 0);
            writeUint32LE(cv, 38, 0);
            writeUint32LE(cv, 42, offset);

            centralParts.push(new Uint8Array(centralHeader), nameBytes);

            offset += 30 + nameBytes.length + size;

            if (onProgress) onProgress((index + 1) / entries.length);
        });

        const centralSize = centralParts.reduce((sum, part) => sum + part.length, 0);
        const centralOffset = offset;

        const end = new ArrayBuffer(22);
        const ev = new DataView(end);
        writeUint32LE(ev, 0, 0x06054b50);
        writeUint16LE(ev, 4, 0);
        writeUint16LE(ev, 6, 0);
        writeUint16LE(ev, 8, entries.length);
        writeUint16LE(ev, 10, entries.length);
        writeUint32LE(ev, 12, centralSize);
        writeUint32LE(ev, 16, centralOffset);
        writeUint16LE(ev, 20, 0);

        return new Blob([...localParts, ...centralParts, new Uint8Array(end)], { type: "application/zip" });
    }

    // --- Public API, called from Upload.razor via IJSRuntime ---

    // Opens the native file/folder picker for a hidden <input type="file">, given its
    // ElementReference — avoids relying on a DOM id or `eval`.
    function triggerClick(inputElement) {
        if (inputElement) inputElement.click();
    }

    // Single file — no zipping, the File itself is the upload payload.
    function prepareFile(inputElement) {
        const file = inputElement && inputElement.files && inputElement.files[0];
        if (!file) return null;

        const id = newId();
        pending.set(id, { blob: file });
        return { id, name: file.name, size: file.size, contentType: file.type || "application/octet-stream" };
    }

    // Folder (an <input webkitdirectory multiple> selection) — zipped entirely client-side.
    // dotNetRef is optional; when given, OnZipProgress(fraction) is invoked as entries are added.
    async function prepareFolderZip(inputElement, dotNetRef, folderZipName) {
        const files = inputElement && inputElement.files ? Array.from(inputElement.files) : [];
        if (files.length === 0) return null;

        const entries = [];
        for (const file of files) {
            const bytes = new Uint8Array(await file.arrayBuffer());
            // webkitRelativePath already includes the picked folder's own name as its first
            // segment (e.g. "my-folder/sub/doc.pdf") — kept as-is so the extracted ZIP reproduces
            // exactly the structure the user selected.
            entries.push({
                relativePath: file.webkitRelativePath || file.name,
                bytes,
                lastModified: new Date(file.lastModified || Date.now()),
            });
        }

        const zipBlob = buildZip(entries, (fraction) => {
            if (dotNetRef) dotNetRef.invokeMethodAsync("OnZipProgress", fraction);
        });

        const id = newId();
        pending.set(id, { blob: zipBlob });
        return { id, name: folderZipName, size: zipBlob.size, contentType: "application/zip" };
    }

    // PUTs the previously-prepared blob straight to a presigned URL, reporting real upload
    // progress via dotNetRef.OnUploadProgress(fraction). Returns { ok, status }.
    function uploadToPresignedUrl(id, presignedUrl, contentType, dotNetRef) {
        const entry = pending.get(id);
        if (!entry) return Promise.resolve({ ok: false, status: 0 });

        return new Promise((resolve) => {
            const xhr = new XMLHttpRequest();
            entry.xhr = xhr;

            xhr.open("PUT", presignedUrl, true);
            xhr.setRequestHeader("Content-Type", contentType);

            xhr.upload.onprogress = (event) => {
                if (event.lengthComputable && dotNetRef) {
                    dotNetRef.invokeMethodAsync("OnUploadProgress", event.loaded / event.total);
                }
            };

            xhr.onload = () => resolve({ ok: xhr.status >= 200 && xhr.status < 300, status: xhr.status });
            xhr.onerror = () => resolve({ ok: false, status: 0 });
            xhr.onabort = () => resolve({ ok: false, status: 0 });

            xhr.send(entry.blob);
        });
    }

    function cancel(id) {
        const entry = pending.get(id);
        if (entry && entry.xhr) entry.xhr.abort();
    }

    function cleanup(id) {
        pending.delete(id);
    }

    return { triggerClick, prepareFile, prepareFolderZip, uploadToPresignedUrl, cancel, cleanup };
})();
