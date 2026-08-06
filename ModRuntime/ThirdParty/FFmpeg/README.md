# FFmpeg redistribution notes

This project bundles `Tools/ffmpeg.exe` in release packages. The bundled binary
is the gyan.dev FFmpeg essentials build and is distributed under GPLv3.

Release checklist:

1. Keep `Tools/ffmpeg.exe` in the release package only together with:
   - `Tools/FFmpeg-BUILD.txt`
   - `Tools/FFmpeg-SOURCE.txt`
   - `Tools/FFmpeg-NOTICE.txt`
   - `ThirdParty/FFmpeg/GPL-3.0.txt`
2. Publish the Corresponding Source for the exact FFmpeg binary next to the mod
   download. The source offer must cover FFmpeg and the linked GPL/LGPL
   libraries in the static Windows build, plus the build scripts/configuration
   needed to rebuild the binary.
3. Do not add any EULA or download terms that prohibit reverse engineering,
   modification, or redistribution of FFmpeg.
4. Do not rename `ffmpeg.exe` to hide that it is FFmpeg.
5. Mention in the mod release notes that FFmpeg is bundled as a separate GPLv3
   command-line executable.

The mod invokes FFmpeg as an external process. FFmpeg is not owned by the
ADOFAI.EditorTweaks author.

Useful links:

- FFmpeg project: https://ffmpeg.org/
- FFmpeg legal page: https://ffmpeg.org/legal.html
- gyan.dev FFmpeg builds: https://www.gyan.dev/ffmpeg/builds/
- GPLv3 text: https://www.gnu.org/licenses/gpl-3.0.html
