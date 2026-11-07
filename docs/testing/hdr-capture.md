# Automatic HDR capture

The existing Windows Graphics Capture adapter detects the active mode of its captured primary monitor, including when Windows HDR is enabled globally for SDR applications. No profile switch or game setting change is required. This does not add capture of secondary monitors or change the WPF overlay renderer.

DisplayConfig advanced-color v2 distinguishes active SDR, WCG and HDR from supported/user-enabled capabilities. Older Windows uses the advanced-color enabled/wide-color flags. The adapter reads the Windows SDR-white level (fixed-point multiplier of the 80-nit scRGB reference), polling at most once per second during capture. A mode or white-level change invalidates cached pixels and the previous pool generation. Old callbacks cannot repopulate the cache. Capture still uses compositor timestamps and the existing freshness budget.

SDR retains the existing BGRA8 path. HDR/WCG use R16G16B16A16Float and native D3D11 staging readback; SoftwareBitmap integer conversion must not clip HDR before mapping. Unknown mode also uses FP16 and is explicitly reported as Unknown. If SDR white is unavailable, mapping uses 80 nits and metadata marks the white level unknown. Errors remain capture failures; there is no silent clipped-SDR retry for an HDR failure.

The OCR image is opaque BGRA8. Each linear scRGB component is normalized by 80 / Windows SDR-white nits. Values through 0.75 retain their linear value; higher values use the fixed shoulder `1 - 0.25 / (1 + (x - 0.75) / 0.25)`, followed by sRGB encoding. A per-white-level half-float lookup table keeps this bounded and deterministic. Negative/NaN values map to black; positive infinity maps to white. This is an OCR-oriented conversion, not a color-critical HDR screenshot exporter. It preserves highlight gradation without frame-dependent exposure pumping; per-channel compression can change saturated highlight colors.

Local diagnostic PNGs remain the exact delivered OCR input, not raw HDR. Their JSON includes displayColorMode, capturePoolFormat, hdrToneMapping, sdrWhiteNits, sdrWhiteLevelKnown and colorDetection. Existing privacy, sampling and storage limits apply.

## Verification

- Synthetic tests cover active versus supported HDR, older detection flags, unknown mode, invalid white values, native packet layouts, mode/white-level transitions and polling, failed transition retry, highlight ordering, gamma, white normalization, invalid float values, channel order, row padding and diagnostic metadata.
- Native development smoke on an HDR-enabled 2560×1440 desktop detected HDR through v2 and read Windows SDR white as 240 nits. Twenty warm full-screen captures completed through the FP16 path. One comparable run measured median/P95 35.7/40.1 ms; the legacy 8-bit path measured 35.6/42.6 ms. These short desktop measurements are not a game endurance or OCR-quality acceptance result.
- Smoke source, captured desktop pixels and measurements under ignored `work/hdr-support/` are local-only generated evidence and must not be published.
- Manually verify HDR on/off while live capture runs, Windows SDR-brightness changes, SDR-only displays, dark/light game menus and scene transitions. Check correct mode/format in newly sampled metadata and fresh pixels after a transition. Physical HDR toggling and in-game flicker acceptance remain unverified by synthetic tests.
- A capture fix does not prove that whole-screen brightness flicker caused by overlay/display composition is resolved. Test that separately in the affected game. Overlay rendering, profile migrations, translation-provider behavior and runtime-package changes are outside this change; their specific delivery gates are N/A.

References: [Microsoft screen capture HDR guidance](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture), [Windows DisplayConfig structures](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/wingdi.h), [Direct3D surface interface access](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.directx.direct3d11.interop/nf-windows-graphics-directx-direct3d11-interop-idirect3ddxgiinterfaceaccess-getinterface).
