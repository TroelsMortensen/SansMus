# SansMus - Keyboard-Driven Mouse Control

A desktop application that provides keyboard-driven mouse control using a grid overlay system, similar to mousemaster. Control your mouse cursor entirely with your keyboard - no mouse required!

## Overview

SansMus enables you to control your mouse cursor using only keyboard input. Press a hotkey (Space) to display a full-screen grid overlay with two-letter labels. Type two letters to instantly teleport your cursor to that cell's center. Perfect for accessibility, efficiency, or when you prefer keyboard navigation.

## Features

- **Grid Overlay System**: Full-screen grid (10 rows × 24 columns) with two-letter cell labels
- **Global Hotkey**: Press Space from anywhere to activate the grid overlay
- **Hint Mode**: After typing the first letter, only matching cells are shown
- **Keyboard-Only Control**: Complete mouse control without touching your mouse
- **Semi-Transparent Overlay**: See your screen through the grid overlay
- **Fast Cursor Teleportation**: Instantly move cursor to any cell center

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
   - Press **SPACE** (global hotkey) to show the grid overlay
   - Type the **first letter** of a cell label (e.g., 'A')
   - The grid filters to show only cells starting with that letter (hint mode)
   - Type the **second letter** (e.g., 'B') to select cell "AB"
   - Cursor instantly moves to the center of the selected cell
   - Press **ESC** to cancel and close the overlay

## How It Works

1. **Global Keyboard Hook**: Captures Space key presses system-wide (even when app not focused)
2. **Grid Overlay**: Displays a semi-transparent full-screen grid with labeled cells
3. **Letter Mapping**: Each cell has a unique two-letter label (AA, AB, AC, etc.)
4. **Hint Mode**: After first letter, filters grid to show only matching cells
5. **Cursor Movement**: Calculates cell center and teleports cursor using Windows API

## Grid Specifications

- **Grid Size**: 10 rows × 24 columns = 240 cells
- **Cell Labels**: Two uppercase letters (AA through ZZ)
- **Cell Size**: Adapts to screen resolution (~216px × 216px at 5120×2160)
- **Letter Grouping**: Cells with same first letter are grouped spatially

## Current Implementation Status

The current implementation provides:
- ✅ Global keyboard hook (Space key)
- ✅ Full-screen grid overlay
- ✅ Two-letter cell labeling system
- ✅ Hint mode filtering
- ✅ Cursor teleportation to cell center
- ✅ Semi-transparent overlay display

## Future Enhancements

- Support for F13-F24 function keys as hotkeys
- Additional mouse movement modes (continuous movement, scrolling, etc.)
- Configurable grid size
- Custom letter mapping
- Keyboard shortcut customization
- Multiple screen support
- Click actions (left/right/middle click via keyboard)

## Project Structure

```
SansMus/
├── Program.cs          # Main application code
├── GridOverlayForm.cs  # Grid overlay window
├── KeyboardHook.cs     # Global keyboard hook implementation
├── SansMus.csproj      # Project file
└── README.md          # This file
```

## Troubleshooting

### Grid overlay doesn't appear
- Ensure the application is running (check system tray or taskbar)
- Try clicking the "Test Overlay" button in the main window
- Check if another application is blocking the overlay

### Space key doesn't work
- Ensure the application window is open (it can be minimized)
- Try restarting the application
- Check if another application is using the Space key globally

### Build errors
- Ensure you have .NET 8.0 SDK installed: `dotnet --version`
- Try cleaning and rebuilding: `dotnet clean && dotnet build`

## License

This project is provided as-is for educational purposes.
