const http = require('node:http');
const https = require('node:https');
const path = require('node:path');

const MAX_REDIRECTS = 5;
const PROBE_TIMEOUT_MS = 15000;
const USER_AGENT = 'ByteSplit/1.0';

function validateHttpUrl(rawUrl) {
  let parsed;
  try {
    parsed = new URL(rawUrl);
  } catch {
    throw new Error(`URL tidak valid: ${rawUrl}`);
  }
  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
    throw new Error(`Protokol tidak didukung (${parsed.protocol}). Hanya http dan https.`);
  }
  return parsed.toString();
}

function sanitizeFileName(rawName) {
  const base = path.basename(String(rawName || ''));
  // Ganti karakter terlarang di Windows dan buang titik/spasi di akhir nama file
  const cleaned = base
    .replace(/[<>:"/\\|?*\x00-\x1F]/g, '_')
    .replace(/[. ]+$/, '')
    .trim();

  return cleaned || 'download.bin';
}

function extractFileName(headers, finalUrl) {
  const disposition = headers['content-disposition'];
  if (disposition) {
    const utf8Match = disposition.match(/filename\*\s*=\s*UTF-8''([^;]+)/i);
    if (utf8Match) {
      try {
        return sanitizeFileName(decodeURIComponent(utf8Match[1].trim()));
      } catch {
        return sanitizeFileName(utf8Match[1].trim());
      }
    }
    const asciiMatch = disposition.match(/filename\s*=\s*"?([^";]+)"?/i);
    if (asciiMatch) {
      return sanitizeFileName(asciiMatch[1].trim());
    }
  }

  try {
    const urlObj = new URL(finalUrl);
    const segment = urlObj.pathname.split('/').filter(Boolean).pop();
    if (segment) {
      try {
        return sanitizeFileName(decodeURIComponent(segment));
      } catch {
        return sanitizeFileName(segment);
      }
    }
  } catch {
    // Abaikan jika gagal parse ulang; fallback di bawah
  }

  return 'download.bin';
}

function requestHeaders(targetUrl, method, extraHeaders = {}, redirectsLeft = MAX_REDIRECTS) {
  return new Promise((resolve, reject) => {
    const validUrl = validateHttpUrl(targetUrl);
    const urlObj = new URL(validUrl);
    const transport = urlObj.protocol === 'https:' ? https : http;
    let settled = false;

    const finish = (fn, value) => {
      if (settled) return;
      settled = true;
      fn(value);
    };

    const req = transport.request(
      urlObj,
      {
        method,
        headers: { 'User-Agent': USER_AGENT, ...extraHeaders },
        timeout: PROBE_TIMEOUT_MS,
      },
      (res) => {
        const status = res.statusCode || 0;

        if ([301, 302, 303, 307, 308].includes(status) && res.headers.location) {
          res.destroy();
          if (redirectsLeft <= 0) {
            return finish(reject, new Error('Terlalu banyak redirect (> 5 kali).'));
          }
          const nextUrl = new URL(res.headers.location, validUrl).toString();
          return requestHeaders(nextUrl, method, extraHeaders, redirectsLeft - 1)
            .then((result) => finish(resolve, result))
            .catch((err) => finish(reject, err));
        }

        // Hentikan body segera agar GET probe tidak mengunduh seluruh isi file
        res.destroy();
        finish(resolve, {
          finalUrl: validUrl,
          statusCode: status,
          headers: res.headers,
        });
      }
    );

    req.on('timeout', () => {
      req.destroy(new Error(`Timeout saat menghubungi server (${PROBE_TIMEOUT_MS} ms).`));
    });

    req.on('error', (err) => {
      finish(reject, err);
    });

    req.end();
  });
}

function parseTotalSizeFromContentRange(contentRange) {
  if (!contentRange) return 0;
  const match = String(contentRange).match(/\/(\d+)$/);
  if (!match) return 0;
  const size = Number(match[1]);
  return Number.isSafeInteger(size) && size > 0 ? size : 0;
}

async function probe(rawUrl) {
  const validUrl = validateHttpUrl(rawUrl);

  // Coba HEAD terlebih dahulu
  try {
    const head = await requestHeaders(validUrl, 'HEAD');
    if (head.statusCode >= 200 && head.statusCode < 300) {
      const len = Number(head.headers['content-length']);
      const totalSize = Number.isSafeInteger(len) && len > 0 ? len : 0;
      const acceptRanges = String(head.headers['accept-ranges'] || '').toLowerCase();

      if (totalSize > 0 && acceptRanges.includes('bytes')) {
        return {
          finalUrl: head.finalUrl,
          fileName: extractFileName(head.headers, head.finalUrl),
          totalSize,
          supportsRange: true,
        };
      }
    }
  } catch {
    // Lanjut ke fallback GET Range jika HEAD ditolak atau gagal
  }

  // Fallback: uji langsung dengan GET Range bytes=0-0
  const getProbe = await requestHeaders(validUrl, 'GET', { Range: 'bytes=0-0' });

  if (getProbe.statusCode === 206) {
    const totalSize = parseTotalSizeFromContentRange(getProbe.headers['content-range']);
    return {
      finalUrl: getProbe.finalUrl,
      fileName: extractFileName(getProbe.headers, getProbe.finalUrl),
      totalSize,
      supportsRange: totalSize > 0,
    };
  }

  if (getProbe.statusCode >= 200 && getProbe.statusCode < 300) {
    const len = Number(getProbe.headers['content-length']);
    const totalSize = Number.isSafeInteger(len) && len > 0 ? len : 0;
    return {
      finalUrl: getProbe.finalUrl,
      fileName: extractFileName(getProbe.headers, getProbe.finalUrl),
      totalSize,
      supportsRange: false,
    };
  }

  throw new Error(`Server menolak permintaan (HTTP ${getProbe.statusCode}).`);
}

module.exports = { probe, sanitizeFileName, validateHttpUrl };