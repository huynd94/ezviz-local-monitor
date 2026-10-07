# Native gate: Ubuntu 24.04 x64

Observed on WSL2 Ubuntu 24.04.5, x86_64, non-root user `ubuntu`, .NET SDK 8.0.131/runtime 8.0.31. Windows probe uses SDK 8.0.424. Test video: FFmpeg-generated H.264 640x480 at 5 FPS. Model hash: `b2bc52f40e8e1c532427d5bde3575a5d5b571b739fab2c6df443733ed1589cbd`.

| Probe | Result |
|---|---|
| Baseline managed without native | RED: native load failure and missing audit library; ONNX inference passes. |
| OpenCvSharp4 4.10.0.20241108 official Linux runtime | Rejected: missing tesseract4, GTK2/GDK X11, avcodec58, avformat58, avutil56, swscale5, TIFF5 and OpenEXR2.5; GUI dependencies. |
| OpenCvSharp5 5.0.0.20261003 headless | Linux 3/3 pass: H.264 decode, resize/JPEG round-trip, CPU ONNX and dependency audit. |
| OpenCvSharp5 5.0.0.20261003 Windows runtime | Decode/JPEG and CPU inference pass; Linux-specific audit not executed on Windows. |

Headless library's ldd lists only libm, libpthread, libdl, libstdc++, libgcc_s, libc and the loader. No GTK/X11/Wayland/Qt and no missing library.

Selected packages are pinned in `build/NativeRuntime.props`. SDK 8 emits CS9057 for OpenCvSharp5's optional analyzer compiled with a newer compiler; runtime tests pass. The managed package's analyzer assets will be excluded rather than upgrading the project's SDK or suppressing runtime checks.

Subagent read-only review found and addressed output-glob isolation, incomplete X11 dependency matching and Ubuntu/RID validation. No real camera credentials were used; live RTSP/ONVIF camera acceptance remains a separate gate.
