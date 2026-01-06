using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace SansMus
{
    public partial class MainForm : Form
    {
        private GlobalKeyboardHook? keyboardHook;
        private GridOverlayForm? overlayForm;
        private OrbitalVisualizationForm? orbitalVisualizationForm = null;
        private Keys? configuredHotkey = null; // null means config invalid
        private string? configError = null;
        private Label? infoLabel;
        private Button? testButton;
        private Button? reloadButton;
        private CheckBox? orbitalCheckBox;
        private List<MonitorConfig>? monitorConfigs = null;
        private double gridOpacity = 1.0; // Default: fully opaque grid
        private string? duplicateWarning = null;
        
        // Mouse movement configuration
        private double defaultSpeedPixelsPerSecond = 10.0;
        private Keys? moveUpKey = null;
        private Keys? moveDownKey = null;
        private Keys? moveLeftKey = null;
        private Keys? moveRightKey = null;
        private Dictionary<Keys, double> speedModifiers = new Dictionary<Keys, double>();
        private HashSet<Keys> heldDirectionKeys = new HashSet<Keys>();
        private HashSet<Keys> heldSpeedModifierKeys = new HashSet<Keys>();
        private System.Windows.Forms.Timer? movementTimer = null;
        private double accumulatedMoveX = 0.0;
        private double accumulatedMoveY = 0.0;
        
        // Movement acceleration
        private DateTime? movementStartTime = null; // Timestamp when current movement started (null when not moving)
        private double accelerationDurationMs = 100.0; // Time to reach full speed (100ms) - loaded from config
        private double initialSpeedFactor = 0.025; // Starting speed as fraction of target speed (2.5%) - loaded from config
        
        // Orbital mouse configuration
        private const double ROTATION_POINT_DISTANCE = 25.0; // Distance behind cursor for rotation point (in pixels)
        private bool useOrbitalMouse = false;
        private double currentHeading = 0.0; // Heading angle in degrees (0 = up/north, 90 = right/east)
        private Keys? moveForwardKey = null;
        private Keys? moveBackwardKey = null;
        private Keys? turnLeftKey = null;
        private Keys? turnRightKey = null;
        private HashSet<Keys> heldTurnKeys = new HashSet<Keys>();
        
        public MainForm()
        {
            try
            {
                LoadConfig();
            }
            catch (Exception ex)
            {
                configError = ex.Message;
                configuredHotkey = null;
            }
            
            InitializeComponent();
            
            // Create orbital visualization form if orbital mouse is enabled
            if (useOrbitalMouse)
            {
                orbitalVisualizationForm = new OrbitalVisualizationForm();
                orbitalVisualizationForm.Show();
            }
            
            if (configuredHotkey == null)
            {
                // Show error message in UI
                if (infoLabel != null)
                {
                    infoLabel.Text = $"Configuration Error:\n{configError}\n\nPlease fix config.json and restart the application.";
                    infoLabel.ForeColor = System.Drawing.Color.Red;
                }
                if (testButton != null)
                {
                    testButton.Enabled = false;
                }
                // Don't initialize keyboard hook
            }
            else
            {
                InitializeKeyboardHook();
            }
        }
        
        private void InitializeComponent()
        {
            this.Text = "SansMus - Keyboard Mouse Control";
            this.Size = new System.Drawing.Size(300, 250);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormClosing += MainForm_FormClosing;
            this.Activated += MainForm_Activated;
            
            string hotkeyText = configuredHotkey?.ToString() ?? "SPACE";
            string labelText = $"Press {hotkeyText} to show grid overlay.\nPress ESC in overlay to close.";
            
            // Append duplicate warning if present
            if (duplicateWarning != null)
            {
                labelText += "\n\n" + duplicateWarning;
            }
            
            infoLabel = new Label
            {
                Text = labelText,
                Dock = DockStyle.Top,
                Height = 100,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                AutoSize = false
            };
            
            // Set warning color if duplicates were found
            if (duplicateWarning != null && configError == null)
            {
                infoLabel.ForeColor = System.Drawing.Color.Orange;
            }
            
            this.Controls.Add(infoLabel);
            
            orbitalCheckBox = new CheckBox
            {
                Text = "Use Orbital Mouse Controls",
                Dock = DockStyle.Top,
                Height = 30,
                Checked = useOrbitalMouse
            };
            orbitalCheckBox.CheckedChanged += OrbitalCheckBox_CheckedChanged;
            this.Controls.Add(orbitalCheckBox);
            
            reloadButton = new Button
            {
                Text = "Reload Config",
                Dock = DockStyle.Bottom,
                Height = 30,
                Enabled = true
            };
            reloadButton.Click += ReloadButton_Click;
            this.Controls.Add(reloadButton);
            
            testButton = new Button
            {
                Text = $"Test Overlay (Click or Press {hotkeyText})",
                Dock = DockStyle.Fill,
                Height = 50,
                Enabled = configuredHotkey != null
            };
            testButton.Click += (s, e) => ShowGridOverlay();
            this.Controls.Add(testButton);
        }
        
        private void LoadConfig()
        {
            string configPath = "config.json";
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException("Configuration file 'config.json' not found.");
            }
            
            string json = File.ReadAllText(configPath);
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.TryGetProperty("WarpGrid", out var warpGrid))
                {
                    throw new InvalidOperationException("Configuration file missing 'WarpGrid' section.");
                }
                
                if (!warpGrid.TryGetProperty("Hotkey", out var hotkeyProp))
                {
                    throw new InvalidOperationException("Configuration file missing 'Hotkey' in WarpGrid section.");
                }
                
                string? hotkeyStr = hotkeyProp.GetString();
                if (string.IsNullOrWhiteSpace(hotkeyStr))
                {
                    throw new InvalidOperationException("Hotkey value is empty or invalid.");
                }
                
                configuredHotkey = ParseHotkey(hotkeyStr);
                if (configuredHotkey == null)
                {
                    throw new InvalidOperationException($"Invalid hotkey value: '{hotkeyStr}'. Supported values: Space, F1-F24, Enter, Escape, etc.");
                }
                
                // Load opacity settings
                if (warpGrid.TryGetProperty("GridOpacity", out var gridOpacityProp))
                {
                    if (gridOpacityProp.ValueKind == JsonValueKind.Number)
                    {
                        gridOpacity = gridOpacityProp.GetDouble();
                        if (gridOpacity < 0.0 || gridOpacity > 1.0)
                        {
                            throw new InvalidOperationException($"GridOpacity must be between 0.0 and 1.0, got {gridOpacity}.");
                        }
                    }
                }
                
                // Load monitor configurations
                if (!warpGrid.TryGetProperty("Monitors", out var monitorsProp))
                {
                    throw new InvalidOperationException("Configuration file missing 'Monitors' array in WarpGrid section.");
                }
                
                if (monitorsProp.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException("'Monitors' must be an array.");
                }
                
                if (monitorsProp.GetArrayLength() == 0)
                {
                    throw new InvalidOperationException("'Monitors' array must contain at least one monitor configuration.");
                }
                
                monitorConfigs = new List<MonitorConfig>();
                foreach (var monitorElement in monitorsProp.EnumerateArray())
                {
                    if (!monitorElement.TryGetProperty("NumOfRows", out var rowsProp))
                    {
                        throw new InvalidOperationException("Monitor configuration missing 'NumOfRows'.");
                    }
                    
                    if (!monitorElement.TryGetProperty("NumOfColumns", out var colsProp))
                    {
                        throw new InvalidOperationException("Monitor configuration missing 'NumOfColumns'.");
                    }
                    
                    int rows = rowsProp.GetInt32();
                    int cols = colsProp.GetInt32();
                    
                    if (rows <= 0)
                    {
                        throw new InvalidOperationException($"Monitor configuration has invalid 'NumOfRows': {rows}. Must be a positive integer.");
                    }
                    
                    if (cols <= 0)
                    {
                        throw new InvalidOperationException($"Monitor configuration has invalid 'NumOfColumns': {cols}. Must be a positive integer.");
                    }
                    
                    List<string>? cellShortcuts = null;
                    if (monitorElement.TryGetProperty("CellShortcuts", out var shortcutsProp) && shortcutsProp.ValueKind == JsonValueKind.Array)
                    {
                        cellShortcuts = new List<string>();
                        foreach (var shortcut in shortcutsProp.EnumerateArray())
                        {
                            if (shortcut.ValueKind == JsonValueKind.String)
                            {
                                cellShortcuts.Add(shortcut.GetString() ?? "");
                            }
                        }
                        
                        // Validate array length matches grid size
                        int expectedCount = rows * cols;
                        if (cellShortcuts.Count != expectedCount)
                        {
                            // If length doesn't match, set to null to use fallback generation
                            cellShortcuts = null;
                        }
                        else
                        {
                            // Validate that all shortcuts have the same length (1-3 characters)
                            int? shortcutLength = null;
                            bool allSameLength = true;
                            foreach (string shortcut in cellShortcuts)
                            {
                                if (shortcut.Length < 1 || shortcut.Length > 3)
                                {
                                    // Invalid length, use fallback
                                    allSameLength = false;
                                    break;
                                }
                                
                                if (shortcutLength == null)
                                {
                                    shortcutLength = shortcut.Length;
                                }
                                else if (shortcut.Length != shortcutLength.Value)
                                {
                                    // Different lengths found, use fallback
                                    allSameLength = false;
                                    break;
                                }
                            }
                            
                            if (!allSameLength || shortcutLength == null)
                            {
                                // Invalid shortcuts, use fallback generation
                                cellShortcuts = null;
                            }
                            else
                            {
                                // Validate and fix duplicates
                                cellShortcuts = ValidateAndFixDuplicates(cellShortcuts, rows, cols, monitorConfigs.Count, shortcutLength.Value);
                            }
                        }
                    }
                    
                    // If cellShortcuts is null, generate them automatically
                    if (cellShortcuts == null)
                    {
                        cellShortcuts = GenerateCellShortcuts(rows, cols);
                    }
                    
                    monitorConfigs.Add(new MonitorConfig
                    {
                        Rows = rows,
                        Columns = cols,
                        CellShortcuts = cellShortcuts
                    });
                }
                
                // Load mouse movement configuration (optional)
                if (doc.RootElement.TryGetProperty("MouseMovement", out var mouseMovement))
                {
                    // Parse control scheme (default: "Directional")
                    useOrbitalMouse = false;
                    if (mouseMovement.TryGetProperty("ControlScheme", out var controlSchemeProp))
                    {
                        string? controlSchemeStr = controlSchemeProp.GetString();
                        if (!string.IsNullOrWhiteSpace(controlSchemeStr) && controlSchemeStr.Equals("Orbital", StringComparison.OrdinalIgnoreCase))
                        {
                            useOrbitalMouse = true;
                        }
                    }
                    
                    // Parse default speed
                    if (mouseMovement.TryGetProperty("DefaultSpeedInPixelsPerSecond", out var defaultSpeedProp))
                    {
                        if (defaultSpeedProp.ValueKind == JsonValueKind.Number)
                        {
                            defaultSpeedPixelsPerSecond = defaultSpeedProp.GetDouble();
                            if (defaultSpeedPixelsPerSecond <= 0)
                            {
                                throw new InvalidOperationException($"DefaultSpeedInPixelsPerSecond must be greater than 0, got {defaultSpeedPixelsPerSecond}.");
                            }
                        }
                    }
                    
                    // Parse acceleration settings
                    if (mouseMovement.TryGetProperty("InitialSpeedFactor", out var initialSpeedFactorProp))
                    {
                        if (initialSpeedFactorProp.ValueKind == JsonValueKind.Number)
                        {
                            double factor = initialSpeedFactorProp.GetDouble();
                            if (factor > 0 && factor <= 1.0)
                            {
                                initialSpeedFactor = factor;
                            }
                            else
                            {
                                throw new InvalidOperationException($"InitialSpeedFactor must be greater than 0 and less than or equal to 1.0, got {factor}.");
                            }
                        }
                    }
                    
                    if (mouseMovement.TryGetProperty("AccelerationDurationMs", out var accelerationDurationProp))
                    {
                        if (accelerationDurationProp.ValueKind == JsonValueKind.Number)
                        {
                            double duration = accelerationDurationProp.GetDouble();
                            if (duration > 0)
                            {
                                accelerationDurationMs = duration;
                            }
                            else
                            {
                                throw new InvalidOperationException($"AccelerationDurationMs must be greater than 0, got {duration}.");
                            }
                        }
                    }
                    
                    // Parse directional keys
                    if (mouseMovement.TryGetProperty("MoveUp", out var moveUpProp))
                    {
                        string? moveUpStr = moveUpProp.GetString();
                        if (!string.IsNullOrWhiteSpace(moveUpStr))
                        {
                            moveUpKey = ParseHotkey(moveUpStr);
                        }
                    }
                    
                    if (mouseMovement.TryGetProperty("MoveDown", out var moveDownProp))
                    {
                        string? moveDownStr = moveDownProp.GetString();
                        if (!string.IsNullOrWhiteSpace(moveDownStr))
                        {
                            moveDownKey = ParseHotkey(moveDownStr);
                        }
                    }
                    
                    if (mouseMovement.TryGetProperty("MoveLeft", out var moveLeftProp))
                    {
                        string? moveLeftStr = moveLeftProp.GetString();
                        if (!string.IsNullOrWhiteSpace(moveLeftStr))
                        {
                            moveLeftKey = ParseHotkey(moveLeftStr);
                        }
                    }
                    
                    if (mouseMovement.TryGetProperty("MoveRight", out var moveRightProp))
                    {
                        string? moveRightStr = moveRightProp.GetString();
                        if (!string.IsNullOrWhiteSpace(moveRightStr))
                        {
                            moveRightKey = ParseHotkey(moveRightStr);
                        }
                    }
                    
                    // Parse speed modifiers
                    speedModifiers.Clear();
                    string[] speedKeys = { "Speed0", "Speed1", "Speed2" };
                    foreach (string speedKey in speedKeys)
                    {
                        if (mouseMovement.TryGetProperty(speedKey, out var speedProp) && speedProp.ValueKind == JsonValueKind.Object)
                        {
                            if (speedProp.TryGetProperty("HotKey", out var speedHotkeyProp))
                            {
                                string? speedHotkeyStr = speedHotkeyProp.GetString();
                                if (!string.IsNullOrWhiteSpace(speedHotkeyStr))
                                {
                                    Keys? speedKeyValue = ParseHotkey(speedHotkeyStr);
                                    if (speedKeyValue != null)
                                    {
                                        if (speedProp.TryGetProperty("SpeedInPixelsPerSecond", out var speedValueProp))
                                        {
                                            if (speedValueProp.ValueKind == JsonValueKind.Number)
                                            {
                                                double speed = speedValueProp.GetDouble();
                                                if (speed > 0)
                                                {
                                                    speedModifiers[speedKeyValue.Value] = speed;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                
                // Load orbital mouse movement configuration (optional, always load so checkbox toggle works)
                if (doc.RootElement.TryGetProperty("OrbitalMouseMovement", out var orbitalMouseMovement))
                {
                    // Parse orbital movement keys
                    if (orbitalMouseMovement.TryGetProperty("MoveForward", out var moveForwardProp))
                    {
                        string? moveForwardStr = moveForwardProp.GetString();
                        if (!string.IsNullOrWhiteSpace(moveForwardStr))
                        {
                            moveForwardKey = ParseHotkey(moveForwardStr);
                        }
                    }
                    
                    if (orbitalMouseMovement.TryGetProperty("MoveBackward", out var moveBackwardProp))
                    {
                        string? moveBackwardStr = moveBackwardProp.GetString();
                        if (!string.IsNullOrWhiteSpace(moveBackwardStr))
                        {
                            moveBackwardKey = ParseHotkey(moveBackwardStr);
                        }
                    }
                    
                    if (orbitalMouseMovement.TryGetProperty("TurnLeft", out var turnLeftProp))
                    {
                        string? turnLeftStr = turnLeftProp.GetString();
                        if (!string.IsNullOrWhiteSpace(turnLeftStr))
                        {
                            turnLeftKey = ParseHotkey(turnLeftStr);
                        }
                    }
                    
                    if (orbitalMouseMovement.TryGetProperty("TurnRight", out var turnRightProp))
                    {
                        string? turnRightStr = turnRightProp.GetString();
                        if (!string.IsNullOrWhiteSpace(turnRightStr))
                        {
                            turnRightKey = ParseHotkey(turnRightStr);
                        }
                    }
                }
            }
        }
        
        private int CalculateRequiredShortcutLength(int totalCells)
        {
            if (totalCells <= 26) return 1;
            if (totalCells <= 676) return 2;  // 26^2
            return 3;  // 26^3 = 17,576
        }
        
        private List<string> GenerateCellShortcuts(int rows, int cols)
        {
            int totalCells = rows * cols;
            int shortcutLength = CalculateRequiredShortcutLength(totalCells);
            List<string> shortcuts = new List<string>();
            
            // Generate sequential shortcuts based on length
            if (shortcutLength == 1)
            {
                // Single letters: A, B, C, ..., Z
                for (int i = 0; i < totalCells && i < 26; i++)
                {
                    shortcuts.Add(((char)('A' + i)).ToString());
                }
                // If more than 26 cells, wrap around (shouldn't happen with 1-letter, but handle gracefully)
                for (int i = 26; i < totalCells; i++)
                {
                    shortcuts.Add(((char)('A' + (i % 26))).ToString());
                }
            }
            else if (shortcutLength == 2)
            {
                // Two letters: AA, AB, AC, ..., AZ, BA, BB, ..., ZZ
                for (int i = 0; i < totalCells; i++)
                {
                    int first = i / 26;
                    int second = i % 26;
                    shortcuts.Add($"{(char)('A' + first)}{(char)('A' + second)}");
                }
            }
            else // shortcutLength == 3
            {
                // Three letters: AAA, AAB, AAC, ..., ZZZ
                for (int i = 0; i < totalCells; i++)
                {
                    int first = i / (26 * 26);
                    int remainder = i % (26 * 26);
                    int second = remainder / 26;
                    int third = remainder % 26;
                    shortcuts.Add($"{(char)('A' + first)}{(char)('A' + second)}{(char)('A' + third)}");
                }
            }
            
            return shortcuts;
        }
        
        private List<string> ValidateAndFixDuplicates(List<string> cellShortcuts, int rows, int cols, int monitorIndex, int shortcutLength)
        {
            // Create a dictionary to track which indices contain each shortcut
            Dictionary<string, List<int>> shortcutIndices = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            
            for (int i = 0; i < cellShortcuts.Count; i++)
            {
                string shortcut = cellShortcuts[i];
                if (!shortcutIndices.ContainsKey(shortcut))
                {
                    shortcutIndices[shortcut] = new List<int>();
                }
                shortcutIndices[shortcut].Add(i);
            }
            
            // Find duplicates
            List<string> duplicateReplacements = new List<string>();
            HashSet<string> usedShortcuts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // First pass: identify unique shortcuts
            foreach (var kvp in shortcutIndices)
            {
                if (kvp.Value.Count == 1)
                {
                    usedShortcuts.Add(kvp.Key);
                }
            }
            
            // Generate sequential unused combinations based on shortcut length
            List<string> replacementList = new List<string>();
            
            if (shortcutLength == 1)
            {
                // Single letters: A-Z
                for (char c = 'A'; c <= 'Z'; c++)
                {
                    string combo = c.ToString();
                    if (!usedShortcuts.Contains(combo))
                    {
                        replacementList.Add(combo);
                    }
                }
            }
            else if (shortcutLength == 2)
            {
                // Two letters: AA-ZZ
                for (char first = 'A'; first <= 'Z'; first++)
                {
                    for (char second = 'A'; second <= 'Z'; second++)
                    {
                        string combo = $"{first}{second}";
                        if (!usedShortcuts.Contains(combo))
                        {
                            replacementList.Add(combo);
                        }
                    }
                }
            }
            else // shortcutLength == 3
            {
                // Three letters: AAA-ZZZ
                for (char first = 'A'; first <= 'Z'; first++)
                {
                    for (char second = 'A'; second <= 'Z'; second++)
                    {
                        for (char third = 'A'; third <= 'Z'; third++)
                        {
                            string combo = $"{first}{second}{third}";
                            if (!usedShortcuts.Contains(combo))
                            {
                                replacementList.Add(combo);
                            }
                        }
                    }
                }
            }
            
            int replacementIndex = 0;
            List<string> fixedShortcuts = new List<string>(cellShortcuts);
            HashSet<string> allUsedShortcuts = new HashSet<string>(fixedShortcuts, StringComparer.OrdinalIgnoreCase);
            
            // Replace duplicates
            foreach (var kvp in shortcutIndices)
            {
                if (kvp.Value.Count > 1)
                {
                    // This shortcut appears multiple times - replace all but the first occurrence
                    string duplicateShortcut = kvp.Key;
                    for (int i = 1; i < kvp.Value.Count; i++)
                    {
                        int indexToReplace = kvp.Value[i];
                        
                        // Find next unused replacement
                        string? replacement = null;
                        while (replacementIndex < replacementList.Count)
                        {
                            string candidate = replacementList[replacementIndex++];
                            if (!allUsedShortcuts.Contains(candidate))
                            {
                                replacement = candidate;
                                break;
                            }
                        }
                        
                        if (replacement == null)
                        {
                            // Fallback: generate a unique replacement
                            // For 1-letter: use numbers (A1, A2, ...)
                            // For 2-letter: use numbers (AA1, AA2, ...)
                            // For 3-letter: use numbers (AAA1, AAA2, ...)
                            int fallbackNum = 1;
                            string prefix = shortcutLength == 1 ? "A" : (shortcutLength == 2 ? "AA" : "AAA");
                            do
                            {
                                replacement = $"{prefix}{fallbackNum++}";
                            } while (allUsedShortcuts.Contains(replacement));
                        }
                        
                        // Calculate row and col from index
                        int row = indexToReplace / cols;
                        int col = indexToReplace % cols;
                        
                        fixedShortcuts[indexToReplace] = replacement;
                        duplicateReplacements.Add($"Monitor {monitorIndex}, cell ({row},{col}): '{duplicateShortcut}' -> '{replacement}'");
                        allUsedShortcuts.Add(replacement);
                    }
                }
            }
            
            // Build warning message if duplicates were found
            if (duplicateReplacements.Count > 0)
            {
                string warning = "Warning: Duplicate cell shortcuts found and replaced:\n" + string.Join("\n", duplicateReplacements);
                if (duplicateWarning == null)
                {
                    duplicateWarning = warning;
                }
                else
                {
                    duplicateWarning += "\n\n" + warning;
                }
            }
            
            return fixedShortcuts;
        }
        
        private Keys? ParseHotkey(string hotkeyStr)
        {
            if (string.IsNullOrWhiteSpace(hotkeyStr))
                return null;
            
            // Try direct enum parse
            if (Enum.TryParse<Keys>(hotkeyStr, true, out Keys key))
            {
                // Validate it's a valid key (not a modifier-only key)
                if (key != Keys.None)
                    return key;
            }
            
            return null; // Invalid hotkey
        }
        
        private void InitializeKeyboardHook()
        {
            keyboardHook = new GlobalKeyboardHook();
            keyboardHook.KeyDown += KeyboardHook_KeyDown;
            keyboardHook.KeyUp += KeyboardHook_KeyUp;
        }
        
        private void KeyboardHook_KeyDown(object? sender, KeyEventArgs e)
        {
            try
            {
                // Only handle configured hotkey when overlay is NOT visible
                // When overlay is visible, it will handle the hotkey itself to close
                if (configuredHotkey != null && e.KeyCode == configuredHotkey && (overlayForm == null || overlayForm.IsDisposed))
                {
                    if (this.InvokeRequired)
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                ShowGridOverlay();
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Error showing overlay: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }));
                    }
                    else
                    {
                        ShowGridOverlay();
                    }
                    e.Handled = true;
                    return;
                }
                
                // Handle mouse movement keys (only when overlay is not visible)
                if (overlayForm == null || overlayForm.IsDisposed)
                {
                    bool keyHandled = false;
                    
                    if (useOrbitalMouse)
                    {
                        // Orbital mouse mode: forward/backward and turn keys
                        if ((moveForwardKey != null && e.KeyCode == moveForwardKey.Value) ||
                            (moveBackwardKey != null && e.KeyCode == moveBackwardKey.Value))
                        {
                            if (!heldDirectionKeys.Contains(e.KeyCode))
                            {
                                // Track movement start if this is the first key pressed
                                if (heldDirectionKeys.Count == 0 && heldTurnKeys.Count == 0)
                                {
                                    movementStartTime = DateTime.Now;
                                }
                                heldDirectionKeys.Add(e.KeyCode);
                                StartMovementTimer();
                            }
                            keyHandled = true;
                        }
                        
                        if ((turnLeftKey != null && e.KeyCode == turnLeftKey.Value) ||
                            (turnRightKey != null && e.KeyCode == turnRightKey.Value))
                        {
                            if (!heldTurnKeys.Contains(e.KeyCode))
                            {
                                // Track movement start if this is the first key pressed (and no directional keys)
                                if (heldDirectionKeys.Count == 0 && heldTurnKeys.Count == 0)
                                {
                                    movementStartTime = DateTime.Now;
                                }
                                heldTurnKeys.Add(e.KeyCode);
                                StartMovementTimer();
                            }
                            keyHandled = true;
                        }
                    }
                    else
                    {
                        // Directional mode: up/down/left/right keys
                        if ((moveUpKey != null && e.KeyCode == moveUpKey.Value) ||
                            (moveDownKey != null && e.KeyCode == moveDownKey.Value) ||
                            (moveLeftKey != null && e.KeyCode == moveLeftKey.Value) ||
                            (moveRightKey != null && e.KeyCode == moveRightKey.Value))
                        {
                            if (!heldDirectionKeys.Contains(e.KeyCode))
                            {
                                // Track movement start if this is the first key pressed
                                if (heldDirectionKeys.Count == 0)
                                {
                                    movementStartTime = DateTime.Now;
                                }
                                heldDirectionKeys.Add(e.KeyCode);
                                StartMovementTimer();
                            }
                            keyHandled = true;
                        }
                    }
                    
                    // Check if it's a speed modifier key (works for both modes)
                    if (speedModifiers.ContainsKey(e.KeyCode))
                    {
                        if (!heldSpeedModifierKeys.Contains(e.KeyCode))
                        {
                            heldSpeedModifierKeys.Add(e.KeyCode);
                        }
                        keyHandled = true;
                    }
                    
                    if (keyHandled)
                    {
                        e.Handled = true;
                    }
                }
            }
            catch (Exception)
            {
                // Handle exceptions silently to prevent crash
            }
        }
        
        private void KeyboardHook_KeyUp(object? sender, KeyEventArgs e)
        {
            try
            {
                // Handle mouse movement keys
                if (heldDirectionKeys.Contains(e.KeyCode))
                {
                    heldDirectionKeys.Remove(e.KeyCode);
                    if (heldDirectionKeys.Count == 0 && heldTurnKeys.Count == 0)
                    {
                        StopMovementTimer();
                        // Reset acceleration when all movement keys are released
                        movementStartTime = null;
                    }
                    e.Handled = true;
                }
                
                // Handle turn keys (orbital mode)
                if (heldTurnKeys.Contains(e.KeyCode))
                {
                    heldTurnKeys.Remove(e.KeyCode);
                    if (heldDirectionKeys.Count == 0 && heldTurnKeys.Count == 0)
                    {
                        StopMovementTimer();
                        // Reset acceleration when all movement keys are released
                        movementStartTime = null;
                    }
                    e.Handled = true;
                }
                
                if (heldSpeedModifierKeys.Contains(e.KeyCode))
                {
                    heldSpeedModifierKeys.Remove(e.KeyCode);
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                // Handle exceptions silently to prevent crash
            }
        }
        
        private void ShowGridOverlay()
        {
            if (configuredHotkey == null)
            {
                MessageBox.Show($"Configuration Error:\n{configError}\n\nPlease fix config.json and restart the application.", "Configuration Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            
            if (monitorConfigs == null || monitorConfigs.Count == 0)
            {
                MessageBox.Show("No monitor configurations available.", "Configuration Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            
            try
            {
                // Get monitor config for the current cursor position
                MonitorConfig monitorConfig = GetMonitorConfigForCursor();
                
                // Get the screen that contains the cursor for proper positioning
                Point cursorPos = Cursor.Position;
                Screen? cursorScreen = Screen.FromPoint(cursorPos);
                
                overlayForm = new GridOverlayForm(monitorConfig.Rows, monitorConfig.Columns, gridOpacity, configuredHotkey.Value, cursorScreen, monitorConfig.CellShortcuts);
                overlayForm.CellSelected += OverlayForm_CellSelected;
                
                // Use null as parent to prevent parent form from affecting positioning
                DialogResult result = overlayForm.ShowDialog(null);
                
                // Store CellSelectedEventArgs before disposing, in case it gets cleared
                CellSelectedEventArgs? selectedCell = overlayForm.CellSelectedEventArgs;
                
                if (result == DialogResult.OK && selectedCell != null)
                {
                    // Move cursor to selected cell center
                    Cursor.Position = selectedCell.ScreenPosition;
                }
                
                overlayForm.Dispose();
                overlayForm = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error in ShowGridOverlay: {ex.Message}\n{ex.StackTrace}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private MonitorConfig GetMonitorConfigForCursor()
        {
            if (monitorConfigs == null || monitorConfigs.Count == 0)
            {
                throw new InvalidOperationException("No monitor configurations available.");
            }
            
            // Get the screen that contains the cursor
            Point cursorPos = Cursor.Position;
            Screen? cursorScreen = Screen.FromPoint(cursorPos);
            
            if (cursorScreen == null)
            {
                // Fallback to primary screen
                return monitorConfigs[0];
            }
            
            // Find the index of the screen in Screen.AllScreens
            Screen[] allScreens = Screen.AllScreens;
            int screenIndex = -1;
            for (int i = 0; i < allScreens.Length; i++)
            {
                if (allScreens[i].Bounds.Equals(cursorScreen.Bounds))
                {
                    screenIndex = i;
                    break;
                }
            }
            
            // If screen not found or index is out of bounds, use first monitor config
            if (screenIndex < 0 || screenIndex >= monitorConfigs.Count)
            {
                return monitorConfigs[0];
            }
            
            return monitorConfigs[screenIndex];
        }
        
        private void OverlayForm_CellSelected(object? sender, CellSelectedEventArgs e)
        {
            // This is called before the form closes
            // Store the event args so we can access them after ShowDialog returns
            if (sender is GridOverlayForm form)
            {
                form.CellSelectedEventArgs = e;
            }
        }
        
        private void ReloadButton_Click(object? sender, EventArgs e)
        {
            ReloadConfig();
        }
        
        private void OrbitalCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            if (orbitalCheckBox == null) return;
            
            useOrbitalMouse = orbitalCheckBox.Checked;
            
            // Stop movement timer if running
            StopMovementTimer();
            
            // Clear all held keys
            heldDirectionKeys.Clear();
            heldTurnKeys.Clear();
            
            // Show or hide orbital visualization
            if (useOrbitalMouse)
            {
                if (orbitalVisualizationForm == null || orbitalVisualizationForm.IsDisposed)
                {
                    orbitalVisualizationForm = new OrbitalVisualizationForm();
                    orbitalVisualizationForm.Show();
                }
            }
            else
            {
                if (orbitalVisualizationForm != null && !orbitalVisualizationForm.IsDisposed)
                {
                    orbitalVisualizationForm.Close();
                    orbitalVisualizationForm.Dispose();
                    orbitalVisualizationForm = null;
                }
            }
            
            // Reset heading when switching to orbital (optional - could persist)
            // For now, we'll keep the heading persistent as per user preference
            
            // Update UI to reflect current scheme
            UpdateUIAfterReload();
        }
        
        private void ReloadConfig()
        {
            // Store old hotkey to check if it changed
            Keys? oldHotkey = configuredHotkey;
            
            // Reset state
            configError = null;
            duplicateWarning = null;
            configuredHotkey = null;
            monitorConfigs = null;
            gridOpacity = 1.0;
            
            // Reset mouse movement state
            defaultSpeedPixelsPerSecond = 10.0;
            moveUpKey = null;
            moveDownKey = null;
            moveLeftKey = null;
            moveRightKey = null;
            speedModifiers.Clear();
            heldDirectionKeys.Clear();
            heldSpeedModifierKeys.Clear();
            accumulatedMoveX = 0.0;
            accumulatedMoveY = 0.0;
            StopMovementTimer();
            
            // Reset acceleration settings to defaults
            accelerationDurationMs = 100.0;
            initialSpeedFactor = 0.025;
            
            // Reset orbital mouse state
            moveForwardKey = null;
            moveBackwardKey = null;
            turnLeftKey = null;
            turnRightKey = null;
            heldTurnKeys.Clear();
            currentHeading = 0.0;
            
            // Dispose existing keyboard hook
            if (keyboardHook != null)
            {
                keyboardHook.Dispose();
                keyboardHook = null;
            }
            
            // Reload config
            try
            {
                LoadConfig();
                
                // Update UI
                UpdateUIAfterReload();
                
                // Sync checkbox state with config
                if (orbitalCheckBox != null)
                {
                    orbitalCheckBox.Checked = useOrbitalMouse;
                }
                
                // Update orbital visualization form based on new config
                if (useOrbitalMouse)
                {
                    if (orbitalVisualizationForm == null || orbitalVisualizationForm.IsDisposed)
                    {
                        orbitalVisualizationForm = new OrbitalVisualizationForm();
                        orbitalVisualizationForm.Show();
                    }
                }
                else
                {
                    if (orbitalVisualizationForm != null && !orbitalVisualizationForm.IsDisposed)
                    {
                        orbitalVisualizationForm.Close();
                        orbitalVisualizationForm.Dispose();
                        orbitalVisualizationForm = null;
                    }
                }
                
                // Re-initialize keyboard hook if hotkey is valid
                if (configuredHotkey != null)
                {
                    InitializeKeyboardHook();
                }
                
                // Show success message
                if (infoLabel != null)
                {
                    string hotkeyText = configuredHotkey?.ToString() ?? "SPACE";
                    string message = $"Config reloaded successfully!\nPress {hotkeyText} to show grid overlay.\nPress ESC in overlay to close.";
                    
                    if (duplicateWarning != null)
                    {
                        message += "\n\n" + duplicateWarning;
                    }
                    
                    infoLabel.Text = message;
                    
                    // Set color based on state
                    if (duplicateWarning != null && configError == null)
                    {
                        infoLabel.ForeColor = System.Drawing.Color.Orange;
                    }
                    else if (configError == null)
                    {
                        infoLabel.ForeColor = System.Drawing.Color.Black;
                    }
                }
            }
            catch (Exception ex)
            {
                configError = ex.Message;
                configuredHotkey = null;
                
                // Update UI to show error
                UpdateUIAfterReload();
                
                if (infoLabel != null)
                {
                    infoLabel.Text = $"Configuration Error:\n{configError}\n\nPlease fix config.json and try reloading again.";
                    infoLabel.ForeColor = System.Drawing.Color.Red;
                }
            }
        }
        
        private void UpdateUIAfterReload()
        {
            string hotkeyText = configuredHotkey?.ToString() ?? "SPACE";
            
            // Update test button
            if (testButton != null)
            {
                testButton.Text = $"Test Overlay (Click or Press {hotkeyText})";
                testButton.Enabled = configuredHotkey != null;
            }
        }
        
        private void MainForm_Activated(object? sender, EventArgs e)
        {
            // Close the overlay if it's open when the main window gets focus
            // This ensures that clicking on the main window closes the overlay
            // Don't close if DialogResult is already set (cell was selected) or if overlay is being shown
            if (overlayForm != null && !overlayForm.IsDisposed)
            {
                try
                {
                    // Only close if DialogResult is None (not set yet)
                    // If it's OK, the overlay is closing after cell selection - don't interfere
                    if (overlayForm.DialogResult == DialogResult.None)
                    {
                        overlayForm.DialogResult = DialogResult.Cancel;
                        overlayForm.Close();
                    }
                }
                catch
                {
                    // Ignore errors when closing overlay
                }
            }
        }
        
        private double GetCurrentSpeed()
        {
            double baseSpeed;
            
            // If any speed modifier keys are held, use the highest speed from those modifiers
            if (heldSpeedModifierKeys.Count > 0)
            {
                double maxSpeed = 0;
                foreach (Keys key in heldSpeedModifierKeys)
                {
                    if (speedModifiers.TryGetValue(key, out double speed))
                    {
                        if (speed > maxSpeed)
                        {
                            maxSpeed = speed;
                        }
                    }
                }
                baseSpeed = maxSpeed;
            }
            else
            {
                // Otherwise, use default speed
                baseSpeed = defaultSpeedPixelsPerSecond;
            }
            
            // Apply acceleration if movement has started
            if (movementStartTime != null)
            {
                // Calculate elapsed time since movement started
                double elapsedMs = (DateTime.Now - movementStartTime.Value).TotalMilliseconds;
                
                // Clamp elapsed time to acceleration duration
                elapsedMs = Math.Min(elapsedMs, accelerationDurationMs);
                
                // Calculate acceleration factor: linear interpolation from initialSpeedFactor to 1.0
                double accelerationFactor = initialSpeedFactor + (1.0 - initialSpeedFactor) * (elapsedMs / accelerationDurationMs);
                
                // Apply acceleration to base speed
                return baseSpeed * accelerationFactor;
            }
            
            // No acceleration (should not happen during active movement, but return base speed as fallback)
            return baseSpeed;
        }
        
        private void StartMovementTimer()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => StartMovementTimer()));
                return;
            }
            
            if (movementTimer == null)
            {
                movementTimer = new System.Windows.Forms.Timer();
                movementTimer.Interval = 16; // ~60fps for smooth movement
                movementTimer.Tick += MovementTimer_Tick;
            }
            
            if (!movementTimer.Enabled)
            {
                movementTimer.Start();
            }
        }
        
        private void StopMovementTimer()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => StopMovementTimer()));
                return;
            }
            
            if (movementTimer != null && movementTimer.Enabled)
            {
                movementTimer.Stop();
            }
        }
        
        private void MovementTimer_Tick(object? sender, EventArgs e)
        {
            if (heldDirectionKeys.Count == 0 && heldTurnKeys.Count == 0)
            {
                StopMovementTimer();
                return;
            }
            
            // Calculate current speed
            double speed = GetCurrentSpeed();
            
            // Calculate movement delta based on speed and timer interval
            double pixelsPerFrame = (speed * movementTimer!.Interval) / 1000.0;
            
            if (useOrbitalMouse)
            {
                // Orbital mouse mode
                Point currentPos = Cursor.Position;
                double headingRad = currentHeading * Math.PI / 180.0;
                
                // Calculate rotation point (25 pixels behind cursor in opposite direction of heading)
                double rotationPointX = currentPos.X - ROTATION_POINT_DISTANCE * Math.Sin(headingRad);
                double rotationPointY = currentPos.Y + ROTATION_POINT_DISTANCE * Math.Cos(headingRad); // Positive because Y increases downward
                
                bool isTurning = (turnLeftKey != null && heldTurnKeys.Contains(turnLeftKey.Value)) ||
                                (turnRightKey != null && heldTurnKeys.Contains(turnRightKey.Value));
                
                double newX = currentPos.X;
                double newY = currentPos.Y;
                
                // Handle rotation around rotation point
                if (isTurning)
                {
                    double turnRate = speed * 0.25; // Turn rate is 25% of movement speed (degrees per second)
                    double turnDeltaRad = (turnRate * movementTimer.Interval / 1000.0) * Math.PI / 180.0;
                    
                    // Determine turn direction
                    if (turnLeftKey != null && heldTurnKeys.Contains(turnLeftKey.Value))
                    {
                        turnDeltaRad = -turnDeltaRad; // Counter-clockwise
                    }
                    // else turnRightKey is clockwise (positive)
                    
                    // Translate cursor position relative to rotation point
                    double dx = currentPos.X - rotationPointX;
                    double dy = currentPos.Y - rotationPointY;
                    
                    // Apply rotation matrix
                    double cosAngle = Math.Cos(turnDeltaRad);
                    double sinAngle = Math.Sin(turnDeltaRad);
                    double rotatedX = dx * cosAngle - dy * sinAngle;
                    double rotatedY = dx * sinAngle + dy * cosAngle;
                    
                    // Translate back to absolute coordinates
                    newX = rotatedX + rotationPointX;
                    newY = rotatedY + rotationPointY;
                    
                    // Update heading based on new cursor position relative to rotation point
                    double newDx = newX - rotationPointX;
                    double newDy = newY - rotationPointY;
                    // Calculate angle from rotation point to cursor (0 = up/north, increases clockwise)
                    double newHeadingRad = Math.Atan2(newDx, -newDy); // Negative Y because screen Y increases downward
                    currentHeading = newHeadingRad * 180.0 / Math.PI;
                    
                    // Normalize heading to 0-360 range
                    while (currentHeading < 0) currentHeading += 360.0;
                    while (currentHeading >= 360.0) currentHeading -= 360.0;
                }
                
                // Handle forward/backward movement (after rotation, if any)
                double moveDeltaX = 0.0;
                double moveDeltaY = 0.0;
                
                // Recalculate heading in radians after potential rotation
                headingRad = currentHeading * Math.PI / 180.0;
                
                if (moveForwardKey != null && heldDirectionKeys.Contains(moveForwardKey.Value))
                {
                    moveDeltaX += Math.Sin(headingRad) * pixelsPerFrame;
                    moveDeltaY -= Math.Cos(headingRad) * pixelsPerFrame; // Negative because Y increases downward
                }
                if (moveBackwardKey != null && heldDirectionKeys.Contains(moveBackwardKey.Value))
                {
                    moveDeltaX -= Math.Sin(headingRad) * pixelsPerFrame;
                    moveDeltaY += Math.Cos(headingRad) * pixelsPerFrame;
                }
                
                // Apply forward/backward movement to new position (after rotation)
                newX += moveDeltaX;
                newY += moveDeltaY;
                
                // Accumulate fractional movement
                accumulatedMoveX += (newX - currentPos.X);
                accumulatedMoveY += (newY - currentPos.Y);
                
                // Move cursor by whole pixels, keeping remainder for next frame
                int moveX = (int)accumulatedMoveX;
                int moveY = (int)accumulatedMoveY;
                
                // Keep the fractional part for next frame
                accumulatedMoveX -= moveX;
                accumulatedMoveY -= moveY;
                
                // Move the cursor
                if (moveX != 0 || moveY != 0)
                {
                    Point newPos = new Point(currentPos.X + moveX, currentPos.Y + moveY);
                    Cursor.Position = newPos;
                }
                
                // Update visualization form (always update, even if only rotation occurred)
                orbitalVisualizationForm?.UpdateCursorInfo(Cursor.Position, currentHeading);
            }
            else
            {
                // Directional mode (existing implementation)
                int deltaX = 0;
                int deltaY = 0;
                
                if (moveUpKey != null && heldDirectionKeys.Contains(moveUpKey.Value))
                {
                    deltaY -= 1;
                }
                if (moveDownKey != null && heldDirectionKeys.Contains(moveDownKey.Value))
                {
                    deltaY += 1;
                }
                if (moveLeftKey != null && heldDirectionKeys.Contains(moveLeftKey.Value))
                {
                    deltaX -= 1;
                }
                if (moveRightKey != null && heldDirectionKeys.Contains(moveRightKey.Value))
                {
                    deltaX += 1;
                }
                
                // Normalize diagonal movement to maintain consistent speed
                double magnitude = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                if (magnitude > 0)
                {
                    // Normalize the vector to maintain consistent speed in all directions
                    double normalizedX = deltaX / magnitude;
                    double normalizedY = deltaY / magnitude;
                    
                    // Calculate movement delta with normalized direction
                    double moveDeltaX = normalizedX * pixelsPerFrame;
                    double moveDeltaY = normalizedY * pixelsPerFrame;
                    
                    // Accumulate fractional movement
                    accumulatedMoveX += moveDeltaX;
                    accumulatedMoveY += moveDeltaY;
                    
                    // Move cursor by whole pixels, keeping remainder for next frame
                    int moveX = (int)accumulatedMoveX;
                    int moveY = (int)accumulatedMoveY;
                    
                    // Keep the fractional part for next frame
                    accumulatedMoveX -= moveX;
                    accumulatedMoveY -= moveY;
                    
                    // Move the cursor
                    if (moveX != 0 || moveY != 0)
                    {
                        Point currentPos = Cursor.Position;
                        Cursor.Position = new Point(currentPos.X + moveX, currentPos.Y + moveY);
                    }
                }
            }
        }
        
        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            movementTimer?.Stop();
            movementTimer?.Dispose();
            keyboardHook?.Dispose();
            overlayForm?.Dispose();
            orbitalVisualizationForm?.Close();
            orbitalVisualizationForm?.Dispose();
        }
        
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
    
    internal class MonitorConfig
    {
        public int Rows { get; set; }
        public int Columns { get; set; }
        public List<string>? CellShortcuts { get; set; }
    }
}
