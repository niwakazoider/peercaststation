using System;
using System.Linq;
using System.IO;
using PeerCastStation.Core;
using System.Threading;
using System.Collections.Generic;

namespace PeerCastStation.TS
{
  internal class BadDataException : ApplicationException
  {
  }

  public class TSContentReader
    : IContentReader
  {
    private static readonly Logger logger = new Logger(typeof(TSContentReader));

    public TSContentReader(Channel channel)
    {
      this.Channel = channel;
    }

    private long position = 0;
    private int streamIndex = -1;
    private DateTime streamOrigin;
    private MemoryStream cache = new MemoryStream();
    private int sequence = 0;

    private DateTime displayTime = DateTime.Now;

    public ParsedContent Read(Stream stream)
    {
      if (stream.Length - stream.Position <= 0) throw new EndOfStreamException();
      var res = new ParsedContent();
      if (Channel.ContentHeader == null)
      {
        streamIndex = Channel.GenerateStreamID();
        streamOrigin = DateTime.Now;
        res.ContentHeader = new Content(streamIndex, TimeSpan.Zero, position, new byte[] { });
        var channel_info = new AtomCollection(Channel.ChannelInfo.Extra);
        channel_info.SetChanInfoType("TS");
        channel_info.SetChanInfoStreamType("video/mpeg");  /* video/mp2t */
        channel_info.SetChanInfoStreamExt(".ts");
        res.ChannelInfo = new ChannelInfo(channel_info);
        sequence = 0;
        position = 0;
      }
      res.Contents = new List<Content>();

      var processed = false;
      var eos = false;
      while (!eos)
      {
        var start_pos = stream.Position;
        try
        {
          var bytes10 = ReadBytes(stream, 188 * 10);
          for (int i = 0; i < 10; i++)
          {
            var bytes = new byte[188];
            Array.Copy(bytes10, 188 * i, bytes, 0, 188);

            var packet = new TSPacket(bytes);
            if (packet.sync_byte != 0x47)
            {
              if (cache.Length > 0) clearCache();
              throw new BadDataException();
            }
            if (packet.payload_unit_start_indicator > 0)
            {
              if (cache.Length >= 7144)
              {
                addContent(res, cache, position);
                position += cache.ToArray().Length;
                clearCache();
              }
            }
            addCache(bytes);
          }

          processed = true;
        }
        catch (EndOfStreamException)
        {
          if (!processed) throw;
          stream.Position = start_pos;
          eos = true;
        }
        catch (BadDataException)
        {
          stream.Position = start_pos + 1;
        }
      }

      return res;
    }

    private void addContent(ParsedContent res, MemoryStream cache, long pos)
    {
      if (cache.Length > 0)
      {
        cache.Close();
        byte[] bytes = cache.ToArray();
        cache = new MemoryStream();

        res.Contents.Add(new Content(streamIndex, DateTime.Now - streamOrigin, pos, bytes));
      }
    }

    private void clearCache()
    {
      cache = new MemoryStream();
    }

    private void addCache(byte[] bytes)
    {
      if (cache.Length < 8 * 1024 * 1024)
      {
        cache.Write(bytes, 0, bytes.Length);
      }
    }

    private static byte[] ReadBytes(Stream stream, int len)
    {
      var res = new byte[len];
      if (stream.Read(res, 0, len) < len)
      {
        throw new EndOfStreamException();
      }
      return res;
    }

    public string Name { get { return "TS"; } }
    public Channel Channel { get; private set; }
  }

  public class TSContentReaderFactory
    : IContentReaderFactory
  {
    public string Name { get { return "MPEG-TS (TS)"; } }

    public IContentReader Create(Channel channel)
    {
      return new TSContentReader(channel);
    }
  }

  [Plugin]
  public class TSContentReaderPlugin
    : PluginBase
  {
    override public string Name { get { return "TS Content Reader"; } }

    private TSContentReaderFactory factory;
    override protected void OnAttach()
    {
      if (factory == null) factory = new TSContentReaderFactory();
      Application.PeerCast.ContentReaderFactories.Add(factory);
    }

    override protected void OnDetach()
    {
      Application.PeerCast.ContentReaderFactories.Remove(factory);
    }
  }
}
