using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

namespace SquareSnap
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Bitmap? capturedImage;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void RegionButton_Click(object sender, RoutedEventArgs e)
        {
            CaptureRegion();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveImage();
        }

        private void CaptureRegion()
        {
            try
            {
                // Hide the window during capture
                this.WindowState = WindowState.Minimized;
                
                // Give time for the window to minimize
                System.Threading.Thread.Sleep(300);

                // Create a form for region selection
                using (RegionCaptureForm regionForm = new RegionCaptureForm())
                {
                    // Show the form and wait for result
                    if (regionForm.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        // Get the selected region
                        Bitmap selectedRegion = regionForm.CapturedRegion;
                        
                        if (selectedRegion != null)
                        {
                            // Process the image to ensure 1:1 ratio with padding
                            capturedImage = ProcessImageToSquare(selectedRegion);
                            
                            // Display the captured image
                            DisplayCapturedImage();
                            
                            // Update status
                            StatusText.Text = "Region captured";
                            UpdateImageInfo();
                        }
                        else
                        {
                            StatusText.Text = "Region capture canceled";
                        }
                    }
                    else
                    {
                        StatusText.Text = "Region capture canceled";
                    }
                }

                // Restore window
                this.WindowState = WindowState.Normal;
            }
            catch (Exception ex)
            {
                this.WindowState = WindowState.Normal;
                StatusText.Text = "Error capturing region";
                System.Windows.MessageBox.Show($"Error capturing region: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Bitmap ProcessImageToSquare(Bitmap originalImage)
        {
            // Since the region selection is already enforced to be square,
            // we just need to verify and return the original image
            
            // Verify the dimensions are equal
            if (originalImage.Width != originalImage.Height)
            {
                // If for some reason the image is not square, make it square
                // by using the larger dimension
                int size = Math.Max(originalImage.Width, originalImage.Height);
                Bitmap squareImage = new Bitmap(size, size);
                
                using (Graphics g = Graphics.FromImage(squareImage))
                {
                    // Draw the original image in the center
                    int x = (size - originalImage.Width) / 2;
                    int y = (size - originalImage.Height) / 2;
                    g.DrawImage(originalImage, x, y, originalImage.Width, originalImage.Height);
                    
                    // Draw a border to visualize the square boundaries
                    using (System.Drawing.Pen pen = new System.Drawing.Pen(System.Drawing.Color.LightGray, 1))
                    {
                        g.DrawRectangle(pen, 0, 0, size - 1, size - 1);
                    }
                }
                
                return squareImage;
            }
            
            // If the image is already square, just add a border
            Bitmap result = new Bitmap(originalImage);
            using (Graphics g = Graphics.FromImage(result))
            {
                // Draw a border to visualize the square boundaries
                using (System.Drawing.Pen pen = new System.Drawing.Pen(System.Drawing.Color.LightGray, 1))
                {
                    g.DrawRectangle(pen, 0, 0, result.Width - 1, result.Height - 1);
                }
            }
            
            return result;
        }

        private void DisplayCapturedImage()
        {
            if (capturedImage != null)
            {
                // Convert Bitmap to BitmapImage for WPF display
                BitmapImage bitmapImage = new BitmapImage();
                using (MemoryStream memory = new MemoryStream())
                {
                    capturedImage.Save(memory, ImageFormat.Png);
                    memory.Position = 0;
                    bitmapImage.BeginInit();
                    bitmapImage.StreamSource = memory;
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze(); // Important for cross-thread access
                }
                
                // Set the image source
                PreviewImage.Source = bitmapImage;
                
                // Enable save button
                SaveButton.IsEnabled = true;
            }
        }

        private void UpdateImageInfo()
        {
            if (capturedImage != null)
            {
                double aspectRatio = (double)capturedImage.Width / capturedImage.Height;
                string aspectRatioText = aspectRatio == 1.0 ? "1:1 (Square)" : $"{aspectRatio:F2}:1";
                
                ImageInfoText.Text = $"{capturedImage.Width} x {capturedImage.Height} pixels | Aspect Ratio: {aspectRatioText}";
            }
            else
            {
                ImageInfoText.Text = string.Empty;
            }
        }

        private void SaveImage()
        {
            if (capturedImage == null)
            {
                System.Windows.MessageBox.Show("No image to save", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Create save file dialog
            Microsoft.Win32.SaveFileDialog saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG Image|*.png",
                Title = "Save Snapshot",
                DefaultExt = ".png",
                FileName = Settings.Instance.GetNextFileName() // Use default naming scheme
            };

            // Set initial directory if default save location is set
            if (!string.IsNullOrEmpty(Settings.Instance.DefaultSaveLocation) && 
                Directory.Exists(Settings.Instance.DefaultSaveLocation))
            {
                saveDialog.InitialDirectory = Settings.Instance.DefaultSaveLocation;
            }

            // Show save file dialog
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    // Save the image
                    capturedImage.Save(saveDialog.FileName, ImageFormat.Png);
                    StatusText.Text = $"Image saved to {saveDialog.FileName}";
                }
                catch (Exception ex)
                {
                    StatusText.Text = "Error saving image";
                    System.Windows.MessageBox.Show($"Error saving image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SetDefaultSaveLocationMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Create folder browser dialog
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                // Set initial directory if default save location is already set
                if (!string.IsNullOrEmpty(Settings.Instance.DefaultSaveLocation) && 
                    Directory.Exists(Settings.Instance.DefaultSaveLocation))
                {
                    dialog.InitialDirectory = Settings.Instance.DefaultSaveLocation;
                }

                dialog.Description = "Select Default Save Location";
                dialog.UseDescriptionForTitle = true;
                
                // Show dialog
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    // Save selected folder as default save location
                    Settings.Instance.DefaultSaveLocation = dialog.SelectedPath;
                    Settings.Instance.Save();
                    
                    // Show confirmation
                    StatusText.Text = $"Default save location set to: {dialog.SelectedPath}";
                }
            }
        }

        // Menu item click handlers
        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }
        
        private void LightModeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            bool isLightMode = LightModeMenuItem.IsChecked;
            ApplyTheme(!isLightMode); // Invert because we're now tracking light mode
        }
        
        private void ReadMeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Create a window to display the README content
            Window readmeWindow = new Window
            {
                Title = "SquareSnap - Read Me",
                Width = 600,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };
            
            // Create a text box to display the README content
            System.Windows.Controls.TextBox textBox = new System.Windows.Controls.TextBox
            {
                IsReadOnly = true,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Margin = new Thickness(10)
            };
            
            // Load README content
            try
            {
                string readmePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "README.md");
                if (File.Exists(readmePath))
                {
                    textBox.Text = File.ReadAllText(readmePath);
                }
                else
                {
                    // Embedded README content if file doesn't exist
                    textBox.Text = "# SquareSnap\n\nSquareSnap is a Windows 11 application that takes screen snapshots with a 1:1 aspect ratio (square format).\n\n" +
                        "## Features\n\n- Square Region Selection: Enforces a 1:1 aspect ratio during region selection\n" +
                        "- 1:1 Aspect Ratio: Ensures all captures are perfectly square\n" +
                        "- PNG Output: Saves high-quality images in PNG format\n" +
                        "- Preview: Shows the captured image before saving\n\n" +
                        "## How to Use\n\n1. Click \"Capture Square Region\" to select an area of the screen\n" +
                        "2. Click and drag to select the region (selection is automatically made square)\n" +
                        "3. Click \"Save Image\" to save the captured image\n";
                }
            }
            catch (Exception ex)
            {
                textBox.Text = $"Error loading README: {ex.Message}";
            }
            
            readmeWindow.Content = textBox;
            readmeWindow.ShowDialog();
        }
        
        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show(
                "SquareSnap\n\nDeveloped by Richard Bejtlich using Visual Studio Code and Cline.\n\nVersion 1.0",
                "About SquareSnap",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        
        // Apply theme (light or dark)
        private void ApplyTheme(bool isDarkMode)
        {
            // Get the appropriate style
            var themeName = isDarkMode ? "DarkTheme" : "LightTheme";
            var theme = (Style)FindResource(themeName);
            
            // Apply background color to main grid
            MainGrid.Background = isDarkMode ? 
                new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2D2D30")) : 
                new SolidColorBrush(Colors.White);
            
            // Apply border color to preview border
            PreviewBorder.BorderBrush = isDarkMode ? 
                new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3F3F46")) : 
                new SolidColorBrush(Colors.Gray);
            
            // Apply text colors with high contrast
            StatusText.Foreground = new SolidColorBrush(Colors.Black);
            ImageInfoText.Foreground = new SolidColorBrush(Colors.Black);
            
            // Set status bar background to light color for contrast with black text
            var statusBar = FindVisualParent<System.Windows.Controls.Primitives.StatusBar>(StatusText);
            if (statusBar != null)
            {
                statusBar.Background = new SolidColorBrush(Colors.LightGray);
            }
        }
        
        // Add a context menu or button for saving
        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            
            // Apply dark theme by default
            ApplyTheme(true);
            
            // Add a context menu to the image
            System.Windows.Controls.ContextMenu contextMenu = new System.Windows.Controls.ContextMenu();
            System.Windows.Controls.MenuItem saveMenuItem = new System.Windows.Controls.MenuItem
            {
                Header = "Save Image"
            };
            saveMenuItem.Click += (s, args) => SaveImage();
            contextMenu.Items.Add(saveMenuItem);
            
            PreviewImage.ContextMenu = contextMenu;
            
            // Also add a key binding for Ctrl+S
            System.Windows.Input.KeyBinding saveKeyBinding = new System.Windows.Input.KeyBinding
            {
                Key = System.Windows.Input.Key.S,
                Modifiers = System.Windows.Input.ModifierKeys.Control,
                Command = new SaveCommand(SaveImage)
            };
            this.InputBindings.Add(saveKeyBinding);
        }
        // Helper method to find a parent of a specific type
        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = System.Windows.Media.VisualTreeHelper.GetParent(child);
            
            if (parentObject == null)
                return null;
            
            if (parentObject is T parent)
                return parent;
            
            return FindVisualParent<T>(parentObject);
        }
    }

    // Command for save functionality
    public class SaveCommand : System.Windows.Input.ICommand
    {
        private readonly Action _execute;

        public SaveCommand(Action execute)
        {
            _execute = execute;
        }

        public bool CanExecute(object? parameter)
        {
            return true;
        }

        public void Execute(object? parameter)
        {
            _execute();
        }

        public event EventHandler? CanExecuteChanged;
    }
}
