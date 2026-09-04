// Minimal, self-contained QR code encoder + SVG renderer - no external service, no network call,
// so a wallet address never leaves the machine just to render its own QR code. Deliberately fixed
// to QR Version 4 (33x33 modules) at error-correction level M (byte-mode capacity 62 bytes), which
// comfortably covers every address this app renders (longest is 42 bytes, a 0x... ERC-20 address) -
// fixing the version avoids needing the full version-1-through-40 capacity/block tables a general
// encoder needs, which is most of the surface area an encoder like this can get subtly wrong.
// Mask pattern is fixed at 0 ((row+col)%2==0) rather than dynamically scored against all 8 patterns -
// any valid mask produces a fully correct, scannable code (masking is a scan-reliability
// optimization, not a correctness requirement), and the format-info bits below always correctly
// declare whichever mask is actually used.
(function () {
  'use strict';

  const VERSION = 4;
  const SIZE = 4 * VERSION + 17; // 33
  const TOTAL_DATA_CODEWORDS = 64; // 2 blocks x 32, V4-M
  const ECC_PER_BLOCK = 18;
  const BLOCK_COUNT = 2;
  const BLOCK_SIZE = TOTAL_DATA_CODEWORDS / BLOCK_COUNT; // 32
  const MAX_BYTES = TOTAL_DATA_CODEWORDS - 2; // 62 (12 bits mode+count overhead = 1.5 bytes, rounded down)

  // ---- GF(256) arithmetic, primitive polynomial x^8+x^4+x^3+x^2+1 (0x11D) - standard for QR ----
  const GF_EXP = new Array(256);
  const GF_LOG = new Array(256);
  (function initGF() {
    let x = 1;
    for (let i = 0; i < 255; i++) {
      GF_EXP[i] = x;
      GF_LOG[x] = i;
      x <<= 1;
      if (x >= 256) x ^= 0x11D;
    }
  })();

  function gfMul(a, b) {
    if (a === 0 || b === 0) return 0;
    return GF_EXP[(GF_LOG[a] + GF_LOG[b]) % 255];
  }

  function polyMulGF(a, b) {
    const result = new Array(a.length + b.length - 1).fill(0);
    for (let i = 0; i < a.length; i++) {
      if (a[i] === 0) continue;
      for (let j = 0; j < b.length; j++) {
        result[i + j] ^= gfMul(a[i], b[j]);
      }
    }
    return result;
  }

  // Generator polynomial for `eccCount` ECC codewords: product of (x + alpha^i) for i=0..eccCount-1,
  // coefficients in decreasing-degree order (index 0 = leading/monic term).
  function rsGeneratorPoly(eccCount) {
    let poly = [1];
    for (let i = 0; i < eccCount; i++) {
      poly = polyMulGF(poly, [1, GF_EXP[i]]);
    }
    return poly;
  }

  // Reed-Solomon encode via polynomial long division (the standard LFSR-style algorithm): append
  // eccCount zero bytes, then repeatedly cancel the leading term using the generator polynomial.
  // The remainder (last eccCount bytes) is the ECC codeword block.
  function rsEncode(dataBytes, eccCount) {
    const gen = rsGeneratorPoly(eccCount);
    const result = dataBytes.slice();
    for (let i = 0; i < eccCount; i++) result.push(0);
    for (let i = 0; i < dataBytes.length; i++) {
      const coef = result[i];
      if (coef === 0) continue;
      for (let j = 0; j < gen.length; j++) {
        result[i + j] ^= gfMul(gen[j], coef);
      }
    }
    return result.slice(dataBytes.length);
  }

  // ---- Bitstream: byte mode, mode(4) + count(8, versions 1-9) + data + terminator + padding ----
  function buildDataCodewords(bytes) {
    const bits = [];
    const push = (value, len) => { for (let i = len - 1; i >= 0; i--) bits.push((value >> i) & 1); };
    push(0b0100, 4);
    push(bytes.length, 8);
    for (const b of bytes) push(b, 8);

    const totalBits = TOTAL_DATA_CODEWORDS * 8;
    push(0, Math.max(0, Math.min(4, totalBits - bits.length)));
    while (bits.length % 8 !== 0) bits.push(0);

    const padBytes = [0xEC, 0x11];
    let padIdx = 0;
    while (bits.length < totalBits) {
      push(padBytes[padIdx % 2], 8);
      padIdx++;
    }

    const codewords = [];
    for (let i = 0; i < bits.length; i += 8) {
      let byte = 0;
      for (let j = 0; j < 8; j++) byte = (byte << 1) | bits[i + j];
      codewords.push(byte);
    }
    return codewords; // length TOTAL_DATA_CODEWORDS
  }

  // Splits into BLOCK_COUNT equal blocks, RS-encodes each, interleaves data then ECC codewords.
  function buildAllCodewords(dataCodewords) {
    const blocks = [];
    const eccBlocks = [];
    for (let b = 0; b < BLOCK_COUNT; b++) {
      const block = dataCodewords.slice(b * BLOCK_SIZE, (b + 1) * BLOCK_SIZE);
      blocks.push(block);
      eccBlocks.push(rsEncode(block, ECC_PER_BLOCK));
    }
    const out = [];
    for (let i = 0; i < BLOCK_SIZE; i++) for (let b = 0; b < BLOCK_COUNT; b++) out.push(blocks[b][i]);
    for (let i = 0; i < ECC_PER_BLOCK; i++) for (let b = 0; b < BLOCK_COUNT; b++) out.push(eccBlocks[b][i]);
    return out; // length TOTAL_DATA_CODEWORDS + BLOCK_COUNT*ECC_PER_BLOCK
  }

  // ---- Format info (which ECC level + mask pattern is in use): BCH(15,5) ----
  function computeFormatBits(eccLevelBits, maskPattern) {
    const data = (eccLevelBits << 3) | maskPattern;
    let d = data << 10;
    const G = 0b10100110111; // generator polynomial, degree 10 - standard for QR format info
    for (let i = 14; i >= 10; i--) {
      if ((d >> i) & 1) d ^= (G << (i - 10));
    }
    const formatBits = (data << 10) | d;
    return formatBits ^ 0b101010000010010; // fixed XOR mask, standard for QR format info
  }

  function formatInfoCoords(size) {
    const copy1 = [
      [0, 8], [1, 8], [2, 8], [3, 8], [4, 8], [5, 8], [7, 8], [8, 8],
      [8, 7], [8, 5], [8, 4], [8, 3], [8, 2], [8, 1], [8, 0]
    ];
    const copy2 = [
      [8, size - 1], [8, size - 2], [8, size - 3], [8, size - 4], [8, size - 5], [8, size - 6], [8, size - 7], [8, size - 8],
      [size - 7, 8], [size - 6, 8], [size - 5, 8], [size - 4, 8], [size - 3, 8], [size - 2, 8], [size - 1, 8]
    ];
    return { copy1, copy2 };
  }

  // ---- Matrix construction: finder/timing/alignment/dark-module function patterns, data placement ----
  function drawFinder(matrix, reserved, topRow, topCol, size) {
    for (let r = -1; r <= 7; r++) {
      for (let c = -1; c <= 7; c++) {
        const rr = topRow + r, cc = topCol + c;
        if (rr < 0 || rr >= size || cc < 0 || cc >= size) continue;
        reserved[rr][cc] = true;
        let dark;
        if (r === -1 || r === 7 || c === -1 || c === 7) dark = false;
        else if (r === 0 || r === 6 || c === 0 || c === 6) dark = true;
        else if (r >= 2 && r <= 4 && c >= 2 && c <= 4) dark = true;
        else dark = false;
        matrix[rr][cc] = dark;
      }
    }
  }

  function drawTiming(matrix, reserved, size) {
    for (let i = 8; i <= size - 9; i++) {
      const dark = i % 2 === 0;
      matrix[6][i] = dark; reserved[6][i] = true;
      matrix[i][6] = dark; reserved[i][6] = true;
    }
  }

  function drawAlignment(matrix, reserved, centerRow, centerCol) {
    for (let r = -2; r <= 2; r++) {
      for (let c = -2; c <= 2; c++) {
        const rr = centerRow + r, cc = centerCol + c;
        reserved[rr][cc] = true;
        matrix[rr][cc] = Math.abs(r) === 2 || Math.abs(c) === 2 || (r === 0 && c === 0);
      }
    }
  }

  function placeData(matrix, reserved, size, dataBits) {
    let bitIndex = 0;
    let col = size - 1;
    let goingUp = true;
    while (col > 0) {
      if (col === 6) col--; // timing column has no data
      for (let count = 0; count < size; count++) {
        const r = goingUp ? size - 1 - count : count;
        for (let cOff = 0; cOff < 2; cOff++) {
          const c = col - cOff;
          if (!reserved[r][c]) {
            const bit = bitIndex < dataBits.length ? dataBits[bitIndex] : 0;
            const maskBit = (r + c) % 2 === 0 ? 1 : 0; // mask pattern 0
            matrix[r][c] = (bit ^ maskBit) === 1;
            bitIndex++;
          }
        }
      }
      goingUp = !goingUp;
      col -= 2;
    }
  }

  function buildMatrix(dataBits) {
    const size = SIZE;
    const matrix = Array.from({ length: size }, () => new Array(size).fill(false));
    const reserved = Array.from({ length: size }, () => new Array(size).fill(false));

    drawFinder(matrix, reserved, 0, 0, size);
    drawFinder(matrix, reserved, 0, size - 7, size);
    drawFinder(matrix, reserved, size - 7, 0, size);
    drawTiming(matrix, reserved, size);
    drawAlignment(matrix, reserved, 4 * VERSION + 10, 4 * VERSION + 10); // (26,26) for V4

    const darkRow = 4 * VERSION + 9; // always-dark module, adjacent to the format-info strip
    matrix[darkRow][8] = true; reserved[darkRow][8] = true;

    const { copy1, copy2 } = formatInfoCoords(size);
    for (const [r, c] of copy1) reserved[r][c] = true;
    for (const [r, c] of copy2) reserved[r][c] = true;

    placeData(matrix, reserved, size, dataBits);

    const formatBits = computeFormatBits(0b00, 0); // ECC level M, mask pattern 0
    for (let i = 0; i < 15; i++) {
      const bit = (formatBits >> (14 - i)) & 1;
      matrix[copy1[i][0]][copy1[i][1]] = bit === 1;
      matrix[copy2[i][0]][copy2[i][1]] = bit === 1;
    }

    return matrix;
  }

  function generateMatrix(text) {
    const bytes = [];
    for (let i = 0; i < text.length; i++) bytes.push(text.charCodeAt(i) & 0xFF);
    if (bytes.length > MAX_BYTES) return null;

    const dataCodewords = buildDataCodewords(bytes);
    const allCodewords = buildAllCodewords(dataCodewords);

    const dataBits = [];
    for (const byte of allCodewords) for (let i = 7; i >= 0; i--) dataBits.push((byte >> i) & 1);

    return buildMatrix(dataBits);
  }

  function renderSvg(text, options) {
    const matrix = generateMatrix(text);
    if (!matrix) return null;
    const size = matrix.length;
    const quiet = 4; // spec-recommended minimum quiet zone, in modules
    const total = size + quiet * 2;
    const dark = (options && options.dark) || '#000000';
    const light = (options && options.light) || '#ffffff';

    let rects = '';
    for (let r = 0; r < size; r++) {
      for (let c = 0; c < size; c++) {
        if (matrix[r][c]) rects += `<rect x="${c + quiet}" y="${r + quiet}" width="1" height="1"/>`;
      }
    }

    return `<svg viewBox="0 0 ${total} ${total}" xmlns="http://www.w3.org/2000/svg" shape-rendering="crispEdges" role="img" aria-label="QR code">` +
      `<rect width="${total}" height="${total}" fill="${light}"/>` +
      `<g fill="${dark}">${rects}</g>` +
      `</svg>`;
  }

  window.CompressarrQR = { renderSvg };
})();
