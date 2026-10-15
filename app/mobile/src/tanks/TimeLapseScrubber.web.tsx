import { useTranslation } from "react-i18next";
import { useTheme } from "../theme";

export function TimeLapseScrubber({ index, count, onSeek }: { index: number; count: number; onSeek: (index: number) => void }) {
  const { t } = useTranslation();
  const theme = useTheme();
  return <input type="range" data-testid="tank-time-machine-scrub" aria-label={t("tankStory.scrub")}
    min={0} max={count - 1} value={index} onChange={event => onSeek(Number(event.target.value))}
    style={{ width: "100%", minWidth: 48, minHeight: 48, accentColor: theme.colors.accent, cursor: "pointer" }} />;
}
