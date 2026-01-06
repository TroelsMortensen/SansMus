using System;
using System.Windows.Forms;

namespace SansMouse
{
    public partial class MainForm : Form
    {
        private GlobalKeyboardHook? keyboardHook;
        private GridOverlayForm? overlayForm;
        
        public MainForm()
        {
            InitializeComponent();
            InitializeKeyboardHook();
        }
        
        private void InitializeComponent()
        {
            this.Text = "SansMouse - Keyboard Mouse Control";
            this.Size = new System.Drawing.Size(300, 200);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormClosing += MainForm_FormClosing;
            
            Label infoLabel = new Label
            {
                Text = "Press SPACE to show grid overlay.\nPress ESC in overlay to close.",
                Dock = DockStyle.Top,
                Height = 100,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                AutoSize = false
            };
            this.Controls.Add(infoLabel);
            
            Button testButton = new Button
            {
                Text = "Test Overlay (Click or Press Space)",
                Dock = DockStyle.Fill,
                Height = 50
            };
            testButton.Click += (s, e) => ShowGridOverlay();
            this.Controls.Add(testButton);
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
                // Only handle Space key when overlay is not visible
                if (e.KeyCode == Keys.Space && (overlayForm == null || overlayForm.IsDisposed))
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
