# ByteSplit

Aplikasi download manager HTTP/HTTPS native untuk Windows yang dibangun menggunakan C# (.NET 8 Windows Forms) tanpa WebView maupun Electron. ByteSplit memecah berkas menjadi beberapa segmen paralel agar proses unduhan lebih cepat sekaligus tetap ringan dipakai di laptop berspesifikasi rendah.

Versi saat ini: `v2.0.0-beta.1` (Native Windows)

## Perubahan Utama di v2.0.0-beta.1

- Migrasi penuh dari Electron ke aplikasi desktop native C# (.NET 8 Windows Forms) tanpa browser tertanam.
- Penggunaan RAM saat diam turun menjadi sekitar 20-30 MB dengan waktu buka instan.
- Tampilan baru bergaya Fluent Windows dengan dukungan tema Terang dan Gelap serta penyesuaian skala DPI layar.
- Pemantau clipboard berbasis event Windows (`AddClipboardFormatListener`) dengan penggunaan CPU 0% saat diam.

## Fitur

- **Unduhan Multi-Koneksi (1-16 Segmen):** Memecah file menggunakan HTTP Range request dan menulis data langsung ke offset berkas sementara (`.bytesplit.part`).
- **Jeda dan Lanjutkan (Pause/Resume):** Status dan progres tiap segmen disimpan otomatis di `data/tasks.json`, sehingga unduhan bisa dilanjutkan kapan saja setelah aplikasi ditutup.
- **Perbarui Link Kedaluwarsa:** Mengganti tautan unduhan yang sudah kedaluwarsa dengan tautan baru tanpa mengulang dari awal selama ukuran berkas sama.
- **Pengelompokan Folder Otomatis:** Mengarahkan hasil unduhan ke sub-folder kategori (`Compressed`, `Documents`, `Music`, `Programs`, `Video`, atau `Other`).
- **Pembatas Kecepatan (Speed Limiter):** Mengatur batas kecepatan unduh global (`Tanpa Batas`, `512 KB/s`, `1 MB/s`, `2 MB/s`, `5 MB/s`, atau `10 MB/s`).
- **System Tray:** Tetap berjalan di latar belakang saat jendela ditutup ketika masih ada unduhan aktif.

## Cara Menggunakan (Rilis Jadi)

Unduh salah satu berkas rilis pada halaman [Releases](https://github.com/mhdsyahrinharahap-web/bytesplit/releases):

1. `ByteSplit-v2.0.0-beta.1-win-x64-portable.exe`: Versi mandiri (*self-contained*), langsung bisa dijalankan di Windows 10/11 64-bit tanpa menginstal .NET Runtime tambahan.
2. `ByteSplit-v2.0.0-beta.1-win-x64-lite.exe`: Versi ringan (*framework-dependent*), membutuhkan [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) terpasang di Windows.

## Menjalankan dari Source Code

### Prasyarat

- Windows 10 atau Windows 11 (64-bit)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Langkah Kompilasi

```powershell
git clone [https://github.com/mhdsyahrinharahap-web/bytesplit.git](https://github.com/mhdsyahrinharahap-web/bytesplit.git)
cd bytesplit
dotnet build -c Release
dotnet run -c Release