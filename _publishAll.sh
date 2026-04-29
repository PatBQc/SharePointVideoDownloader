#!/bin/bash
# Cross-platform publish script — companion to _publishAll.bat for Linux/macOS.
# Originally contributed by https://github.com/mmueller22/SharePointVideoDownloader
# and adapted to v02.00 (no bundled yt-dlp; ffmpeg is a soft runtime dependency
# that the user installs separately).

set -e

# Clean previous artefacts.
rm -rf ./Releases/*
mkdir -p Releases
rm -rf ./bin/Release/*

# Framework-dependent (.NET Desktop Runtime 9 required on the user's machine)
echo "Building .NET framework-dependent version..."
dotnet publish ./SharePointVideoDownloader.sln -c Release
( cd ./bin/Release/net9.0/publish && zip -r ../../../../Releases/SharePointVideoDownloader-v02.00-DotNet.zip . )
rm -rf ./bin/Release/*

# macOS x64 (Intel)
echo "Building macOS x64 self-contained version..."
dotnet publish ./SharePointVideoDownloader.sln -c Release -r osx-x64 --self-contained
( cd ./bin/Release/net9.0/osx-x64/publish && zip -r ../../../../../Releases/SharePointVideoDownloader-v02.00-macOS-x64-Self-Contained.zip . )
rm -rf ./bin/Release/*

# macOS ARM64 (Apple Silicon)
echo "Building macOS ARM64 self-contained version..."
dotnet publish ./SharePointVideoDownloader.sln -c Release -r osx-arm64 --self-contained
( cd ./bin/Release/net9.0/osx-arm64/publish && zip -r ../../../../../Releases/SharePointVideoDownloader-v02.00-macOS-ARM64-Self-Contained.zip . )
rm -rf ./bin/Release/*

# Linux x64
echo "Building Linux x64 self-contained version..."
dotnet publish ./SharePointVideoDownloader.sln -c Release -r linux-x64 --self-contained
( cd ./bin/Release/net9.0/linux-x64/publish && zip -r ../../../../../Releases/SharePointVideoDownloader-v02.00-linux-x64-Self-Contained.zip . )
rm -rf ./bin/Release/*

# Windows x64
echo "Building Windows x64 self-contained version..."
dotnet publish ./SharePointVideoDownloader.sln -c Release -r win-x64 --self-contained
( cd ./bin/Release/net9.0/win-x64/publish && zip -r ../../../../../Releases/SharePointVideoDownloader-v02.00-win-x64-Self-Contained.zip . )
rm -rf ./bin/Release/*

# Windows x86
echo "Building Windows x86 self-contained version..."
dotnet publish ./SharePointVideoDownloader.sln -c Release -r win-x86 --self-contained
( cd ./bin/Release/net9.0/win-x86/publish && zip -r ../../../../../Releases/SharePointVideoDownloader-v02.00-win-x86-Self-Contained.zip . )
rm -rf ./bin/Release/*

# Windows ARM64
echo "Building Windows ARM64 self-contained version..."
dotnet publish ./SharePointVideoDownloader.sln -c Release -r win-arm64 --self-contained
( cd ./bin/Release/net9.0/win-arm64/publish && zip -r ../../../../../Releases/SharePointVideoDownloader-v02.00-win-ARM64-Self-Contained.zip . )
rm -rf ./bin/Release/*

echo
echo "All builds completed successfully!"
echo "Release packages are in the Releases/ folder."
echo
echo "Reminder: ffmpeg is a soft runtime dependency. Users without ffmpeg in"
echo "PATH will get raw .webm output from capture mode (no seek index, no"
echo "transcode, no -a mp3). Install ffmpeg via:"
echo "  Windows: winget install Gyan.FFmpeg"
echo "  macOS:   brew install ffmpeg"
echo "  Linux:   apt install ffmpeg  /  dnf install ffmpeg  /  pacman -S ffmpeg"
