/** The parts of gifenc (no bundled types) the showcase capture uses. */
declare module 'gifenc' {
  type Palette = number[][];
  interface Encoder {
    writeFrame(
      index: Uint8Array,
      width: number,
      height: number,
      options: { palette: Palette; delay: number },
    ): void;
    finish(): void;
    bytes(): Uint8Array;
  }
  export function GIFEncoder(): Encoder;
  export function quantize(rgba: Uint8Array | Buffer, maxColors: number): Palette;
  export function applyPalette(rgba: Uint8Array | Buffer, palette: Palette): Uint8Array;
}
