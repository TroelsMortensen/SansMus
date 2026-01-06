using System;
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
            
            try
            {
                overlayForm = new GridOverlayForm();
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
}
