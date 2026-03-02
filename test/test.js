'use strict';
// Standalone test for Katana V2X — auth + LED colors over COM4
// Run: node test.js [COM4]

const { SerialPort } = require('serialport');
const crypto = require('crypto');

const PORT = process.argv[2] || 'COM4';
const BAUD = 115200;

// AES key material extracted from CTCDC.dll (via nns.ee research + v2x crate)
const KEY_DATA = Buffer.from([
    0xD3, 0x1A, 0x21, 0x27, 0x9B, 0xE3, 0x46, 0xF0,
    0x99, 0x9D, 0x6E, 0xC4, 0xC3, 0xFE, 0xBE, 0x98,
    0x90, 0x18, 0x69, 0xC1, 0x18, 0xFB, 0xB1, 0x25,
    0x6E, 0x0C, 0xE0, 0x7B,
]);

// ---------------------------------------------------------------------------
// Serial helpers
// ---------------------------------------------------------------------------

function openPort(path, baudRate) {
    return new Promise((resolve, reject) => {
        const port = new SerialPort({ path, baudRate, autoOpen: false });
        port.open(err => err ? reject(err) : resolve(port));
    });
}

function write(port, data) {
    return new Promise((resolve, reject) => {
        port.write(data, err => {
            if (err) return reject(err);
            port.drain(resolve);
        });
    });
}

function sleep(ms) {
    return new Promise(r => setTimeout(r, ms));
}

// Collect all data arriving within timeoutMs into one buffer.
function readWithTimeout(port, timeoutMs) {
    return new Promise(resolve => {
        const chunks = [];
        const timer = setTimeout(() => {
            port.off('data', onData);
            resolve(Buffer.concat(chunks));
        }, timeoutMs);
        const onData = (data) => {
            chunks.push(data);
            clearTimeout(timer);
            // give a little more time for remaining bytes
            setTimeout(() => {
                port.off('data', onData);
                resolve(Buffer.concat(chunks));
            }, 100);
        };
        port.on('data', onData);
    });
}

// Read bytes until 0x0A, return raw Buffer (strips \r).
function readLine(port, timeoutMs = 5000) {
    return new Promise((resolve, reject) => {
        const chunks = [];
        const timer = setTimeout(() => {
            port.off('data', onData);
            reject(new Error(`readLine timed out after ${timeoutMs}ms — got ${chunks.length} bytes so far: ${Buffer.concat(chunks).toString('hex')}`));
        }, timeoutMs);

        const onData = (data) => {
            for (const byte of data) {
                if (byte === 0x0A) {
                    clearTimeout(timer);
                    port.off('data', onData);
                    resolve(Buffer.concat(chunks));
                    return;
                }
                if (byte !== 0x0D) chunks.push(Buffer.from([byte]));
            }
        };
        port.on('data', onData);
    });
}

// ---------------------------------------------------------------------------
// Auth
// ---------------------------------------------------------------------------

function buildKey(h0, h1, pid0, pid1) {
    return Buffer.concat([Buffer.from([h0, h1]), KEY_DATA, Buffer.from([pid0, pid1])]);
}

async function handleChallenge(port, challengeBuf) {
    if (challengeBuf.length < 45) {
        throw new Error(`Challenge too short: ${challengeBuf.length} bytes (hex: ${challengeBuf.toString('hex')})`);
    }
    const h0   = challengeBuf[9];
    const h1   = challengeBuf[10];
    const pid0 = challengeBuf[11];
    const pid1 = challengeBuf[12];
    const nonce = challengeBuf.slice(13, 45);

    console.log(`  ← challenge: h=${h0.toString(16).padStart(2,'0')}${h1.toString(16).padStart(2,'0')} pid=${pid0.toString(16).padStart(2,'0')}${pid1.toString(16).padStart(2,'0')} nonce=${nonce.toString('hex').slice(0,16)}...`);

    const key = buildKey(h0, h1, pid0, pid1);
    console.log(`     derived key: ${key.toString('hex')}`);

    const iv       = crypto.randomBytes(16);
    const gcmNonce = iv.subarray(0, 12);
    const cipher   = crypto.createCipheriv('aes-256-gcm', key, gcmNonce);
    const ciphertext = Buffer.concat([cipher.update(nonce), cipher.final()]);
    const tag        = cipher.getAuthTag();

    // Wire: "unlock" + iv(16) + ciphertext(32) + tag(16) + "\r\n"
    const response = Buffer.concat([
        Buffer.from('unlock'), iv, ciphertext, tag, Buffer.from('\r\n'),
    ]);
    await write(port, response);
    console.log(`  → sent unlock (${response.length} bytes)`);

    const ack = await readLine(port);
    console.log(`  ← ${ack.toString().trim()}`);
    if (!ack.toString().startsWith('unlock_OK')) {
        throw new Error(`Auth failed: ${ack.toString().trim()}`);
    }
    console.log('  Auth OK!');
}

