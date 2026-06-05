### --- Overview ---
- This is a Unity 6.3 project.
- Our custom c# code is found in Assets/Scripts
- The project is a 2D game that is still in early development about descending underwater while gathering resources, encountering enemies, and building a modular submarine.
- Look for a context.md in some folder roots for more information about that specific system and interactions it has with other systems.
- Create or add to context.md files as needed to document the systems and interactions at a high level to help understand the project.

### --- Code Intelligence ---
**IMPORTANT: Always prefer `rider-code-intel` MCP tools over Grep, Glob, Bash directory listings, and file reading for C# codebase exploration. The routing rules below are mandatory, not suggestions.**

#### Exploration routing (use INSTEAD OF Grep/Glob/Bash):
- **"Where is X defined?"** → `go_to_definition` (not Grep for class/method name)
- **"What uses X?"** → `find_usages` (not Grep for the symbol string)
- **"What implements this interface/base class?"** → `find_implementations` (not Grep for the type name)
- **"Find a class/method/field by name"** → `search_symbol` (not Glob + Grep combo)
- **"What's in this file?"** → `list_symbols_in_file` (not Read for a structural overview)
- **"What does this method do?"** → `flow` for control-flow summary (not Read + manual analysis)
- **"What's the project structure?"** → `get_solution_structure` or `browse_namespace` (not recursive directory listing)
- **"What are the errors after my edit?"** → `get_file_errors` (not waiting for user to report)

#### After editing C# files, always:
- Run `fix_usings` on edited files to resolve missing using directives
- Run `get_file_errors` to verify the edit compiles
- Run `format_file` if formatting may have drifted

#### When Grep/Glob ARE still appropriate:
- Searching non-C# files (YAML, JSON, XML, shaders, CLAUDE.md, context.md, etc.)
- Searching for string literals, comments, or patterns that aren't symbol names
- Finding files by extension or naming pattern (e.g. all `*.asmdef` files)

#### Batch calls:
- Most rider-code-intel tools accept arrays (`symbols`, `filePaths`, `queries`) — use batch params to check multiple things in a single call instead of sequential calls.

### --- Coding Style Guidelines --- 
- For simple if statements that return on some condition or similar, I prefer a one line format: `if (Time.time - _lastTriggerTime < cooldownSeconds) return;`
- Provide high level code commenting when creating new code to help the developer understand the basic flow.
- Code lines should be logically grouped and organized into small stanza blocks, and every stanza should have a comment explaining its purpose. The code should have a nice, readable comment rhythm without being overly verbose.
- Comments should provide examples for complex logic or calculations to help clarify intent.
- Add multiline/block comments (/** style) to all methods unless they are very trivial that explain the purpose of the function.

### --- Tooling ---
- I'm using Odin Inspector. When it is sensible for important or complex components, I like to have manual controls to invoke script elements from the editor for testing and create nicer organizations using Odin for elements and properties for a clean editing experience.
- i'M USING More Mountains "Feel" asset for game juice and effects
- I'm using DOTeen Pro for some tweeing and animations, but for simple things it's often better to code them without it than to introduce a dependency, but where DOTween may provide substantial benefit, particularly for tweaking and experimentation in the editor, you can use it.
- I'm using TextMeshPro text rendering.