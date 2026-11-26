# ByteSplit

Aplikasi *multi-connection download manager* open-source untuk Windows yang dibangun menggunakan Node.js dan Electron. ByteSplit memecah file unduhan menjadi beberapa segmen paralel menggunakan HTTP `Range` request dan menulis setiap potongan langsung ke posisi *byte offset* masing-masing pada disk.

[![Release](https://img.shields.io/github/v/release/mhdsyahrinharahap-web/bytesplit)](https://github.com/mhdsyahrinharahap-web/bytesplit/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D6.svg)](https://github.com/mhdsyahrinharahap-web/bytesplit/releases)

## Fitur Utama

- **Multi-Connection Chunking (1-16 Koneksi):** Membagi file menjadi beberapa bagian paralel apabila server mendukung `Accept-Ranges: bytes`, lengkap dengan peta progres per segmen secara *real-time*.
- **Direct Offset File Writing:** Setiap koneksi menulis data langsung ke posisi byte tujuan di file `.bytesplit.part`, sehingga tidak perlu proses penggabungan file ulang setelah unduhan selesai.
- **Pause, Resume, dan Auto-Retry:** Progres tiap segmen tersimpan otomatis di file JSON lokal. Unduhan dapat dijeda dan dilanjutkan kapan saja, serta otomatis mencoba ulang saat server merespons `HTTP 429` atau `503` (mematuhi header `Retry-After`).
- **Global Speed Limiter:** Pembatas kecepatan berbasis algoritma *token bucket* yang bisa diubah langsung saat unduhan sedang berjalan.
- **Auto-Folder Kategori:** Otomatis membuat dan mengarahkan hasil unduhan ke folder `Compressed`, `Documents`, `Music`, `Programs`, atau `Video` berdasarkan ekstensi file dari server.
- **Dua Mode Eksekusi:** Dapat dijalankan sebagai aplikasi desktop mandiri (Electron) maupun melalui server HTTP lokal (`node server.js`).

## Instalasi dan Penggunaan

### 1. Menggunakan Versi Portable / Installer (Windows)
1. Buka halaman [Releases](https://github.com/mhdsyahrinharahap-web/bytesplit/releases).
2. Unduh file **`ByteSplit 1.0.0.exe`** (versi portable, langsung jalan tanpa instalasi).
3. Jalankan aplikasi. Jika muncul peringatan *Windows Defender SmartScreen*, klik **More info** lalu pilih **Run anyway** (wajar karena rilis open-source ini belum menggunakan sertifikat *code signing* berbayar).
4. Klik **Tambah URL** (atau tekan `Ctrl + N`), tempel tautan langsung file HTTP/HTTPS, pilih jumlah koneksi, lalu klik **Mulai Unduh**.

### 2. Menjalankan dari Source Code
Pastikan **Node.js v20+** sudah terpasang di sistem:

```bash
git clone [https://github.com/mhdsyahrinharahap-web/bytesplit.git](https://github.com/mhdsyahrinharahap-web/bytesplit.git)
cd bytesplit
npm install
