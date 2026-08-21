# Play Mode Changes Saver

**Play Mode Changes Saver** is a powerful editor toolset designed to capture, review, and apply modifications made during Play Mode. Inspired by Unity’s Prefab Overrides system, it ensures no iteration is lost.

> **Need help?** For a visual guide, please check out the **[\[demo video\]](https://assetstore.unity.com/packages/slug/354984)** on the Unity Asset Store page.


## Key Features
* **Automatic Tracking:** Instantly snapshots GameObjects, Components, and names when entering Play Mode.
* **Inspector Integration:** A dedicated "Play Mode Overrides" panel appears on any GameObject with detected changes.
* **Granular Control:** Side-by-side property comparison. Select individual properties, components, or entire objects to keep.
* **Overrides Browser:** A central window to manage all pending changes across all open scenes (`Tools > Play Mode Overrides Browser`).
* **Robust Mapping:** Uses hybrid path and GUID mapping to track objects even after hierarchy or name changes.
* **Persistent Storage:** All changes are safely stored in ScriptableObjects until applied or reverted.

## Supported Changes
* **Transform & RectTransform:** Position, rotation, scale, anchors, pivots, and offsets.
* **Component Properties:** All serializable fields, including custom MonoBehaviours.
* **Renderer Materials:** Material assignments on Renderer components.
* **GameObject Names:** Full tracking of name changes during runtime.

---


## How to Use

### A. Inspector Integration (Per Object)
1. **Enter Play Mode:** The tool automatically snapshots your current scene state upon entry.
2. **Modify Assets:** Adjust objects, components, or values as you normally would during runtime.
3. **Review Changes:** Once a change is detected, the **Play Mode Overrides** button in the Inspector becomes active. Click it to see a list of modified components.
   - **Quick Actions:** Use the three main buttons—*Revert to Original*, *Revert to Saved*, or *Apply All*—for fast management.
   - **Detailed Comparison:** Click on any component in the list to open a **Comparison Popup**. This side-by-side view shows the original (read-only) values on the left and your current editable values on the right. This allows you to apply or revert changes at a granular, per-component level.
4. **Exit Play Mode:** Your modifications are safely stored in the background.

### B. Post-Play Mode Workflow & Dialogs
After exiting Play Mode, the tool guides you through the review process via dialog popups:
* **Apply Confirmation:** You will be prompted to confirm if you want to finalize the captured changes.
* **Multi-Scene Navigation:** If changes were made across multiple scenes, the tool will ask to switch scenes automatically so you can review and apply them. Simply follow the prompts to iterate through each affected scene.
* **Scene Return:** Once all changes are processed, the tool offers to return you to your original starting scene.

### C. Overrides Browser
Open the browser via `Tools > Play Mode Overrides Browser` to maintain a bird's-eye view during both Edit and Play Mode:
* **Central Overview:** Lists every GameObject in the scene that has applied or pending overrides.
* **Persistent Management:** Use this window at any time to revert changes or make further adjustments, ensuring you never lose track of your iterations.


---

## Technical Specifications
* **Editor-Only:** Zero runtime overhead; works exclusively in the Unity Editor.
* **Undo/Redo Integration:** Full support for Unity’s Undo system after applying changes.
* **Multi-Scene Support:** Handles changes across multiple additively loaded scenes.
* **No Dependencies:** 100% self-contained, no third-party packages required.


## Support & Contribution
If you encounter any issues, please do not hesitate to contact me!

* **GitHub:** [\[github.com/062Leo\]](https://github.com/062Leo)

*  [![Contact Me](https://img.shields.io/badge/Contact-Me-blue?style=for-the-badge&logo=maildotru)](https://tally.so/r/KYx5ak)


**Note:** This tool is provided for free. If you find it helpful, all I ask is that you leave a **positive rating and a review** on the Unity Asset Store. Your feedback helps a lot!