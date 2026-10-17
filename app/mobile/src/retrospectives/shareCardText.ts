/**
 * Breaks a text into at most maxLines lines that each fit maxWidth by the given measure; words are
 * kept whole. The last line ends with an ellipsis when words are left over.
 */
export function wrapText(text: string, maxWidth: number, maxLines: number, measure: (text: string) => number): string[] {
  const words = text.split(/\s+/).filter(Boolean);
  const lines: string[] = [];
  let current = "";
  for (let index = 0; index < words.length; index++) {
    const word = words[index]!;
    const candidate = current ? `${current} ${word}` : word;
    if (measure(candidate) <= maxWidth || !current) {
      current = candidate;
      continue;
    }
    lines.push(current);
    current = word;
    if (lines.length === maxLines - 1) {
      current = words.slice(index).join(" ");
      break;
    }
  }
  if (current) lines.push(current);
  const last = lines.length - 1;
  if (last >= 0 && measure(lines[last]!) > maxWidth) {
    let shortened = lines[last]!;
    while (shortened.length > 1 && measure(`${shortened}…`) > maxWidth) shortened = shortened.slice(0, -1);
    lines[last] = `${shortened}…`;
  }
  return lines.slice(0, maxLines);
}
