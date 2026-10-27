# Transitions and animations: matching choices and deviations

Normative notes for the behavior in `Animation/` where the specifications leave room or the engine differs. The tests and the WPT replay are named in `architecture.md`.

## Choices

- **Before-change style.** The style a transition starts from is the previous tree's style of the same element as the page last showed it, so a running transition or animation counts at its current value (Transitions 1 §3 says the same for transitions, and for animations in the "before-change style" definition).
- **Reversal.** A transition whose end value equals the start of a running one reverses with the shortening factor and reversing-adjusted start of Transitions 1 §3.2.
- **Discrete transitions.** `allow-discrete` transitions flip at the midpoint, except `display`: a transition with `none` at one end keeps the other value for the whole interval, and a transition of `display` from `block` to `none` keeps the element displayed until it ends. An element whose after-change `display` is `none` starts no other transition and runs no animation (Transitions 2 §3.1).
- **Keyframes.** `unset` in a keyframe means `inherit` or `initial` by property. Declarations marked `!important` and custom properties in a keyframe are ignored. The implicit 0% and 100% keyframes take the element's underlying value. Animation names compare case-sensitively; `"none"` as a string is an animation named none.
- **Computed text.** `animation-name` keeps names that read as keywords quoted; identifiers are escaped (CSSOM "serialize an identifier"). `steps(n, start)` and `steps(n, jump-start)` serialize as written. `animation-duration: auto` computes to `0s` (no scroll timelines). `transition-property` keeps the case of unknown names. Times and the iteration count accept `calc()` and the other math functions that need no layout context.
- **Ends of a length mix.** Interpolating a px length with a percentage gives `calc()`; at the end of the range the result is the end value itself, at the start it stays a sum such as `calc(0% + 40px)` (matches Chrome 156, and the WPT interpolation tests expect it).
- **`vertical-align`.** `baseline` takes part in interpolation as the zero offset.
- **Colors.** All colors interpolate premultiplied in sRGB, whatever syntax they were written in. A color written in `oklab()` or another space interpolates in sRGB too, so a mid-point differs slightly from Chrome. `currentcolor` in a shadow or filter resolves against the element's cascaded `color`.
- **Serialization of channels.** Color channels round half away from zero when serialized (`rgb(178.5)` is 179).
- **Events.** `animationiteration` is dispatched for the iteration boundary a frame lands in; an iteration boundary skipped by a late frame is not repeated. `transitionend` and `animationend` carry the elapsed time of the whole effect. A cancelled effect that never started a run (a transition cancelled before its first tick) fires nothing.
- **Order of effects.** Base style, then transitions, then animations in the order of `animation-name`; a later animation of the same property wins.

## Known deviations

- Colors in `oklab()`/`oklch()`/`color-mix()` do not interpolate in their own space.
- `font-size` animation does not recompute `em` lengths of element descendants (text follows).
- Animated colors, shadows and filters repaint and are not patched in place; only transform and opacity are.
- Pseudo-elements, `::marker` and `::first-line` do not animate.
- `animation-composition`, `animation-timeline`, `animation-range` and `var()` inside `@keyframes` are not implemented.
- A page parsed without `HtmlPageOptions.AnimationTime` and not wrapped in `InteractivePage` is static.

## Replay of the WPT helpers

`testdata/wpt-motion/` holds the css-transitions and css-animations testharness files that need no script: the `parsing/` files (replayed like the other parsing files, `WptStyleRunner`) and the files calling `test_interpolation`, `test_no_interpolation` and `test_not_animatable`. For the latter, `WptMotionRunner` rebuilds what the helper builds: a target and a reference element, the reference carrying the expected value as a plain declaration; the target gets a 100 s animation or transition with a -50 s delay and the helper's timing function, which yields the wanted progress at the midpoint. A transition starts from a style change (focusing the target). The comparison is the engine's own computed text on both sides. The Web Animations variant of the helper, composition tests and everything that needs promises, `getAnimations()`, event handlers or frames are excluded with a reason in `excluded.tsv`. `expected-failures.tsv` lists every replayed failure with its reason; `floors.tsv` holds each module's rate, which only rises.
