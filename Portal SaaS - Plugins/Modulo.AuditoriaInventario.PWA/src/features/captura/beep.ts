/**
 * Beep corto de "código no encontrado en el maestro" -- generado con Web Audio
 * (sin archivo de sonido, no infla el bundle ni depende de red). Si el
 * navegador no tiene AudioContext (o el entorno de test no lo simula), no
 * hace nada -- nunca bloquea la captura por esto.
 */
export function reproducirBeepError(): void {
  try {
    const AudioContextCtor: typeof AudioContext | undefined =
      window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
    if (!AudioContextCtor) return;

    const ctx = new AudioContextCtor();
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    osc.type = 'square';
    osc.frequency.value = 220;
    gain.gain.value = 0.15;
    osc.connect(gain);
    gain.connect(ctx.destination);
    osc.start();
    osc.stop(ctx.currentTime + 0.18);
    osc.onended = () => ctx.close();
  } catch {
    // Sin audio disponible -- silencioso a propósito.
  }
}
