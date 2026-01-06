# SansMus - Keyboard-Driven Mouse Control

First, this is heavily inspired by mousemaster (also found on GitHub, which has a lot more features, and is much more complete and polished), and I am grateful for the work that has been done there.

This is a desktop application that provides keyboard-driven mouse control using a grid overlay system and continuous keyboard movement, similar to mousemaster. Control your mouse cursor entirely with your keyboard - no mouse required!

It is intentionally fairly limited in features, as this was developed for personal use, and I use it together with a QMK/VIAL keyboard, which has built in functionality for mouse clicks and scrolling, among other things. Some day, I might expand this application to also include these features.

I also intentionally bind the hotkeys to un-used function keys, F13 to F24, to avoid conflicts with other applications.

## Disclaimer

This application is entirely vibe coded with Cursor AI. I don't understand much of the code, so..

## Overview

SansMus enables you to control your mouse cursor using only keyboard input. It provides two main modes:
1. **Grid Warp Mode**: Press a hotkey to display a full-screen grid overlay with customizable cell labels. Type two characters to instantly teleport your cursor to that cell's center.
2. **Keyboard Movement Mode**: Hold directional keys to continuously move the cursor, with speed modifier keys for precision or fast movement.


## Features

### Grid Warp Mode
- **Configurable Grid Overlay**: Full-screen grid with customizable rows/columns per monitor
- **Custom Cell Shortcuts**: Define your own two-character labels (supports letters, numbers, and special characters like comma, period, and Danish letters å/æ/ø)
- **Global Hotkey**: Configurable hotkey (default: F13) to activate the grid overlay from anywhere
- **Hint Mode**: After typing the first character, only matching cells are shown with just the second character displayed
- **Multi-Monitor Support**: Different grid configurations for each monitor
- **Transparent Overlay**: Fully transparent background with visible grid lines and text (with black outline for visibility)
- **Fast Cursor Teleportation**: Instantly move cursor to any cell center
- **Config Reload**: Reload configuration without restarting the application

### Keyboard Movement Mode
- **Directional Movement**: Hold keys to continuously move cursor (Up/Down/Left/Right)
- **Speed Modifiers**: Hold modifier keys to temporarily change movement speed
- **Smooth Movement**: Fractional pixel accumulation for smooth cursor movement at any speed
- **Diagonal Movement**: Move diagonally by holding multiple directional keys simultaneously
- **Configurable Speeds**: Default speed and up to 3 speed presets, all configurable

### Configuration
- **JSON Configuration**: All settings in `config.json` 
- **Per-Monitor Settings**: Different grid sizes and cell shortcuts for each monitor
- **Duplicate Detection**: Automatically detects and fixes duplicate cell shortcuts with warnings
- **Hotkey Customization**: Configure any function key or standard key as hotkeys

## Technology Stack

- **.NET 8.0** (Windows Forms application)
- **Windows Forms** - UI and mouse control
- **Windows API (P/Invoke)** - Global keyboard hook for system-wide hotkey detection
- **C#** - Primary programming language

## Prerequisites

- Windows 10/11
- .NET 8.0 SDK or later
- Visual Studio 2022 or VS Code (or any .NET IDE)

## Installation

1. **Clone or download this repository**

2. **Restore NuGet packages:**
   ```bash
   dotnet restore
   ```

3. **Build the project:**
   ```bash
   dotnet build
   ```

I should build a release version exe file eventually, when I figure out how. Maybe there is a hint a few lines down. I should look into this.

## Usage

1. **Run the application:**
   ```bash
   dotnet run
   ```
   
   Or build and run the executable:
   ```bash
   dotnet build -c Release
   .\bin\Release\net8.0-windows\SansMus.exe
   ```

2. **Use the application:**

   **Grid Warp Mode:**
   - Press the configured hotkey (default: **F13**) to show the grid overlay
   - Type the **first character** of a cell label (e.g., 'q', ',', 'å')
   - The grid filters to show only cells starting with that character (hint mode)
   - Only the **second character** is displayed in hint mode for clarity
   - Type the **second character** to select the cell
   - Cursor instantly moves to the center of the selected cell
   - Press **ESC**, **Backspace**, or the **toggle hotkey** to close the overlay
   - Press **Alt+F4** as a fallback to close if stuck

   **Keyboard Movement Mode:**
   - Hold **F14** (Move Up), **F15** (Move Down), **F16** (Move Left), **F17** (Move Right) to move cursor
   - Hold **F18-F20** to temporarily change movement speed while moving
   - Release keys to stop movement
   - Multiple directional keys can be held for diagonal movement

## How It Works

