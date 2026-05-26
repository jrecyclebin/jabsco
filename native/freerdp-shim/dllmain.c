#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <freerdp/freerdp.h>

BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD fdwReason, LPVOID lpvReserved)
{
    (void)hinstDLL;
    (void)lpvReserved;
    if (fdwReason == DLL_PROCESS_ATTACH)
    {
        WSADATA wsa;
        WSAStartup(MAKEWORD(2, 2), &wsa);
    }
    else if (fdwReason == DLL_PROCESS_DETACH)
    {
        WSACleanup();
    }
    return TRUE;
}

/* Returns rdpSettings* via C struct access — avoids hardcoded offsets that
 * differ between Linux and Windows builds of FreeRDP. */
rdpSettings* jabsco_get_settings(const freerdp* instance)
{
    return instance->context->settings;
}

/* Tests getaddrinfo for a given host — returns 0 on success, WSA error code on failure.
 * Used to isolate whether Winsock resolution works independently of FreeRDP. */
int jabsco_test_getaddrinfo(const char* host)
{
    struct addrinfo hints = {0};
    hints.ai_family   = AF_UNSPEC;
    hints.ai_socktype = SOCK_STREAM;
    struct addrinfo* res = NULL;
    int ret = getaddrinfo(host, NULL, &hints, &res);
    if (res) freeaddrinfo(res);
    if (ret != 0) return WSAGetLastError();
    return 0;
}
