const path = require('node:path');
const { DownloadManager } = require('./core/manager');

const url = process.argv[2] || 'https://ash-speed.hetzner.com/100MB.bin';
const connections = Number(process.argv[3]) || 8;

function formatMB(bytes) {
  return (bytes / (1024 * 1024)).toFixed(2);
}

(async () => {
  const manager = new DownloadManager({
    dataDir: path.join(__dirname, 'data'),
    defaultOutputDir: path.join(__dirname, 'downloads'),
  });
  await manager.init();

  // Uji speed limiter 2 MB/s dulu selama 3 detik pertama
  manager.setSpeedLimit(2 * 1024 * 1024);

  let pausedOnce = false;
  let taskId = null;

  manager.on('update', (t) => {
    if (t.id !== taskId) return;
    const pct = t.totalSize > 0 ? ((t.downloaded / t.totalSize) * 100).toFixed(1) : '?';
    const chunks = t.chunks
      .map((c) => `C${c.id}:${c.size > 0 ? Math.floor((c.downloaded / c.size) * 100) : 0}%`)
      .join(' ');

    process.stdout.write(
      `\r[${t.status.toUpperCase()}] ${pct}% | ${formatMB(t.downloaded)}/${formatMB(t.totalSize)} MB | ${formatMB(t.speedBps)} MB/s | ${chunks}   `
    );

    if (t.status === 'completed') {
      console.log(`\n\nSelesai! Tersimpan di: ${t.destPath}`);
      process.exit(0);
    }
    if (t.status === 'error') {
      console.error(`\n\nGagal: ${t.error}`);
      process.exit(1);
    }
  });

  console.log(`Mulai uji 8 koneksi + speed limit 2 MB/s + auto pause/resume...`);
  const task = manager.addDownload({ url, connections });
  taskId = task.id;

  // Pause setelah 3 detik, lalu lepas limit & resume setelah 2 detik
  setTimeout(async () => {
    if (pausedOnce) return;
    pausedOnce = true;
    console.log('\n\n--- Menguji PAUSE ---');
    await manager.pauseDownload(taskId);
    console.log('\nPaused! Cek folder data/tasks.json — melanjutkan dalam 2 detik (unlimited speed)...');

    setTimeout(() => {
      console.log('--- Menguji RESUME ---');
      manager.setSpeedLimit(0);
      manager.resumeDownload(taskId);
    }, 2000);
  }, 3000);
})();