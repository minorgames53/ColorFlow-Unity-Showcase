Do not edit .unity scene files manually unless explicitly requested.
Do not edit .prefab files manually unless explicitly requested.
Do not edit .asset ScriptableObject files manually unless explicitly requested.
Do not modify ProjectSettings.
Do not install, remove, or update packages without explicit approval.
Do not delete, rename, or move existing files without explicit approval.
The project uses Unity Hot Reload; don’t force manual compile commands for normal method edits, but note that new classes/files, asmdef changes, define symbols, or other unsupported edits may require a Unity recompile, and final Unity Console validation is handled by the developer.Explain required Inspector assignments after completing scripts.
After each task, report affected files/folders, functional changes, and the specific tests that should be run.


## Unity MCP usage

- Use Unity MCP when the task requires information from the running Unity Editor.
- Use Unity MCP for scene hierarchy, GameObjects, components, Inspector values,
  prefabs, materials, Console messages, Play Mode, tests, and scene changes.
- For pure C# code changes that do not require live Unity state, prefer normal
  file editing and do not invoke MCP unnecessarily.
- Inspect the current scene and Console before making Editor changes.
- Do not manually edit `.unity`, `.prefab`, or `.asset` YAML files.
- Ask for confirmation before deleting GameObjects, assets, prefabs, or scenes.
- After making changes, check the Unity Console for errors.
- Save the scene only when the requested work is complete and there are no errors.