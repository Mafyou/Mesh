// @ts-check
// ES module — imported by Flasher.razor via IJSRuntime isolation

import { ESPLoader, Transport } from 'https://unpkg.com/esptool-js@0.4.1/bundle.js';

/** @type {ESPLoader|null} */
let _loader = null;
/** @type {Transport|null} */
let _transport = null;
/** @type {SerialPort|null} */
let _port = null;

export function isSerialSupported() {
    return 'serial' in navigator;
}

/**
 * Wires a plain <input type="file"> to the Blazor component.
 * Avoids using Blazor's InputFile (EventCallback<T> causes SSR serialization issues).
 * @param {HTMLInputElement} element
 * @param {import('@microsoft/dotnet-runtime').DotNetObject} dotnetRef
 */
export function setupFileInput(element, dotnetRef) {
    element.addEventListener('change', async () => {
        const file = element.files?.[0];
        if (!file) return;
        const buffer = await file.arrayBuffer();
        const bytes = new Uint8Array(buffer);
        let binary = '';
        for (let i = 0; i < bytes.byteLength; i++) binary += String.fromCharCode(bytes[i]);
        dotnetRef.invokeMethodAsync('OnFileSelected', file.name, btoa(binary));
    });
}

/**
 * Requests a serial port from the user, connects, and enters the ESP32
 * ROM bootloader. Returns the chip description string.
 * @param {number} baudRate
 * @param {import('@microsoft/dotnet-runtime').DotNetObject} dotnetRef
 */
export async function connect(baudRate, dotnetRef) {
    if (_port) {
        await _silentDisconnect();
    }

    _port = await navigator.serial.requestPort();

    _transport = new Transport(_port, true);

    const terminal = {
        clean() {},
        writeLine(data) { dotnetRef.invokeMethodAsync('OnLog', String(data)); },
        write(data)     { dotnetRef.invokeMethodAsync('OnLog', String(data)); },
    };

    _loader = new ESPLoader({ transport: _transport, baudrate: baudRate, terminal });

    const chipFamilyStr = await _loader.main();
    return chipFamilyStr ?? 'ESP32';
}

/**
 * Flashes firmware to the connected chip.
 * @param {string} firmwareBase64   base-64 encoded .bin contents
 * @param {number} flashOffset      flash address (0x10000 for app, 0x0 for bootloader)
 * @param {import('@microsoft/dotnet-runtime').DotNetObject} dotnetRef
 */
export async function flash(firmwareBase64, flashOffset, dotnetRef) {
    if (!_loader) throw new Error('Not connected to a chip.');

    // base64 → binary string (Latin1)
    const binaryStr = atob(firmwareBase64);

    await _loader.writeFlash({
        fileArray: [{ data: binaryStr, address: flashOffset }],
        flashSize: 'keep',
        eraseAll: false,
        compress: true,
        reportProgress(fileIndex, written, total) {
            const pct = total > 0 ? Math.round((written / total) * 100) : 0;
            dotnetRef.invokeMethodAsync('OnProgress', pct);
        },
    });
}

/**
 * Fetches a firmware binary from a URL and returns its base-64 encoded contents.
 * @param {string} url
 * @returns {Promise<string>} base-64 encoded binary
 */
export async function loadFirmwareFromUrl(url) {
    const response = await fetch(url);
    if (!response.ok) throw new Error(`HTTP ${response.status} — ${response.statusText}`);
    const buffer = await response.arrayBuffer();
    const bytes = new Uint8Array(buffer);
    let binary = '';
    for (let i = 0; i < bytes.byteLength; i++) binary += String.fromCharCode(bytes[i]);
    return btoa(binary);
}

/** Resets the chip and releases the serial port. */
export async function disconnect() {
    try { if (_loader) await _loader.hardReset(); } catch { /* ignore */ }
    await _silentDisconnect();
}

async function _silentDisconnect() {
    try { if (_transport) await _transport.disconnect(); } catch { /* ignore */ }
    _loader    = null;
    _transport = null;
    _port      = null;
}
