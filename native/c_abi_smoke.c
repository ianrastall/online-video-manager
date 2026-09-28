#include "ovm.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* This client is compiled as C and links only ovm_core.dll, with no .NET host. */
int main(int argc, char **argv)
{
    char *reply;
    char *at;
    unsigned long long handle;
    if (argc != 2 || ovm_abi_version() != 2)
        return 1;
    reply = ovm_open(argv[1]);
    if (!reply || !(at = strstr(reply, "\"handle\":")))
    {
        if (reply)
            puts(reply);
        ovm_free(reply);
        return 2;
    }
    handle = strtoull(at + strlen("\"handle\":"), NULL, 10);
    ovm_free(reply);
    reply = ovm_execute(handle, "{\"operation\":\"clipboard.collect\",\"text\":\"https://vimeo.com/123\"}");
    if (!reply || strncmp(reply, "{\"error\":", 9) == 0 || !strstr(reply, "\"items\":[]"))
    {
        ovm_free(reply);
        ovm_close(handle);
        return 3;
    }
    ovm_free(reply);
    ovm_close(handle);
    puts("PASS: plain C client opened the engine and collected a link without starting downloads.");
    return 0;
}
