# SquareSnap

SquareSnap is a Windows 11 application that takes screen snapshots with a 1:1 aspect ratio (square format).

## Features

- **Square Region Selection**: Enforces a 1:1 aspect ratio during region selection
- **1:1 Aspect Ratio**: Ensures all captures are perfectly square
- **PNG Output**: Saves high-quality images in PNG format
- **Preview**: Shows the captured image before saving
- **Dark Mode by Default**: Toggle to light theme if desired
- **Square Interface**: The application window itself uses a square ratio
- **Default Save Location**: Set a preferred folder for saving captures
- **Automatic File Naming**: Uses format "SquareCap-YYYYMMDD###" for organized file management

## How to Use

1. **Launch the Application**: Run SquareSnap.exe
2. **Capture a Region**:
   - Click "Capture Square Region" to select an area of the screen
   - Click and drag to select the region (selection is automatically made square)
3. **Save the Image**:
   - Click the "Save Image" button
   - Choose a location to save the PNG file
   - The image is saved in PNG format with the square aspect ratio preserved

## Keyboard Shortcuts

- **Ctrl+S**: Save the current image
- **Esc**: Cancel region selection (when in region selection mode)

## Technical Details

- Built with .NET 6.0 and WPF
- Uses System.Drawing for screen capture
- Enforces square selection during capture
- Displays aspect ratio information in the status bar
- Adds a light gray border to visualize the square boundaries

## Building from Source

1. Ensure you have .NET 6.0 SDK installed
2. Clone or download the repository
3. Open a terminal in the project directory
4. Run `dotnet build` to build the application
5. Run `dotnet run` to start the application

## Requirements

- Windows 11 or Windows 10
- .NET 6.0 Runtime

## About

Developed by Richard Bejtlich using Visual Studio Code and Cline.