### Grid Warp Mode
1. **Global Keyboard Hook**: Captures hotkey presses system-wide (even when app not focused)
2. **Grid Overlay**: Displays a transparent full-screen grid with labeled cells using layered windows
3. **Custom Cell Mapping**: Each cell has a configurable two-character label (can be letters, numbers, or special characters)
4. **Hint Mode**: After first character, filters grid to show only matching cells with just the second character displayed
5. **Cursor Teleportation**: Calculates cell center and teleports cursor using Windows API
6. **Multi-Monitor**: Detects which monitor the cursor is on and uses the appropriate configuration

### Keyboard Movement Mode
1. **Key State Tracking**: Tracks which directional and speed modifier keys are held
2. **Movement Timer**: Uses Windows Forms Timer (~60fps) to continuously move cursor
3. **Speed Calculation**: Uses default speed or highest speed modifier if any are held
4. **Fractional Accumulation**: Accumulates fractional pixels for smooth movement at any speed
5. **Vector Normalization**: Normalizes diagonal movement to maintain consistent speed

## Configuration

All settings are in `config.json`:

### WarpGrid Section
- **Hotkey**: Hotkey to show grid overlay (e.g., "F13")
- **GridOpacity**: Opacity of grid lines and text (0.0-1.0, default: 1.0)
- **Monitors**: Array of monitor configurations:
  - **NumOfRows**: Number of grid rows for this monitor
  - **NumOfColumns**: Number of grid columns for this monitor
  - **CellShortcuts**: Array of two-character labels (row by row, left to right)

### MouseMovement Section
- **DefaultSpeedInPixelsPerSecond**: Default cursor movement speed
- **MoveUp**, **MoveDown**, **MoveLeft**, **MoveRight**: Hotkeys for directional movement
- **Speed0**, **Speed1**, **Speed2**: Speed presets with:
  - **SpeedInPixelsPerSecond**: Speed value
  - **HotKey**: Hotkey to activate this speed while held

## Grid Specifications

- **Grid Size**: Configurable per monitor (defined in `config.json`)
- **Cell Labels**: Custom two-character labels (letters, numbers, special characters)
- **Cell Size**: Adapts to screen resolution with edge cells slightly larger to fill screen
- **Text Display**: White text with black outline for visibility on any background
- **Transparency**: Fully transparent background, only grid lines and labels are visible

## Current Implementation Status

The current implementation provides:
- ✅ Global keyboard hook (configurable hotkey, supports F1-F24 and standard keys)
- ✅ Full-screen grid overlay with per-pixel alpha transparency
- ✅ Configurable grid size per monitor
- ✅ Custom cell shortcuts (two-character labels with special character support)
- ✅ Hint mode filtering (shows only second character when first is typed)
- ✅ Cursor teleportation to cell center
- ✅ Multi-monitor support with per-monitor configurations
- ✅ Configurable grid opacity
- ✅ Text with black outline for visibility
- ✅ Config reload button (no restart required)
- ✅ Duplicate shortcut detection and automatic fixing
- ✅ Keyboard-driven continuous mouse movement
- ✅ Speed modifier keys for movement
- ✅ Diagonal movement support
- ✅ Focus loss handling (overlay closes if focus is lost)
- ✅ Escape/Backspace/Alt+F4 to close overlay

## Project Structure

```
SansMus/
├── Program.cs          # Main application code (config loading, keyboard hook, mouse movement)
├── GridOverlayForm.cs  # Grid overlay window (rendering, cell selection)
├── KeyboardHook.cs     # Global keyboard hook implementation (KeyDown/KeyUp events)
├── config.json         # Configuration file (grid settings, mouse movement settings)
├── SansMus.csproj      # Project file
└── README.md          # This file
```

## Troubleshooting

### Grid overlay doesn't appear
- Ensure the application is running (check system tray or taskbar)
- Try clicking the "Test Overlay" button in the main window
- Check if another application is blocking the overlay

### Hotkey doesn't work
- Check `config.json` to see what hotkey is configured (default: F13)
- Ensure the application window is open (it can be minimized)
- Try restarting the application
- Check if another application is using the same hotkey globally
- Use the "Reload Config" button to reload configuration without restarting

### Mouse movement doesn't work
- Check `config.json` MouseMovement section is configured correctly
- Verify directional keys (MoveUp, MoveDown, MoveLeft, MoveRight) are set
- Ensure keys are not conflicting with other applications
- Try reloading the config using the "Reload Config" button

### Grid overlay gets stuck
- Press **Alt+F4** to force close the overlay
- Click on the main application window to close the overlay
- The overlay automatically closes if it loses focus
- Open the Task Manager, then the Details tab, and kill SansMus.exe

### Build errors
- Ensure you have .NET 8.0 SDK installed: `dotnet --version`
- Try cleaning and rebuilding: `dotnet clean && dotnet build`

## License

I don't really know, ask me..?
