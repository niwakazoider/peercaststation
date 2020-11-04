using System;
using System.IO;
using System.Linq;
using PeerCastStation.Core;
using System.Threading;
using System.Threading.Tasks;

namespace PeerCastStation.MP4
{
  internal class BadDataException : ApplicationException
  {
  }

  public class MP4ContentReader
    : IContentReader
  {
    public MP4ContentReader(Channel channel)
    {
      this.Channel = channel;
    }

    public class Mbox
    {
      public enum BoxType {
        FTYP,
        MOOV,
        MOOF,
        MDAT,
        UNKNOWN
      }
      public BoxType Type { get; private set; }
      public int Size { get; private set; }
      public byte[] Data { get; private set; }

      public async Task ReadBoxAsync(Stream s, CancellationToken cancel_token)
      {
        byte[] b;
        MemoryStream mem = new MemoryStream();

        b = await s.ReadBytesAsync(4, cancel_token);
        mem.Write(b, 0, 4);
        Size = (b[0]<<24) | (b[1]<<16) | (b[2]<<8) | (b[3]);

        if(Size > 8*1024*1024){
          throw new BadDataException();
        }

        b = await s.ReadBytesAsync(4, cancel_token);
        mem.Write(b, 0, 4);
        Type = ParseType(b);

        b = await s.ReadBytesAsync(Size-8, cancel_token);
        mem.Write(b, 0, b.Length);
        Data = mem.ToArray();
      }

      public static BoxType ParseType(byte[] bytes){
        var encoding = System.Text.Encoding.UTF8;
        var type = encoding.GetString(bytes).ToUpper();
        try{
          return (Mbox.BoxType) Enum.Parse(typeof(Mbox.BoxType), type);
        }catch(Exception){
          return BoxType.UNKNOWN;
        }
      }

      public static int ParseBitrate(byte[] bytes){
        //fix me
        //
        //https://developer.apple.com/library/archive/documentation/QuickTime/QTFF/QTFFChap3/qtff3.html#//apple_ref/doc/uid/TP40000939-CH205-SW120
        //Bit Rate Atom (20bytes) [moov trak mdia minf ... btrt]
        //
        //Atom size (4)
        //Type 'btrt' (4)
        //Buffer size (4)
        //Max bit rate (4)
        //Average bit rate (4)
        
        byte[] pattern = new byte[] {0x00, 0x00, 0x00, 0x14, 0x62, 0x74, 0x72, 0x74};
        
        var bitrate = 0;

        for (int i = 0; i < bytes.Length; i++){
          if (bytes.Skip(i).Take(pattern.Length).SequenceEqual(pattern)){
            var b = bytes.Skip(i+12).Take(4).ToArray();
            var maxBitrate = (b[0]<<24) | (b[1]<<16) | (b[2]<<8) | (b[3]);
            bitrate += maxBitrate;
          }
        }

        return bitrate;
      }
    }

    public string Name { get { return "Fragmented MP4 (MP4)"; } }
    public Channel Channel { get; private set; }
    public long Position { get { return position; } }
    private long position = 0;
    private int streamIndex = -1;
    private DateTime streamOrigin = DateTime.Now;

    public async Task<byte[]> ReadHeaderAsync(Stream s, CancellationToken cancel_token)
    {
      var typebox = new Mbox();
      await typebox.ReadBoxAsync(s, cancel_token);
      if(typebox.Type != Mbox.BoxType.FTYP){
        throw new BadDataException();
      }

      var moovbox = new Mbox();
      await moovbox.ReadBoxAsync(s, cancel_token);
      if(moovbox.Type != Mbox.BoxType.MOOV){
        throw new BadDataException();
      }

      var mem = new MemoryStream();
      mem.Write(typebox.Data, 0, typebox.Data.Length);
      mem.Write(moovbox.Data, 0, moovbox.Data.Length);

      return mem.ToArray();
    }

    public async Task<byte[]> ReadBodyAsync(Stream s, CancellationToken cancel_token)
    {
      var moofBox = new Mbox();
      await moofBox.ReadBoxAsync(s, cancel_token);
      if(moofBox.Type != Mbox.BoxType.MOOF){
        return new byte[0];
      }

      var mdatBox = new Mbox();
      await mdatBox.ReadBoxAsync(s, cancel_token);
      if(mdatBox.Type != Mbox.BoxType.MDAT){
        return new byte[0];
      }

      var mem = new MemoryStream();
      mem.Write(moofBox.Data, 0, moofBox.Data.Length);
      mem.Write(mdatBox.Data, 0, mdatBox.Data.Length);
      return mem.ToArray();
    }

    public async Task ReadAsync(IContentSink sink, Stream stream, CancellationToken cancel_token)
    {
      streamIndex = Channel.GenerateStreamID();
      streamOrigin = DateTime.Now;

      try {
        var header = await ReadHeaderAsync(stream, cancel_token);
        sink.OnContentHeader(
          new Content(streamIndex, TimeSpan.Zero, 0, header, PCPChanPacketContinuation.None)
        );
        position += header.Length;

        var info = new AtomCollection(Channel.ChannelInfo.Extra);
        info.SetChanInfoType("MP4");
        info.SetChanInfoStreamType("video/mp4");
        info.SetChanInfoStreamExt(".mp4");
        info.SetChanInfoBitrate((int)(Mbox.ParseBitrate(header)/1000));
        sink.OnChannelInfo(new ChannelInfo(info));

        while (true) {
          var body = await ReadBodyAsync(stream, cancel_token);
          if(body.Length == 0){
            continue;
          }
          sink.OnContent(
            new Content(streamIndex, DateTime.Now-streamOrigin, position, body, PCPChanPacketContinuation.None)
          );
          position += body.Length;
        }
      }
      catch (EndOfStreamException) {
      }
      catch (BadDataException) {
      }
    }
  }

  public class MP4ContentReaderFactory
    : IContentReaderFactory
  {
    public string Name { get { return "Fragmented MP4 (MP4)"; } }

    public IContentReader Create(Channel channel)
    {
      return new MP4ContentReader(channel);
    }

    public bool TryParseContentType(byte[] header, out string content_type, out string mime_type)
    {
      if(header.Length>=8){
        if (header[4]=='f' && header[5]=='t' && header[6]=='y' && header[7]=='p') {
          content_type = "MP4";
          mime_type = "video/mp4";
          return true;
        }
      }
      content_type = null;
      mime_type    = null;
      return false;
    }
  }

  [Plugin]
  public class MP4ContentReaderPlugin
    : PluginBase
  {
    override public string Name { get { return "MP4 Content Reader"; } }

    private MP4ContentReaderFactory factory;
    override protected void OnAttach()
    {
      if (factory==null) factory = new MP4ContentReaderFactory();
      Application.PeerCast.ContentReaderFactories.Add(factory);
    }

    override protected void OnDetach()
    {
      Application.PeerCast.ContentReaderFactories.Remove(factory);
    }
  }
}
