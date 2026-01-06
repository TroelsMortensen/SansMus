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
        private List<MonitorConfig>? monitorConfigs = null;
        private double gridOpacity = 1.0; // Default: fully opaque grid
        private double gridBackgroundOpacity = 0.7; // Default: 70% opaque background
        
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
            this.Size = new System.Drawing.Size(300, 200);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormClosing += MainForm_FormClosing;
            
            string hotkeyText = configuredHotkey?.ToString() ?? "SPACE";
            infoLabel = new Label
            {
                Text = $"Press {hotkeyText} to show grid overlay.\nPress ESC in overlay to close.",
                Dock = DockStyle.Top,
                Height = 100,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                AutoSize = false
            };
            this.Controls.Add(infoLabel);
            
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
                // Only handle configured hotkey when overlay is not visible
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
                
                overlayForm = new GridOverlayForm(monitorConfig.Rows, monitorConfig.Columns, gridOpacity, gridBackgroundOpacity);
                overlayForm.CellSelected += OverlayForm_CellSelected;
                
                DialogResult result = overlayForm.ShowDialog(this);
                
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
