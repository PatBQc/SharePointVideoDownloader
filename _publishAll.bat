del .\Releases\*.* /S /Q
md Releases 
del .\bin\Release\*.* /S /Q
rd .\bin\Release\*.* /S /Q


REM .Net Version
dotnet publish .\SharePointVideoDownloader.sln -c Release
powershell -command "Compress-Archive -Path .\bin\Release\net9.0\publish\* -DestinationPath .\Releases\SharePointVideoDownloader-v01.01-DotNet.zip"
copy .\Dependencies\*.* .\bin\Release\net9.0\publish\
powershell -command "Compress-Archive -Path .\bin\Release\net9.0\publish\* -DestinationPath .\Releases\SharePointVideoDownloader-v01.01-DotNet-Dependencies.zip"
del .\bin\Release\*.* /S /Q
rd .\bin\Release\*.* /S /Q

REM Windows x64
dotnet publish .\SharePointVideoDownloader.sln -c Release -r win-x64 --self-contained
powershell -command "Compress-Archive -Path .\bin\Release\net9.0\win-x64\publish\* -DestinationPath .\Releases\SharePointVideoDownloader-v01.01-x64-Self-Contained.zip"
REM copy .\Dependencies\*.* .\bin\Release\net9.0\win-x64\publish\
REM powershell -command "Compress-Archive -Path .\bin\Release\net9.0\win-x64\publish\* -DestinationPath .\Releases\SharePointVideoDownloader-v01.01-x64-Self-Contained-Dependencies.zip"
del .\bin\Release\*.* /S /Q
rd .\bin\Release\*.* /S /Q

REM Windows x86
dotnet publish .\SharePointVideoDownloader.sln -c Release -r win-x86 --self-contained
powershell -command "Compress-Archive -Path .\bin\Release\net9.0\win-x86\publish\* -DestinationPath .\Releases\SharePointVideoDownloader-v01.01-x86-Self-Contained.zip"
REM copy .\Dependencies\*.* .\bin\Release\net9.0\win-x86\publish\
REM powershell -command "Compress-Archive -Path .\bin\Release\net9.0\win-x86\publish\* -DestinationPath .\Releases\SharePointVideoDownloader-v01.01-x86-Self-Contained-Dependencies.zip"
del .\bin\Release\*.* /S /Q
rd .\bin\Release\*.* /S /Q

REM Windows ARM 64 
dotnet publish .\SharePointVideoDownloader.sln -c Release -r win-arm64 --self-contained
powershell -command "Compress-Archive -Path .\bin\Release\net9.0\win-arm64\publish\* -DestinationPath .\Releases\SharePointVideoDownloader-v01.01-ARM64-Self-Contained.zip"
REM copy .\Dependencies\*.* .\bin\Release\net9.0\win-arm64\publish\
REM powershell -command "Compress-Archive -Path .\bin\Release\net9.0\win-arm64\publish\* -DestinationPath .\Releases\SharePointVideoDownloader-v01.01-ARM64-Self-Contained-Dependencies.zip"
del .\bin\Release\*.* /S /Q
rd .\bin\Release\*.* /S /Q