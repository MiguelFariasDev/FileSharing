namespace FileSharing.Web.Components.Shared;

/// <summary>
/// Inline SVG source for every icon used in this app, vendored locally from Tabler Icons
/// (https://tabler.io/icons, MIT License, Copyright (c) 2020-2026 Paweł Kuna — see
/// THIRD-PARTY-NOTICES.md) rather than loaded from a CDN. Every icon keeps its original
/// stroke="currentColor" so &lt;Icon&gt; (Icon.razor) can recolor it purely via CSS `color`,
/// exactly like a font icon, without needing a colored variant per icon per use site.
/// </summary>
public static class IconLibrary
{
    private static readonly Dictionary<string, string> Icons = new()
    {
        ["activity"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M3 12h4l3 8l4 -16l3 8h4" />
</svg>
""""",
        ["alert-circle"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M3 12a9 9 0 1 0 18 0a9 9 0 0 0 -18 0" />
<path d="M12 8v4" />
<path d="M12 16h.01" />
</svg>
""""",
        ["alert-triangle"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M12 9v4" />
<path d="M10.363 3.591l-8.106 13.534a1.914 1.914 0 0 0 1.636 2.871h16.214a1.914 1.914 0 0 0 1.636 -2.87l-8.106 -13.536a1.914 1.914 0 0 0 -3.274 0" />
<path d="M12 16h.01" />
</svg>
""""",
        ["arrow-left"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M5 12l14 0" />
<path d="M5 12l6 6" />
<path d="M5 12l6 -6" />
</svg>
""""",
        ["arrow-right"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M5 12l14 0" />
<path d="M13 18l6 -6" />
<path d="M13 6l6 6" />
</svg>
""""",
        ["bell"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M10 5a2 2 0 1 1 4 0a7 7 0 0 1 4 6v3a4 4 0 0 0 2 3h-16a4 4 0 0 0 2 -3v-3a7 7 0 0 1 4 -6" />
<path d="M9 17v1a3 3 0 0 0 6 0v-1" />
</svg>
""""",
        ["chevron-down"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M6 9l6 6l6 -6" />
</svg>
""""",
        ["circle-check"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0" />
<path d="M9 12l2 2l4 -4" />
</svg>
""""",
        ["circle-x"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0" />
<path d="M10 10l4 4m0 -4l-4 4" />
</svg>
""""",
        ["clock"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M3 12a9 9 0 1 0 18 0a9 9 0 0 0 -18 0" />
<path d="M12 7v5l3 3" />
</svg>
""""",
        ["cloud-download"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M19 18a3.5 3.5 0 0 0 0 -7h-1a5 4.5 0 0 0 -11 -2a4.6 4.4 0 0 0 -2.1 8.4" />
<path d="M12 13l0 9" />
<path d="M9 19l3 3l3 -3" />
</svg>
""""",
        ["cloud-upload"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M7 18a4.6 4.4 0 0 1 0 -9a5 4.5 0 0 1 11 2h1a3.5 3.5 0 0 1 0 7h-1" />
<path d="M9 15l3 -3l3 3" />
<path d="M12 12l0 9" />
</svg>
""""",
        ["copy"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M7 9.667a2.667 2.667 0 0 1 2.667 -2.667h8.666a2.667 2.667 0 0 1 2.667 2.667v8.666a2.667 2.667 0 0 1 -2.667 2.667h-8.666a2.667 2.667 0 0 1 -2.667 -2.667l0 -8.666" />
<path d="M4.012 16.737a2.005 2.005 0 0 1 -1.012 -1.737v-10c0 -1.1 .9 -2 2 -2h10c.75 0 1.158 .385 1.5 1" />
</svg>
""""",
        ["download"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2 -2v-2" />
<path d="M7 11l5 5l5 -5" />
<path d="M12 4l0 12" />
</svg>
""""",
        ["eye"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M10 12a2 2 0 1 0 4 0a2 2 0 0 0 -4 0" />
<path d="M21 12c-2.4 4 -5.4 6 -9 6c-3.6 0 -6.6 -2 -9 -6c2.4 -4 5.4 -6 9 -6c3.6 0 6.6 2 9 6" />
</svg>
""""",
        ["eye-off"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M10.585 10.587a2 2 0 0 0 2.829 2.828" />
<path d="M16.681 16.673a8.717 8.717 0 0 1 -4.681 1.327c-3.6 0 -6.6 -2 -9 -6c1.272 -2.12 2.712 -3.678 4.32 -4.674m2.86 -1.146a9.055 9.055 0 0 1 1.82 -.18c3.6 0 6.6 2 9 6c-.666 1.11 -1.379 2.067 -2.138 2.87" />
<path d="M3 3l18 18" />
</svg>
""""",
        ["file"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M14 3v4a1 1 0 0 0 1 1h4" />
<path d="M17 21h-10a2 2 0 0 1 -2 -2v-14a2 2 0 0 1 2 -2h7l5 5v11a2 2 0 0 1 -2 2" />
</svg>
""""",
        ["file-type-pdf"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M14 3v4a1 1 0 0 0 1 1h4" />
<path d="M5 12v-7a2 2 0 0 1 2 -2h7l5 5v4" />
<path d="M5 18h1.5a1.5 1.5 0 0 0 0 -3h-1.5v6" />
<path d="M17 18h2" />
<path d="M20 15h-3v6" />
<path d="M11 15v6h1a2 2 0 0 0 2 -2v-2a2 2 0 0 0 -2 -2h-1" />
</svg>
""""",
        ["files"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M15 3v4a1 1 0 0 0 1 1h4" />
<path d="M18 17h-7a2 2 0 0 1 -2 -2v-10a2 2 0 0 1 2 -2h4l5 5v7a2 2 0 0 1 -2 2" />
<path d="M16 17v2a2 2 0 0 1 -2 2h-7a2 2 0 0 1 -2 -2v-10a2 2 0 0 1 2 -2h2" />
</svg>
""""",
        ["folder"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M5 4h4l3 3h7a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-11a2 2 0 0 1 2 -2" />
</svg>
""""",
        ["history"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M12 8l0 4l2 2" />
<path d="M3.05 11a9 9 0 1 1 .5 4m-.5 5v-5h5" />
</svg>
""""",
        ["home"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M5 12l-2 0l9 -9l9 9l-2 0" />
<path d="M5 12v7a2 2 0 0 0 2 2h10a2 2 0 0 0 2 -2v-7" />
<path d="M9 21v-6a2 2 0 0 1 2 -2h2a2 2 0 0 1 2 2v6" />
</svg>
""""",
        ["key"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M16.555 3.843l3.602 3.602a2.877 2.877 0 0 1 0 4.069l-2.643 2.643a2.877 2.877 0 0 1 -4.069 0l-.301 -.301l-6.558 6.558a2 2 0 0 1 -1.239 .578l-.175 .008h-1.172a1 1 0 0 1 -.993 -.883l-.007 -.117v-1.172a2 2 0 0 1 .467 -1.284l.119 -.13l.414 -.414h2v-2h2v-2l2.144 -2.144l-.301 -.301a2.877 2.877 0 0 1 0 -4.069l2.643 -2.643a2.877 2.877 0 0 1 4.069 0" />
<path d="M15 9h.01" />
</svg>
""""",
        ["link"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M9 15l6 -6" />
<path d="M11 6l.463 -.536a5 5 0 0 1 7.071 7.072l-.534 .464" />
<path d="M13 18l-.397 .534a5.068 5.068 0 0 1 -7.127 0a4.972 4.972 0 0 1 0 -7.071l.524 -.463" />
</svg>
""""",
        ["loader-2"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M12 3a9 9 0 1 0 9 9" />
</svg>
""""",
        ["lock"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M5 13a2 2 0 0 1 2 -2h10a2 2 0 0 1 2 2v6a2 2 0 0 1 -2 2h-10a2 2 0 0 1 -2 -2v-6" />
<path d="M11 16a1 1 0 1 0 2 0a1 1 0 0 0 -2 0" />
<path d="M8 11v-4a4 4 0 1 1 8 0v4" />
</svg>
""""",
        ["logout"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M14 8v-2a2 2 0 0 0 -2 -2h-7a2 2 0 0 0 -2 2v12a2 2 0 0 0 2 2h7a2 2 0 0 0 2 -2v-2" />
<path d="M9 12h12l-3 -3" />
<path d="M18 15l3 -3" />
</svg>
""""",
        ["mail"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M3 7a2 2 0 0 1 2 -2h14a2 2 0 0 1 2 2v10a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-10" />
<path d="M3 7l9 6l9 -6" />
</svg>
""""",
        ["music"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M3 17a3 3 0 1 0 6 0a3 3 0 0 0 -6 0" />
<path d="M13 17a3 3 0 1 0 6 0a3 3 0 0 0 -6 0" />
<path d="M9 17v-13h10v13" />
<path d="M9 8h10" />
</svg>
""""",
        ["photo"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M15 8h.01" />
<path d="M3 6a3 3 0 0 1 3 -3h12a3 3 0 0 1 3 3v12a3 3 0 0 1 -3 3h-12a3 3 0 0 1 -3 -3v-12" />
<path d="M3 16l5 -5c.928 -.893 2.072 -.893 3 0l5 5" />
<path d="M14 14l1 -1c.928 -.893 2.072 -.893 3 0l3 3" />
</svg>
""""",
        ["plus"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M12 5l0 14" />
<path d="M5 12l14 0" />
</svg>
""""",
        ["settings"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M10.325 4.317c.426 -1.756 2.924 -1.756 3.35 0a1.724 1.724 0 0 0 2.573 1.066c1.543 -.94 3.31 .826 2.37 2.37a1.724 1.724 0 0 0 1.065 2.572c1.756 .426 1.756 2.924 0 3.35a1.724 1.724 0 0 0 -1.066 2.573c.94 1.543 -.826 3.31 -2.37 2.37a1.724 1.724 0 0 0 -2.572 1.065c-.426 1.756 -2.924 1.756 -3.35 0a1.724 1.724 0 0 0 -2.573 -1.066c-1.543 .94 -3.31 -.826 -2.37 -2.37a1.724 1.724 0 0 0 -1.065 -2.572c-1.756 -.426 -1.756 -2.924 0 -3.35a1.724 1.724 0 0 0 1.066 -2.573c-.94 -1.543 .826 -3.31 2.37 -2.37c1 .608 2.296 .07 2.572 -1.065" />
<path d="M9 12a3 3 0 1 0 6 0a3 3 0 0 0 -6 0" />
</svg>
""""",
        ["share"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M3 12a3 3 0 1 0 6 0a3 3 0 1 0 -6 0" />
<path d="M15 6a3 3 0 1 0 6 0a3 3 0 1 0 -6 0" />
<path d="M15 18a3 3 0 1 0 6 0a3 3 0 1 0 -6 0" />
<path d="M8.7 10.7l6.6 -3.4" />
<path d="M8.7 13.3l6.6 3.4" />
</svg>
""""",
        ["shield"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M12 3a12 12 0 0 0 8.5 3a12 12 0 0 1 -8.5 15a12 12 0 0 1 -8.5 -15a12 12 0 0 0 8.5 -3" />
</svg>
""""",
        ["shield-lock"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M12 3a12 12 0 0 0 8.5 3a12 12 0 0 1 -8.5 15a12 12 0 0 1 -8.5 -15a12 12 0 0 0 8.5 -3" />
<path d="M11 11a1 1 0 1 0 2 0a1 1 0 1 0 -2 0" />
<path d="M12 12l0 2.5" />
</svg>
""""",
        ["upload"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2 -2v-2" />
<path d="M7 9l5 -5l5 5" />
<path d="M12 4l0 12" />
</svg>
""""",
        ["user"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M8 7a4 4 0 1 0 8 0a4 4 0 0 0 -8 0" />
<path d="M6 21v-2a4 4 0 0 1 4 -4h4a4 4 0 0 1 4 4v2" />
</svg>
""""",
        ["user-circle"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0" />
<path d="M9 10a3 3 0 1 0 6 0a3 3 0 1 0 -6 0" />
<path d="M6.168 18.849a4 4 0 0 1 3.832 -2.849h4a4 4 0 0 1 3.834 2.855" />
</svg>
""""",
        ["video"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M15 10l4.553 -2.276a1 1 0 0 1 1.447 .894v6.764a1 1 0 0 1 -1.447 .894l-4.553 -2.276v-4" />
<path d="M3 8a2 2 0 0 1 2 -2h8a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-8a2 2 0 0 1 -2 -2l0 -8" />
</svg>
""""",
        ["x"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M18 6l-12 12" />
<path d="M6 6l12 12" />
</svg>
""""",
        ["zip"] =
"""""
<svg
xmlns="http://www.w3.org/2000/svg"
viewBox="0 0 24 24"
fill="none"
stroke="currentColor"
stroke-width="2"
stroke-linecap="round"
stroke-linejoin="round"
>
<path d="M16 16v-8h2a2 2 0 1 1 0 4h-2" />
<path d="M12 8v8" />
<path d="M4 8h4l-4 8h4" />
</svg>
""""",
    };

    public static string Get(string name) =>
        Icons.TryGetValue(name, out var svg) ? svg : throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown icon name - check FileSharing.Web.Components.Shared.IconLibrary.");
}
