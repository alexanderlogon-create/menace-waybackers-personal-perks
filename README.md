# Waybackers Personal Perks — source and rebuild

Optional MENACE MelonLoader add-on: 22 personal starting perks and Clover's Dublin Rooftops tier-3 choice, with 23 embroidered badges. Runtime 0.3.0; artwork revision badges-v2.

Nexus page: https://www.nexusmods.com/menace/mods/262?tab=files . Optional file: Waybackers - Personal Perks, file ID 1137. It is currently awaiting Nexus quarantine review; see that page for its availability.

## Build from source

1. Install MENACE and MelonLoader. Launch once so interop assemblies exist in the game's MelonLoader/Il2CppAssemblies directory. Install the Waybackers character pack and its prerequisites before gameplay.
2. Install the .NET SDK. The mod targets net6.0 and the included formula tests target .NET 10.
3. In this repository, run `dotnet build src -c Release -p:GameDir="D:\Steam\steamapps\common\Menace"` and `dotnet run --project tests -c Release`, substituting your game installation path.
4. Output is `src/bin/Release/net6.0/MenaceSnipersPromise.dll`. The game and loader assemblies are read from your installation and are not redistributed here.
5. With MENACE closed, run `./install.ps1 -GameDir 'D:\Steam\steamapps\common\Menace'`. It copies the compiled DLL and bundled assets and backs up an earlier installation. Alternatively, copy the DLL into Mods and copy the 23 badge images plus waybackers-perks.json into Mods/MenaceSnipersPromise, preserving its waybackers subdirectory.

build.ps1 combines compilation and formula tests. verify.ps1 installs the mod and launches a muted isolated test process inside MENACE; it does not load or save player campaigns. uninstall.ps1 archives the installed mod files.

## Scope and validation

PERKS.md lists every mechanic and quotation source. gallery.html displays the badges. verification.md and Verification contain the checks for the unchanged runtime: 35 formula tests, 24 native Clover scenarios, 104 additional effect checks and native save/load round trips. Full missions and all mod combinations have not been tested.

The archive contains only this add-on, not the Waybackers character pack, game binaries, MelonLoader or other installers. Art is AI-generated, including the corrected Luo, Lynx, Null and Rook badges. Exact generation and edit prompts are included in assets. Filepaths in the image-source inventory are portable.
