# SquareSnap Development Log

This document outlines the development process and key decisions made during the creation of the SquareSnap application.

## Project Overview

SquareSnap is a Windows 11 application that takes screen snapshots with a 1:1 aspect ratio (square format). It was developed using C# and WPF (.NET 6.0).

## Development Timeline

### Initial Setup
- Created a new WPF application project
- Set up the main window with a square aspect ratio (600x600 pixels)
- Designed the basic UI layout with capture and save buttons

### Core Functionality
- Implemented screen region capture functionality
- Added logic to enforce 1:1 aspect ratio during region selection
- Created the image processing pipeline to handle captured images
- Implemented save functionality with PNG format

### UI Enhancements
- Added dark mode as the default theme
- Implemented light mode toggle option
- Designed status bar with high-contrast text
- Added image information display (dimensions and aspect ratio)

### Advanced Features
- Implemented settings persistence using JSON serialization
- Added default save location option
- Created automatic file naming scheme using "SquareCap-YYYYMMDD###" format
- Added keyboard shortcuts (Ctrl+S for save)

### Finalization
- Added README.md documentation
- Created a stand-alone executable using self-contained deployment
- Implemented menu system with File, Options, and Help sections
- Added About and Read Me dialogs

## Key Components

### MainWindow.xaml.cs
The main application window that handles the UI and coordinates the capture and save operations.

### RegionCaptureForm.cs
A Windows Forms component that handles the screen region selection with square constraint.

### Settings.cs
Manages application settings, including default save location and file numbering.

## Design Decisions

### Square Aspect Ratio
- Enforced 1:1 aspect ratio during capture to ensure perfect squares
- Used real-time constraint during region selection for better user experience

### Dark Mode Default
- Implemented dark mode as the default for better visibility during screen captures
- Added light mode option for user preference

### File Naming Scheme
- Used "SquareCap-YYYYMMDD###" format for organized file management
- Implemented automatic incrementing of sequence numbers

### Stand-alone Executable
- Created a self-contained executable that includes all dependencies
- Ensures the application can run on any Windows machine without requiring .NET installation
