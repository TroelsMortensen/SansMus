using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SansMus
{
    public partial class OrbitalVisualizationForm : Form
    {
        // Windows API for layered window support
        private const int WS_EX_LAYERED = 0x80000;
        private const int GWL_EXSTYLE = -20;
        private const uint ULW_ALPHA = 0x2;
        private const double ROTATION_POINT_DISTANCE = 25.0; // Distance behind cursor
        
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
        
        private System.Windows.Forms.Timer? updateTimer;
        private double currentHeading = 0.0;
        private Point currentCursorPos;
        private double visualizationOpacity = 0.7;
        private Screen? currentScreen;
        
        public OrbitalVisualizationForm()
        {
            InitializeComponent();
            StartUpdateTimer();
        }
        
        private void InitializeComponent()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Normal;
            this.StartPosition = FormStartPosition.Manual;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            
            this.BackColor = Color.Black;
            this.Opacity = 1.0;
            this.DoubleBuffered = true;
            
            // Enable proper painting for alpha blending
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            
            // Set up layered window after form is loaded
            this.Load += OrbitalVisualizationForm_Load;
            
            // Initialize to current cursor screen
            UpdateScreen();
        }
        
        private void UpdateScreen()
        {
            Point cursorPos = Cursor.Position;
            Screen? screenToUse = Screen.FromPoint(cursorPos);
            
            if (screenToUse != null && (currentScreen == null || !screenToUse.Bounds.Equals(currentScreen.Bounds)))
            {
                currentScreen = screenToUse;
                Rectangle screenBounds = screenToUse.Bounds;
                
                this.Location = new Point(screenBounds.Left, screenBounds.Top);
                this.Size = new Size(screenBounds.Width, screenBounds.Height);
            }
        }
        
        private void OrbitalVisualizationForm_Load(object? sender, EventArgs e)
        {
            // Enable layered window style for per-pixel alpha transparency
            int exStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);
            exStyle |= WS_EX_LAYERED;
            SetWindowLong(this.Handle, GWL_EXSTYLE, exStyle);
            
            // Update the layered window with initial bitmap
            UpdateLayeredWindowBitmap();
        }
        
        public void UpdateCursorInfo(Point cursorPos, double heading)
        {
            currentCursorPos = cursorPos;
            currentHeading = heading;
            
            // Check if cursor moved to different screen
            UpdateScreen();
        }
        
        private void StartUpdateTimer()
        {
            if (updateTimer == null)
            {
                updateTimer = new System.Windows.Forms.Timer();
                updateTimer.Interval = 16; // ~60fps
                updateTimer.Tick += UpdateTimer_Tick;
            }
            
            if (!updateTimer.Enabled)
            {
                updateTimer.Start();
            }
        }
        
        private void StopUpdateTimer()
        {
            if (updateTimer != null && updateTimer.Enabled)
            {
                updateTimer.Stop();
            }
        }
        
        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            UpdateLayeredWindowBitmap();
        }
        
        private void UpdateLayeredWindowBitmap()
        {
            if (this.Width <= 0 || this.Height <= 0)
                return;
            
            using (Bitmap bitmap = new Bitmap(this.Width, this.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    // Clear to fully transparent
                    g.Clear(Color.Transparent);
                    
                    // Calculate rotation point (25px behind cursor in opposite direction of heading)
                    double headingRad = currentHeading * Math.PI / 180.0;
                    double rotationPointX = currentCursorPos.X - ROTATION_POINT_DISTANCE * Math.Sin(headingRad);
                    double rotationPointY = currentCursorPos.Y + ROTATION_POINT_DISTANCE * Math.Cos(headingRad);
                    
                    // Convert to screen-relative coordinates (form coordinates)
                    Point formLocation = this.Location;
                    float rotationPointFormX = (float)(rotationPointX - formLocation.X);
                    float rotationPointFormY = (float)(rotationPointY - formLocation.Y);
                    float cursorFormX = (float)(currentCursorPos.X - formLocation.X);
                    float cursorFormY = (float)(currentCursorPos.Y - formLocation.Y);
                    
                    // Check if rotation point and cursor are within form bounds
                    if (rotationPointFormX >= 0 && rotationPointFormX < this.Width &&
                        rotationPointFormY >= 0 && rotationPointFormY < this.Height)
                    {
                        // Draw circle at rotation point (priority)
                        int circleRadius = 6;
                        int alpha = (int)(visualizationOpacity * 255);
                        Color circleColor = Color.FromArgb(alpha, Color.White);
                        
                        using (Pen circlePen = new Pen(circleColor, 2.0f))
                        {
                            g.DrawEllipse(circlePen, 
                                rotationPointFormX - circleRadius, 
                                rotationPointFormY - circleRadius, 
                                circleRadius * 2, 
                                circleRadius * 2);
                        }
                        
                        // Draw line from rotation point to cursor (optional)
                        if (cursorFormX >= 0 && cursorFormX < this.Width &&
                            cursorFormY >= 0 && cursorFormY < this.Height)
                        {
                            using (Pen linePen = new Pen(circleColor, 1.5f))
                            {
                                g.DrawLine(linePen, 
                                    rotationPointFormX, rotationPointFormY,
                                    cursorFormX, cursorFormY);
                            }
                            
                            // Draw small arrowhead at cursor end
                            double lineAngle = Math.Atan2(cursorFormY - rotationPointFormY, cursorFormX - rotationPointFormX);
                            float arrowSize = 4.0f;
                            float arrowAngle = (float)(Math.PI / 6.0); // 30 degrees
                            
                            PointF[] arrowPoints = new PointF[]
                            {
                                new PointF(cursorFormX, cursorFormY),
                                new PointF(
                                    cursorFormX - arrowSize * (float)Math.Cos(lineAngle - arrowAngle),
                                    cursorFormY - arrowSize * (float)Math.Sin(lineAngle - arrowAngle)
                                ),
                                new PointF(
                                    cursorFormX - arrowSize * (float)Math.Cos(lineAngle + arrowAngle),
                                    cursorFormY - arrowSize * (float)Math.Sin(lineAngle + arrowAngle)
                                )
                            };
                            
                            using (SolidBrush arrowBrush = new SolidBrush(circleColor))
                            {
                                g.FillPolygon(arrowBrush, arrowPoints);
                            }
                        }
                    }
                }
                
                // Update layered window
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
        
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopUpdateTimer();
            if (updateTimer != null)
            {
                updateTimer.Dispose();
                updateTimer = null;
            }
            base.OnFormClosing(e);
        }
    }
}

