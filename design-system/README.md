# Design System — cichlids.com app

Binding design foundation for the app UI. Mobile-first (Android first, iOS later),
light **and** dark, target WCAG AA.

## Source of truth

- **[`tokens.css`](tokens.css)** / **[`tokens.json`](tokens.json)** — color, spacing, typography,
  radii, elevation as machine-readable tokens (Palette A, OKLCH). These values are authoritative;
  components and screens must not introduce one-off colors.
- **[`../cichlids-design-system.html`](../cichlids-design-system.html)** — visual documentation of
  the foundations and core components (live).

## Reference screens (design template, not the shipping app)

- **[`../cichlids-tank-prototype.html`](../cichlids-tank-prototype.html)** — full interactive user
  flow (light/dark toggle): "My tanks", tank detail (Story/Care), quick-log, care dashboard,
  explore, profile, onboarding, "create tank" wizard (incl. compatibility warning), tank settings
  (visibility levels).
- **[`../cichlids-tank-detail-hifi.html`](../cichlids-tank-detail-hifi.html)** — high-fidelity
  treatment of the tank detail, including the time-machine player.

Product/PMF rationale: [`../PRODUCT-VISION.md`](../PRODUCT-VISION.md) (the tank as a living
project; care produces pride). Epics 1 and 2 are the focus of the first implementation.

## Binding rules

- Use the tokens from `tokens.*`; a single accent, **at most 2x per screen**.
- Text/icon on accent fills always via `--on-accent` (white in light, dark in dark).
- Theme switching: the scope must set `color: var(--fg)`, not only the variables.
- Material patterns on Android (top app bar, bottom nav, FAB); touch targets ≥ 48dp.
