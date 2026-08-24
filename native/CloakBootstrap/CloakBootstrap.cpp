// Native DLL injected into the target game process via classic LoadLibrary-based injection
// (Reloaded.Injector, called from MotionInput.Core). Its only job is to host the .NET runtime
// (via the documented nethost/hostfxr native-hosting APIs) and load the managed cloak payload
// (MotionInput.Cloak.Payload.dll), which does the actual XInput hooking.
//
// This exists because a plain `dotnet build` managed assembly is not a LoadLibrary-able native
// module on modern .NET (no CLR is registered system-wide the way .NET Framework's was) - a
// small native host is the standard, Microsoft-documented way to bridge that gap. See:
// https://learn.microsoft.com/dotnet/core/tutorials/netcore-hosting

#include <windows.h>
#include <string>
#include <vector>
#include <cstdio>

#include "nethost.h"
#include "hostfxr.h"
#include "coreclr_delegates.h"

namespace {

// Debug-only: an injected DLL has no console, so this is the only way to see what happened.
void Log(const char* msg) {
    FILE* f = nullptr;
    if (fopen_s(&f, "C:\\Temp\\236KO_cloak_debug.log", "a") == 0 && f) {
        fprintf(f, "[pid=%lu tid=%lu] %s\n", GetCurrentProcessId(), GetCurrentThreadId(), msg);
        fclose(f);
    }
}

using hostfxr_initialize_for_runtime_config_fn = int (*)(const char_t*, const void*, hostfxr_handle*);
using hostfxr_get_runtime_delegate_fn = int (*)(hostfxr_handle, hostfxr_delegate_type, void**);
using hostfxr_close_fn = int (*)(hostfxr_handle);

using load_assembly_and_get_function_pointer_fn = int (*)(
    const char_t* assembly_path,
    const char_t* type_name,
    const char_t* method_name,
    const char_t* delegate_type_name,
    void* reserved,
    void** delegate);

using install_cloak_fn = int (STDMETHODCALLTYPE*)(void* arg, int32_t argSizeInBytes);

std::wstring ModuleDirectory() {
    wchar_t path[MAX_PATH];
    HMODULE hModule = nullptr;
    GetModuleHandleExW(
        GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&ModuleDirectory), &hModule);
    GetModuleFileNameW(hModule, path, MAX_PATH);

    std::wstring full(path);
    auto pos = full.find_last_of(L"\\/");
    return pos == std::wstring::npos ? L"." : full.substr(0, pos);
}

hostfxr_initialize_for_runtime_config_fn init_fptr = nullptr;
hostfxr_get_runtime_delegate_fn get_delegate_fptr = nullptr;
hostfxr_close_fn close_fptr = nullptr;

bool LoadHostfxr() {
    wchar_t buffer[MAX_PATH];
    size_t bufferSize = sizeof(buffer) / sizeof(wchar_t);
    int rc = get_hostfxr_path(buffer, &bufferSize, nullptr);
    if (rc != 0) {
        char msg[256];
        sprintf_s(msg, "get_hostfxr_path failed, rc=0x%08X", rc);
        Log(msg);
        return false;
    }

    HMODULE lib = LoadLibraryW(buffer);
    if (!lib) {
        char msg[256];
        sprintf_s(msg, "LoadLibraryW(hostfxr) failed, GetLastError=%lu", GetLastError());
        Log(msg);
        return false;
    }

    init_fptr = reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(GetProcAddress(lib, "hostfxr_initialize_for_runtime_config"));
    get_delegate_fptr = reinterpret_cast<hostfxr_get_runtime_delegate_fn>(GetProcAddress(lib, "hostfxr_get_runtime_delegate"));
    close_fptr = reinterpret_cast<hostfxr_close_fn>(GetProcAddress(lib, "hostfxr_close"));

    if (!(init_fptr && get_delegate_fptr && close_fptr)) {
        Log("GetProcAddress failed for one or more hostfxr entry points");
        return false;
    }
    return true;
}

load_assembly_and_get_function_pointer_fn GetLoadAssemblyAndGetFunctionPointer(const std::wstring& runtimeConfigPath) {
    // hostfxr status codes: 0 = Success, 1 = Success_HostAlreadyInitialized (expected here - the
    // target process, e.g. a managed game or this very test host, already has hostfxr/coreclr
    // loaded for its own code), 2 = Success_DifferentRuntimeProperties. Only negative codes are
    // real failures - a plain "!= 0" check would wrongly reject the (very common) already-hosted
    // case.
    hostfxr_handle handle = nullptr;
    int rc = init_fptr(runtimeConfigPath.c_str(), nullptr, &handle);
    if (rc < 0 || !handle) {
        char msg[512];
        sprintf_s(msg, "hostfxr_initialize_for_runtime_config failed, rc=0x%08X", rc);
        Log(msg);
        if (handle) close_fptr(handle);
        return nullptr;
    }

    void* delegatePtr = nullptr;
    rc = get_delegate_fptr(handle, hdt_load_assembly_and_get_function_pointer, &delegatePtr);
    close_fptr(handle);

    if (rc < 0 || !delegatePtr) {
        char msg[256];
        sprintf_s(msg, "hostfxr_get_runtime_delegate failed, rc=0x%08X", rc);
        Log(msg);
        return nullptr;
    }

    return reinterpret_cast<load_assembly_and_get_function_pointer_fn>(delegatePtr);
}

DWORD WINAPI BootstrapThread(LPVOID) {
    // Deliberately NOT done inside DllMain: the loader lock is held there, and
    // LoadLibrary/CLR-init work under it can deadlock the host process.
    std::wstring dir = ModuleDirectory();
    std::wstring runtimeConfigPath = dir + L"\\MotionInput.Cloak.Payload.runtimeconfig.json";
    std::wstring payloadAssemblyPath = dir + L"\\MotionInput.Cloak.Payload.dll";

    if (!LoadHostfxr()) {
        return 1;
    }

    auto loadAssemblyAndGetFunctionPointer = GetLoadAssemblyAndGetFunctionPointer(runtimeConfigPath);
    if (!loadAssemblyAndGetFunctionPointer) {
        return 2;
    }

    install_cloak_fn installCloak = nullptr;
    int rc = loadAssemblyAndGetFunctionPointer(
        payloadAssemblyPath.c_str(),
        L"MotionInput.Cloak.Payload.Payload, MotionInput.Cloak.Payload",
        L"InstallCloak",
        UNMANAGEDCALLERSONLY_METHOD,
        nullptr,
        reinterpret_cast<void**>(&installCloak));

    if (rc < 0 || !installCloak) {
        char msg[256];
        sprintf_s(msg, "load_assembly_and_get_function_pointer failed, rc=0x%08X", rc);
        Log(msg);
        return 3;
    }

    int result = installCloak(nullptr, 0);
    if (result != 0) {
        char msg[128];
        sprintf_s(msg, "InstallCloak returned %d (see MotionInput.Cloak.Payload's own log for the exception)", result);
        Log(msg);
    }
    return 0;
}

}  // namespace

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(hModule);
        HANDLE thread = CreateThread(nullptr, 0, BootstrapThread, nullptr, 0, nullptr);
        if (thread) CloseHandle(thread);
    }
    return TRUE;
}
