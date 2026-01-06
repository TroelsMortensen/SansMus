using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SansMus
{
    public partial class GridOverlayForm : Form
    {
        // Windows API for layered window support
        private const int WS_EX_LAYERED = 0x80000;
        private const int GWL_EXSTYLE = -20;
        private const uint ULW_ALPHA = 0x2;
        
        [StructLayout(LayoutKind.Sequential)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
            
            public BLENDFUNCTION(byte opacity)
            {
                BlendOp = 0; // AC_SRC_OVER
                BlendFlags = 0;
                SourceConstantAlpha = opacity;
                AlphaFormat = 1; // AC_SRC_ALPHA
            }
        }
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        
        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, 
            ref Size psize, IntPtr hdcSrc, ref Point pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
        
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        
        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hGDIObj);
        
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
        
        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hDC);
        
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        private readonly int gridRows;
        private readonly int gridCols;
        private readonly double gridOpacity;
        private readonly double gridBackgroundOpacity;
        
        private string? firstLetter = null;
        private Dictionary<(int row, int col), string> cellLabels = new Dictionary<(int row, int col), string>();
        
        // Variable cell size fields
        private int baseCellWidth;
        private int baseCellHeight;
        private int firstColWidth;
        private int lastColWidth;
        private int firstRowHeight;
        private int lastRowHeight;
        
        public CellSelectedEventArgs? CellSelectedEventArgs { get; set; }
        
        public GridOverlayForm(int gridRows, int gridCols, double gridOpacity, double gridBackgroundOpacity)
        {
            this.gridRows = gridRows;
            this.gridCols = gridCols;
            this.gridOpacity = gridOpacity;
            this.gridBackgroundOpacity = gridBackgroundOpacity;
            InitializeComponent();
            InitializeGrid();
        }
        
        private void InitializeComponent()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Normal;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            
            this.BackColor = Color.Black; // Background color (not visible with layered window - transparency handled by bitmap alpha)
            this.Opacity = 1.0; // Fully opaque form - transparency controlled via layered window
            this.KeyPreview = true;
            this.DoubleBuffered = true;
            
            // Enable proper painting for alpha blending
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            
            // Set up layered window after form is loaded
            this.Load += GridOverlayForm_Load;
            
            // Get the screen that contains the cursor
            Point cursorPos = Cursor.Position;
            Screen? cursorScreen = Screen.FromPoint(cursorPos);
            Rectangle screenBounds = cursorScreen?.Bounds ?? Screen.PrimaryScreen?.Bounds ?? Screen.AllScreens[0].Bounds;
            
            this.Location = new Point(screenBounds.Left, screenBounds.Top);
            this.Size = new Size(screenBounds.Width, screenBounds.Height);
            
            // Calculate base cell dimensions for internal cells
            baseCellWidth = screenBounds.Width / gridCols;
            baseCellHeight = screenBounds.Height / gridRows;
            
            // Calculate remainder pixels
            int widthRemainder = screenBounds.Width % gridCols;
            int heightRemainder = screenBounds.Height % gridRows;
            
            // Calculate edge cell sizes to absorb remainder pixels
            firstColWidth = baseCellWidth + (widthRemainder / 2);
            lastColWidth = baseCellWidth + (widthRemainder - widthRemainder / 2);
            firstRowHeight = baseCellHeight + (heightRemainder / 2);
            lastRowHeight = baseCellHeight + (heightRemainder - heightRemainder / 2);
            
            this.Paint += GridOverlayForm_Paint;
            this.KeyDown += GridOverlayForm_KeyDown;
            this.FormClosing += GridOverlayForm_FormClosing;
        }
        
        private void GridOverlayForm_Load(object? sender, EventArgs e)
        {
            // Enable layered window style for per-pixel alpha transparency
            int exStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);
            exStyle |= WS_EX_LAYERED;
            SetWindowLong(this.Handle, GWL_EXSTYLE, exStyle);
            
            // Update the layered window with initial bitmap
            UpdateLayeredWindowBitmap();
        }
        
        private void InitializeGrid()
        {
            // Initialize letter mapping with grouping
            // Group cells with same first letter together in rectangular regions
            const int LETTERS_COUNT = 26;
            
            // Organize first letters in roughly rectangular regions
            // Use 5 columns of first letters (5 cols × 6 rows = 30, but we only use 26)
            const int FIRST_LETTER_COLS = 5;
            const int FIRST_LETTER_ROWS = (LETTERS_COUNT + FIRST_LETTER_COLS - 1) / FIRST_LETTER_COLS; // 6
            
            for (int row = 0; row < gridRows; row++)
            {
                for (int col = 0; col < gridCols; col++)
                {
                    // Calculate which first letter region this cell belongs to
                    // Map grid position to first letter region
                    int firstLetterCol = (col * FIRST_LETTER_COLS) / gridCols;
                    int firstLetterRow = (row * FIRST_LETTER_ROWS) / gridRows;
                    int firstLetterIndex = firstLetterRow * FIRST_LETTER_COLS + firstLetterCol;
                    
                    if (firstLetterIndex >= LETTERS_COUNT)
                    {
                        firstLetterIndex = LETTERS_COUNT - 1; // Use last letter if overflow
                    }
                    
                    char firstLetter = (char)('A' + firstLetterIndex);
                    
                    // Calculate second letter within this first letter group
                    // Count how many cells in this first letter group have been assigned
                    int cellsInThisGroup = 0;
                    for (int r = 0; r <= row; r++)
                    {
                        int startCol = (r == row) ? 0 : 0;
                        int endCol = (r == row) ? col : gridCols;
                        
                        for (int c = startCol; c < endCol; c++)
                        {
                            int prevFirstLetterCol = (c * FIRST_LETTER_COLS) / gridCols;
                            int prevFirstLetterRow = (r * FIRST_LETTER_ROWS) / gridRows;
                            int prevFirstLetterIndex = prevFirstLetterRow * FIRST_LETTER_COLS + prevFirstLetterCol;
                            if (prevFirstLetterIndex >= LETTERS_COUNT) prevFirstLetterIndex = LETTERS_COUNT - 1;
                            
                            if (prevFirstLetterIndex == firstLetterIndex)
                            {
                                cellsInThisGroup++;
                            }
                        }
                    }
                    
                    char secondLetter = (char)('A' + (cellsInThisGroup % 26));
                    
                    string label = $"{firstLetter}{secondLetter}";
                    cellLabels[(row, col)] = label;
                }
            }
        }
        
        private int GetCellWidth(int col)
        {
            if (col == 0) return firstColWidth;
            if (col == gridCols - 1) return lastColWidth;
            return baseCellWidth;
        }
        
        private int GetCellHeight(int row)
        {
            if (row == 0) return firstRowHeight;
            if (row == gridRows - 1) return lastRowHeight;
            return baseCellHeight;
        }
        
        private int GetCellX(int col)
        {
            int x = 0;
            for (int c = 0; c < col; c++)
            {
                x += GetCellWidth(c);
            }
            return x;
        }
        
        private int GetCellY(int row)
        {
            int y = 0;
            for (int r = 0; r < row; r++)
            {
                y += GetCellHeight(r);
            }
            return y;
        }
        
        private void UpdateLayeredWindowBitmap()
        {
            if (this.Width <= 0 || this.Height <= 0)
                return;
            
            // Create bitmap with per-pixel alpha support
            using (Bitmap bitmap = new Bitmap(this.Width, this.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    
                    // Draw grid lines with semi-transparent white using gridOpacity
                    // This will blend with the desktop behind the form
                    int gridAlpha = (int)(gridOpacity * 255);
                    Color gridColor = Color.FromArgb(gridAlpha, Color.White);
                    using (Pen gridPen = new Pen(gridColor, 1))
                    {
                        // Vertical lines
                        for (int col = 0; col <= gridCols; col++)
                        {
                            int x = GetCellX(col);
                            g.DrawLine(gridPen, x, 0, x, this.Height);
                        }
                        
                        // Horizontal lines
                        for (int row = 0; row <= gridRows; row++)
                        {
                            int y = GetCellY(row);
                            g.DrawLine(gridPen, 0, y, this.Width, y);
                        }
                    }
                    
                    // Draw cell labels with opacity based on gridOpacity
                    int textAlpha = (int)(gridOpacity * 255);
                    Color textColor = Color.FromArgb(textAlpha, Color.White);
                    using (Font labelFont = new Font("Arial", 14, FontStyle.Bold))
                    using (Brush textBrush = new SolidBrush(textColor))
                    using (StringFormat format = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    })
                    {
                        foreach (var kvp in cellLabels)
                        {
                            (int row, int col) = kvp.Key;
                            string label = kvp.Value;
                            
                            // If hint mode is active, only show cells starting with firstLetter
                            if (firstLetter != null && !label.StartsWith(firstLetter, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            
                            Rectangle cellRect = new Rectangle(
                                GetCellX(col),
                                GetCellY(row),
                                GetCellWidth(col),
                                GetCellHeight(row)
                            );
                            
                            g.DrawString(label, labelFont, textBrush, cellRect, format);
                        }
                    }
                }
                
                // Update the layered window with the bitmap
                IntPtr screenDc = GetDC(IntPtr.Zero);
                IntPtr memDc = CreateCompatibleDC(screenDc);
                IntPtr hBitmap = IntPtr.Zero;
                IntPtr hOldBitmap = IntPtr.Zero;
                
                try
                {
                    hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                    hOldBitmap = SelectObject(memDc, hBitmap);
                    
                    Point topLeft = new Point(this.Left, this.Top);
                    Size size = new Size(this.Width, this.Height);
                    Point sourceLocation = new Point(0, 0);
                    BLENDFUNCTION blend = new BLENDFUNCTION(255);
                    
                    UpdateLayeredWindow(this.Handle, screenDc, ref topLeft, ref size, 
                        memDc, ref sourceLocation, 0, ref blend, ULW_ALPHA);
                }
                finally
                {
                    ReleaseDC(IntPtr.Zero, screenDc);
                    if (hBitmap != IntPtr.Zero)
                    {
                        SelectObject(memDc, hOldBitmap);
                        DeleteObject(hBitmap);
                    }
                    DeleteDC(memDc);
                }
            }
        }
        
        private void GridOverlayForm_Paint(object? sender, PaintEventArgs e)
        {
            // Use layered window bitmap update instead of direct painting
            UpdateLayeredWindowBitmap();
        }
        
        private void GridOverlayForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (firstLetter == null)
            {
                // First letter
                if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z)
                {
                    firstLetter = e.KeyCode.ToString().ToUpper();
                    UpdateLayeredWindowBitmap(); // Redraw to show filtered cells
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    // Cancel grid
                    this.DialogResult = DialogResult.Cancel;
                    this.Close();
                }
            }
            else
            {
                // Second letter
                if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z)
                {
                    string secondLetter = e.KeyCode.ToString().ToUpper();
                    string targetLabel = firstLetter + secondLetter;
                    
                    // Find cell with this label
                    var cell = cellLabels.FirstOrDefault(kvp => kvp.Value.Equals(targetLabel, StringComparison.OrdinalIgnoreCase));
                    
                    if (cell.Key != default)
                    {
                        // Calculate cell center using variable cell sizes
                        int cellX = GetCellX(cell.Key.col);
                        int cellY = GetCellY(cell.Key.row);
                        int cellW = GetCellWidth(cell.Key.col);
                        int cellH = GetCellHeight(cell.Key.row);
                        
                        int centerX = cellX + (cellW / 2);
                        int centerY = cellY + (cellH / 2);
                        
                        // Add screen offset (in case of multi-monitor setup)
                        Point screenLocation = this.Location;
                        centerX += screenLocation.X;
                        centerY += screenLocation.Y;
                        
                        // Store event args and trigger event
                        CellSelectedEventArgs = new CellSelectedEventArgs(cell.Key.row, cell.Key.col, new Point(centerX, centerY));
                        CellSelected?.Invoke(this, CellSelectedEventArgs);
                        
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    }
                    
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    // Cancel hint mode, return to full grid
                    firstLetter = null;
                    UpdateLayeredWindowBitmap();
                    e.Handled = true;
                }
            }
        }
        
        private void GridOverlayForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            // Allow closing via ESC key (DialogResult.Cancel) or programmatic close
            // Only prevent accidental closure via Alt+F4 or X button
            if (e.CloseReason == CloseReason.UserClosing && this.DialogResult == DialogResult.None)
            {
                e.Cancel = true;
            }
        }
        
        public event EventHandler<CellSelectedEventArgs>? CellSelected;
        
        public void ResetHintMode()
        {
            firstLetter = null;
            UpdateLayeredWindowBitmap();
        }
    }
    
    public class CellSelectedEventArgs : EventArgs
    {
        public int Row { get; }
        public int Col { get; }
        public Point ScreenPosition { get; }
        
        public CellSelectedEventArgs(int row, int col, Point screenPosition)
        {
            Row = row;
            Col = col;
            ScreenPosition = screenPosition;
        }
    }
}

