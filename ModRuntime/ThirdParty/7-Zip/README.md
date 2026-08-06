# 7-Zip

Archive compression and extraction use the x64 `7z.dll` distributed by the
SharpSevenZip 2.0.109 NuGet package.

- Project: https://www.7-zip.org/
- Source: https://github.com/ip7z/7zip
- License details: `License.txt`

The DLL is loaded dynamically from `ThirdParty/7-Zip/x64/7z.dll`. Users do not
need to install 7-Zip separately. Archive extraction is detected by file
signature and supports the common ZIP, RAR, 7z, TAR, GZip, BZip2, XZ and CAB
formats. Mod exports remain standard ZIP archives for game compatibility.
