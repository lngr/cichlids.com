import { useState } from "react";
import { View } from "react-native";
import { useTheme } from "../theme";

export interface OpacitySliderProps {
  /** The value between 0 and 1. */
  value: number;
  onChange(value: number): void;
  label: string;
  /** The spoken value, such as "40 percent". */
  valueText: string;
  testID?: string;
}

const STEP = 0.05;

/** A horizontal slider from 0 to 1 that follows a finger and answers the increment and decrement actions of screen readers. */
export function OpacitySlider({ value, onChange, label, valueText, testID }: OpacitySliderProps) {
  const theme = useTheme();
  const [width, setWidth] = useState(1);
  const at = (x: number) => onChange(Math.min(1, Math.max(0, x / width)));
  return <View accessible testID={testID} accessibilityRole="adjustable" accessibilityLabel={label}
    accessibilityValue={{ min: 0, max: 100, now: Math.round(value * 100), text: valueText }}
    accessibilityActions={[{ name: "increment" }, { name: "decrement" }]}
    onAccessibilityAction={event => onChange(Math.min(1, Math.max(0, value + (event.nativeEvent.actionName === "increment" ? STEP : -STEP))))}
    onLayout={event => setWidth(Math.max(1, event.nativeEvent.layout.width))}
    onStartShouldSetResponder={() => true}
    onResponderGrant={event => at(event.nativeEvent.locationX)}
    onResponderMove={event => at(event.nativeEvent.locationX)}
    style={{ minWidth: 48, minHeight: 48, justifyContent: "center" }}>
    <View style={{ height: 6, borderRadius: theme.radius.pill, backgroundColor: theme.colors.border }}>
      <View style={{ height: 6, width: `${100 * value}%`, borderRadius: theme.radius.pill, backgroundColor: theme.colors.accent }} />
    </View>
  </View>;
}
