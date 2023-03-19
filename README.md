# This is a fork of [CadeEvs/FrostyToolsuite](https://github.com/CadeEvs/FrostyToolsuite)

# New
One can now export all meshes and textures from certain map with LevelEditor plugin.
Since I am not very familiar with C# and Windows applications development, I simply inserted required code to **export all assets automatically when a LevelData file is opened**.
Export path is controlled by *export_path* variable in *Editors/LevelEditor.cs* (*C:* by default). 

Export creates two json files: *instances.json* and *materials.json*. These contain information about textures a certain material uses, materials that should be applied to a certain 3D model, etc.

To build the project I recommend [these](https://docs.google.com/document/d/1fVFt37MRPsl22kQrO5cX-N59mt-FcVoFCfNp0FcSLmE/edit#heading=h.esmoanldb5rz) notes (taken from Frosty Toolsuite Discord server).

## Unreal Engine
A python script to import assets into UE project is included. Use the corresponding UE plugin to run it.

![](img/photo_1.png?raw=true "parially textured Death Star")

![](img/photo_2.png?raw=true "untextured Bespin")

# FrostyToolsuite
The most advanced modding platform for games running on DICE's Frostbite game engine.

## Setup

1. Download Git https://git-scm.com/download/win.
2. Create an empty folder, go inside it, right click an empty space and hit "Git Bash Here". That should open up a command prompt.
3. Press the green "Code" button in the repository and copy the text under "HTTPS".
4. Type out ``git clone -b <branch_name> <HTTPS code>`` in the command prompt and hit enter. This should clone the project files into the folder.
5. Open the solution (found under FrostyEditor) with **Visual Studio 2019**, and make sure the project is set to ``DeveloperDebug`` and ``x64``. Close out of retarget window if prompted.
6. Only build the projects themselves, never the solution.

## License
The Content, Name, Code, and all assets are licensed under a Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International License.
