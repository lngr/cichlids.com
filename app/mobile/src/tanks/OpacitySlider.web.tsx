import { useTheme } from "../theme";
import type { OpacitySliderProps } from "./OpacitySlider";

/** The browser slider is a range input, which brings keyboard and screen reader operation along. */
export function OpacitySlider({ value, onChange, label, valueText, testID }: OpacitySliderProps) {
  const theme = useTheme();
  return <input type="range" data-testid={testID} aria-label={label} aria-valuetext={valueText}
    min={0} max={100} step={5} value={Math.round(value * 100)} onChange={event => onChange(Number(event.target.value) / 100)}
    style={{ width: "100%", minWidth: 48, minHeight: 48, accentColor: theme.colors.accent, cursor: "pointer" }} />;
}
