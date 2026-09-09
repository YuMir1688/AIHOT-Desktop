using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YuMir.Cards;

public static class LongReportPng
{
    public static async Task<(int Width, int Height)> SaveAsync(ReportDocument document, string path,
        IProgress<int>? progress = null, CancellationToken cancellation = default)
    {
        path = Path.GetFullPath(path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            // Measure strips independently; never allocate a bitmap the size of the entire report.
            int height = 0;
            for (int i = 0; i < document.Pages.Count; i++)
            { cancellation.ThrowIfCancellationRequested(); height = checked(height + ReportCards.RenderLongSection(document, i).PixelHeight); await Task.Yield(); }
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                file.Write(new byte[] {137,80,78,71,13,10,26,10});
                var ihdr = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0,4),1080); BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4,4),height);
                ihdr[8] = 8; ihdr[9] = 2; Chunk(file,"IHDR",ihdr);
                using (var chunks = new IdatStream(file))
                using (var compressor = new ZLibStream(chunks, CompressionLevel.Fastest, leaveOpen:true))
                {
                    var rgb = new byte[1080 * 3 + 1];
                    for (int i = 0; i < document.Pages.Count; i++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var section = new FormatConvertedBitmap(ReportCards.RenderLongSection(document, i), PixelFormats.Bgr24, null, 0);
                        int stride = 1080 * 3; var pixels = new byte[stride * section.PixelHeight]; section.CopyPixels(pixels, stride, 0);
                        for (int y = 0; y < section.PixelHeight; y++)
                        {
                            for (int x = 0; x < 1080; x++)
                            { int source = y * stride + x * 3, target = x * 3 + 1; rgb[target] = pixels[source+2]; rgb[target+1] = pixels[source+1]; rgb[target+2] = pixels[source]; }
                            compressor.Write(rgb);
                        }
                        progress?.Report(i + 1); await Task.Yield();
                    }
                }
                Chunk(file,"IEND",[]);
            }
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite:true); return (1080,height);
        }
        catch { if(File.Exists(temporary)) File.Delete(temporary); throw; }
    }
    private static readonly uint[] Table = Enumerable.Range(0,256).Select(i => { uint c=(uint)i; for(int n=0;n<8;n++) c=(c&1)!=0 ? 0xedb88320U^(c>>1) : c>>1; return c; }).ToArray();
    private static void Chunk(Stream output,string name, ReadOnlySpan<byte> bytes)
    {
        Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(number,bytes.Length); output.Write(number);
        var type=System.Text.Encoding.ASCII.GetBytes(name); output.Write(type); output.Write(bytes); uint crc=0xffffffff;
        foreach(byte value in type) crc=Table[(crc^value)&255]^(crc>>8);
        foreach(byte value in bytes) crc=Table[(crc^value)&255]^(crc>>8);
        BinaryPrimitives.WriteUInt32BigEndian(number,crc^0xffffffff);output.Write(number);
    }
    private sealed class IdatStream(Stream output) : Stream
    {
        private readonly byte[] buffer = new byte[65536]; private int used;
        public override void Write(byte[] bytes,int offset,int count) => Write(bytes.AsSpan(offset,count));
        public override void Write(ReadOnlySpan<byte> bytes)
        { while(bytes.Length>0){int n=Math.Min(buffer.Length-used,bytes.Length);bytes[..n].CopyTo(buffer.AsSpan(used));used+=n;bytes=bytes[n..];if(used==buffer.Length)Flush();} }
        public override void Flush(){if(used>0){Chunk(output,"IDAT",buffer.AsSpan(0,used));used=0;}}
        protected override void Dispose(bool disposing){if(disposing)Flush();base.Dispose(disposing);}
        public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();
    }
}
