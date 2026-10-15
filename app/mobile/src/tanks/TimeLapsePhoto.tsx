import { useEffect, useRef, useState } from "react";
import { AccessibilityInfo, Animated, Platform, StyleSheet, View } from "react-native";
import { Image } from "expo-image";

export function useReducedMotion() {
  const [reduced, setReduced] = useState(true);
  useEffect(() => {
    let active = true;
    const update = (value: boolean) => { if (active) setReduced(value); };
    void AccessibilityInfo.isReduceMotionEnabled().then(update);
    const listener = AccessibilityInfo.addEventListener("reduceMotionChanged", update);
    return () => { active = false; listener.remove(); };
  }, []);
  return reduced;
}

function FadeLayer({ uri, label, reduced }: { uri: string; label: string; reduced: boolean }) {
  const opacity = useRef(new Animated.Value(reduced ? 1 : 0)).current;
  const animation = useRef<Animated.CompositeAnimation | null>(null);
  useEffect(() => {
    if (reduced) { animation.current?.stop(); opacity.setValue(1); }
    return () => animation.current?.stop();
  }, [reduced, opacity]);
  return <Animated.View testID="tank-time-machine-frame" style={[StyleSheet.absoluteFill, { opacity }]}>
    <Image source={uri} accessibilityLabel={label} style={StyleSheet.absoluteFill} contentFit="contain"
      onLoad={() => {
        animation.current = Animated.timing(opacity, { toValue: 1, duration: reduced ? 0 : 450, useNativeDriver: Platform.OS !== "web" });
        animation.current.start();
      }} />
  </Animated.View>;
}

export function TimeLapsePhoto({ uri, label, reduced }: { uri: string; label: string; reduced: boolean }) {
  const [pair, setPair] = useState({ current: uri, previous: uri });
  if (pair.current !== uri) setPair({ current: uri, previous: pair.current });
  return <View style={{ flex: 1, minHeight: 180 }}>
    <Image source={pair.previous} accessible={false} style={StyleSheet.absoluteFill} contentFit="contain" />
    <FadeLayer key={uri} uri={uri} label={label} reduced={reduced} />
  </View>;
}