async function authenticate(port) {
    // Step 1: Send binary ping — if device is already in command mode it responds with 0x5A...
    console.log('  Probing for binary command mode (ping 5A 03 00)...');
    await write(port, Buffer.from([0x5A, 0x03, 0x00]));
    const pingReply = await readWithTimeout(port, 500);
    if (pingReply.length > 0) {
        console.log(`  ← ping reply (${pingReply.length} bytes): ${pingReply.toString('hex')}`);
        if (pingReply[0] === 0x5A) {
            console.log('  Device already in binary command mode — skipping auth!');
            return;
        }
    } else {
        console.log('  No ping reply (device not in command mode yet)');
    }

    // Step 2: Check if device spontaneously sent a challenge or other text
    // (some devices send the challenge as soon as the serial port opens)
    console.log('  Checking for unsolicited challenge from device...');
    const spontaneous = await readWithTimeout(port, 500);
    if (spontaneous.length > 0) {
        console.log(`  ← spontaneous data (${spontaneous.length} bytes): ${spontaneous.slice(0,20).toString('hex')}...`);
        if (spontaneous.toString('latin1').startsWith('whoareyou')) {
            console.log('  Device sent challenge spontaneously!');
            await handleChallenge(port, spontaneous);
            return;
        }
    }

    // Step 3: Explicitly request the challenge
    console.log('  Sending whoareyou...');
    await write(port, Buffer.from('whoareyou\r\n'));

    const line = await readLine(port, 6000);
    const lineStr = line.toString('latin1');
    console.log(`  ← (${line.length} bytes) ${lineStr.startsWith('whoareyou') ? 'challenge received' : JSON.stringify(lineStr.slice(0,40))}`);

    if (lineStr.startsWith('Unknown command') || lineStr.startsWith('unlock_OK')) {
        console.log('  Already authenticated!');
        return;
    }
    if (lineStr.startsWith('NotYet')) {
        throw new Error('Device says NotYet — try again in a moment');
    }
    if (!lineStr.startsWith('whoareyou')) {
        throw new Error(`Unexpected response: ${JSON.stringify(lineStr.slice(0, 60))}`);
    }

    await handleChallenge(port, line);
}

// ---------------------------------------------------------------------------
// Command mode + LED commands
// ---------------------------------------------------------------------------

async function enterCommandMode(port) {
    await write(port, Buffer.from('SW_MODE1\r\n'));
    await sleep(150);
    port.flush();
    await write(port, Buffer.from([0x5A, 0x03, 0x00])); // confirm ping
    await sleep(100);
    port.flush();
    console.log('  Entered command mode');
}

function makeLedPacket(colors /* [{r,g,b}] × 7 */) {
    // 5A 3A 20 2B 00 01 01 + 7×[FF B G R] = 35 bytes total
    const pkt = Buffer.alloc(35, 0xFF);
    pkt[0] = 0x5A;
    pkt[1] = 0x3A;
    pkt[2] = 0x20; // length = 32 (4 sub-header + 28 color bytes)
    pkt[3] = 0x2B;
    pkt[4] = 0x00;
    pkt[5] = 0x01;
    pkt[6] = 0x01;
    for (let i = 0; i < 7; i++) {
        const c = colors[i] || { r: 0, g: 0, b: 0 };
        const base = 7 + i * 4;
        pkt[base + 0] = 0xFF;
        pkt[base + 1] = c.b;
        pkt[base + 2] = c.g;
        pkt[base + 3] = c.r;
    }
    return pkt;
}

async function ledSetup(port) {
    await write(port, Buffer.from([0x5A, 0x3A, 0x02, 0x25, 0x01]));       // lighting on
    await write(port, Buffer.from([0x5A, 0x3A, 0x03, 0x37, 0x00, 0x07])); // color count = 7
    await write(port, Buffer.from([0x5A, 0x3A, 0x03, 0x29, 0x00, 0x03])); // mode = static
}

async function sendColor(port, label, colors) {
    process.stdout.write(`  → ${label} ... `);
    await write(port, makeLedPacket(colors));
    process.stdout.write('sent\n');
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

async function main() {
    console.log(`\nOpening ${PORT} at ${BAUD} baud...`);
    const port = await openPort(PORT, BAUD);
    console.log('Port open.\n');

    console.log('[1] Auth...');
    await authenticate(port);

    console.log('\n[2] Entering command mode...');
    await enterCommandMode(port);

    console.log('\n[3] LED setup...');
    await ledSetup(port);
    await sleep(50);

    console.log('\n[4] Solid color tests (1.5s each):');
    const R = { r:255, g:0,   b:0   };
    const G = { r:0,   g:255, b:0   };
    const B = { r:0,   g:0,   b:255 };
    const W = { r:255, g:255, b:255 };
    const O = { r:0,   g:0,   b:0   };

    await sendColor(port, 'ALL RED',   Array(7).fill(R)); await sleep(1500);
    await sendColor(port, 'ALL GREEN', Array(7).fill(G)); await sleep(1500);
    await sendColor(port, 'ALL BLUE',  Array(7).fill(B)); await sleep(1500);

    const rainbow = [
        {r:255,g:0,  b:0  },
        {r:255,g:127,b:0  },
        {r:255,g:255,b:0  },
        {r:0,  g:255,b:0  },
        {r:0,  g:0,  b:255},
        {r:75, g:0,  b:130},
        {r:148,g:0,  b:211},
    ];
    await sendColor(port, 'RAINBOW',   rainbow); await sleep(1500);

    console.log('\n[5] Zone isolation (300ms each — confirms LED count + order):');
    for (let z = 0; z < 7; z++) {
        const single = Array(7).fill(O);
        single[z] = W;
        await sendColor(port, `zone ${z+1}/7 white`, single);
        await sleep(300);
    }

    await sendColor(port, 'OFF', Array(7).fill(O));
    await sleep(200);

    console.log('\nAll tests complete. Closing port.');
    await new Promise(r => port.close(r));
}

main().catch(err => {
    console.error('\nERROR:', err.message);
    process.exit(1);
});
