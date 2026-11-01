const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const srcAsar = process.argv[2];
const outAsar = process.argv[3];
const injection = fs.readFileSync(process.argv[4], 'utf8');
const PRELOAD = 'out/preload/index.js';

function readAsar(file) {
  const fd = fs.openSync(file, 'r');
  const head = Buffer.alloc(16);
  fs.readSync(fd, head, 0, 16, 0);
  const pickleSize = head.readUInt32LE(4);
  const jsonLen = head.readUInt32LE(12);
  const hdr = Buffer.alloc(jsonLen);
  fs.readSync(fd, hdr, 0, jsonLen, 16);
  const header = JSON.parse(hdr.toString('utf8').replace(/\u0000+$/, ''));
  return { fd, header, dataStart: 8 + pickleSize };
}

function flatten(node, prefix, out) {
  for (const name of Object.keys(node.files || {})) {
    const info = node.files[name];
    const p = prefix ? prefix + '/' + name : name;
    if (info.files) flatten(info, p, out);
    else out.push({ path: p, info });
  }
  return out;
}

const { fd, header, dataStart } = readAsar(srcAsar);
const entries = flatten(header, '', []);
const parts = [];
let offset = 0;
let replaced = false;
for (const e of entries) {
  if (e.info.unpacked) continue;
  const size = e.info.size;
  const buf = Buffer.alloc(Number(size));
  fs.readSync(fd, buf, 0, Number(size), dataStart + Number(e.info.offset));
  if (e.path === PRELOAD) {
    const next = Buffer.concat([buf, Buffer.from(injection, 'utf8')]);
    e.info.size = next.length;
    e.info.offset = String(offset);
    const h = crypto.createHash('sha256').update(next).digest('hex');
    e.info.integrity = { algorithm: 'SHA256', hash: h, blockSize: 4194304, blocks: [h] };
    offset += next.length;
    parts.push(next);
    replaced = true;
  } else {
    e.info.offset = String(offset);
    offset += size;
    parts.push(buf);
  }
}
fs.closeSync(fd);
if (!replaced) { console.error('preload not found'); process.exit(3); }

const json = JSON.stringify(header);
const jsonBuf = Buffer.from(json, 'utf8');
const pad = (4 - (jsonBuf.length % 4)) % 4;
const pickleSize = 8 + jsonBuf.length + pad;
const headerBuf = Buffer.alloc(16);
headerBuf.writeUInt32LE(4, 0);
headerBuf.writeUInt32LE(pickleSize, 4);
headerBuf.writeUInt32LE(4 + jsonBuf.length + pad, 8);
headerBuf.writeUInt32LE(jsonBuf.length, 12);
const out = Buffer.concat([headerBuf, jsonBuf, Buffer.alloc(pad), ...parts]);
fs.writeFileSync(outAsar, out);
console.log(JSON.stringify({ entries: entries.length, dataBytes: offset, outBytes: out.length, pickleSize }));
