using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace MemoApp.Core.Transfer;
// Internal detached input primitive; no executable, source/session or publication authority.
internal static class PpmOcrInput
{
 internal static PreparedTextExport Capture(OwnedBgraRaster raster,CancellationToken token=default)
 {
  ArgumentNullException.ThrowIfNull(raster);byte[]? output=null;bool returned=false;
  try
  {
   token.ThrowIfCancellationRequested();int width=raster.Width,height=raster.Height;
   if(width is <1 or >1024||height is <1 or >1024||raster.Stride!=width*4)throw new InvalidDataException("OCR raster dimensions");
   byte[] header=Encoding.ASCII.GetBytes("P6\n"+width.ToString(CultureInfo.InvariantCulture)+" "+height.ToString(CultureInfo.InvariantCulture)+"\n255\n");
   output=new byte[checked(header.Length+width*height*3)];header.CopyTo(output,0);
   bool consumed=raster.ConsumePixels(pixels=>
   {
    if(pixels.Length!=checked(width*height*4))throw new InvalidDataException("OCR raster length");
    int target=header.Length;
    for(int y=0;y<height;y++)
    {
     token.ThrowIfCancellationRequested();
     for(int x=0;x<width;x++)
     {
      int at=(y*width+x)*4,alpha=pixels[at+3];
      output[target++]=(byte)((pixels[at+2]*alpha+255*(255-alpha)+127)/255);
      output[target++]=(byte)((pixels[at+1]*alpha+255*(255-alpha)+127)/255);
      output[target++]=(byte)((pixels[at]*alpha+255*(255-alpha)+127)/255);
     }
    }
   });
   if(!consumed)throw new OperationCanceledException();token.ThrowIfCancellationRequested();returned=true;return new(output);
  }
  finally{raster.Dispose();if(!returned&&output is not null)CryptographicOperations.ZeroMemory(output);}
 }
}
