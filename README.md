# ByteSplit

Multi-connection HTTP/HTTPS download manager untuk Windows yang memecah file menjadi beberapa segmen paralel dengan *direct offset writing* serta mengelompokkan hasil unduhan ke folder kategori secara otomatis.

## Fitur Utama

- **Multi-Connection Segmented Download (1–16 Koneksi):** Membagi file berukuran besar ke beberapa segmen paralel menggunakan HTTP `Range` request untuk memaksimalkan kecepatan unduh.
- **Direct Offset Writing & Safe Resume:** Setiap segmen ditulis langsung ke offset byte masing-masing pada berkas sementara (`.bytesplit.part`), sehingga unduhan yang dijeda atau terputus dapat dilanjutkan tanpa mengulang dari awal.
- **Auto-Categorize Folder:** Mengelompokkan hasil unduhan secara otomatis ke subfolder `Compressed`, `Documents`, `Music`, `Programs`, dan `Video`.
- **Pantau Clipboard Otomatis:** Mendeteksi tautan unduhan langsung yang disalin dari browser dan membuka dialog tambah unduhan secara otomatis.
- **Refresh Expired Link:** Memperbarui URL unduhan yang kedaluwarsa tanpa kehilangan progres segmen yang sudah terunduh.
- **System Tray & Notifikasi:** Tetap berjalan di latar belakang (*System Tray*) saat jendela ditutup ketika unduhan masih aktif, disertai notifikasi saat selesai.
- **Pengatur Batas Kecepatan & Tema:** Mendukung pembatasan bandwidth global serta pilihan tampilan tema Terang (*Light*) dan Gelap (*Dark*).

## Unduh Aplikasi (Windows)

Buka halaman [**Releases**](https://github.com/mhdsyahrinharahap-web/bytesplit/releases) untuk mengunduh versi terbaru:
- **Installer (`.exe`):** Instalasi standar Windows dengan pintasan Start Menu dan Desktop.
- **Portable (`.exe`):** Langsung dijalankan tanpa perlu proses instalasi.

## Menjalankan dari Source Code

Pastikan **Node.js** (v18 atau lebih baru) dan **Git** sudah terpasang di perangkat Anda:

```bash
git clone [https://github.com/mhdsyahrinharahap-web/bytesplit.git](https://github.com/mhdsyahrinharahap-web/bytesplit.git)
cd bytesplit
npm install
npm start
