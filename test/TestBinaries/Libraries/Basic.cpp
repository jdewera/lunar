#include <Windows.h>

bool __stdcall DllMain(void* module_handle, const unsigned long reason, void* reserved)
{
    return reason == DLL_PROCESS_ATTACH || reason == DLL_PROCESS_DETACH;
}