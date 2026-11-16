using System;
using System.Drawing;
using System.Windows.Forms;

namespace SquareSnap
{
    /// <summary>
    /// Form for selecting a region of the screen to capture
    /// </summary>
    public class RegionCaptureForm : Form
    {
        private Point startPoint;
        private Point endPoint;
        private bool isSelecting;
        private Bitmap screenCapture;
        private Rectangle selectionRectangle;

        public Bitmap CapturedRegion { get; private set; }

        public RegionCaptureForm()
        {
            // Initialize form properties
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.Cursor = Cursors.Cross;
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
            this.Opacity = 0.3;
            this.ShowInTaskbar = false;
            this.TopMost = true;

            // Capture the entire screen
            CaptureScreen();

            // Set up event handlers
            this.MouseDown += RegionCaptureForm_MouseDown;
            this.MouseMove += RegionCaptureForm_MouseMove;
            this.MouseUp += RegionCaptureForm_MouseUp;
            this.Paint += RegionCaptureForm_Paint;
            this.KeyDown += RegionCaptureForm_KeyDown;

            // Instructions
            this.Text = "Click and drag to select a region. Press Esc to cancel.";
        }

        private void CaptureScreen()
        {
            // Get screen dimensions
            int screenWidth = Screen.PrimaryScreen.Bounds.Width;
            int screenHeight = Screen.PrimaryScreen.Bounds.Height;

            // Create bitmap to hold the screenshot
            screenCapture = new Bitmap(screenWidth, screenHeight);

            // Create graphics object from bitmap
            using (Graphics g = Graphics.FromImage(screenCapture))
            {
                // Capture screen
                g.CopyFromScreen(Point.Empty, Point.Empty, new Size(screenWidth, screenHeight));
            }
        }

        private void RegionCaptureForm_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isSelecting = true;
                startPoint = e.Location;
                endPoint = e.Location;
                selectionRectangle = Rectangle.Empty;
                this.Invalidate();
            }
        }

        private void RegionCaptureForm_MouseMove(object sender, MouseEventArgs e)
        {
            if (isSelecting)
            {
                endPoint = e.Location;
                selectionRectangle = GetSelectionRectangle();
                this.Invalidate();
            }
        }

        private void RegionCaptureForm_MouseUp(object sender, MouseEventArgs e)
        {
            if (isSelecting && e.Button == MouseButtons.Left)
            {
                isSelecting = false;
                endPoint = e.Location;
                selectionRectangle = GetSelectionRectangle();

                if (selectionRectangle.Width > 0 && selectionRectangle.Height > 0)
                {
                    // Capture the selected region
                    CaptureRegion();
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
            }
        }

        private void RegionCaptureForm_Paint(object sender, PaintEventArgs e)
        {
            if (selectionRectangle != Rectangle.Empty)
            {
                // Draw selection rectangle
                using (Pen pen = new Pen(Color.Red, 2))
                {
                    e.Graphics.DrawRectangle(pen, selectionRectangle);
                }

                // Fill the area outside the selection with semi-transparent color
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(120, Color.Black)))
                {
                    // Top region
                    e.Graphics.FillRectangle(brush, 0, 0, this.Width, selectionRectangle.Top);
                    // Left region
                    e.Graphics.FillRectangle(brush, 0, selectionRectangle.Top, selectionRectangle.Left, selectionRectangle.Height);
                    // Right region
                    e.Graphics.FillRectangle(brush, selectionRectangle.Right, selectionRectangle.Top, this.Width - selectionRectangle.Right, selectionRectangle.Height);
                    // Bottom region
                    e.Graphics.FillRectangle(brush, 0, selectionRectangle.Bottom, this.Width, this.Height - selectionRectangle.Bottom);
                }

                // Display selection size and indicate it's square
                string sizeText = $"{selectionRectangle.Width} x {selectionRectangle.Height} (1:1 Square)";
                using (Font font = new Font("Arial", 10))
                using (SolidBrush brush = new SolidBrush(Color.White))
                using (SolidBrush shadowBrush = new SolidBrush(Color.Black))
                {
                    Point textPosition = new Point(selectionRectangle.Right - 100, selectionRectangle.Bottom + 5);
                    e.Graphics.DrawString(sizeText, font, shadowBrush, textPosition.X + 1, textPosition.Y + 1);
                    e.Graphics.DrawString(sizeText, font, brush, textPosition);
                }
            }
        }

        private void RegionCaptureForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            }
        }

        private Rectangle GetSelectionRectangle()
        {
            // Calculate the basic rectangle based on start and end points
            int x = Math.Min(startPoint.X, endPoint.X);
            int y = Math.Min(startPoint.Y, endPoint.Y);
            int width = Math.Abs(startPoint.X - endPoint.X);
            int height = Math.Abs(startPoint.Y - endPoint.Y);
            
            // Enforce square selection by using the larger dimension
            int size = Math.Max(width, height);
            
            // Adjust the rectangle to be square while maintaining the starting corner
            if (startPoint.X < endPoint.X)
            {
                // Dragging right
                if (startPoint.Y < endPoint.Y)
                {
                    // Dragging down-right
                    return new Rectangle(x, y, size, size);
                }
                else
                {
                    // Dragging up-right
                    return new Rectangle(x, y - (size - height), size, size);
                }
            }
            else
            {
                // Dragging left
                if (startPoint.Y < endPoint.Y)
                {
                    // Dragging down-left
                    return new Rectangle(x - (size - width), y, size, size);
                }
                else
                {
                    // Dragging up-left
                    return new Rectangle(x - (size - width), y - (size - height), size, size);
                }
            }
        }

        private void CaptureRegion()
        {
            if (selectionRectangle.Width > 0 && selectionRectangle.Height > 0)
            {
                // Create a new bitmap for the selected region
                CapturedRegion = new Bitmap(selectionRectangle.Width, selectionRectangle.Height);

                // Copy the selected region from the screen capture
                using (Graphics g = Graphics.FromImage(CapturedRegion))
                {
                    g.DrawImage(screenCapture, 
                                new Rectangle(0, 0, CapturedRegion.Width, CapturedRegion.Height), 
                                selectionRectangle, 
                                GraphicsUnit.Pixel);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                screenCapture?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
