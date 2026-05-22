const ESC = 0x1b;
const GS = 0x1d;
const LF = 0x0a;

const ASCII_FOLD: Record<string, string> = {
  á: 'a', à: 'a', â: 'a', ã: 'a', ä: 'a',
  Á: 'A', À: 'A', Â: 'A', Ã: 'A', Ä: 'A',
  é: 'e', è: 'e', ê: 'e', ë: 'e',
  É: 'E', È: 'E', Ê: 'E', Ë: 'E',
  í: 'i', ì: 'i', î: 'i', ï: 'i',
  Í: 'I', Ì: 'I', Î: 'I', Ï: 'I',
  ó: 'o', ò: 'o', ô: 'o', õ: 'o', ö: 'o',
  Ó: 'O', Ò: 'O', Ô: 'O', Õ: 'O', Ö: 'O',
  ú: 'u', ù: 'u', û: 'u', ü: 'u',
  Ú: 'U', Ù: 'U', Û: 'U', Ü: 'U',
  ç: 'c', Ç: 'C', ñ: 'n', Ñ: 'N',
  '–': '-', '—': '-', '“': '"', '”': '"', '’': "'"
};

function fold(input: string): string {
  return [...String(input || '')].map((ch) => ASCII_FOLD[ch] || ch).join('');
}

class EscposBuilder {
  private parts: number[] = [];

  raw(bytes: number[]) { this.parts.push(...bytes); return this; }
  init() { return this.raw([ESC, 0x40]); }
  center() { return this.raw([ESC, 0x61, 0x01]); }
  left() { return this.raw([ESC, 0x61, 0x00]); }
  bold(on: boolean) { return this.raw([ESC, 0x45, on ? 1 : 0]); }
  double(on: boolean) { return this.raw([GS, 0x21, on ? 0x11 : 0x00]); }
  feed(n = 3) { return this.raw([ESC, 0x64, Math.max(0, Math.min(8, n))]); }
  cut() { return this.raw([GS, 0x56, 0x42, 0x00]); }
  text(value: string) {
    for (const byte of Buffer.from(fold(value), 'ascii')) this.parts.push(byte);
    return this;
  }
  line(value = '') { return this.text(value).raw([LF]); }
  sep(width = 32, ch = '-') { return this.line(ch.repeat(width)); }
  build() { return Buffer.from(this.parts); }
}

export function buildTestReceipt(): Buffer {
  const b = new EscposBuilder();
  b.init()
    .center().bold(true).double(true).line('ZELO IMPRESSAO')
    .double(false).bold(false)
    .line('Teste de impressao')
    .left().sep()
    .line('Se voce esta lendo isso,')
    .line('a impressora esta configurada.')
    .line('')
    .line(`Data: ${new Date().toLocaleString('pt-BR')}`)
    .sep()
    .center().line('Zelo')
    .feed(3)
    .cut();
  return b.build();
}
