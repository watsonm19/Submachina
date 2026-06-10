# Core/UI

Generic, reusable UI display components.

## Value → Text display

Two components that work together (or independently) to show dynamic values in TextMeshPro text:

- **ValueTextDisplay** — sits on a GameObject with a `TMP_Text`. A flexible "text receiver": anything sent to `SetText/SetFloat/SetInt/SetBool/SetValue` is run through a configurable composite format string (e.g. `"{0:0.0} m"`) and written to the text. Wireable from any UnityEvent source — Unity Atoms event listeners, Feel feedbacks, buttons, or code. Has an Odin test button to preview formatting in edit mode.

- **AtomVariableTextBinder** — requires a ValueTextDisplay on the same GameObject. Reference *any* Unity Atoms variable (`FloatVariable`, `IntVariable`, `StringVariable`, ...) via the untyped `AtomBaseVariable` base; it displays the current value on `OnEnable` (so text is correct before the first change) and live-updates on the variable's `Changed` event. It grabs the typed `Changed` event via reflection through the base reference and registers on the untyped `AtomEventBase` notification, which typed `Raise(T)` calls always fire — this is what lets one component bind every variable type. Value formatting is configured on the ValueTextDisplay, keeping subscription and presentation concerns separate.

### Typical setups
- **Simple HUD readout** (depth, resources, hull integrity): TMP text object + ValueTextDisplay + AtomVariableTextBinder pointing at the variable. No event wiring needed.
- **Event-driven text** (messages, computed strings): just ValueTextDisplay, with a Unity Atoms `*EventListener` or any UnityEvent wired to the appropriate `Set*` method.
