del .\Releases\*.* /S /Q
md Releases
del .\bin\Release\*.* /S /Q
rd .\bin\Release /S /Q


REM .NET (framework-dependent — requires .NET 9 Desktop Runtime on the user's machine)
REM ffmpeg is a soft dependency: not bundled. Users install it themselves
REM (winget install Gyan.FFmpeg) to get seekable webm, .mp4 transcode, and -a mp3 extraction.
dotnet publish .\SharePointVideoDownloader.sln -c Release
powershell -command "Compress-Archive -Path .\bin\Release\net9.0\publish\* -DestinationPath .\Releases\SharePointVideoDownloader-v02.00-DotNet.zip"
del .\bin\Release\*.* /S /Q
rd .\bin\Release /S /Q

REM Windows x64 self-contained (bundles the .NET runtime; no separate install needed)
dotnet publish .\SharePointVideoDownloader.sln -c Release -r win-x64 --self-contained
powershell -command "Compress-Archive -Path .\bin\Release\net9.0\win-x64\publish\* -DestinationPath .\Releases\SharePointVideoDownloader-v02.00-x64-Self-Contained.zip"
del .\bin\Release\*.* /S /Q
rd .\bin\Release /S /Q

REM Windows x86 self-contained
dotnet publish .\SharePointVideoDownloader.sln -c Release -r win-x86 --self-contained
powershell -command "Compress-Archive -Path .\bin\Release\net9.0\win-x86\publish\* -DestinationPath .\Releases\SharePointVideoDownloader-v02.00-x86-Self-Contained.zip"
del .\bin\Release\*.* /S /Q
rd .\bin\Release /S /Q

REM Windows ARM 64 self-contained
dotnet publish .\SharePointVideoDownloader.sln -c Release -r win-arm64 --self-contained
powershell -command "Compress-Archive -Path .\bin\Release\net9.0\win-arm64\publish\* -DestinationPath .\Releases\SharePointVideoDownloader-v02.00-ARM64-Self-Contained.zip"
del .\bin\Release\*.* /S /Q
rd .\bin\Release /S /Q
