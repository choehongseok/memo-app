/* Replacement for pixReadMemPnm in pinned Leptonica 1.87.0 pnmio.c.
 * Insert this text in place of that complete function; no includes are added.
 * Windows accepts only the bounded canonical P6 emitted by PpmOcrInput.
 * The non-Windows implementation below is the original upstream function. */
#ifdef _WIN32
static l_int32
memoPnmDimension(const l_uint8 *data, size_t size, size_t *position,
                 l_uint8 delimiter, l_int32 *value)
{
    size_t at = *position;
    l_int32 number = 0;

    if (at >= size || data[at] < '1' || data[at] > '9')
        return 1;
    do {
        number = number * 10 + (data[at] - '0');
        if (number > 1024)
            return 1;
        ++at;
    } while (at < size && data[at] >= '0' && data[at] <= '9');
    if (at >= size || data[at] != delimiter)
        return 1;
    *position = at + 1;
    *value = number;
    return 0;
}
#endif

PIX *
pixReadMemPnm(const l_uint8 *data, size_t size)
{
#ifdef _WIN32
    size_t position = 3, payload;
    l_int32 width, height, x, y, wpl;
    l_uint32 *pixels, *line;
    PIX *pix;

    /* 1x1 has an 11-byte header; 1024x1024 has a 17-byte header.
     * Validate the complete input before allocating any PIX storage. */
    if (!data || size < 14 || size > (size_t)3145745)
        return (PIX *)ERROR_PTR("bounded canonical P6 required", __func__, NULL);
    if (data[0] != 'P' || data[1] != '6' || data[2] != '\n' ||
        memoPnmDimension(data, size, &position, ' ', &width) ||
        memoPnmDimension(data, size, &position, '\n', &height))
        return (PIX *)ERROR_PTR("canonical P6 header required", __func__, NULL);
    if (size - position < 4 || data[position] != '2' ||
        data[position + 1] != '5' || data[position + 2] != '5' ||
        data[position + 3] != '\n')
        return (PIX *)ERROR_PTR("canonical P6 maxval required", __func__, NULL);
    position += 4;
    payload = (size_t)width * (size_t)height * 3;
    if (size - position != payload)
        return (PIX *)ERROR_PTR("exact P6 payload required", __func__, NULL);

    if ((pix = pixCreate(width, height, 32)) == NULL)
        return (PIX *)ERROR_PTR("pix not made", __func__, NULL);
    pixSetInputFormat(pix, IFF_PNM);
    pixels = pixGetData(pix);
    wpl = pixGetWpl(pix);
    for (y = 0; y < height; ++y) {
        line = pixels + (size_t)y * (size_t)wpl;
        for (x = 0; x < width; ++x) {
            composeRGBPixel(data[position], data[position + 1],
                            data[position + 2], &line[x]);
            position += 3;
        }
    }
    return pix;
#else
    FILE *fp;
    PIX *pix;

    if (!data)
        return (PIX *)ERROR_PTR("data not defined", __func__, NULL);
    if ((fp = fopenReadFromMemory(data, size)) == NULL)
        return (PIX *)ERROR_PTR("stream not opened", __func__, NULL);
    pix = pixReadStreamPnm(fp);
    fclose(fp);
    if (!pix) L_ERROR("pix not read\n", __func__);
    return pix;
#endif
}
