# UxmlPreview

> The shipped widget now builds its rows, panes and prompts in code and takes its colors from a
> separate theme stylesheet, so this converter shows only the static skeleton. For design review of
> the real widget, use the PNGs from `Tests/Preview/WidgetPreviewCapture`. The converter stays useful
> for static UXML screens.

Converts a UI Toolkit `.uxml` (and the `.uss` it references) into a standalone HTML page that
approximates Unity's rendering, so a screen can be reviewed in a browser without opening Unity.

```bash
ruby Tools~/UxmlPreview/uxml2html.rb Runtime/UI/BugReportWidget.uxml /tmp/preview.html --width 1280 --height 720
```

## Why it is close

Both systems lay out with flexbox. Every Unity element becomes a custom HTML element
(`Label` -> `<uxml-label>`) inside a reset that mirrors UI Toolkit defaults: flex column,
`flex-shrink: 1`, `align-items: stretch`, `position: relative`, no margins. Type selectors keep
type specificity, class and name selectors are unchanged, USS variables pass through.

## Mapping

| USS | CSS |
|---|---|
| `-unity-font-style: bold` | `font-weight: 700` |
| `-unity-text-align: middle-center` | `justify-content` + `align-items` + `text-align` |
| `white-space: nowrap` / `normal` | `pre` / `pre-wrap` (Unity keeps explicit newlines) |
| `rotate`, `translate`, `scale` | one `transform` |
| `letter-spacing: Npx` | `letter-spacing: (N/100)em` (measured: Unity applies the px value as 1/100 em) |
| `display: flex` / `none` | same |
| other `-unity-*` | dropped |

`TextField` renders as the element plus an inner `#unity-text-input` (`unity-base-text-field__input`)
with the value as text, matching the selectors you write in USS.

## Known deviations

- Fonts: preview uses Inter from Google Fonts. Unity uses the font asset in the runtime theme or your USS. Text metrics differ slightly.
- The Unity runtime theme's default styles for `Button`, `TextField` and `Toggle` are not replicated. Neutralize them in your USS (this project does) or expect differences.
- `Template` / `Instance` are not expanded.
- `background-image: resource(...)` is dropped; `url(...)` is kept.
- No text input behaviour, hover or focus. Static picture only.

## Measuring

`Tests/PlayMode/WidgetPreviewCapture.cs` renders the same UXML through a runtime panel to a
1280x720 PNG. Screenshot the HTML at the same size and compare.

## Capture notes (measured 2026-09-06, Unity 2022.3.62f3)

- `-batchmode` does not repaint offscreen runtime panels on its own. The capture test forces it by
  calling UI Toolkit's internal `UIElementsRuntimeUtility.RepaintOffscreenPanels` through reflection,
  so the capture runs headless. `WaitForEndOfFrame` never resolves in batchmode; the test avoids it there.
- `PanelSettings.colorClearValue` is consumed in the project's working color space. In a Linear
  project pass `color.linear`, otherwise the backdrop comes out lighter than the USS colors.
- Yoga has no content-based automatic minimum size; the preview reset sets `min-width: 0; min-height: 0`
  or long unwrappable text (nowrap log lines) widens a flex card in the browser only.
- `TextField` wraps 2px earlier in Unity: its inner `TextElement` carries `padding-right: 2px` for the
  caret. The converter reproduces the inner structure and the padding.
- `letter-spacing: 1px` in USS renders as 0.01 em (0.12 px at 12 px, 0.16 px at 16 px), measured with
  two probe values at two font sizes. The converter emits em units accordingly. Unity adds no spacing
  after the last glyph while CSS does, so a spaced label is up to one spacing unit wider in the browser.
- Fonts: reference the same `.ttf` from USS (`-unity-font-definition: url(...)`); the converter embeds
  it as a data-URI `@font-face`. Remaining text differences are SDF versus browser rasterization and
  sub-pixel advance rounding, which can still move a wrap by one word on long lines.
