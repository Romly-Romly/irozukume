# Changelog

> 🌐 **[日本語 →](CHANGELOG.ja.md)**

## 0.6.0-alpha — 2026/06/29

### Added

- Added a color preview window. It reflects the color you're editing, alpha included, so you can place it next to other on-screen elements to compare. Grab it anywhere to move it, and press F to toggle fullscreen.
- Added palette extraction from an image. Load an image and build a palette with various algorithms, then use those colors or save them to your favorites. Algorithms include k-means and octree, plus simulated annealing, which can also surface understated accent colors.

### Changed

- Drew custom icons for trash, web-safe color, contrast check, lens effect, and more.
- Windows now restore to the monitor where they were last shown on multi-monitor setups.
- Title bar caption button colors now follow the system's custom color scheme and High Contrast settings.
- Other minor changes.

### Fixed

- Fixed the tab bar becoming empty at startup when the default-selected tab was set to be hidden.
- Other minor fixes.



## 0.3.0-alpha — 2026/06/22

### Added

- Each tab can now be shown or hidden, so you can tuck away tabs you don't need. Available from the tab bar's right-click menu.
- Added a 2D-plane + vertical-bar presentation to the RGB and CMYK tabs.
- Added settings actions such as delete and reset.

### Changed

- Reorganized the settings page with a sidebar and revised its categories.
- Moved the color picker lens size into the regular settings.

### Fixed
- Fixed the initial window opening smaller than its minimum size on high-DPI displays (125% and above).
- Contrast-check mode no longer defaults to on for new users.
- Corrupt settings no longer wipe everything; only the damaged part is discarded while readable settings are kept.

Other minor fixes.



## 0.2.0-alpha — 2026/06/20

### Added
- Layout (slider presentation) selection for each color-space tab (HSV, HSL, HWB, LCH, Lab, YUV).
- Option to show the full gamut at maximum size for gamut-limited color models such as LCH, Lab, and YUV.
- Change magnification with the mouse wheel while using the color picker.
- Area-average sampling (capture region) in the color picker, adjustable with Ctrl + mouse wheel.
- Sampled value, coordinates, and other info shown in the color picker.
- Move the cursor one pixel at a time with the arrow keys while using the color picker.
- "Restart as administrator" added to the File menu.

### Changed
- Lab and YUV lightness/luma controls unified to the same vertical-rail + bottom horizontal-slider layout as HSV/LCH.
- Delete actions now use the system danger color so destructive operations are recognizable.
- Replaced the display-language setting icon.



## 0.1.0-alpha — 2026/06/18

- Initial alpha release.
