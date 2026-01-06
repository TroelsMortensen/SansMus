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
        private Keys? configuredHotkey = null; // null means config invalid
        private string? configError = null;
        private Label? infoLabel;
        private Button? testButton;
        private Button? reloadButton;
        private List<MonitorConfig>? monitorConfigs = null;
        private double gridOpacity = 1.0; // Default: fully opaque grid
        private double gridBackgroundOpacity = 0.7; // Default: 70% opaque background
        private string? duplicateWarning = null;
        
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
                
                if (!warpGrid.TryGetProperty("hotkey", out var hotkeyProp))
                {
                    throw new InvalidOperationException("Configuration file missing 'hotkey' in WarpGrid section.");
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
                if (warpGrid.TryGetProperty("gridOpacity", out var gridOpacityProp))
                {
                    if (gridOpacityProp.ValueKind == JsonValueKind.Number)
                    {
                        gridOpacity = gridOpacityProp.GetDouble();
                        if (gridOpacity < 0.0 || gridOpacity > 1.0)
                        {
                            throw new InvalidOperationException($"gridOpacity must be between 0.0 and 1.0, got {gridOpacity}.");
                        }
                    }
                }
                
                if (warpGrid.TryGetProperty("gridBackgroundOpacity", out var gridBackgroundOpacityProp))
                {
                    if (gridBackgroundOpacityProp.ValueKind == JsonValueKind.Number)
                    {
                        gridBackgroundOpacity = gridBackgroundOpacityProp.GetDouble();
                        if (gridBackgroundOpacity < 0.0 || gridBackgroundOpacity > 1.0)
                        {
                            throw new InvalidOperationException($"gridBackgroundOpacity must be between 0.0 and 1.0, got {gridBackgroundOpacity}.");
                        }
                    }
                }
                
                // Load monitor configurations
                if (!warpGrid.TryGetProperty("monitors", out var monitorsProp))
                {
                    throw new InvalidOperationException("Configuration file missing 'monitors' array in WarpGrid section.");
                }
                
                if (monitorsProp.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException("'monitors' must be an array.");
                }
                
                if (monitorsProp.GetArrayLength() == 0)
                {
                    throw new InvalidOperationException("'monitors' array must contain at least one monitor configuration.");
                }
                
                monitorConfigs = new List<MonitorConfig>();
                foreach (var monitorElement in monitorsProp.EnumerateArray())
                {
                    if (!monitorElement.TryGetProperty("numOfRows", out var rowsProp))
                    {
                        throw new InvalidOperationException("Monitor configuration missing 'numOfRows'.");
                    }
                    
                    if (!monitorElement.TryGetProperty("numOfColumns", out var colsProp))
                    {
                        throw new InvalidOperationException("Monitor configuration missing 'numOfColumns'.");
                    }
                    
                    int rows = rowsProp.GetInt32();
                    int cols = colsProp.GetInt32();
                    
                    if (rows <= 0)
                    {
                        throw new InvalidOperationException($"Monitor configuration has invalid 'numOfRows': {rows}. Must be a positive integer.");
                    }
                    
                    if (cols <= 0)
                    {
                        throw new InvalidOperationException($"Monitor configuration has invalid 'numOfColumns': {cols}. Must be a positive integer.");
                    }
                    
                    List<string>? cellShortcuts = null;
                    if (monitorElement.TryGetProperty("cellShortcuts", out var shortcutsProp) && shortcutsProp.ValueKind == JsonValueKind.Array)
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
                            // Validate and fix duplicates
                            cellShortcuts = ValidateAndFixDuplicates(cellShortcuts, rows, cols, monitorConfigs.Count);
                        }
                    }
                    
                    monitorConfigs.Add(new MonitorConfig
                    {
                        Rows = rows,
                        Columns = cols,
                        CellShortcuts = cellShortcuts
                    });
                }
            }
        }
        
        private List<string> ValidateAndFixDuplicates(List<string> cellShortcuts, int rows, int cols, int monitorIndex)
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
            
            // Generate sequential unused combinations
            List<string> replacementList = new List<string>();
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
                            // Fallback: generate a unique replacement using numbers
                            int fallbackNum = 1;
                            do
                            {
                                replacement = $"AA{fallbackNum++}";
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
                
                overlayForm = new GridOverlayForm(monitorConfig.Rows, monitorConfig.Columns, gridOpacity, gridBackgroundOpacity, configuredHotkey.Value, cursorScreen, monitorConfig.CellShortcuts);
                overlayForm.CellSelected += OverlayForm_CellSelected;
                
                // Use null as parent to prevent parent form from affecting positioning
                DialogResult result = overlayForm.ShowDialog(null);
                
                if (result == DialogResult.OK && overlayForm.CellSelectedEventArgs != null)
                {
                    // Move cursor to selected cell center
                    Cursor.Position = overlayForm.CellSelectedEventArgs.ScreenPosition;
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
            gridBackgroundOpacity = 0.7;
            
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
        
        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            keyboardHook?.Dispose();
            overlayForm?.Dispose();
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
