mkdir ..\bin\TestBinaries\x86
mkdir ..\bin\TestBinaries\x64
cmake -A Win32 -B ..\bin\TestBinaries\x86
cmake -A x64 -B ..\bin\TestBinaries\x64
cmake --build ..\bin\TestBinaries\x86 --config Release
cmake --build ..\bin\TestBinaries\x64 --config Release