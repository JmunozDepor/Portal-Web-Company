import { useEffect, useRef, useState } from 'react';

export function BarcodeInput({ onCommit }: { onCommit: (barcode: string) => void }) {
  const [value, setValue] = useState('');
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    inputRef.current?.focus();
    const refocus = () => inputRef.current?.focus();
    document.addEventListener('visibilitychange', refocus);
    return () => document.removeEventListener('visibilitychange', refocus);
  }, []);

  function handleKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'Enter' && value.trim().length > 0) {
      onCommit(value.trim());
      setValue('');
    }
  }

  return (
    <input
      ref={inputRef}
      value={value}
      onChange={(e) => setValue(e.target.value)}
      onKeyDown={handleKeyDown}
      placeholder="Escanear código de barra"
      autoFocus
    />
  );
}
