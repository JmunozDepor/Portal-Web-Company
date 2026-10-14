import { useEffect, useRef, useState } from 'react';
import { BrowserMultiFormatReader } from '@zxing/browser';
import { BarcodeFormat, DecodeHintType, type Result } from '@zxing/library';
import type { IScannerControls } from '@zxing/browser';

const FORMATOS_1D = [BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.UPC_A];

// Evita que sostener el mismo código frente a la cámara dispare el mismo
// commit en cada frame decodificado (varias veces por segundo) -- un código
// DISTINTO interrumpe la espera al toque, solo se debounce la repetición.
const DEBOUNCE_MISMO_CODIGO_MS = 1500;

/**
 * "focusMode"/"advanced" no están en el tipo MediaTrackConstraints estándar
 * de TS (son una extensión de Chrome/Android, no parte del spec W3C) -- sin
 * esto la cámara trasera de un celular suele quedar en el enfoque fijo que
 * trae por default (pensado para fotos a distancia media), no en modo macro
 * continuo, y un código de barra pegado a 10-15cm queda borroso siempre.
 */
interface ConstraintsConEnfoque extends MediaTrackConstraints {
  advanced?: Array<MediaTrackConstraintSet & { focusMode?: 'continuous' }>;
}

export function CameraScanner({ onDetected, onClose }: { onDetected: (barcode: string) => void; onClose: () => void }) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const [error, setError] = useState<string | null>(null);
  const [torchDisponible, setTorchDisponible] = useState(false);
  const [torchOn, setTorchOn] = useState(false);
  const controlsRef = useRef<IScannerControls | null>(null);
  // Refs (no state) para que el loop de decodificación, que arranca una sola
  // vez al montar, siempre vea el callback/valor mas reciente sin tener que
  // reiniciar la cámara en cada render del padre.
  const onDetectedRef = useRef(onDetected);
  onDetectedRef.current = onDetected;
  const ultimoRef = useRef<{ texto: string; en: number } | null>(null);

  useEffect(() => {
    const hints = new Map();
    hints.set(DecodeHintType.POSSIBLE_FORMATS, FORMATOS_1D);
    // TRY_HARDER: el lector pone más esfuerzo por frame (acepta algo de
    // blur/ángulo) -- hay margen de sobra de CPU porque esto corre a lo
    // sumo un par de frames por segundo, no video completo.
    hints.set(DecodeHintType.TRY_HARDER, true);
    const codeReader = new BrowserMultiFormatReader(hints);
    let cancelado = false;

    function manejarResultado(result?: Result) {
      if (!result) return;
      const texto = result.getText();
      const ahora = Date.now();
      const u = ultimoRef.current;
      if (u && u.texto === texto && ahora - u.en < DEBOUNCE_MISMO_CODIGO_MS) return;
      ultimoRef.current = { texto, en: ahora };
      onDetectedRef.current(texto);
    }

    const videoConstraints: ConstraintsConEnfoque = {
      facingMode: 'environment',
      // "ideal" (no "exact"): si el dispositivo no llega a esa resolución,
      // getUserMedia cae a la máxima disponible en vez de fallar.
      width: { ideal: 1920 },
      height: { ideal: 1080 },
      advanced: [{ focusMode: 'continuous' }],
    };

    codeReader
      .decodeFromConstraints(
        { video: videoConstraints },
        videoRef.current ?? undefined,
        // El segundo parametro (err) es NotFoundException en cada frame sin
        // código legible -- normal y constante mientras se encuadra, se ignora.
        (result) => manejarResultado(result),
      )
      .then((c) => {
        if (cancelado) {
          c.stop();
          return;
        }
        controlsRef.current = c;
        setTorchDisponible(typeof c.switchTorch === 'function');
      })
      .catch(() => {
        if (!cancelado) setError('No se pudo acceder a la cámara. Revisá los permisos del navegador.');
      });

    return () => {
      cancelado = true;
      controlsRef.current?.stop();
    };
  }, []);

  async function handleToggleTorch() {
    try {
      await controlsRef.current?.switchTorch?.(!torchOn);
      setTorchOn(!torchOn);
    } catch {
      // Torch "disponible" en la API pero el hardware no respondió -- se
      // deja el estado como estaba, no es un error que valga interrumpir el
      // escaneo por él.
    }
  }

  return (
    <div className="modal-overlay">
      <div className="modal camera-scanner-modal">
        <div className="modal__header">
          <span>Escanear con cámara</span>
          <button type="button" className="modal__close" onClick={onClose} aria-label="Cerrar">×</button>
        </div>
        {error ? (
          <p role="alert" className="alert">{error}</p>
        ) : (
          <>
            <div className="camera-scanner-viewport">
              <video ref={videoRef} className="camera-scanner-video" muted playsInline />
              <div className="camera-scanner-guide" aria-hidden="true" />
            </div>
            <p className="panel__hint camera-scanner-hint">
              Acercá el código hasta llenar el recuadro -- si se ve borroso, alejalo unos centímetros y esperá a que enfoque solo.
            </p>
            {torchDisponible && (
              <button type="button" className="btn btn--ghost btn--sm btn--block" onClick={handleToggleTorch}>
                {torchOn ? 'Apagar linterna' : 'Encender linterna'}
              </button>
            )}
          </>
        )}
      </div>
    </div>
  );
}
