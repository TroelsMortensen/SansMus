using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SansMouse
{
    public partial class GridOverlayForm : Form
    {
        private const int GRID_ROWS = 10;
        private const int GRID_COLS = 24;
        
        private string? firstLetter = null;
        private Dictionary<(int row, int col), string> cellLabels = new Dictionary<(int row, int col), string>();
        private int cellWidth;
        private int cellHeight;
        public CellSelectedEventArgs? CellSelectedEventArgs { get; set; }
        
        public GridOverlayForm()
        {
            InitializeComponent();
            InitializeGrid();
        }
        
        private void InitializeComponent()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Normal;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.BackColor = Color.Black; // Solid black background
            this.Opacity = 0.7; // Make form 70% opaque (30% transparent)
            this.KeyPreview = true;
            this.DoubleBuffered = true;
            
            // Get primary screen bounds
            Rectangle screenBounds = Screen.PrimaryScreen?.Bounds ?? Screen.AllScreens[0].Bounds;
            this.Location = new Point(screenBounds.Left, screenBounds.Top);
            this.Size = new Size(screenBounds.Width, screenBounds.Height);
            
            // Calculate cell dimensions
            cellWidth = screenBounds.Width / GRID_COLS;
            cellHeight = screenBounds.Height / GRID_ROWS;
            
            this.Paint += GridOverlayForm_Paint;
            this.KeyDown += GridOverlayForm_KeyDown;
            this.FormClosing += GridOverlayForm_FormClosing;
        }
        
        private void InitializeGrid()
        {
            // Initialize letter mapping with grouping
            // Group cells with same first letter together in rectangular regions
            // With 240 cells and 26 letters (A-Z), each group gets ~9 cells
            // Arrange first letters in a pattern: roughly 5 columns × 6 rows = 30 regions (using 26)
            
            const int TOTAL_CELLS = GRID_ROWS * GRID_COLS; // 240
            const int LETTERS_COUNT = 26;
            
            // Organize first letters in roughly rectangular regions
            // Use 5 columns of first letters (5 cols × 6 rows = 30, but we only use 26)
            const int FIRST_LETTER_COLS = 5;
            const int FIRST_LETTER_ROWS = (LETTERS_COUNT + FIRST_LETTER_COLS - 1) / FIRST_LETTER_COLS; // 6
            
            int cellIndex = 0;
            
            for (int row = 0; row < GRID_ROWS; row++)
            {
                for (int col = 0; col < GRID_COLS; col++)
                {
                    // Calculate which first letter region this cell belongs to
                    // Map grid position to first letter region
                    int firstLetterCol = (col * FIRST_LETTER_COLS) / GRID_COLS;
                    int firstLetterRow = (row * FIRST_LETTER_ROWS) / GRID_ROWS;
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
                        int endCol = (r == row) ? col : GRID_COLS;
                        
                        for (int c = startCol; c < endCol; c++)
                        {
                            int prevFirstLetterCol = (c * FIRST_LETTER_COLS) / GRID_COLS;
                            int prevFirstLetterRow = (r * FIRST_LETTER_ROWS) / GRID_ROWS;
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
                    
                    cellIndex++;
                }
            }
        }
        
        private void GridOverlayForm_Paint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            
            // Draw grid lines
            Pen gridPen = new Pen(Color.White, 1);
            
            // Vertical lines
            for (int col = 0; col <= GRID_COLS; col++)
            {
                int x = col * cellWidth;
                g.DrawLine(gridPen, x, 0, x, this.Height);
            }
            
            // Horizontal lines
            for (int row = 0; row <= GRID_ROWS; row++)
            {
                int y = row * cellHeight;
                g.DrawLine(gridPen, 0, y, this.Width, y);
            }
            
            gridPen.Dispose();
            
            // Draw cell labels
            Font labelFont = new Font("Arial", 14, FontStyle.Bold);
            Brush textBrush = new SolidBrush(Color.White);
            StringFormat format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            
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
                    col * cellWidth,
                    row * cellHeight,
                    cellWidth,
                    cellHeight
                );
                
                g.DrawString(label, labelFont, textBrush, cellRect, format);
            }
            
            labelFont.Dispose();
            textBrush.Dispose();
            format.Dispose();
        }
        
        private void GridOverlayForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (firstLetter == null)
            {
                // First letter
                if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z)
                {
                    firstLetter = e.KeyCode.ToString().ToUpper();
                    this.Invalidate(); // Redraw to show filtered cells
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
                        // Calculate cell center
                        int centerX = (cell.Key.col * cellWidth) + (cellWidth / 2);
                        int centerY = (cell.Key.row * cellHeight) + (cellHeight / 2);
                        
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
                    this.Invalidate();
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
            this.Invalidate();
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

