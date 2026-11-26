class SpeedLimiter {
  constructor(bytesPerSec = 0) {
    this.bytesPerSec = 0;
    this.nextAvailableMs = Date.now();
    this._waiters = new Set();
    this.setLimit(bytesPerSec);
  }

  setLimit(bytesPerSec) {
    const num = Number(bytesPerSec);
    this.bytesPerSec = Number.isFinite(num) && num > 0 ? Math.floor(num) : 0;
    this.nextAvailableMs = Date.now();

    for (const wake of this._waiters) {
      wake();
    }
    this._waiters.clear();
  }

  async consume(bytes, signal) {
    if (this.bytesPerSec <= 0 || bytes <= 0) return;

    const now = Date.now();
    const durationMs = (bytes / this.bytesPerSec) * 1000;
    const scheduledStart = Math.max(now, this.nextAvailableMs);
    this.nextAvailableMs = scheduledStart + durationMs;
    const waitMs = scheduledStart - now;

    if (waitMs <= 0) return;

    await new Promise((resolve, reject) => {
      if (signal && signal.aborted) {
        return reject(new Error('Dibatalkan.'));
      }

      const cleanup = () => {
        clearTimeout(timer);
        this._waiters.delete(onWake);
        if (signal) signal.removeEventListener('abort', onAbort);
      };

      const onWake = () => {
        cleanup();
        resolve();
      };

      const onAbort = () => {
        cleanup();
        reject(new Error('Dibatalkan.'));
      };

      const timer = setTimeout(onWake, waitMs);
      this._waiters.add(onWake);
      if (signal) {
        signal.addEventListener('abort', onAbort, { once: true });
      }
    });
  }
}

module.exports = { SpeedLimiter };