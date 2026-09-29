import 'fake-indexeddb/auto';

// jsdom no implementa Blob/File.text() -- MantenedorPage lo usa para leer el
// JSON del maestro cargado desde archivo. Polyfill vía FileReader (que jsdom
// sí implementa) para que el test ejercite el mismo código que el navegador real.
if (!Blob.prototype.text) {
  Blob.prototype.text = function (this: Blob) {
    return new Promise<string>((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(String(reader.result));
      reader.onerror = () => reject(reader.error);
      reader.readAsText(this);
    });
  };
}
