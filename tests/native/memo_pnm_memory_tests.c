#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <leptonica/allheaders.h>

static int failures;
static int cases;

#ifdef MEMO_PNM_REPLACEMENT_TEST
static int pix_create_calls;
static int refuse_allocation;
static PIX *counted_pix_create(l_int32 w, l_int32 h, l_int32 d)
{
    ++pix_create_calls;
    return refuse_allocation ? NULL : pixCreate(w, h, d);
}
#define pixCreate counted_pix_create
#define pixReadMemPnm memo_test_pix_read_mem_pnm
#include "../../tools/native/memo_pnm_memory.c"
#undef pixReadMemPnm
#undef pixCreate
#define READ_PNM memo_test_pix_read_mem_pnm
#else
#define READ_PNM pixReadMemPnm
#endif

static int check(int condition, const char *name)
{
    if (!condition) { fprintf(stderr, "FAIL: %s\n", name); ++failures; }
    return condition;
}

/* Put short buffers immediately before an inaccessible page. No NUL byte is
 * promised beyond size, including for every truncated prefix. */
static PIX *guarded_read(const l_uint8 *data, size_t size)
{
    SYSTEM_INFO info;
    DWORD old_protection;
    l_uint8 *pages, *input;
    PIX *pix;
    GetSystemInfo(&info);
    if (!check(size <= info.dwPageSize, "guard fixture fits one page")) return NULL;
    pages = (l_uint8 *)VirtualAlloc(NULL, (size_t)info.dwPageSize * 2,
                                  MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    if (!check(pages != NULL, "guard allocation")) return NULL;
    if (!check(VirtualProtect(pages + info.dwPageSize, info.dwPageSize,
                              PAGE_NOACCESS, &old_protection) != 0,
               "guard protection")) {
        VirtualFree(pages, 0, MEM_RELEASE);
        return NULL;
    }
    input = pages + info.dwPageSize - size;
    if (size) memcpy(input, data, size);
    if (!check(VirtualProtect(pages, info.dwPageSize, PAGE_READONLY,
                              &old_protection) != 0, "borrowed input protection")) {
        VirtualFree(pages, 0, MEM_RELEASE);
        return NULL;
    }
    pix = READ_PNM(input, size);
    if (size) check(memcmp(input, data, size) == 0, "input bytes remain unchanged");
    VirtualFree(pages, 0, MEM_RELEASE);
    return pix;
}

static void rejected(const l_uint8 *data, size_t size, const char *name)
{
    PIX *pix;
    ++cases;
#ifdef MEMO_PNM_REPLACEMENT_TEST
    pix_create_calls = 0;
#endif
    pix = guarded_read(data, size);
    check(pix == NULL, name);
    pixDestroy(&pix);
#ifdef MEMO_PNM_REPLACEMENT_TEST
    check(pix_create_calls == 0, "invalid input rejected before pixCreate");
#endif
}

static void valid_image(l_int32 width, l_int32 height, int use_guard, l_uint8 first)
{
    char header[18];
    int header_size = snprintf(header, sizeof(header), "P6\n%d %d\n255\n", width, height);
    size_t payload = (size_t)width * (size_t)height * 3;
    size_t size = (size_t)header_size + payload;
    l_uint8 *data = (l_uint8 *)malloc(size);
    l_uint8 *before = (l_uint8 *)malloc(size);
    PIX *pix;
    l_int32 x, y;
    size_t at;
    ++cases;
    if (!check(data != NULL && before != NULL, "valid fixture allocation")) {
        free(data); free(before); return;
    }
    memcpy(data, header, (size_t)header_size);
    for (at = 0; at < payload; ++at) data[header_size + at] = (l_uint8)((at * 37 + 11) & 255);
    /* Payload begins with bytes a permissive header scanner might skip. */
    data[header_size] = first; data[header_size + 1] = ' '; data[header_size + 2] = '\n';
    if (payload >= 6) { data[header_size + 3] = '#'; data[header_size + 4] = '\r'; data[header_size + 5] = 255; }
    memcpy(before, data, size);
#ifdef MEMO_PNM_REPLACEMENT_TEST
    pix_create_calls = 0;
#endif
    pix = use_guard ? guarded_read(data, size) : READ_PNM(data, size);
    if (!check(pix != NULL, "canonical P6 must decode without temporary file access")) {
        free(data); free(before); return;
    }
#ifdef MEMO_PNM_REPLACEMENT_TEST
    check(pix_create_calls == 1, "valid input has one PIX allocation");
#endif
    check(pixGetWidth(pix) == width && pixGetHeight(pix) == height && pixGetDepth(pix) == 32,
          "dimensions and 32-bit PIX");
    check(pixGetInputFormat(pix) == IFF_PNM, "PNM input format retained");
    check(memcmp(data, before, size) == 0, "decoder does not mutate borrowed bytes");
    /* A returned PIX must not alias source bytes. */
    data[header_size] ^= 255;
    for (y = 0; y < height; ++y) for (x = 0; x < width; ++x) {
        l_uint32 pixel;
        l_uint32 expected;
        at = (size_t)header_size + ((size_t)y * (size_t)width + (size_t)x) * 3;
        expected = ((l_uint32)before[at] << 24) | ((l_uint32)before[at + 1] << 16) |
                   ((l_uint32)before[at + 2] << 8);
        if (pixGetPixel(pix, x, y, &pixel) != 0 || pixel != expected) {
            check(0, "all RGB channels, rows, and independent ownership");
            y = height; break;
        }
    }
    pixDestroy(&pix);
    free(data); free(before);
}

int main(void)
{
    static const l_uint8 valid[] = {'P','6','\n','1',' ','1','\n','2','5','5','\n',0,' ','\n'};
    static const l_uint8 first_pixels[] = {0, '\t', '\n', '\r', ' ', '#', 255};
    static const char *bad_headers[] = {
        "P5\n1 1\n255\n", "P6 1 1\n255\n", "P6\r\n1 1\n255\n",
        "P6\n0 1\n255\n", "P6\n1 0\n255\n", "P6\n01 1\n255\n", "P6\n1 01\n255\n",
        "P6\n-1 1\n255\n", "P6\n+1 1\n255\n", "P6\n1\t1\n255\n", "P6\n1  1\n255\n",
        "P6\n 1 1\n255\n", "P6\n1 1 \n255\n", "P6\n1 1\r\n255\n",
        "P6\n1 1\n256\n", "P6\n1 1\n0255\n", "P6\n1 1\n255 \n", "P6\n1 1\n255\r\n",
        "P6\n# comment\n1 1\n255\n", "P6\n1025 1\n255\n", "P6\n1 1025\n255\n",
        "P6\n2147483647 1\n255\n", "P6\n18446744073709551616 1\n255\n",
        "P6\n1024 1024\n255\n"
    };
    size_t i;
    l_uint8 buffer[128];
    PIX *pix;
    /* This runs first so the unmodified library RED identifies the actual
     * canonical memory-read failure, before malformed-input hardening checks. */
    valid_image(1, 1, 1, 0);
    if (failures) return 1;
    for (i = 0; i < sizeof(first_pixels); ++i) valid_image(3, 2, 1, first_pixels[i]);
    valid_image(1, 1024, 0, 0);
    valid_image(1024, 1, 0, 0);
    valid_image(1024, 1024, 0, 0);
    for (i = 0; i < sizeof(valid); ++i) rejected(valid, i, "every truncated prefix rejected");
    for (i = 0; i < sizeof(bad_headers) / sizeof(bad_headers[0]); ++i) {
        size_t length = strlen(bad_headers[i]);
        memcpy(buffer, bad_headers[i], length);
        memset(buffer + length, 0, 3);
        rejected(buffer, length + 3, "noncanonical or oversized header rejected");
    }
    memcpy(buffer, valid, sizeof(valid)); buffer[sizeof(valid)] = 42;
    rejected(buffer, sizeof(valid) + 1, "extra payload byte rejected");
    memcpy(buffer + sizeof(valid), valid, sizeof(valid));
    rejected(buffer, sizeof(valid) * 2, "concatenated images rejected");
    memcpy(buffer, valid, sizeof(valid)); buffer[3] = 0;
    rejected(buffer, sizeof(valid), "NUL in header rejected");
#ifdef MEMO_PNM_REPLACEMENT_TEST
    pix_create_calls = 0;
#endif
    pix = READ_PNM(NULL, sizeof(valid));
    check(pix == NULL, "null input rejected"); pixDestroy(&pix);
    pix = READ_PNM(valid, (size_t)-1);
    check(pix == NULL, "unrepresentable size refused before reading"); pixDestroy(&pix);
#ifdef MEMO_PNM_REPLACEMENT_TEST
    check(pix_create_calls == 0, "null and oversized buffers rejected before allocation");
    refuse_allocation = 1; pix_create_calls = 0;
    pix = READ_PNM(valid, sizeof(valid));
    check(pix == NULL && pix_create_calls == 1, "PIX allocation failure is returned safely");
    pixDestroy(&pix);
    refuse_allocation = 0;
#endif
    if (failures) { fprintf(stderr, "FAIL: %d native P6 assertions\n", failures); return 1; }
    printf("PASS: %d canonical P6 memory cases; exact RGB/ownership/guard pages/bounds; no temp environment supplied\n", cases);
    return 0;
}
