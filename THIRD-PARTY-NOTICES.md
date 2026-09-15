# Third-Party Notices

## Tabler Icons

Used by: `src/FileSharing.Web/Components/Shared/IconLibrary.cs` (Web, inline SVG) and
`src/FileSharing.Mobile/Resources/Images/*.svg` (Mobile, rasterized at build time by the MAUI
resizetizer).

- Source: https://tabler.io/icons
- Repository: https://github.com/tabler/tabler-icons
- License: MIT
- Copyright (c) 2020-2026 Paweł Kuna

Icons are vendored locally (not loaded from a CDN) and used unmodified except for stripping the
per-file metadata comment block and, for Mobile only, baking a fixed `stroke` color at build time
in place of `currentColor` (MAUI's SVG-to-PNG rasterization has no live "current color" to
resolve against, unlike the Web, where the original `currentColor` is kept so CSS controls it).

```
MIT License

Copyright (c) 2020-2026 Paweł Kuna

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
